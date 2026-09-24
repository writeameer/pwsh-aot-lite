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
// different table shapes. Every segment carries its canonical record batch;
// compatibility rows are created only by the terminal projector.
//
// Presentation is a static cmdlet/pipeline-plan contract, never an inference
// from the runtime record implementation. Prose is intentionally narrow: a
// direct, untransformed pipeline whose terminal cmdlet declares prose. Once a
// structural transform or typed input stage participates, terminal rendering
// reverts to an ordinary table.
internal enum AotTerminalPresentation { Table, Prose }

// A table layout is compiled data supplied by a reviewed port. It is not a
// format/type lookup facility: the execution record shape remains the sole
// pipeline contract, while the terminal may apply an attributed fixed view to
// a direct, untransformed command result.
internal enum AotTableAlignment { Left, Right }

// The finite set of source-attributed terminal projections. A command may not
// supply a delegate/script formatter here; new entries require an upstream
// format-data citation and review.
internal enum AotTableValueFormat { Default, FileSystemLastWriteTime }

internal sealed record AotTableColumn
{
    internal AotTableColumn(
        string field,
        string header,
        AotTableAlignment alignment = AotTableAlignment.Left,
        int minimumWidth = 0,
        AotTableValueFormat valueFormat = AotTableValueFormat.Default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentException.ThrowIfNullOrWhiteSpace(header);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumWidth);
        Field = field;
        Header = header;
        Alignment = alignment;
        MinimumWidth = minimumWidth;
        ValueFormat = valueFormat;
    }

    internal string Field { get; }
    internal string Header { get; }
    internal AotTableAlignment Alignment { get; }
    internal int MinimumWidth { get; }
    internal AotTableValueFormat ValueFormat { get; }
}

internal sealed record AotTableGroup
{
    internal AotTableGroup(string field, string header)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentException.ThrowIfNullOrWhiteSpace(header);
        Field = field;
        Header = header;
    }

    internal string Field { get; }
    internal string Header { get; }
}

internal sealed class AotTableLayout
{
    internal AotTableLayout(
        IReadOnlyList<AotTableColumn> columns,
        AotTableGroup? group = null,
        string columnSeparator = "  ",
        int leadingBlankLines = 0,
        int trailingBlankLines = 0)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count == 0 || columns.Any(static column => column is null))
        {
            throw new ArgumentException("A static terminal table layout requires one or more declared columns.", nameof(columns));
        }

        if (columns.Select(static column => column.Field).Distinct(StringComparer.OrdinalIgnoreCase).Count() != columns.Count)
        {
            throw new ArgumentException("A static terminal table layout cannot declare the same field twice.", nameof(columns));
        }

        if (string.IsNullOrEmpty(columnSeparator) || columnSeparator.Any(static character => character != ' '))
        {
            throw new ArgumentException("A static terminal table layout requires a non-empty literal-space column separator.", nameof(columnSeparator));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(leadingBlankLines);
        ArgumentOutOfRangeException.ThrowIfNegative(trailingBlankLines);

        Columns = columns.ToArray();
        Group = group;
        ColumnSeparator = columnSeparator;
        LeadingBlankLines = leadingBlankLines;
        TrailingBlankLines = trailingBlankLines;
    }

    internal IReadOnlyList<AotTableColumn> Columns { get; }
    internal AotTableGroup? Group { get; }
    // Table controls own this fixed, source-attributed presentation fact.
    // It cannot be a format script or a runtime-selectable setting.
    internal string ColumnSeparator { get; }
    // A source-attributed table-control literal only.  This cannot become a
    // formatter/global terminal policy; each reviewed layout must opt in.
    internal int LeadingBlankLines { get; }
    // Same static table-control fact as LeadingBlankLines. A port must opt in;
    // there is no generic post-table whitespace policy.
    internal int TrailingBlankLines { get; }
}

internal sealed record AotExecutionOutput(
    AotRecordBatch Batch,
    AotRecordShape Shape,
    AotTerminalPresentation Presentation = AotTerminalPresentation.Table,
    AotSourceSpan? ShapeSpan = null,
    AotTableLayout? TableLayout = null)
{
    internal IReadOnlyList<string> Columns => Shape.Fields;
    internal IReadOnlyList<AotRecord> Rows => Batch.Records;

    internal static AotExecutionOutput FromTypedRows(
        AotExecutionContext context,
        IReadOnlyList<IPipelineRecord> rows,
        IReadOnlyList<string> columns,
        AotTerminalPresentation presentation = AotTerminalPresentation.Table,
        AotSourceSpan? shapeSpan = null) =>
        new(AotRecordBatch.FromTypedRows(context, rows, shapeSpan), new AotRecordShape(columns), presentation, shapeSpan);
}

