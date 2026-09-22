using System.Globalization;
using System.Management.Automation.Language;

namespace PwshAotLite;

// The executable syntax entry point. It consumes only the pinned upstream AST
// and lowers a deliberately reviewed subset into an explicit block plan.
// Parser acceptance never implies execution support.
internal static class UpstreamAstPipelineLowerer
{
    // A deliberately small, reviewable execution cap. It applies after the
    // optional statically typed input handoff; it is not a grammar limit.
    private const int MaxStructuralTailStages = 4;
    private readonly record struct AotLoweringContext(bool AllowRootFunctionDefinitions, bool AllowLocalFunctionReturn);
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
        return LowerStatements(ast.EndBlock!.Statements, ast.EndBlock.Traps, ast.EndBlock.Extent, new AotLoweringContext(AllowRootFunctionDefinitions: true, AllowLocalFunctionReturn: false));
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

    private static AotBlockPlan LowerStatementBlock(StatementBlockAst block, AotLoweringContext parentContext) =>
        LowerStatements(block.Statements, block.Traps, block.Extent, parentContext with { AllowRootFunctionDefinitions = false });

    private static AotBlockPlan LowerStatements(
        IEnumerable<StatementAst> source,
        IEnumerable<TrapStatementAst>? traps,
        IScriptExtent extent,
        AotLoweringContext context)
    {
        if (traps?.Any() == true)
        {
            throw Unsupported("trap statements", extent);
        }

        List<AotStatementPlan> statements = [];
        foreach (StatementAst statement in source)
        {
            statements.Add(LowerStatement(statement, context));
        }

        return new AotBlockPlan(statements);
    }

    private static AotStatementPlan LowerStatement(StatementAst statement, AotLoweringContext context)
    {
        if (statement is FunctionDefinitionAst function)
        {
            return context.AllowRootFunctionDefinitions
                ? LowerFunctionDefinition(function)
                : throw Unsupported("function definitions outside the root script block", function.Extent);
        }

        if (statement is ReturnStatementAst @return)
        {
            return LowerReturn(@return, context);
        }

        return statement switch
        {
            AssignmentStatementAst assignment => LowerAssignment(assignment),
            PipelineAst pipeline => LowerPipeline(pipeline),
            IfStatementAst conditional => LowerIf(conditional, context),
            ForEachStatementAst forEach => LowerForEach(forEach, context),
            _ => throw Unsupported($"statement '{statement.GetType().Name}'", statement.Extent),
        };
    }

    private static AotReturnStatementPlan LowerReturn(ReturnStatementAst @return, AotLoweringContext context)
    {
        if (!context.AllowLocalFunctionReturn)
        {
            throw Unsupported("return statements outside a local function", @return.Extent);
        }

        if (@return.Pipeline is not null)
        {
            throw Unsupported("return values or pipelines", @return.Pipeline.Extent);
        }

        return new AotReturnStatementPlan();
    }

    private static AotFunctionDefinitionPlan LowerFunctionDefinition(FunctionDefinitionAst function)
    {
        if (function.IsFilter || function.IsWorkflow)
        {
            throw Unsupported("filters and workflows", function.Extent);
        }

        if (function.Name.Contains(':', StringComparison.Ordinal))
        {
            throw Unsupported("scope-qualified local function names", function.Extent);
        }

        List<AotLocalFunctionParameter> parameters = [];
        HashSet<string> parameterNames = new(StringComparer.OrdinalIgnoreCase);
        bool sawOptionalParameter = false;
        foreach (ParameterAst parameter in function.Parameters ?? [])
        {
            if (parameter.Attributes.Count != 0)
            {
                throw Unsupported("function parameter attributes or type constraints", parameter.Extent);
            }

            string parameterName = GetFunctionParameterName(parameter.Name);
            if (!parameterNames.Add(parameterName))
            {
                throw Unsupported("duplicate local function parameters", parameter.Name.Extent);
            }

            AotExpressionPlan? defaultValue = null;
            if (parameter.DefaultValue is not null)
            {
                if (!IsClosedFunctionDefault(parameter.DefaultValue))
                {
                    throw Unsupported("function parameter defaults other than direct closed literals or literal lists", parameter.DefaultValue.Extent);
                }

                defaultValue = LowerExpression(parameter.DefaultValue);
                sawOptionalParameter = true;
            }
            else if (sawOptionalParameter)
            {
                throw Unsupported("required function parameters after an optional parameter", parameter.Name.Extent);
            }

            parameters.Add(new AotLocalFunctionParameter(parameterName, AotScriptParser.ToSpan(parameter.Name.Extent), defaultValue));
        }

        return new AotFunctionDefinitionPlan(new AotLocalFunctionPlan(
            function.Name,
            parameters,
            LowerFunctionBody(function.Body),
            AotScriptParser.ToSpan(function.Extent)));
    }

