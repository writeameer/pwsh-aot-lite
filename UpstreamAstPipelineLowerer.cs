using System.Globalization;
using System.Management.Automation.Language;

namespace PwshAotLite;

// The executable syntax entry point. It consumes only the pinned upstream AST
// and lowers a deliberately reviewed subset into an explicit block plan.
// Parser acceptance never implies execution support.
internal static class UpstreamAstPipelineLowerer
{
    // Compatibility entry points used by established cmdlet tests. They retain
    // the old one-pipeline shape, while the host itself compiles a block plan.
    internal static (IAotCmdlet Cmdlet, CommandInvocation Invocation) BindSingleCommand(string script)
    {
        AotParseResult parseResult = AotScriptParser.Parse(script);
        ThrowIfParseFailed(parseResult);
        PipelineAst pipeline = RequireOnePipeline(parseResult.Ast);
        if (pipeline.PipelineElements.Count != 1 || pipeline.PipelineElements[0] is not CommandAst command)
        {
            throw Unsupported("one direct command", pipeline.Extent);
        }

        return LowerCommand(command).Bind(new AotScope());
    }

    internal static PipelinePlan Parse(string script)
    {
        AotParseResult parseResult = AotScriptParser.Parse(script);
        ThrowIfParseFailed(parseResult);
        return LowerPipeline(RequireOnePipeline(parseResult.Ast)).BindPipeline(new AotScope());
    }

    internal static AotBlockPlan LowerBlock(AotParseResult parseResult)
    {
        ScriptBlockAst ast = parseResult.Ast;
        ValidateTopLevelBlock(ast);
        List<AotStatementPlan> statements = [];
        foreach (StatementAst statement in ast.EndBlock!.Statements)
        {
            switch (statement)
            {
                case AssignmentStatementAst assignment:
                    statements.Add(LowerAssignment(assignment));
                    break;
                case PipelineAst pipeline:
                    statements.Add(LowerPipeline(pipeline));
                    break;
                default:
                    throw Unsupported($"statement '{statement.GetType().Name}'", statement.Extent);
            }
        }

        return new AotBlockPlan(statements);
    }

    private static void ValidateTopLevelBlock(ScriptBlockAst ast)
    {
        if (ast.UsingStatements?.Count is > 0
            || ast.Attributes?.Count is > 0
            || ast.ParamBlock is not null
            || ast.BeginBlock is not null
            || ast.ProcessBlock is not null
            || ast.DynamicParamBlock is not null
            || ast.CleanBlock is not null
            || ast.EndBlock is null
            || !ast.EndBlock.Unnamed
            || ast.EndBlock.Traps?.Count is > 0)
        {
            throw Unsupported("only an unnamed top-level statement block", ast.Extent);
        }
    }

    private static PipelineAst RequireOnePipeline(ScriptBlockAst ast)
    {
        ValidateTopLevelBlock(ast);
        if (ast.EndBlock!.Statements.Count != 1 || ast.EndBlock.Statements[0] is not PipelineAst pipeline)
        {
            throw Unsupported("only one top-level command pipeline", ast.Extent);
        }

        return pipeline;
    }

    private static AotAssignmentPlan LowerAssignment(AssignmentStatementAst assignment)
    {
        if (assignment.Operator != TokenKind.Equals)
        {
            throw Unsupported("assignment operators other than '='", assignment.ErrorPosition);
        }

        if (assignment.Left is not VariableExpressionAst variable)
        {
            throw Unsupported("assignment targets other than one variable", assignment.Left.Extent);
        }

        string name = GetAssignableVariableName(variable);
        if (assignment.Right is not CommandExpressionAst { Expression: ExpressionAst expression })
        {
            throw Unsupported("assignment from commands or pipelines", assignment.Right.Extent);
        }

        return new AotAssignmentPlan(name, LowerExpression(expression));
    }

