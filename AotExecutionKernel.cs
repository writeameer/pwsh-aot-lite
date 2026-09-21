namespace PwshAotLite;

// Explicit AST-to-plan boundary. The first plan delegates to the existing
// reviewed typed pipeline executor; later plans (blocks, assignments, control
// flow, and functions) extend this model rather than bypassing it.
internal sealed class AotExecutionPlan(AotParseResult parseResult, PipelinePlan pipeline)
{
    internal AotParseResult ParseResult { get; } = parseResult;
    internal PipelinePlan Pipeline { get; } = pipeline;

    internal IReadOnlyList<string> Columns => Pipeline.Columns;

    internal IReadOnlyList<IPipelineRecord> Execute(AotExecutionContext context) => Pipeline.Execute(context);
}

internal static class AotExecutionKernel
{
    internal static AotExecutionPlan Compile(string source, string? documentName = null, long documentVersion = 0)
    {
        AotParseResult parseResult = AotScriptParser.Parse(source, documentName, documentVersion);
        if (parseResult.Diagnostics.Count != 0)
        {
            throw new ScriptException(parseResult.Diagnostics[0]);
        }

        return new AotExecutionPlan(parseResult, UpstreamAstPipelineLowerer.Lower(parseResult));
    }
}