    private static AotBlockPlan LowerFunctionBody(ScriptBlockAst body)
    {
        if (body.UsingStatements?.Count is > 0
            || body.Attributes?.Count is > 0
            || body.ParamBlock is not null
            || body.BeginBlock is not null
            || body.ProcessBlock is not null
            || body.DynamicParamBlock is not null
            || body.CleanBlock is not null
            || body.EndBlock is null
            || !body.EndBlock.Unnamed
            || body.EndBlock.Traps?.Count is > 0)
        {
            throw Unsupported("advanced local function blocks or body param declarations", body.Extent);
        }

        return LowerStatements(body.EndBlock.Statements, body.EndBlock.Traps, body.EndBlock.Extent, new AotLoweringContext(AllowRootFunctionDefinitions: false, AllowLocalFunctionReturn: true));
    }

    private static AotIfStatementPlan LowerIf(IfStatementAst conditional, AotLoweringContext context)
    {
        List<AotIfClausePlan> clauses = [];
        foreach (Tuple<PipelineBaseAst, StatementBlockAst> clause in conditional.Clauses)
        {
            clauses.Add(new AotIfClausePlan(LowerCondition(clause.Item1), LowerStatementBlock(clause.Item2, context)));
        }

        AotBlockPlan? elseBlock = conditional.ElseClause is null ? null : LowerStatementBlock(conditional.ElseClause, context);
        return new AotIfStatementPlan(clauses, elseBlock);
    }

    private static AotForEachStatementPlan LowerForEach(ForEachStatementAst forEach, AotLoweringContext context)
    {
        if (!string.IsNullOrEmpty(forEach.Label))
        {
            throw Unsupported("labeled foreach statements", forEach.Extent);
        }

        if (forEach.Flags != ForEachFlags.None || forEach.ThrottleLimit is not null)
        {
            throw Unsupported("foreach -parallel or throttle options", forEach.Extent);
        }

        if (forEach.Condition is not PipelineAst { Background: false, PipelineElements: [CommandExpressionAst command] })
        {
            throw Unsupported("foreach collections other than one direct closed expression", forEach.Condition.Extent);
        }

        return new AotForEachStatementPlan(
            GetAssignableVariableName(forEach.Variable),
            LowerExpression(command.Expression),
            LowerStatementBlock(forEach.Body, context));
    }

    private static AotConditionPlan LowerCondition(PipelineBaseAst condition)
    {
        if (condition is not PipelineAst { Background: false, PipelineElements: [CommandExpressionAst command] })
        {
            throw Unsupported("if conditions other than one direct closed expression", condition.Extent);
        }

        return command.Expression switch
        {
            BinaryExpressionAst binary => LowerComparisonCondition(binary),
            ExpressionAst expression => new AotBooleanConditionPlan(LowerExpression(expression)),
            _ => throw Unsupported("if condition expression", command.Extent),
        };
    }

    private static AotConditionPlan LowerComparisonCondition(BinaryExpressionAst binary)
    {
        if (!TryGetConditionComparison(binary.Operator, out Comparison comparison, out StringComparison stringComparison))
        {
            throw Unsupported($"if operator '{binary.Operator}'", binary.ErrorPosition);
        }

        return new AotComparisonConditionPlan(
            LowerExpression(binary.Left),
            LowerExpression(binary.Right),
            comparison,
            stringComparison,
            AotScriptParser.ToSpan(binary.ErrorPosition));
    }

