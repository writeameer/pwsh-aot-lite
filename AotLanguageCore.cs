using System.Globalization;

namespace PwshAotLite;

// A scope belongs to one execution (or an explicitly owned REPL session), not
// to the process.  It is deliberately a small lexical store of closed values:
// it is not PowerShell SessionState and never consults providers, environment
// variables, automatic variables, or CLR objects.
internal sealed class AotScope(AotScope? parent = null)
{
    private readonly Dictionary<string, AotValue> _values = new(StringComparer.OrdinalIgnoreCase);

    internal AotScope? Parent { get; } = parent;

    internal void Set(string name, AotValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _values[name] = value;
    }

    internal bool TryGet(string name, out AotValue value) =>
        _values.TryGetValue(name, out value) || (Parent?.TryGet(name, out value) ?? false);
}

// Block output is segmented because successive commands may legitimately have
// different table shapes.  Flattening them under one column list would invent
// a formatting contract that PowerShell itself does not have.
internal sealed record AotExecutionOutput(IReadOnlyList<IPipelineRecord> Rows, IReadOnlyList<string> Columns);

internal sealed class AotExecutionResult(IReadOnlyList<AotExecutionOutput> outputs)
{
    internal IReadOnlyList<AotExecutionOutput> Outputs { get; } = outputs;
}

internal sealed class AotBlockPlan(IReadOnlyList<AotStatementPlan> statements)
{
    internal IReadOnlyList<AotStatementPlan> Statements { get; } = statements;

    // The host may observe a completed pipeline before a later statement
    // terminates.  Accumulating the result as well keeps the programmatic
    // execution API useful without making host output transactional.
    internal AotExecutionResult Execute(
        AotExecutionContext context,
        AotScope scope,
        Action<AotExecutionOutput>? onOutput = null)
    {
        List<AotExecutionOutput> outputs = [];
        foreach (AotStatementPlan statement in Statements)
        {
            if (statement.Execute(context, scope) is { } output)
            {
                outputs.Add(output);
                onOutput?.Invoke(output);
            }
        }

        return new AotExecutionResult(outputs);
    }
}

internal abstract class AotStatementPlan
{
    internal abstract AotExecutionOutput? Execute(AotExecutionContext context, AotScope scope);
}

internal sealed class AotAssignmentPlan(string name, AotExpressionPlan value) : AotStatementPlan
{
    internal override AotExecutionOutput? Execute(AotExecutionContext context, AotScope scope)
    {
        scope.Set(name, value.Evaluate(scope));
        return null;
    }
}

internal sealed class AotPipelineStatementPlan(
    AotCommandPlan source,
    AotFilterStagePlan? filter,
    IReadOnlyList<string>? columns,
    bool projected,
    AotSourceSpan? projectionSpan) : AotStatementPlan
{
    internal override AotExecutionOutput Execute(AotExecutionContext context, AotScope scope)
    {
        PipelinePlan pipeline = BindPipeline(scope);
        return new AotExecutionOutput(pipeline.Execute(context), pipeline.Columns);
    }

    internal PipelinePlan BindPipeline(AotScope scope)
    {
        (IAotCmdlet cmdlet, CommandInvocation invocation) = source.Bind(scope);
        Filter? resolvedFilter = filter?.Resolve(scope);
        return new PipelinePlan(cmdlet, invocation, null, null, resolvedFilter, columns ?? cmdlet.DefaultColumns, projected, projectionSpan);
    }
}

// Expressions are intentionally closed and AST-derived.  Adding a new node is
// a reviewed language/runtime decision rather than an incidental fallback to
// upstream evaluation APIs.
internal abstract class AotExpressionPlan(AotSourceSpan span)
{
    internal AotSourceSpan Span { get; } = span;
    internal abstract AotValue Evaluate(AotScope scope);
}

internal sealed class AotLiteralExpressionPlan : AotExpressionPlan
{
    internal AotLiteralExpressionPlan(AotValue value, AotSourceSpan span)
        : base(span)
    {
        Value = value;
    }

    internal AotValue Value { get; }
    internal override AotValue Evaluate(AotScope scope) => Value;
}

internal sealed class AotListExpressionPlan(IReadOnlyList<AotExpressionPlan> elements, AotSourceSpan span) : AotExpressionPlan(span)
{
    internal override AotValue Evaluate(AotScope scope) => AotValue.FromList(elements.Select(element => element.Evaluate(scope)));
}

internal sealed class AotVariableExpressionPlan(string name, AotSourceSpan span) : AotExpressionPlan(span)
{
    internal override AotValue Evaluate(AotScope scope)
    {
        if (!scope.TryGet(name, out AotValue value))
        {
            throw new ScriptException(AotDiagnostics.Scope(
                "AOT5001",
                $"Variable '${name}' has not been assigned in this AOT scope.",
                Span,
                "undefined variable",
                "Assign the variable earlier in this script, or pass a direct literal."));
        }

        return value;
    }
}

