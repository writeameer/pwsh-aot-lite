using System.Globalization;
using System.Management.Automation.Language;

namespace PwshAotLite;

// The executable syntax entry point. It consumes only the pinned upstream AST
// and lowers a deliberately narrow, reviewed structural subset into the
// existing static registry/binder. Parser acceptance never implies execution
// support: script blocks, expressions, redirections, DSC, and unrecognized
// commands/stages fail with an explicit stable diagnostic.
internal static class UpstreamAstPipelineLowerer
{
    // Test and port fixtures sometimes need to bind one command directly. This
    // remains an AST operation so the repository has no alternate string lexer.
    internal static (IAotCmdlet Cmdlet, CommandInvocation Invocation) BindSingleCommand(string script)
    {
        ScriptBlockAst ast = Parser.ParseInput(script, out _, out ParseError[] errors);
        if (errors.Length != 0)
        {
            throw new ScriptException($"AotParseError: {errors[0].ErrorId}");
        }

        if (ast.EndBlock is null
            || ast.EndBlock.Statements.Count != 1
            || ast.EndBlock.Statements[0] is not PipelineAst { PipelineElements.Count: 1 } pipeline
            || pipeline.PipelineElements[0] is not CommandAst command)
        {
            throw Unsupported("one direct command");
        }

        LoweredCommand lowered = LowerCommand(command);
        return AotCmdletRegistry.BindCommand(lowered.Name, lowered.Arguments);
    }

    internal static PipelinePlan Parse(string script)
    {
        ScriptBlockAst ast = Parser.ParseInput(script, out _, out ParseError[] errors);
        if (errors.Length != 0)
        {
            throw new ScriptException($"AotParseError: {errors[0].ErrorId}");
        }

        if (ast.EndBlock is null || ast.EndBlock.Statements.Count != 1 || ast.EndBlock.Statements[0] is not PipelineAst pipeline)
        {
            throw Unsupported("only one top-level command pipeline is executable");
        }

        if (pipeline.PipelineElements.Count is < 1 or > 3)
        {
            throw Unsupported("the structural subset accepts one source command plus Where-Object and Select-Object only");
        }

        IAotCmdlet? source = null;
        CommandInvocation? sourceInvocation = null;
        Filter? filter = null;
        IReadOnlyList<string>? columns = null;
        var projected = false;

        for (var index = 0; index < pipeline.PipelineElements.Count; index++)
        {
            if (pipeline.PipelineElements[index] is not CommandAst command)
            {
                throw Unsupported("pipeline elements must be command ASTs");
            }

            LoweredCommand lowered = LowerCommand(command);
            if (index == 0)
            {
                (source, sourceInvocation) = AotCmdletRegistry.BindCommand(lowered.Name, lowered.Arguments);
                columns = source.DefaultColumns;
                continue;
            }

            if (lowered.Name.Equals("Where-Object", StringComparison.OrdinalIgnoreCase))
            {
                if (filter is not null
                    || projected
                    || lowered.Arguments.Count != 3
                    || lowered.Arguments[0].IsParameter
                    || !lowered.Arguments[1].IsParameter
                    || lowered.Arguments[2].IsParameter)
                {
                    throw Unsupported("Where-Object accepts one direct property/operator/value predicate in this subset");
                }

                filter = ScriptParser.ParseFilterArguments(
                [
                    lowered.Arguments[0].Text,
                    "-" + lowered.Arguments[1].Text,
                    lowered.Arguments[2].Text,
                ]);
                continue;
            }

            if (lowered.Name.Equals("Select-Object", StringComparison.OrdinalIgnoreCase))
            {
                if (projected || lowered.Arguments.Any(static argument => argument.IsParameter))
                {
                    throw Unsupported("Select-Object accepts one direct property projection only in this subset");
                }

                columns = ScriptParser.ParseColumns(lowered.Arguments.Select(static argument => argument.Text).ToArray());
                projected = true;
                continue;
            }

            throw Unsupported($"pipeline stage '{lowered.Name}'");
        }

        if (source is null || sourceInvocation is null || columns is null)
        {
            throw Unsupported("a source command is required");
        }

        return new PipelinePlan(source, sourceInvocation, null, null, filter, columns, projected);
    }

    private static LoweredCommand LowerCommand(CommandAst command)
    {
        if (command.Redirections.Count > 0)
        {
            throw Unsupported("redirections");
        }

        if (command.CommandElements.Count == 0 || command.CommandElements[0] is not StringConstantExpressionAst commandName)
        {
            throw Unsupported("command expressions or invocation operators");
        }

        var arguments = new List<CommandSyntaxAtom>();
        foreach (CommandElementAst element in command.CommandElements.Skip(1))
        {
            if (element is CommandParameterAst parameter)
            {
                arguments.Add(new CommandSyntaxAtom(parameter.ParameterName, IsParameter: true));
                if (parameter.Argument is not null)
                {
                    AddExpressionArguments(parameter.Argument, arguments);
                }

                continue;
            }

            if (element is ExpressionAst expression)
            {
                AddExpressionArguments(expression, arguments);
                continue;
            }

            throw Unsupported($"command element '{element.GetType().Name}'");
        }

        return new LoweredCommand(commandName.Value, arguments);
    }

    private static void AddExpressionArguments(ExpressionAst expression, List<CommandSyntaxAtom> arguments)
    {
        switch (expression)
        {
            case StringConstantExpressionAst text:
                arguments.Add(new CommandSyntaxAtom(text.Value, IsParameter: false));
                return;
            case ConstantExpressionAst constant:
                arguments.Add(new CommandSyntaxAtom(FormatConstant(constant.Value), IsParameter: false));
                return;
            case ArrayLiteralAst array:
                foreach (ExpressionAst element in array.Elements)
                {
                    AddExpressionArguments(element, arguments);
                }

                return;
            default:
                throw Unsupported($"expression '{expression.GetType().Name}'");
        }
    }

    // Parser AST constants necessarily surface as object.  This is the single
    // bridge into the string-valued descriptor binder, so keep it closed: a
    // future CLR constant must be explicitly designed, never gain command-line
    // meaning through an incidental ToString implementation.
    private static string FormatConstant(object? value) => value switch
    {
        null => string.Empty,
        bool boolean => boolean ? "True" : "False",
        char character => character.ToString(),
        sbyte number => number.ToString(CultureInfo.InvariantCulture),
        byte number => number.ToString(CultureInfo.InvariantCulture),
        short number => number.ToString(CultureInfo.InvariantCulture),
        ushort number => number.ToString(CultureInfo.InvariantCulture),
        int number => number.ToString(CultureInfo.InvariantCulture),
        uint number => number.ToString(CultureInfo.InvariantCulture),
        long number => number.ToString(CultureInfo.InvariantCulture),
        ulong number => number.ToString(CultureInfo.InvariantCulture),
        float number => number.ToString("R", CultureInfo.InvariantCulture),
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        _ => throw Unsupported($"literal CLR type '{value.GetType().FullName}'"),
    };

    private static ScriptException Unsupported(string detail) =>
        new($"AotUnsupportedSyntax: {detail} is parsed but not executable by the Native AOT structural subset.");

    private sealed record LoweredCommand(string Name, IReadOnlyList<CommandSyntaxAtom> Arguments);
}