    private static AotPipelineStatementPlan LowerPipeline(PipelineAst pipeline)
    {
        if (pipeline.Background)
        {
            throw Unsupported("background pipelines", pipeline.Extent);
        }

        if (pipeline.PipelineElements.Count is < 1 or > 3)
        {
            throw Unsupported("the structural subset accepts one source command plus Where-Object and Select-Object only", pipeline.Extent);
        }

        AotCommandPlan? source = null;
        AotFilterStagePlan? filter = null;
        IReadOnlyList<string>? columns = null;
        AotSourceSpan? projectionSpan = null;
        bool projected = false;

        for (int index = 0; index < pipeline.PipelineElements.Count; index++)
        {
            if (pipeline.PipelineElements[index] is not CommandAst command)
            {
                throw Unsupported("pipeline elements must be command ASTs", pipeline.PipelineElements[index].Extent);
            }

            AotCommandPlan lowered = LowerCommand(command);
            if (index == 0)
            {
                source = lowered;
                continue;
            }

            if (lowered.Name.Equals("Where-Object", StringComparison.OrdinalIgnoreCase))
            {
                if (filter is not null
                    || projected
                    || lowered.Arguments.Count != 3
                    || lowered.Arguments[0] is not AotValueArgumentPlan propertyArgument
                    || lowered.Arguments[1] is not AotParameterArgumentPlan operatorArgument
                    || lowered.Arguments[2] is not AotValueArgumentPlan valueArgument
                    || !TryGetDirectString(propertyArgument.Expression, out string? property))
                {
                    throw Unsupported("Where-Object accepts one direct property/operator/value predicate in this subset", command.Extent);
                }

                string comparisonText = "-" + operatorArgument.Name;
                AotSourceSpan valueSpan = valueArgument.Span;
                AotSourceSpan propertySpan = propertyArgument.Span;
                AotSourceSpan operatorSpan = operatorArgument.Span;
                if (TryGetDirectScalarText(valueArgument.Expression, out string? literalValue))
                {
                    filter = new AotLiteralFilterStagePlan(ScriptParser.ParseFilterArguments(
                    [property!, comparisonText, literalValue!],
                    valueSpan,
                    propertySpan,
                    operatorSpan));
                }
                else
                {
                    Comparison comparison = ScriptParser.ParseComparison(comparisonText, operatorSpan);
                    filter = new AotVariableFilterStagePlan(property!, comparison, valueArgument.Expression, propertySpan);
                }

                continue;
            }

            if (lowered.Name.Equals("Select-Object", StringComparison.OrdinalIgnoreCase))
            {
                if (projected || lowered.Arguments.Any(static argument => argument is AotParameterArgumentPlan))
                {
                    throw Unsupported("Select-Object accepts one direct property projection only in this subset", command.Extent);
                }

                if (lowered.Arguments.Count == 0)
                {
                    columns = ScriptParser.ParseColumns([], lowered.CommandSpan);
                }
                else
                {
                    string[] directColumns = lowered.Arguments
                        .OfType<AotValueArgumentPlan>()
                        .Select(argument => TryGetDirectString(argument.Expression, out string? column)
                            ? column!
                            : throw Unsupported("variable or expression Select-Object columns", argument.Span))
                        .ToArray();
                    columns = ScriptParser.ParseColumns(directColumns, lowered.Arguments[0].Span);
                    projectionSpan = lowered.Arguments[0].Span;
                }

                projected = true;
                continue;
            }

            throw Unsupported($"pipeline stage '{lowered.Name}'", command.Extent);
        }

        if (source is null)
        {
            throw Unsupported("a source command is required", pipeline.Extent);
        }

        return new AotPipelineStatementPlan(source, filter, columns, projected, projectionSpan);
    }

    private static AotCommandPlan LowerCommand(CommandAst command)
    {
        if (command.Redirections.Count > 0)
        {
            throw Unsupported("redirections", command.Extent);
        }

        if (command.InvocationOperator != TokenKind.Unknown)
        {
            throw Unsupported("invocation operators", command.Extent);
        }

        if (command.CommandElements.Count == 0 || command.CommandElements[0] is not StringConstantExpressionAst commandName)
        {
            throw Unsupported("command expressions", command.Extent);
        }

        List<AotCommandArgumentPlan> arguments = [];
        foreach (CommandElementAst element in command.CommandElements.Skip(1))
        {
            if (element is CommandParameterAst parameter)
            {
                arguments.Add(new AotParameterArgumentPlan(parameter.ParameterName, AotScriptParser.ToSpan(parameter.Extent)));
                if (parameter.Argument is not null)
                {
                    AddCommandValueArguments(parameter.Argument, arguments);
                }

                continue;
            }

            if (element is ExpressionAst expression)
            {
                AddCommandValueArguments(expression, arguments);
                continue;
            }

            throw Unsupported($"command element '{element.GetType().Name}'", element.Extent);
        }

        return new AotCommandPlan(commandName.Value, AotScriptParser.ToSpan(commandName.Extent), arguments);
    }

    // Comma-separated command arguments are already distinct upstream AST
    // elements semantically. Preserve that shape rather than asking the
    // binder to split text; assignment RHS arrays remain one closed AotValue.
    private static void AddCommandValueArguments(ExpressionAst expression, List<AotCommandArgumentPlan> arguments)
    {
        if (expression is ArrayLiteralAst array)
        {
            foreach (ExpressionAst element in array.Elements)
            {
                AddCommandValueArguments(element, arguments);
            }

            return;
        }

        arguments.Add(new AotValueArgumentPlan(LowerExpression(expression)));
    }

    private static AotExpressionPlan LowerExpression(ExpressionAst expression) => expression switch
    {
        StringConstantExpressionAst text => new AotLiteralExpressionPlan(AotValue.FromString(text.Value), AotScriptParser.ToSpan(text.Extent)),
        ConstantExpressionAst constant => new AotLiteralExpressionPlan(ToAotLiteral(constant.Value, constant.Extent), AotScriptParser.ToSpan(constant.Extent)),
        VariableExpressionAst variable => LowerVariable(variable),
        ArrayLiteralAst array => new AotListExpressionPlan(array.Elements.Select(LowerExpression).ToArray(), AotScriptParser.ToSpan(array.Extent)),
        _ => throw Unsupported($"expression '{expression.GetType().Name}'", expression.Extent),
    };