// A producer captures executable typed output, not rendered terminal text.
// A missing default shape is intentional: callers must select an explicit
// shape before rendering heterogeneous segments.
internal sealed record AotFunctionProducerOutput(AotRecordBatch Batch, IReadOnlyList<string>? DefaultColumns);

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

internal sealed record AotLocalFunctionParameter(string Name, AotSourceSpan Span, AotExpressionPlan? DefaultValue);

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
        IReadOnlyList<AotCommandArgumentPlan> arguments,
        AotSourceSpan callSpan,
        Action<AotExecutionOutput> emit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(callerScope);
        ArgumentNullException.ThrowIfNull(emit);
        context.ThrowIfCancellationRequested();
        using IDisposable invocation = context.EnterFunction(Name, callSpan);
        AotScope localScope = BindInvocationScope(callerScope, arguments, callSpan);
        _ = body.ExecuteInto(context, localScope, emit);
    }

    // A function producer executes the already-lowered static body into a
    // private typed-output collector. It never captures formatted tables or
    // arbitrary objects. The ordered result crosses the generic boundary once.
    internal AotFunctionProducerOutput InvokeAsPipelineProducer(
        AotExecutionContext context,
        AotScope callerScope,
        IReadOnlyList<AotCommandArgumentPlan> arguments,
        AotSourceSpan callSpan)
    {
        context.ThrowIfCancellationRequested();
        using IDisposable invocation = context.EnterFunction(Name, callSpan);
        AotScope localScope = BindInvocationScope(callerScope, arguments, callSpan);
        List<AotRecord> records = [];
        IReadOnlyList<string>? commonColumns = null;
        bool hasOutput = false;
        _ = body.ExecuteInto(context, localScope, output =>
        {
            context.ThrowIfCancellationRequested();
            records.AddRange(output.Batch.Records);
            if (!hasOutput)
            {
                commonColumns = output.Columns;
                hasOutput = true;
            }
            else if (commonColumns is not null && !commonColumns.SequenceEqual(output.Columns, StringComparer.OrdinalIgnoreCase))
            {
                commonColumns = null;
            }
        });

        return new AotFunctionProducerOutput(new AotRecordBatch(records), commonColumns);
    }

    private AotScope BindInvocationScope(
        AotScope callerScope,
        IReadOnlyList<AotCommandArgumentPlan> arguments,
        AotSourceSpan callSpan)
    {
        ArgumentNullException.ThrowIfNull(callerScope);
        ArgumentNullException.ThrowIfNull(arguments);
        AotScope localScope = new(callerScope);
        AotLocalFunctionArgumentBinder.Bind(Name, Parameters, arguments, callerScope, localScope, callSpan);
        return localScope;
    }
}

