using System.Globalization;

namespace PwshAotLite;

// A scope belongs to one execution (or an explicitly owned REPL session), not
// to the process.  It is deliberately a small lexical store of closed values:
// it is not PowerShell SessionState and never consults providers, environment
// variables, automatic variables, or CLR objects.
internal sealed class AotScope(AotScope? parent = null)
{
    private readonly Dictionary<string, AotValue> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AotLocalFunctionPlan> _functions = new(StringComparer.OrdinalIgnoreCase);

    internal AotScope? Parent { get; } = parent;

    internal void Set(string name, AotValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _values[name] = value;
    }

    internal bool TryGet(string name, out AotValue value) =>
        _values.TryGetValue(name, out value) || (Parent?.TryGet(name, out value) ?? false);

    // Functions are a separate, closed namespace from values. This is not a
    // SessionState command table: only pre-lowered FunctionDefinitionAst plans
    // can enter it, and lookup remains lexical/dynamic through AotScope.
    internal void DefineFunction(AotLocalFunctionPlan function)
    {
        ArgumentNullException.ThrowIfNull(function);
        _functions[function.Name] = function;
    }

    internal bool TryGetFunction(string name, out AotLocalFunctionPlan function) =>
        _functions.TryGetValue(name, out function!) || (Parent?.TryGetFunction(name, out function!) ?? false);
}

// The AOT scope intentionally has no PowerShell SessionState. Names that
// PowerShell reserves for automatic, AllScope, preference, or host/runtime
// state must therefore never become ordinary lexical variables. This is a
// versioned transcription of every name in pinned upstream
// engine/SpecialVariables.cs, augmented only with the event-action and
// profile automatic variables created outside that catalog. It is shared by
// variable read and assignment lowering.
internal static class AotScopeVariablePolicy
{
    private static readonly HashSet<string> ReservedPowerShellNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Complete SpecialVariables.cs catalog (pinned upstream checkout).
        "MaximumHistoryCount", "MyInvocation", "OFS", "PSStyle", "OutputEncoding", "PSApplicationOutputEncoding", "VerboseHelpErrors",
        "LogEngineHealthEvent", "LogEngineLifecycleEvent", "LogCommandHealthEvent", "LogCommandLifecycleEvent",
        "LogProviderHealthEvent", "LogProviderLifecycleEvent", "LogSettingsEvent", "PSLogUserData",
        "NestedPromptLevel", "CurrentlyExecutingCommand", "PSBoundParameters", "Matches", "LASTEXITCODE", "PSDebugContext", "StackTrace",
        "^", "$", "PSItem", "_", "?", "args", "this", "input", "PSCmdlet", "Error", "env:PATHEXT", "PSEmailServer", "PSDefaultParameterValues",
        "PSScriptRoot", "PSCommandPath", "PSSenderInfo", "foreach", "switch", "PWD", "null", "true", "false", "PSModuleAutoLoadingPreference",
        "IsLinux", "IsMacOS", "IsWindows", "IsCoreCLR",
        "DebugPreference", "ErrorActionPreference", "ProgressPreference", "VerbosePreference", "WarningPreference", "WhatIfPreference", "ConfirmPreference", "InformationPreference",
        "PSNativeCommandUseErrorActionPreference", "PSNativeCommandArgumentPassing", "ErrorView", "PSSessionConfigurationName", "PSSessionApplicationName",
        "ExecutionContext", "HOME", "Host", "PID", "PSCulture", "PSHOME", "PSUICulture", "PSVersionTable", "PSEdition", "ShellId", "EnabledExperimentalFeatures",

        // EventManager.cs event-action scope and the host-created $PROFILE.
        "EventSubscriber", "Event", "Sender", "EventArgs", "PROFILE",
    };

    internal static bool IsReservedPowerShellName(string name) => ReservedPowerShellNames.Contains(name);
}

// Block output is segmented because successive commands may legitimately have
// different table shapes.  Flattening them under one column list would invent
// a formatting contract that PowerShell itself does not have.
internal sealed record AotExecutionOutput(IReadOnlyList<IPipelineRecord> Rows, IReadOnlyList<string> Columns);

internal sealed class AotExecutionResult(IReadOnlyList<AotExecutionOutput> outputs)
{
    internal IReadOnlyList<AotExecutionOutput> Outputs { get; } = outputs;
}

