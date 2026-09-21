using PwshAotLite;

if (args.Length == 0)
{
    Repl.Run();
    return;
}

if (args.SequenceEqual(["--help"]))
{
    Console.WriteLine("Usage: pwsh-aot-lite [-Command <script>] [--complete <incomplete-line>] [<script>]");
    Console.WriteLine("With no arguments, starts an interactive REPL.");
    Console.WriteLine("--complete queries the metadata catalog for command, parameter, direct ValidateSet, module, and help-topic suggestions; it never imports modules or runs argument completers.");
    Console.WriteLine("Supported: Get-Help [<name>|-Name <name>] (built-in source catalog plus installed extension help); Get-Command [<name>|-Name <name>] [-Module <pattern>] [-CommandType Cmdlet|Function] (catalog with visible availability, not executable registry); Get-Module [<name>|-Name <name>] (built-in and registered-extension inventory only, no import); Find-Module [<name>|-Name <name>] [-Repository <pattern>] (read-only local repository indexes, no network/download/import); Install-Module <exact-name> [-Repository <name>] (hash-verified local file packages only; requires explicit PWSH_AOT_PACKAGE_ROOTS and PWSH_AOT_EXTENSIONS_ROOT); Get-Process [-Name <pattern>] [-Id <id>] [-IncludeUserName] [-Module] [-FileVersionInfo]; Get-Uptime [-Since]; Get-UICulture; Get-Culture [-Name <name>] [-NoUserOverrides] [-ListAvailable]; Get-Verb [-Verb <pattern>] [-Group <group>]; Get-TimeZone [-Id <id>] [-Name <standard-or-daylight-pattern>] [-ListAvailable]; Get-Date [-Date <date>|-UnixTimeSeconds <seconds>] [-Year <n>] [-Month <n>] [-Day <n>] [-Hour <n>] [-Minute <n>] [-Second <n>] [-Millisecond <n>] [-AsUTC] [-DisplayHint <Date|Time|DateTime>] [-Format <format>|-UFormat <format>]; Get-FileHash <physical-path> [-Algorithm SHA256]; Where-Object <CPU|Id|WorkingSet> <-gt|-ge|-lt|-le|-eq|-ne> <number>; Select-Object <columns>");
    return;
}

if (args.Length == 2 && args[0].Equals("--complete", StringComparison.OrdinalIgnoreCase))
{
    CompletionWriter.Write(CompletionService.Instance.Suggest(args[1]));
    return;
}

if (args.SequenceEqual(["--self-test"]))
{
    SelfTest.Run();
    Console.WriteLine("self-test passed");
    return;
}

string? script = args.Length switch
{
    2 when args[0] is "-Command" or "-c" => args[1],
    1 => args[0],
    _ => null,
};

if (script is null)
{
    Console.Error.WriteLine("Script error: Expected one script argument or -Command <script>.");
    Environment.ExitCode = 2;
    return;
}

Environment.ExitCode = ScriptRunner.Execute(script);