internal sealed class AotPipelineStatementPlan(
    AotCommandPlan source,
    AotCommandPlan? inputStage,
    IReadOnlyList<AotPipelineTailStagePlan> tailStages,
    int pipelineLength) : AotStatementPlan
{
    internal override AotControlFlow Execute(AotExecutionContext context, AotScope scope, Action<AotExecutionOutput> emit)
    {
        context.ThrowIfCancellationRequested();
        if (scope.TryGetFunction(source.Name, out AotLocalFunctionPlan function))
        {
            if (inputStage is not null)
            {
                throw new ScriptException(AotDiagnostics.Unsupported(
                    "local functions followed by native typed-input pipeline stages",
                    source.CommandSpan,
                    "Use a native source command for typed-input stages; local-function composition supports only the existing Where-Object and Select-Object tail."));
            }

            if (tailStages.Count == 0)
            {
                function.Invoke(context, scope, source.Arguments, source.CommandSpan, emit);
                return AotControlFlow.Continue;
            }

            AotFunctionProducerOutput seed = function.InvokeAsPipelineProducer(context, scope, source.Arguments, source.CommandSpan);
            IReadOnlyList<AotRecordTransform> transforms = ResolveTail(scope);
            AotRecordShape outputShape = ResolveOutputShape(seed.DefaultColumns, source.CommandSpan);
            AotRecordBatch batch = seed.Batch.Apply(context, transforms);
            emit(new AotExecutionOutput(batch, outputShape, AotTerminalPresentation.Table, source.CommandSpan));
            return AotControlFlow.Continue;
        }

        PipelinePlan pipeline = BindPipeline(scope);
        context.ThrowIfCancellationRequested();
        emit(new AotExecutionOutput(pipeline.ExecuteBatch(context), pipeline.Shape, pipeline.TerminalPresentation, source.CommandSpan, pipeline.TerminalTableLayout));
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

        return new PipelinePlan(
            cmdlet,
            invocation,
            boundInputStage,
            ResolveTail(scope),
            ResolveOutputShape(cmdlet.DefaultColumns, source.CommandSpan),
            source.CommandSpan,
            pipelineLength);
    }

    private IReadOnlyList<AotRecordTransform> ResolveTail(AotScope scope) =>
        tailStages.Select(stage => stage.Resolve(scope)).ToArray();

    private AotRecordShape ResolveOutputShape(IReadOnlyList<string>? sourceColumns, AotSourceSpan callSpan)
    {
        AotProjectionTailStagePlan? projection = tailStages.OfType<AotProjectionTailStagePlan>().LastOrDefault();
        if (projection is not null)
        {
            return projection.Shape;
        }

        if (sourceColumns is null)
        {
            throw new ScriptException(AotDiagnostics.Unsupported(
                    "a function producer with heterogeneous output shapes and no last direct Select-Object",
                    callSpan,
                    "Add a last direct Select-Object to establish one output shape."));
        }

        return new AotRecordShape(sourceColumns);
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

internal sealed class AotListExpressionPlan : AotExpressionPlan
{
    internal AotListExpressionPlan(IReadOnlyList<AotExpressionPlan> elements, AotSourceSpan span) : base(span) => Elements = elements;
    internal IReadOnlyList<AotExpressionPlan> Elements { get; }
    internal override AotValue Evaluate(AotScope scope) => AotValue.FromList(Elements.Select(element => element.Evaluate(scope)));
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
    internal AotParameterArgumentPlan(
        string name,
        AotSourceSpan span,
        AotExpressionPlan? attachedValue = null,
        AotSourceSpan? attachedValueSpan = null,
        bool preserveAsJ1Group = false)
        : base(span)
    {
        Name = name;
        AttachedValue = attachedValue;
        AttachedValueSpan = attachedValueSpan;
        PreserveAsJ1Group = preserveAsJ1Group;
    }

    internal string Name { get; }
    // CommandParameterAst.Argument is not interchangeable with the next
    // command element: `-Verbose:$false` is a switch value, whereas
    // `-Verbose $false` is a bare switch followed by an ordinary argument.
    // Retaining that upstream association lets static common-parameter
    // extraction make a truthful decision without text reparsing.
    internal AotExpressionPlan? AttachedValue { get; }
    internal AotSourceSpan? AttachedValueSpan { get; }
    internal bool PreserveAsJ1Group { get; }

    internal override void AppendResolved(AotScope scope, List<CommandSyntaxAtom> atoms)
    {
        atoms.Add(new CommandSyntaxAtom(Name, IsParameter: true, Span));
        if (AttachedValue is not null)
        {
            bool? directBoolean = AttachedValue is AotLiteralExpressionPlan literal
                && literal.Value.TryGetBoolean(out bool value)
                    ? value
                    : null;
            if (PreserveAsJ1Group)
            {
                AotCommandArgumentConverter.Append(AttachedValue.Evaluate(scope), AttachedValueSpan ?? Span, atoms,
                    isAttachedParameterValue: true, attachedDirectBoolean: directBoolean,
                    groupId: AotCommandValueGroupPlan.AllocateGroupId());
            }
            else if (AttachedValue is AotListExpressionPlan list)
            {
                int groupId = AotCommandValueGroupPlan.AllocateGroupId();
                foreach (AotExpressionPlan element in list.Elements)
                {
                    AotCommandArgumentConverter.Append(element.Evaluate(scope), element.Span, atoms,
                        isAttachedParameterValue: true, attachedDirectBoolean: directBoolean, groupId: groupId);
                }
            }
            else
            {
                AotCommandArgumentConverter.Append(
                    AttachedValue.Evaluate(scope),
                    AttachedValueSpan ?? Span,
                    atoms,
                    isAttachedParameterValue: true,
                    attachedDirectBoolean: directBoolean);
            }
        }
    }
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

// Preserves one upstream ArrayLiteralAst command element as one atomic source
// group for the sole J1 sequential binding mode.  It never reparses text and
// legacy descriptors continue to receive their historical flattened atoms.
internal sealed class AotCommandValueGroupPlan(IReadOnlyList<AotExpressionPlan> expressions, AotSourceSpan span) : AotCommandArgumentPlan(span)
{
    private static int _nextGroupId;
    internal static int AllocateGroupId() => Interlocked.Increment(ref _nextGroupId);

    internal override void AppendResolved(AotScope scope, List<CommandSyntaxAtom> atoms)
    {
        int groupId = AllocateGroupId();
        foreach (AotExpressionPlan expression in expressions)
        {
            AotCommandArgumentConverter.Append(expression.Evaluate(scope), expression.Span, atoms, groupId: groupId);
        }
    }
}

internal sealed class AotCommandPlan(string name, AotSourceSpan commandSpan, IReadOnlyList<AotCommandArgumentPlan> arguments)
{
    internal string Name { get; } = name;
    internal AotSourceSpan CommandSpan { get; } = commandSpan;
    internal IReadOnlyList<AotCommandArgumentPlan> Arguments { get; } = arguments;

    internal (IAotCmdlet Cmdlet, CommandInvocation Invocation) Bind(AotScope scope)
    {
        // The static executable registry, rather than generated catalog
        // metadata, admits common extraction. Unknown/catalog-only commands
        // must fail through the normal AOT2001 binder path unchanged.
        AotCommonParameters commonParameters = AotCommonParameters.Default;
        IReadOnlyList<AotCommandArgumentPlan> commandArguments = Arguments;
        if (AotCmdletRegistry.IsStaticNativeCommand(Name))
        {
            commonParameters = AotCommonParameterBinder.Extract(Name, Arguments, out commandArguments);
        }
        List<CommandSyntaxAtom> atoms = [];
        foreach (AotCommandArgumentPlan argument in commandArguments)
        {
            argument.AppendResolved(scope, atoms);
        }

        try
        {
            (IAotCmdlet cmdlet, CommandInvocation invocation) = AotCmdletRegistry.BindCommand(Name, atoms, CommandSpan);
            return (cmdlet, invocation.WithCommonParameters(commonParameters));
        }
        catch (ScriptException exception)
        {
            throw exception.Diagnostic.Span is null ? exception.WithSpan(CommandSpan) : exception;
        }
    }

}

// Local functions need a small signature binder, but never the cmdlet binder:
// it binds only parser-derived header names to closed AotValue arguments and
// does not accept aliases, abbreviations, parameter sets, conversion, splats,
// or text reparsing. AotCmdletRegistry remains the sole cmdlet binder.
internal static class AotLocalFunctionArgumentBinder
{
    internal static void Bind(
        string functionName,
        IReadOnlyList<AotLocalFunctionParameter> parameters,
        IReadOnlyList<AotCommandArgumentPlan> arguments,
        AotScope callerScope,
        AotScope localScope,
        AotSourceSpan callSpan)
    {
        Dictionary<string, AotValue> supplied = new(StringComparer.OrdinalIgnoreCase);
        int nextPositional = 0;
        bool sawNamed = false;

        for (int index = 0; index < arguments.Count; index++)
        {
            AotCommandArgumentPlan argument = arguments[index];
            if (argument is AotParameterArgumentPlan named)
            {
                sawNamed = true;
                AotLocalFunctionParameter? parameter = parameters.FirstOrDefault(candidate =>
                    candidate.Name.Equals(named.Name, StringComparison.OrdinalIgnoreCase));
                if (parameter is null)
                {
                    throw new ScriptException(AotDiagnostics.Scope(
                        "AOT5009",
                        $"Function '{functionName}' does not declare parameter '-{named.Name}'.",
                        named.Span,
                        "unknown local-function parameter",
                        "Use an exact parameter name declared by the local function."));
                }

                if (supplied.ContainsKey(parameter.Name))
                {
                    throw new ScriptException(AotDiagnostics.Scope(
                        "AOT5010",
                        $"Function '{functionName}' parameter '-{parameter.Name}' was specified more than once.",
                        named.Span,
                        "duplicate local-function parameter",
                        "Supply each local-function parameter at most once."));
                }

                if (named.AttachedValue is not null)
                {
                    supplied.Add(parameter.Name, named.AttachedValue.Evaluate(callerScope));
                    continue;
                }

                if (++index >= arguments.Count || arguments[index] is not AotValueArgumentPlan value)
                {
                    throw new ScriptException(AotDiagnostics.Scope(
                        "AOT5011",
                        $"Function '{functionName}' parameter '-{parameter.Name}' requires a closed value.",
                        named.Span,
                        "local-function parameter requires a value",
                        "Supply one closed value immediately after the named parameter."));
                }

                supplied.Add(parameter.Name, value.Expression.Evaluate(callerScope));
                continue;
            }

            if (argument is not AotValueArgumentPlan positional)
            {
                throw new InvalidOperationException("A local-function call contains an unknown pre-lowered argument plan.");
            }

            if (sawNamed)
            {
                throw new ScriptException(AotDiagnostics.Scope(
                    "AOT5012",
                    $"Function '{functionName}' does not permit positional values after a named parameter in the AOT subset.",
                    positional.Span,
                    "ambiguous local-function argument order",
                    "Place positional values before named parameters."));
            }

            while (nextPositional < parameters.Count && supplied.ContainsKey(parameters[nextPositional].Name))
            {
                nextPositional++;
            }

            if (nextPositional >= parameters.Count)
            {
                throw new ScriptException(AotDiagnostics.Scope(
                    "AOT5008",
                    $"Function '{functionName}' received more positional arguments than its declared parameters.",
                    positional.Span,
                    "unsupported function argument count",
                    "Supply one closed positional value for each declared function parameter."));
            }

            supplied.Add(parameters[nextPositional++].Name, positional.Expression.Evaluate(callerScope));
        }

        foreach (AotLocalFunctionParameter parameter in parameters)
        {
            if (supplied.TryGetValue(parameter.Name, out AotValue value))
            {
                localScope.Set(parameter.Name, value);
                continue;
            }

            if (parameter.DefaultValue is not null)
            {
                localScope.Set(parameter.Name, parameter.DefaultValue.Evaluate(localScope));
                continue;
            }

            throw new ScriptException(AotDiagnostics.Scope(
                "AOT5008",
                $"Function '{functionName}' requires a value for parameter '-{parameter.Name}'.",
                callSpan,
                "missing local-function parameter",
                "Supply one closed positional or exact named value for each required parameter."));
        }
    }
}

// This is the sole explicit conversion from language values into the existing
// string-valued generated-metadata binder.  Values never become parameter
// tokens; only AST parameter nodes create parameter atoms.
internal static class AotCommandArgumentConverter
{
    internal static void Append(
        AotValue value,
        AotSourceSpan span,
        List<CommandSyntaxAtom> atoms,
        bool isAttachedParameterValue = false,
        bool? attachedDirectBoolean = null,
        int? groupId = null)
    {
        if (value.TryGetItems(out IReadOnlyList<AotValue>? items))
        {
            foreach (AotValue item in items!)
            {
                Append(item, span, atoms, isAttachedParameterValue, attachedDirectBoolean, groupId);
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

        atoms.Add(new CommandSyntaxAtom(
            text,
            IsParameter: false,
            span,
            isAttachedParameterValue,
            attachedDirectBoolean,
            groupId));
    }
}

internal abstract class AotFilterStagePlan
{
    internal abstract Filter Resolve(AotScope scope);
}

internal abstract class AotPipelineTailStagePlan
{
    internal abstract AotRecordTransform Resolve(AotScope scope);
}

internal sealed class AotFilterTailStagePlan(AotFilterStagePlan filter, AotSourceSpan? span) : AotPipelineTailStagePlan
{
    internal override AotRecordTransform Resolve(AotScope scope) => new AotRecordFilterTransform(filter.Resolve(scope), span);
}

internal sealed class AotProjectionTailStagePlan(IReadOnlyList<string> columns, AotSourceSpan? span) : AotPipelineTailStagePlan
{
    internal IReadOnlyList<string> Columns { get; } = columns;

    internal AotRecordShape Shape
    {
        get
        {
            ValidateColumns();
            return new AotRecordShape(Columns);
        }
    }

    internal override AotRecordTransform Resolve(AotScope scope)
    {
        ValidateColumns();
        return new AotRecordProjectionTransform(Columns, span);
    }

    private void ValidateColumns()
    {
        HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);
        foreach (string column in Columns)
        {
            if (!selected.Add(column))
            {
                throw new ScriptException(AotDiagnostics.Runtime(
                    "AOT4007",
                    "Select-Object does not permit duplicate fields that differ only by case in the AOT subset.",
                    span,
                    "duplicate projection field",
                    "Select each field only once."));
            }
        }
    }
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