    private static AotExpressionPlan LowerVariable(VariableExpressionAst variable)
    {
        if (variable.Splatted)
        {
            throw UnsupportedVariable("splatting", variable.Extent, "Pass a direct variable value after an explicit parameter instead.");
        }

        var path = variable.VariablePath;
        if (!path.IsUnscopedVariable)
        {
            throw UnsupportedVariable($"scoped or drive-qualified variable '${path.UserPath}'", variable.Extent, "Use an unscoped variable assigned within this script.");
        }

        string name = path.UserPath;
        if (name.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return new AotLiteralExpressionPlan(AotValue.FromBoolean(true), AotScriptParser.ToSpan(variable.Extent));
        }

        if (name.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return new AotLiteralExpressionPlan(AotValue.FromBoolean(false), AotScriptParser.ToSpan(variable.Extent));
        }

        if (name.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return new AotLiteralExpressionPlan(AotValue.Null, AotScriptParser.ToSpan(variable.Extent));
        }

        if (IsAutomaticVariable(name))
        {
            throw UnsupportedVariable($"automatic variable '${name}'", variable.Extent, "Use a variable explicitly assigned in this AOT script.");
        }

        return new AotVariableExpressionPlan(name, AotScriptParser.ToSpan(variable.Extent));
    }

    private static string GetAssignableVariableName(VariableExpressionAst variable)
    {
        if (variable.Splatted)
        {
            throw UnsupportedVariable("splat assignment", variable.Extent, "Assign one unscoped variable at a time.");
        }

        var path = variable.VariablePath;
        if (!path.IsUnscopedVariable || IsAutomaticVariable(path.UserPath)
            || path.UserPath.Equals("true", StringComparison.OrdinalIgnoreCase)
            || path.UserPath.Equals("false", StringComparison.OrdinalIgnoreCase)
            || path.UserPath.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            throw UnsupportedVariable($"assignment target '${path.UserPath}'", variable.Extent, "Assign one ordinary unscoped variable at a time.");
        }

        return path.UserPath;
    }

    private static bool IsAutomaticVariable(string name) => name.Equals("_", StringComparison.OrdinalIgnoreCase)
        || name.Equals("PSItem", StringComparison.OrdinalIgnoreCase)
        || name.Equals("args", StringComparison.OrdinalIgnoreCase)
        || name.Equals("input", StringComparison.OrdinalIgnoreCase)
        || name.Equals("this", StringComparison.OrdinalIgnoreCase);

    private static AotValue ToAotLiteral(object? value, IScriptExtent extent) => value switch
    {
        null => AotValue.Null,
        bool boolean => AotValue.FromBoolean(boolean),
        char character => AotValue.FromString(character.ToString()),
        sbyte number => AotValue.FromInteger(number),
        byte number => AotValue.FromInteger(number),
        short number => AotValue.FromInteger(number),
        ushort number => AotValue.FromInteger(number),
        int number => AotValue.FromInteger(number),
        uint number => AotValue.FromInteger(number),
        long number => AotValue.FromInteger(number),
        ulong number when number <= long.MaxValue => AotValue.FromInteger((long)number),
        decimal number => AotValue.FromDecimal(number),
        float number when float.IsFinite(number) => AotValue.FromFloatingPoint(number),
        double number when double.IsFinite(number) => AotValue.FromFloatingPoint(number),
        _ => throw Unsupported($"literal CLR type '{value?.GetType().FullName ?? "null"}'", extent),
    };

    private static bool TryGetDirectString(AotExpressionPlan expression, out string? value)
    {
        if (expression is AotLiteralExpressionPlan literal && literal.Value.TryGetString(out value))
        {
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryGetDirectScalarText(AotExpressionPlan expression, out string? text)
    {
        if (expression is not AotLiteralExpressionPlan literal || literal.Value.TryGetItems(out _))
        {
            text = null;
            return false;
        }

        List<CommandSyntaxAtom> atoms = [];
        try
        {
            AotCommandArgumentConverter.Append(literal.Value, expression.Span, atoms);
        }
        catch (ScriptException)
        {
            text = null;
            return false;
        }

        text = atoms.Count == 1 ? atoms[0].Text : null;
        return text is not null;
    }

    private static void ThrowIfParseFailed(AotParseResult parseResult)
    {
        if (parseResult.Diagnostics.Count != 0)
        {
            throw new ScriptException(parseResult.Diagnostics[0]);
        }
    }

    private static ScriptException Unsupported(string detail, IScriptExtent extent) =>
        new(AotDiagnostics.Unsupported(detail, AotScriptParser.ToSpan(extent), "Use only the documented Native AOT execution subset until this AST node has a reviewed plan."));

    private static ScriptException Unsupported(string detail, AotSourceSpan span) =>
        new(AotDiagnostics.Unsupported(detail, span, "Use only the documented Native AOT execution subset until this AST node has a reviewed plan."));

    private static ScriptException UnsupportedVariable(string detail, IScriptExtent extent, string help) =>
        new(AotDiagnostics.Scope("AOT5002", $"{detail} is not supported by the Native AOT lexical scope.", AotScriptParser.ToSpan(extent), "unsupported variable form", help));
}
