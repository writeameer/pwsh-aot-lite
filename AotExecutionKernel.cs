namespace PwshAotLite;

// Explicit AST-to-plan boundary. The plan now owns a flat block of reviewed
// statement plans; later lexical blocks, control flow, and functions extend
// this model rather than bypassing it.
internal sealed class AotExecutionPlan(AotParseResult parseResult, AotBlockPlan block)
{
    internal AotParseResult ParseResult { get; } = parseResult;
    internal AotBlockPlan Block { get; } = block;

    internal AotExecutionResult Execute(
        AotExecutionContext context,
        AotScope? scope = null,
        Action<AotExecutionOutput>? onOutput = null) =>
        Block.Execute(context, scope ?? new AotScope(), onOutput);
}

internal static class AotExecutionKernel
{
    internal static AotExecutionPlan Compile(string source, string? documentName = null, long documentVersion = 0)
    {
        return Compile(AotScriptParser.Parse(source, documentName, documentVersion));
    }

    // Hosts that need language-service decisions first (for example REPL
    // continuation) compile the exact same upstream AST result. They never
    // split, re-tokenize, or re-parse source through a host-specific path.
    internal static AotExecutionPlan Compile(AotParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        if (parseResult.Diagnostics.Count != 0)
        {
            throw new ScriptException(parseResult.Diagnostics[0]);
        }

        return new AotExecutionPlan(parseResult, UpstreamAstPipelineLowerer.LowerBlock(parseResult));
    }
}