internal abstract class AotCommandArgumentPlan(AotSourceSpan span)
{
    internal AotSourceSpan Span { get; } = span;
    internal abstract void AppendResolved(AotScope scope, List<CommandSyntaxAtom> atoms);
}

internal sealed class AotParameterArgumentPlan : AotCommandArgumentPlan
{
    internal AotParameterArgumentPlan(string name, AotSourceSpan span)
        : base(span)
    {
        Name = name;
    }

    internal string Name { get; }
    internal override void AppendResolved(AotScope scope, List<CommandSyntaxAtom> atoms) => atoms.Add(new CommandSyntaxAtom(Name, IsParameter: true, Span));
}

internal sealed class AotValueArgumentPlan : AotCommandArgumentPlan
{
    internal AotValueArgumentPlan(AotExpressionPlan expression)
        : base(expression.Span)
    {
        Expression = expression;
    }

    internal AotExpressionPlan Expression { get; }

    internal override void AppendResolved(AotScope scope, List<CommandSyntaxAtom> atoms) =>
        AotCommandArgumentConverter.Append(Expression.Evaluate(scope), Span, atoms);
}

internal sealed class AotCommandPlan(string name, AotSourceSpan commandSpan, IReadOnlyList<AotCommandArgumentPlan> arguments)
{
    internal string Name { get; } = name;
    internal AotSourceSpan CommandSpan { get; } = commandSpan;
    internal IReadOnlyList<AotCommandArgumentPlan> Arguments { get; } = arguments;

    internal (IAotCmdlet Cmdlet, CommandInvocation Invocation) Bind(AotScope scope)
    {
        List<CommandSyntaxAtom> atoms = [];
        foreach (AotCommandArgumentPlan argument in Arguments)
        {
            argument.AppendResolved(scope, atoms);
        }

        try
        {
            return AotCmdletRegistry.BindCommand(Name, atoms, CommandSpan);
        }
        catch (ScriptException exception)
        {
            throw exception.Diagnostic.Span is null ? exception.WithSpan(CommandSpan) : exception;
        }
    }
}

// This is the sole explicit conversion from language values into the existing
// string-valued generated-metadata binder.  Values never become parameter
// tokens; only AST parameter nodes create parameter atoms.
internal static class AotCommandArgumentConverter
{
    internal static void Append(AotValue value, AotSourceSpan span, List<CommandSyntaxAtom> atoms)
    {
        if (value.TryGetItems(out IReadOnlyList<AotValue>? items))
        {
            foreach (AotValue item in items!)
            {
                Append(item, span, atoms);
            }

            return;
        }

        string text = value.Kind switch
        {
            AotValueKind.Null => string.Empty,
            AotValueKind.Boolean when value.TryGetBoolean(out bool boolean) => boolean ? "True" : "False",
            AotValueKind.Integer when value.TryGetInteger(out long integer) => integer.ToString(CultureInfo.InvariantCulture),
            AotValueKind.Decimal when value.TryGetDecimal(out decimal decimalValue) => decimalValue.ToString(CultureInfo.InvariantCulture),
            AotValueKind.FloatingPoint when value.TryGetFloatingPoint(out double floatingPoint) => floatingPoint.ToString("R", CultureInfo.InvariantCulture),
            AotValueKind.String when value.TryGetString(out string? stringValue) => stringValue!,
            _ => throw new ScriptException(AotDiagnostics.Scope(
                "AOT5003",
                $"Value kind '{value.Kind}' cannot be passed to a command argument in the AOT subset.",
                span,
                "unsupported command argument value",
                "Use a string, Boolean, finite number, null, or a list of those values.")),
        };

        atoms.Add(new CommandSyntaxAtom(text, IsParameter: false, span));
    }
}

internal abstract class AotFilterStagePlan
{
    internal abstract Filter Resolve(AotScope scope);
}

internal sealed class AotLiteralFilterStagePlan(Filter filter) : AotFilterStagePlan
{
    internal override Filter Resolve(AotScope scope) => filter;
}

internal sealed class AotVariableFilterStagePlan(string property, Comparison comparison, AotExpressionPlan value, AotSourceSpan propertySpan) : AotFilterStagePlan
{
    internal override Filter Resolve(AotScope scope)
    {
        AotValue resolved = value.Evaluate(scope);
        if (resolved.Kind is not (AotValueKind.Integer or AotValueKind.Decimal or AotValueKind.FloatingPoint))
        {
            throw new ScriptException(AotDiagnostics.Scope(
                "AOT5004",
                "Where-Object requires a finite numeric predicate value in the AOT subset.",
                value.Span,
                "non-numeric predicate variable",
                "Assign a finite numeric value before using it in this predicate."));
        }

        return new Filter(property, comparison, resolved, propertySpan);
    }
}
