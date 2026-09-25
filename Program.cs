using PwshAotLite;

// Keep terminal options host-owned and remove them before parsing the compact
// execution CLI. The option applies only to diagnostics on stderr.
AotColorMode colorMode = AotColorMode.Auto;
if (args.Length > 0 && args[0].Equals("--color", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2 || !AotTerminalColorPolicy.TryParse(args[1], out colorMode))
    {
        Console.Error.WriteLine(AotDiagnosticRenderer.Render(
            AotDiagnostics.HostUsage("--color expects auto, always, or never."),
            source: null,
            options: new AotDiagnosticRenderOptions(UseAnsi: false)));
        Environment.ExitCode = 2;
        return;
    }

    args = args[2..];
}

if (args.Length == 0)
{
    Repl.Run(colorMode);
    return;
}

if (args.SequenceEqual(["--help"]))
{
    Console.WriteLine("Usage: pwsh-aot-lite [--color auto|always|never] [-Command <script>|-File <path>] [--complete <incomplete-line>] [<script-or-path>]");
    Console.WriteLine("With no arguments, starts an interactive REPL.");
    Console.WriteLine("--color controls ANSI diagnostics on stderr: auto (default), always, or never.");
    Console.WriteLine("--complete queries the metadata catalog for command, parameter, direct ValidateSet, module, and help-topic suggestions; it never imports modules or runs argument completers.");
    Console.WriteLine("Supported: Get-Help [<name>|-Name <name>] (built-in source catalog plus installed extension help); Get-Command [<name>|-Name <name>] [-Module <pattern>] [-CommandType Cmdlet|Function] (catalog with visible availability, not executable registry); Get-Module [<name>|-Name <name>] (built-in and registered-extension inventory only, no import); Find-Module [<name>|-Name <name>] [-Repository <pattern>] (read-only local repository indexes, no network/download/import); Install-Module <exact-name> [-Repository <name>] (hash-verified local file packages only; requires explicit PWSH_AOT_PACKAGE_ROOTS and PWSH_AOT_EXTENSIONS_ROOT); Get-Process [-Name <pattern>] [-Id <id>] [-IncludeUserName] [-Module] [-FileVersionInfo]; Get-Uptime [-Since]; Get-UICulture; Get-Culture [-Name <name>] [-NoUserOverrides] [-ListAvailable]; Get-Verb [-Verb <pattern>] [-Group <group>]; Get-TimeZone [-Id <id>] [-Name <standard-or-daylight-pattern>] [-ListAvailable]; Get-Date [-Date <date>|-UnixTimeSeconds <seconds>] [-Year <n>] [-Month <n>] [-Day <n>] [-Hour <n>] [-Minute <n>] [-Second <n>] [-Millisecond <n>] [-AsUTC] [-DisplayHint <Date|Time|DateTime>] [-Format <format>|-UFormat <format>]; Get-FileHash <physical-path> [-Algorithm SHA256]; Where-Object <CPU|Id|WorkingSet> <-gt|-ge|-lt|-le|-eq|-ne> <number>; Select-Object <columns>");
    return;
}

if (args.Length == 2 && args[0].Equals("--complete", StringComparison.OrdinalIgnoreCase))
{
    CompletionWriter.Write(AotHostComposition.Completion.Suggest(args[1]));
    return;
}

if (args.SequenceEqual(["--self-test"]))
{
    SelfTest.Run();
    Console.WriteLine("self-test passed");
    return;
}

string? script = null;
string documentName = "<command>";
string? scriptDirectory = null;
AotDiagnostic? scriptInputError = null;

if (args.Length == 2 && args[0] is "-Command" or "-c")
{
    script = args[1];
}
else if (args.Length == 2 && args[0] is "-File" or "-f")
{
    scriptInputError = TryReadScriptFile(args[1], out script, out documentName, out scriptDirectory);
}
else if (args.Length == 1)
{
    // A one-argument invocation remains an inline command unless that exact
    // argument names an existing file. This keeps the original compact CLI
    // behavior while making direct script-file execution ergonomic.
    if (File.Exists(args[0]))
    {
        scriptInputError = TryReadScriptFile(args[0], out script, out documentName, out scriptDirectory);
    }
    else
    {
        script = args[0];
    }
}

if (scriptInputError is not null)
{
    Console.Error.WriteLine(AotDiagnosticRenderer.Render(
        scriptInputError,
        source: null,
        options: AotTerminalColorPolicy.RendererOptions(colorMode)));
    Environment.ExitCode = 2;
    return;
}

if (script is null)
{
    Console.Error.WriteLine(AotDiagnosticRenderer.Render(
        AotDiagnostics.HostUsage("Expected one script argument, -Command <script>, or -File <path>."),
        source: null,
        options: AotTerminalColorPolicy.RendererOptions(colorMode)));
    Environment.ExitCode = 2;
    return;
}

string originalDirectory = Directory.GetCurrentDirectory();
try
{
    // The direct-physical adapters capture their root at host composition.
    // Set it before compilation so relative fixture paths in a script file
    // resolve from that script's directory, never from the caller's CWD.
    if (scriptDirectory is not null)
    {
        Directory.SetCurrentDirectory(scriptDirectory);
    }

    Environment.ExitCode = ScriptRunner.Execute(script, documentName, colorMode: colorMode);
}
finally
{
    if (scriptDirectory is not null)
    {
        Directory.SetCurrentDirectory(originalDirectory);
    }
}

static AotDiagnostic? TryReadScriptFile(
    string path,
    out string? script,
    out string documentName,
    out string? scriptDirectory)
{
    script = null;
    documentName = "<command>";
    scriptDirectory = null;
    string fullPath;
    try
    {
        fullPath = Path.GetFullPath(path);
    }
    catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
    {
        return AotDiagnostics.HostUsage($"Script file path '{path}' is invalid: {error.Message}");
    }

    if (!File.Exists(fullPath))
    {
        return AotDiagnostics.HostUsage($"Script file '{fullPath}' was not found.");
    }

    try
    {
        script = File.ReadAllText(fullPath);
        documentName = fullPath;
        scriptDirectory = Path.GetDirectoryName(fullPath)
            ?? throw new IOException("The script file does not have a directory.");
        return null;
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
        return AotDiagnostics.HostUsage($"Script file '{fullPath}' could not be read: {error.Message}");
    }
}