// Function-local control flow is an explicit plan result, never an exception
// that could be mistaken for a runtime failure or escape into the host.
internal enum AotControlFlow { Continue, Return }

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
        context.ThrowIfCancellationRequested();
        List<AotExecutionOutput> outputs = [];
        using IDisposable collector = context.Subscribe(runtimeEvent =>
        {
            if (runtimeEvent is { Kind: AotRuntimeEventKind.Success, Output: { } output })
            {
                outputs.Add(output);
                onOutput?.Invoke(output);
            }
        });
        if (ExecuteInto(context, scope, context.WriteOutput) is not AotControlFlow.Continue)
        {
            throw new InvalidOperationException("A local-function return escaped a root AOT block.");
        }

        return new AotExecutionResult(outputs);
    }

    // Nested statement blocks use their caller's sink so branch output keeps
    // its original order and table shape. A branch is not a synthetic pipeline.
    internal AotControlFlow ExecuteInto(AotExecutionContext context, AotScope scope, Action<AotExecutionOutput> emit)
    {
        foreach (AotStatementPlan statement in Statements)
        {
            context.ThrowIfCancellationRequested();
            if (statement.Execute(context, scope, emit) is AotControlFlow.Return)
            {
                return AotControlFlow.Return;
            }
        }

        return AotControlFlow.Continue;
    }
}

internal abstract class AotStatementPlan
{
    internal abstract AotControlFlow Execute(AotExecutionContext context, AotScope scope, Action<AotExecutionOutput> emit);
}

internal sealed class AotAssignmentPlan(string name, AotExpressionPlan value) : AotStatementPlan
{
    internal override AotControlFlow Execute(AotExecutionContext context, AotScope scope, Action<AotExecutionOutput> emit)
    {
        scope.Set(name, value.Evaluate(scope));
        return AotControlFlow.Continue;
    }
}

// A declaration is an executable statement, so a function exists only after
// its source position runs. This preserves PowerShell's declaration-before-use
// behavior without pre-scanning or installing a dynamic command table.
internal sealed class AotFunctionDefinitionPlan(AotLocalFunctionPlan function) : AotStatementPlan
{
    internal override AotControlFlow Execute(AotExecutionContext context, AotScope scope, Action<AotExecutionOutput> emit)
    {
        scope.DefineFunction(function);
        return AotControlFlow.Continue;
    }
}

internal sealed class AotReturnStatementPlan : AotStatementPlan
{
    internal override AotControlFlow Execute(AotExecutionContext context, AotScope scope, Action<AotExecutionOutput> emit)
    {
        context.ThrowIfCancellationRequested();
        return AotControlFlow.Return;
    }
}

internal sealed record AotLocalFunctionParameter(string Name, AotSourceSpan Span);

// The function plan is immutable and fully lowered ahead of execution. Its
// invocation creates a child of the *caller* scope, not a closure of the
// definition site: reads follow PowerShell's basic dynamic lookup while writes
// and parameters remain local to the invocation.
internal sealed class AotLocalFunctionPlan(
    string name,
    IReadOnlyList<AotLocalFunctionParameter> parameters,
    AotBlockPlan body,
    AotSourceSpan definitionSpan)
{
    internal string Name { get; } = name;
    internal IReadOnlyList<AotLocalFunctionParameter> Parameters { get; } = parameters;
    internal AotSourceSpan DefinitionSpan { get; } = definitionSpan;

    internal void Invoke(
        AotExecutionContext context,
        AotScope callerScope,
        IReadOnlyList<AotValue> arguments,
        AotSourceSpan callSpan,
        Action<AotExecutionOutput> emit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(callerScope);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(emit);

        if (arguments.Count != Parameters.Count)
        {
            throw new ScriptException(AotDiagnostics.Scope(
                "AOT5008",
                $"Function '{Name}' requires exactly {Parameters.Count} positional argument(s), but received {arguments.Count}.",
                callSpan,
                "unsupported function argument count",
                "Supply one closed positional value for each declared function parameter."));
        }

        context.ThrowIfCancellationRequested();
        using IDisposable invocation = context.EnterFunction(Name, callSpan);
        AotScope localScope = new(callerScope);
        for (int index = 0; index < Parameters.Count; index++)
        {
            localScope.Set(Parameters[index].Name, arguments[index]);
        }

        _ = body.ExecuteInto(context, localScope, emit);
    }
}

