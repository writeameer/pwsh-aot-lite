namespace PwshAotLite;

// This is a deliberately finite, static subset of PowerShell's common
// parameters. It is extracted from the upstream CommandParameterAst shape
// before the generated cmdlet binder sees port-specific parameters. It is not
// a second binder: aliases, parameter sets, and every non-common parameter
// remain owned by AotCmdletRegistry.BindCommand.
internal enum AotErrorAction { Continue, SilentlyContinue, Stop }

internal sealed record AotCommonParameters(AotErrorAction ErrorAction, bool Verbose, bool Debug)
{
    internal static AotCommonParameters Default { get; } = new(AotErrorAction.Continue, Verbose: false, Debug: false);
}

internal static class AotCommonParameterBinder
{
    private static readonly HashSet<string> UnsupportedCommonParameterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "WarningAction", "WarningVariable", "InformationAction", "InformationVariable",
        "OutVariable", "OutBuffer", "PipelineVariable", "ErrorVariable",
        "WhatIf", "Confirm", "ProgressAction",
    };

    internal static AotCommonParameters Extract(
        string commandName,
        IReadOnlyList<AotCommandArgumentPlan> arguments,
        out IReadOnlyList<AotCommandArgumentPlan> remaining)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        ArgumentNullException.ThrowIfNull(arguments);

        List<AotCommandArgumentPlan> commandArguments = [];
        AotErrorAction errorAction = AotErrorAction.Continue;
        bool verbose = false;
        bool debug = false;
        bool sawErrorAction = false;
        bool sawVerbose = false;
        bool sawDebug = false;

        for (int index = 0; index < arguments.Count; index++)
        {
            AotCommandArgumentPlan argument = arguments[index];
            if (argument is not AotParameterArgumentPlan parameter)
            {
                commandArguments.Add(argument);
                continue;
            }

            if (Is(parameter.Name, "ErrorAction") || Is(parameter.Name, "ea"))
            {
                if (sawErrorAction)
                {
                    throw Duplicate(commandName, "ErrorAction", parameter.Span);
                }

                sawErrorAction = true;
                AotExpressionPlan value = RequireErrorActionValue(commandName, parameter, arguments, ref index);
                errorAction = ParseErrorAction(commandName, value, parameter.AttachedValueSpan ?? parameter.Span);
                continue;
            }

            if (Is(parameter.Name, "Verbose"))
            {
                if (sawVerbose)
                {
                    throw Duplicate(commandName, "Verbose", parameter.Span);
                }

                sawVerbose = true;
                verbose = ParseSwitch(commandName, "Verbose", parameter);
                continue;
            }

            if (Is(parameter.Name, "Debug"))
            {
                if (sawDebug)
                {
                    throw Duplicate(commandName, "Debug", parameter.Span);
                }

                sawDebug = true;
                debug = ParseSwitch(commandName, "Debug", parameter);
                continue;
            }

            if (UnsupportedCommonParameterNames.Contains(parameter.Name))
            {
                throw new ScriptException(AotDiagnostics.Unsupported(
                    $"common parameter '-{parameter.Name}'",
                    parameter.Span,
                    "Only -ErrorAction/-ea (Continue, SilentlyContinue, Stop), bare -Verbose, bare -Debug, and attached Boolean -Verbose/-Debug values are supported."));
            }

            commandArguments.Add(argument);
        }

        remaining = commandArguments;
        return new AotCommonParameters(errorAction, verbose, debug);
    }

    private static AotExpressionPlan RequireErrorActionValue(
        string commandName,
        AotParameterArgumentPlan parameter,
        IReadOnlyList<AotCommandArgumentPlan> arguments,
        ref int index)
    {
        if (parameter.AttachedValue is not null)
        {
            return parameter.AttachedValue;
        }

        if (index + 1 < arguments.Count && arguments[index + 1] is AotValueArgumentPlan value)
        {
            index++;
            return value.Expression;
        }

        throw new ScriptException(AotDiagnostics.Binding(
            "AOT2004",
            $"{commandName} parameter '-ErrorAction' requires a static value.",
            parameter.Span));
    }

    private static AotErrorAction ParseErrorAction(string commandName, AotExpressionPlan value, AotSourceSpan span)
    {
        // ErrorAction controls the static host invocation policy. A literal is
        // required so scope changes cannot covertly change transcript/error
        // behavior at execution time.
        if (value is not AotLiteralExpressionPlan literal || !literal.Value.TryGetString(out string? text))
        {
            throw new ScriptException(AotDiagnostics.Unsupported(
                $"non-literal -ErrorAction value for {commandName}",
                span,
                "Use one direct literal: Continue, SilentlyContinue, or Stop."));
        }

        string action = text!;
        return action switch
        {
            var candidate when candidate.Equals("Continue", StringComparison.OrdinalIgnoreCase) => AotErrorAction.Continue,
            var candidate when candidate.Equals("SilentlyContinue", StringComparison.OrdinalIgnoreCase) => AotErrorAction.SilentlyContinue,
            var candidate when candidate.Equals("Stop", StringComparison.OrdinalIgnoreCase) => AotErrorAction.Stop,
            _ => throw new ScriptException(AotDiagnostics.Unsupported(
                $"-ErrorAction value '{action}'",
                span,
                "Use Continue, SilentlyContinue, or Stop.")),
        };
    }

    private static bool ParseSwitch(string commandName, string name, AotParameterArgumentPlan parameter)
    {
        if (parameter.AttachedValue is null)
        {
            return true;
        }

        // Preserve the AST attachment rule. The following command element is
        // never consumed as a switch value, so `-Verbose $false` remains a
        // normal positional/binding failure instead of silently changing the
        // stream policy.
        if (parameter.AttachedValue is AotLiteralExpressionPlan literal
            && literal.Value.TryGetBoolean(out bool boolean))
        {
            return boolean;
        }

        throw new ScriptException(AotDiagnostics.Unsupported(
            $"non-Boolean attached -{name} value for {commandName}",
            parameter.AttachedValueSpan ?? parameter.Span,
            "Use bare -" + name + " or attach $true/$false directly (for example, -" + name + ":$false)."));
    }

    private static ScriptException Duplicate(string commandName, string name, AotSourceSpan span) =>
        new(AotDiagnostics.Binding("AOT2003", $"{commandName} parameter '-{name}' was specified more than once.", span));

    private static bool Is(string candidate, string expected) => candidate.Equals(expected, StringComparison.OrdinalIgnoreCase);
}
