namespace PwshAotLite;

internal static class ScriptRunner
{
    internal static int Execute(string script, AotScope? scope = null, AotColorMode colorMode = AotColorMode.Auto)
    {
        AotDiagnosticRenderOptions renderOptions = AotTerminalColorPolicy.RendererOptions(colorMode);
        try
        {
            AotExecutionPlan plan = AotExecutionKernel.Compile(script, "<command>");
            AotExecutionContext context = new();
            _ = plan.Execute(context, scope, static output => TableWriter.Write(output.Rows, output.Columns));
            foreach (CommandError error in context.Errors)
            {
                Console.Error.WriteLine(AotDiagnosticRenderer.Render(error.Diagnostic, script, "<command>", renderOptions));
            }

            return 0;
        }
        catch (AotDiagnosticException error)
        {
            Console.Error.WriteLine(AotDiagnosticRenderer.Render(error.Diagnostic, script, "<command>", renderOptions));
            return 2;
        }
        catch (Exception)
        {
            Console.Error.WriteLine(AotDiagnosticRenderer.Render(
                AotDiagnostics.Internal("The host encountered an unexpected internal failure."),
                script,
                "<command>",
                renderOptions));
            return 1;
        }
    }
}

internal static class Repl
{
    internal static void Run(AotColorMode colorMode)
    {
        Console.WriteLine("pwsh-aot-lite — AOT command prototype. Type 'help' or 'exit'.");
        AotScope sessionScope = new();

        while (true)
        {
            Console.Write("pwsh-aot> ");
            string? line = Console.ReadLine();
            if (line is null || line.Equals("exit", StringComparison.OrdinalIgnoreCase) || line.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine();
                return;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Get-Process [-Name <pattern>] [-Id <id>] [-IncludeUserName] [-Module] [-FileVersionInfo]");
                Console.WriteLine("Get-Process -Name pwsh* | Where-Object CPU -ge 0 | Select-Object Name, Id");
                Console.WriteLine("$threshold = 10; Get-Process | Where-Object CPU -gt $threshold | Select-Object Name, Id");
                Console.WriteLine("Get-Process | Where-Object CPU -gt 10 | Select-Object Name, Id, CPU");
                Console.WriteLine("Get-Uptime [-Since] | Select-Object Value, Since");
                Console.WriteLine("Get-UICulture | Select-Object Name, DisplayName, LCID");
                Console.WriteLine("Get-Culture [-Name en-US] [-NoUserOverrides] [-ListAvailable]");
                Console.WriteLine("Get-Verb [-Verb Get*] [-Group Common] | Select-Object Verb, AliasPrefix, Group");
                Console.WriteLine("Get-TimeZone [-Id <id>] [-Name <standard-or-daylight-pattern>] [-ListAvailable]");
                Console.WriteLine("Get-FileHash [-Path <physical-pattern>|-LiteralPath <physical-path>] [-Algorithm SHA1|SHA256|SHA384|SHA512|MD5]");
                Console.WriteLine("Get-Help [<name>|-Name <name>] — source catalog plus dynamically discovered extension help");
                Console.WriteLine("Get-Command [<name>|-Name <name>] [-Module <pattern>] [-CommandType Cmdlet|Function] — same catalog, no module import");
                Console.WriteLine("Get-Module [<name>|-Name <name>] — built-in and registered extension inventory only; no module import");
                Console.WriteLine("Find-Module [<name>|-Name <name>] [-Repository <pattern>] — read-only local repository index query; no network/download/import");
                Console.WriteLine("Install-Module <exact-name> [-Repository <name>] — hash-verified local file package proof; requires explicit package and extension roots");
                Console.WriteLine("complete <incomplete line> — metadata-only command, parameter, ValidateSet, module, and help-topic suggestions");
                Console.WriteLine("Wildcard support: * and ?. The REPL retains supported lexical variables until exit. Type exit to leave.");
                continue;
            }

            if (line.StartsWith("complete ", StringComparison.OrdinalIgnoreCase))
            {
                CompletionWriter.Write(CompletionService.Instance.Suggest(line[9..]));
                continue;
            }

            _ = ScriptRunner.Execute(line, sessionScope, colorMode);
        }
    }
}
