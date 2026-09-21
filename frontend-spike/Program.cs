// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
// Adapted front-end spike; provenance is in UPSTREAM-ORIGIN.md.

using PowerShellAotFrontEnd;

var script = args.Length == 0
    ? "Get-Process -Name 'pwsh*' | Where-Object CPU -gt 10 | Select-Object Name, Id"
    : string.Join(' ', args);

if (script == "--self-test")
{
    FrontEndSelfTest.Run();
    Console.WriteLine("front-end self-test passed");
    return;
}

var result = PipelineParser.Parse(script);
foreach (var token in result.Tokens)
{
    Console.WriteLine($"TOKEN {token.Kind,-9} {token.Line}:{token.Column} [{token.Text}]");
}

foreach (var diagnostic in result.Diagnostics)
{
    Console.Error.WriteLine($"ERROR {diagnostic.Line}:{diagnostic.Column} {diagnostic.Message}");
}

if (result.Pipeline is not null)
{
    Console.WriteLine("PIPELINE");
    for (var index = 0; index < result.Pipeline.Stages.Count; index++)
    {
        var stage = result.Pipeline.Stages[index];
        Console.WriteLine($"  [{index}] {stage.CommandName} {string.Join(' ', stage.Arguments.Select(static x => x.Text))}");
    }
}

Environment.ExitCode = result.Diagnostics.Count == 0 ? 0 : 2;