    private static bool TryGetConditionComparison(TokenKind token, out Comparison comparison, out StringComparison stringComparison)
    {
        stringComparison = token is TokenKind.Ceq or TokenKind.Cne or TokenKind.Cge or TokenKind.Cgt or TokenKind.Clt or TokenKind.Cle
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        comparison = token switch
        {
            TokenKind.Ieq or TokenKind.Ceq => Comparison.Equal,
            TokenKind.Ine or TokenKind.Cne => Comparison.NotEqual,
            TokenKind.Igt or TokenKind.Cgt => Comparison.GreaterThan,
            TokenKind.Ige or TokenKind.Cge => Comparison.GreaterThanOrEqual,
            TokenKind.Ilt or TokenKind.Clt => Comparison.LessThan,
            TokenKind.Ile or TokenKind.Cle => Comparison.LessThanOrEqual,
            _ => default,
        };

        return token is TokenKind.Ieq or TokenKind.Ceq
            or TokenKind.Ine or TokenKind.Cne
            or TokenKind.Igt or TokenKind.Cgt
            or TokenKind.Ige or TokenKind.Cge
            or TokenKind.Ilt or TokenKind.Clt
            or TokenKind.Ile or TokenKind.Cle;
    }

    private static bool IsStandaloneRedirectionCommand(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (name[0] is '>' or '<' || name.StartsWith("&>", StringComparison.Ordinal))
        {
            return true;
        }

        int prefixLength = name[0] is '*' or >= '0' and <= '9' ? 1 : 0;
        return prefixLength < name.Length && name[prefixLength] == '>';
    }

    private static AotPipelineStatementPlan LowerPipeline(PipelineAst pipeline)
    {
        if (pipeline.Background)
        {
            throw Unsupported("background pipelines", pipeline.Extent);
        }

        if (pipeline.PipelineElements.Count is < 1 or > (2 + MaxStructuralTailStages))
        {
            throw Unsupported("one source command, one static typed input command, and at most four Where-Object/Select-Object transforms", pipeline.Extent);
        }

        AotCommandPlan? source = null;
        AotCommandPlan? inputStage = null;
        List<AotPipelineTailStagePlan> tailStages = [];

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
                if (tailStages.Count >= MaxStructuralTailStages)
                {
                    throw Unsupported("more than four ordered Where-Object/Select-Object transforms", command.Extent);
                }

                if (lowered.Arguments.Count != 3
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
                    tailStages.Add(new AotFilterTailStagePlan(
                        new AotLiteralFilterStagePlan(ScriptParser.ParseFilterArguments(
                            [property!, comparisonText, literalValue!],
                            valueSpan,
                            propertySpan,
                            operatorSpan)),
                        AotScriptParser.ToSpan(command.Extent)));
                }
                else
                {
                    Comparison comparison = ScriptParser.ParseComparison(comparisonText, operatorSpan);
                    tailStages.Add(new AotFilterTailStagePlan(
                        new AotVariableFilterStagePlan(property!, comparison, valueArgument.Expression, propertySpan),
                        AotScriptParser.ToSpan(command.Extent)));
                }

                continue;
            }

            if (lowered.Name.Equals("Select-Object", StringComparison.OrdinalIgnoreCase))
            {
                if (tailStages.Count >= MaxStructuralTailStages)
                {
                    throw Unsupported("more than four ordered Where-Object/Select-Object transforms", command.Extent);
                }

                if (lowered.Arguments.Any(static argument => argument is AotParameterArgumentPlan))
                {
                    throw Unsupported("Select-Object accepts one direct property projection in this subset", command.Extent);
                }

                IReadOnlyList<string> columns;
                AotSourceSpan projectionSpan;
                if (lowered.Arguments.Count == 0)
                {
                    columns = ScriptParser.ParseColumns([], lowered.CommandSpan);
                    projectionSpan = lowered.CommandSpan;
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

                tailStages.Add(new AotProjectionTailStagePlan(columns, projectionSpan));
                continue;
            }

            // A command can be a downstream stage only when a registered
            // native adapter explicitly opts into one concrete record type.
            // It must precede the closed value-plane Where/Select transforms;
            // no generated metadata declaration, PSObject conversion, or
            // dynamic binder makes another command pipeline-capable.
            if (inputStage is null
                && tailStages.Count == 0
                && AotCmdletRegistry.IsStaticPipelineInputCmdlet(lowered.Name))
            {
                inputStage = lowered;
                continue;
            }

            throw Unsupported($"pipeline stage '{lowered.Name}'", command.Extent);
        }