internal sealed class AotPipelineStatementPlan(
    AotCommandPlan source,
    AotCommandPlan? inputStage,
    AotFilterStagePlan? filter,
    IReadOnlyList<string>? columns,
    bool projected,
    AotSourceSpan? projectionSpan,
    int pipelineLength) : AotStatementPlan
{
    internal override AotControlFlow Execute(AotExecutionContext context, AotScope scope, Action<AotExecutionOutput> emit)
    {
        context.ThrowIfCancellationRequested();
        if (scope.TryGetFunction(source.Name, out AotLocalFunctionPlan function))
        {
            if (pipelineLength != 1)
            {
                throw new ScriptException(AotDiagnostics.Unsupported(
                    "local functions as pipeline sources or stages",
                    source.CommandSpan,
                    "Call a local function as a complete statement; compose supported pipelines inside its body."));
            }

            function.Invoke(context, scope, source.ResolveLocalFunctionArguments(scope), source.CommandSpan, emit);
            return AotControlFlow.Continue;
        }

        PipelinePlan pipeline = BindPipeline(scope);
        context.ThrowIfCancellationRequested();
        emit(new AotExecutionOutput(pipeline.Execute(context), pipeline.Columns));
        return AotControlFlow.Continue;
    }

    internal PipelinePlan BindPipeline(AotScope scope)
    {
        (IAotCmdlet cmdlet, CommandInvocation invocation) = source.Bind(scope);
        AotPipelineInputStage? boundInputStage = null;
        if (inputStage is not null)
        {
            (IAotCmdlet candidate, CommandInvocation inputInvocation) = inputStage.Bind(scope);
            if (candidate is not IAotPipelineInputCmdlet inputCmdlet)
            {
                // The AST lowerer only creates this plan after querying the
                // same static registry. Keep the execution boundary fail-closed
                // if that invariant is ever broken by a future registry edit.
                throw new ScriptException(AotDiagnostics.Binding(
                    "AOT2006",
                    $"{candidate.Descriptor.Name} does not accept static AOT pipeline input.",
                    inputInvocation.SourceSpan));
            }

            boundInputStage = new AotPipelineInputStage(inputCmdlet, inputInvocation);
        }

        Filter? resolvedFilter = filter?.Resolve(scope);
        return new PipelinePlan(
            cmdlet,
            invocation,
            boundInputStage,
            resolvedFilter,
            columns ?? cmdlet.DefaultColumns,
            projected,
            projectionSpan,
            pipelineLength);
    }
}

internal sealed class AotIfStatementPlan(
    IReadOnlyList<AotIfClausePlan> clauses,
    AotBlockPlan? elseBlock) : AotStatementPlan
{
    internal override AotControlFlow Execute(AotExecutionContext context, AotScope scope, Action<AotExecutionOutput> emit)
    {
        foreach (AotIfClausePlan clause in clauses)
        {
            if (clause.Condition.Evaluate(scope))
            {
                // Unlike invoked scriptblocks/functions, PowerShell's if
                // braces do not form a scope. A selected branch can update
                // variables used by following top-level statements.
                return clause.Body.ExecuteInto(context, scope, emit);
            }
        }

        return elseBlock?.ExecuteInto(context, scope, emit) ?? AotControlFlow.Continue;
    }
}

internal sealed record AotIfClausePlan(AotConditionPlan Condition, AotBlockPlan Body);

// Foreach is deliberately an iteration plan over the existing closed list
// value only. It neither asks the CLR to enumerate an arbitrary object nor
// recreates PowerShell's automatic $foreach enumerator. Like upstream
// PowerShell foreach, its loop variable belongs to the surrounding scope and
// retains the final item after successful iteration.
internal sealed class AotForEachStatementPlan(
    string variableName,
    AotExpressionPlan collection,
    AotBlockPlan body) : AotStatementPlan
{
    internal override AotControlFlow Execute(AotExecutionContext context, AotScope scope, Action<AotExecutionOutput> emit)
    {
        AotValue value = collection.Evaluate(scope);
        if (!value.TryGetItems(out IReadOnlyList<AotValue>? items))
        {
            throw AotForEachDiagnostics.UnsupportedCollection(collection.Span);
        }

        foreach (AotValue item in items!)
        {
            context.ThrowIfCancellationRequested();
            scope.Set(variableName, item);
            if (body.ExecuteInto(context, scope, emit) is AotControlFlow.Return)
            {
                return AotControlFlow.Return;
            }
        }

        return AotControlFlow.Continue;
    }
}