        if (source is null)
        {
            throw Unsupported("a source command is required", pipeline.Extent);
        }

        return new AotPipelineStatementPlan(
            source,
            inputStage,
            tailStages,
            pipeline.PipelineElements.Count);
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

        // Upstream represents a redirection after a compound statement (for
        // example `if (...) { ... } > out.txt`) as a following pseudo-command
        // rather than CommandAst.Redirections. Do not let that parser shape
        // drift into the generated command binder as an unknown command.
        if (IsStandaloneRedirectionCommand(commandName.Value))
        {
            throw UnsupportedRedirection(commandName.Extent);
        }

        List<AotCommandArgumentPlan> arguments = [];
        foreach (CommandElementAst element in command.CommandElements.Skip(1))
        {
            if (element is CommandParameterAst parameter)
            {
                AotExpressionPlan? attachedValue = parameter.Argument is null ? null : LowerExpression(parameter.Argument);
                arguments.Add(new AotParameterArgumentPlan(
                    parameter.ParameterName,
                    AotScriptParser.ToSpan(parameter.Extent),
                    attachedValue,
                    parameter.Argument is null ? null : AotScriptParser.ToSpan(parameter.Argument.Extent)));

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

        if (AotScopeVariablePolicy.IsReservedPowerShellName(name))
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
        if (!path.IsUnscopedVariable || AotScopeVariablePolicy.IsReservedPowerShellName(path.UserPath)
            || path.UserPath.Equals("true", StringComparison.OrdinalIgnoreCase)
            || path.UserPath.Equals("false", StringComparison.OrdinalIgnoreCase)
            || path.UserPath.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            throw UnsupportedVariable($"assignment target '${path.UserPath}'", variable.Extent, "Assign one ordinary unscoped variable at a time.");
        }

        return path.UserPath;
    }

    private static string GetFunctionParameterName(VariableExpressionAst parameter)
    {
        if (parameter.Splatted)
        {
            throw UnsupportedVariable("splat local-function parameters", parameter.Extent, "Declare one ordinary unscoped parameter at a time.");
        }

        var path = parameter.VariablePath;
        if (!path.IsUnscopedVariable
            || AotScopeVariablePolicy.IsReservedPowerShellName(path.UserPath)
            || path.UserPath.Equals("true", StringComparison.OrdinalIgnoreCase)
            || path.UserPath.Equals("false", StringComparison.OrdinalIgnoreCase)
            || path.UserPath.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            throw UnsupportedVariable($"local-function parameter '${path.UserPath}'", parameter.Extent, "Declare an ordinary unscoped parameter.");
        }

        return path.UserPath;
    }

    private static bool IsClosedFunctionDefault(ExpressionAst expression) => expression switch
    {
        StringConstantExpressionAst => true,
        ConstantExpressionAst => true,
        VariableExpressionAst variable when variable.VariablePath.UserPath.Equals("true", StringComparison.OrdinalIgnoreCase)
            || variable.VariablePath.UserPath.Equals("false", StringComparison.OrdinalIgnoreCase)
            || variable.VariablePath.UserPath.Equals("null", StringComparison.OrdinalIgnoreCase) => true,
        ArrayLiteralAst array => array.Elements.All(IsClosedFunctionDefault),
        _ => false,
    };

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

    private static ScriptException UnsupportedRedirection(IScriptExtent extent) =>
        new(new AotDiagnostic(
            "AOT1001",
            AotDiagnosticSeverity.Error,
            AotDiagnosticCategory.UnsupportedExecution,
            "Redirections are parsed but not executable by the Native AOT structural subset.",
            AotScriptParser.ToSpan(extent),
            "unsupported redirection",
            "Remove the redirection or use the host's explicit output contract."));

    private static ScriptException UnsupportedVariable(string detail, IScriptExtent extent, string help) =>
        new(AotDiagnostics.Scope("AOT5002", $"{detail} is not supported by the Native AOT lexical scope.", AotScriptParser.ToSpan(extent), "unsupported variable form", help));
}