internal static class AotForEachDiagnostics
{
    internal static ScriptException UnsupportedCollection(AotSourceSpan span) =>
        new(AotDiagnostics.Scope(
            "AOT5006",
            "Foreach requires a closed list value in the Native AOT subset.",
            span,
            "unsupported foreach collection",
            "Assign a comma-list of supported closed values, then iterate that variable."));
}

// Conditions are their own deliberately closed plan family. They reuse value
// expressions and AotValueComparison but do not add general PowerShell
// truthiness, coercion, or operator evaluation.
internal abstract class AotConditionPlan(AotSourceSpan span)
{
    internal AotSourceSpan Span { get; } = span;
    internal abstract bool Evaluate(AotScope scope);
}

internal sealed class AotBooleanConditionPlan(AotExpressionPlan expression) : AotConditionPlan(expression.Span)
{
    internal override bool Evaluate(AotScope scope)
    {
        AotValue value = expression.Evaluate(scope);
        if (value.TryGetBoolean(out bool boolean))
        {
            return boolean;
        }

        throw AotConditionDiagnostics.Unsupported(
            "If requires a Boolean condition in the Native AOT subset.",
            expression.Span,
            "non-Boolean condition",
            "Use $true/$false or compare two supported closed values with -eq, -ne, -gt, -ge, -lt, or -le.");
    }
}

internal sealed class AotComparisonConditionPlan(
    AotExpressionPlan left,
    AotExpressionPlan right,
    Comparison comparison,
    StringComparison stringComparison,
    AotSourceSpan operatorSpan) : AotConditionPlan(operatorSpan)
{
    internal override bool Evaluate(AotScope scope)
    {
        AotValue leftValue = left.Evaluate(scope);
        AotValue rightValue = right.Evaluate(scope);
        if (!SupportsComparison(leftValue, rightValue, comparison)
            || !AotValueComparison.TryCompare(leftValue, rightValue, stringComparison, out int result))
        {
            throw AotConditionDiagnostics.Unsupported(
                "If comparison operands are not supported by the Native AOT subset.",
                Span,
                "unsupported conditional comparison",
                "Compare two numbers, or use -eq/-ne with values of the same supported closed kind.");
        }

        return comparison switch
        {
            Comparison.GreaterThan => result > 0,
            Comparison.GreaterThanOrEqual => result >= 0,
            Comparison.LessThan => result < 0,
            Comparison.LessThanOrEqual => result <= 0,
            Comparison.Equal => result == 0,
            Comparison.NotEqual => result != 0,
            _ => throw new InvalidOperationException($"Unknown comparison '{comparison}'."),
        };
    }

    private static bool SupportsComparison(AotValue left, AotValue right, Comparison comparison)
    {
        bool leftNumber = left.Kind is AotValueKind.Integer or AotValueKind.Decimal or AotValueKind.FloatingPoint;
        bool rightNumber = right.Kind is AotValueKind.Integer or AotValueKind.Decimal or AotValueKind.FloatingPoint;
        if (leftNumber && rightNumber)
        {
            return true;
        }

        if (comparison is not (Comparison.Equal or Comparison.NotEqual))
        {
            return false;
        }

        return left.Kind switch
        {
            AotValueKind.Null => right.Kind == AotValueKind.Null,
            AotValueKind.Boolean => right.Kind == AotValueKind.Boolean,
            AotValueKind.String => right.Kind == AotValueKind.String,
            _ => false,
        };
    }
}

internal static class AotConditionDiagnostics
{
    internal static ScriptException Unsupported(string message, AotSourceSpan span, string label, string help) =>
        new(AotDiagnostics.Scope("AOT5005", message, span, label, help));
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

    // Native cmdlets intentionally receive flattened syntax atoms through the
    // generated-metadata binder. Local-function parameters instead retain one
    // evaluated closed value per source argument; they never stringify and
    // reparse values or grow a second general command binder.
    internal IReadOnlyList<AotValue> ResolveLocalFunctionArguments(AotScope scope)
    {
        List<AotValue> resolved = [];
        foreach (AotCommandArgumentPlan argument in Arguments)
        {
            if (argument is not AotValueArgumentPlan value)
            {
                throw new ScriptException(AotDiagnostics.Unsupported(
                    "named local-function arguments",
                    argument.Span,
                    "Use positional closed values for this Native AOT local-function subset."));
            }

            resolved.Add(value.Expression.Evaluate(scope));
        }

        return resolved;
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
