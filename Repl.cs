namespace PwshAotLite;

internal static class ScriptRunner
{
    internal const int CancellationExitCode = 130;

    internal static int Execute(
        string script,
        AotScope? scope = null,
        AotColorMode colorMode = AotColorMode.Auto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(script);
        return Execute(AotScriptParser.Parse(script, "<command>"), scope, colorMode, cancellationToken);
    }

    // The REPL has already asked the shared parser whether its buffer needs a
    // continuation line. Execute that exact result, not a host-specific
    // reparsing of the text.
    internal static int Execute(
        AotParseResult parseResult,
        AotScope? scope = null,
        AotColorMode colorMode = AotColorMode.Auto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        AotDiagnosticRenderOptions renderOptions = AotTerminalColorPolicy.RendererOptions(colorMode);
        AotExecutionContext context = new(cancellationToken);
        AotTerminalEventProjector projector = new(
            parseResult.Source,
            parseResult.DocumentName ?? "<command>",
            renderOptions,
            Console.Out,
            Console.Error,
            context);
        try
        {
            context.ThrowIfCancellationRequested();
            AotExecutionPlan plan = AotExecutionKernel.Compile(parseResult);
            using IDisposable hostSubscription = context.Subscribe(projector.Project);
            _ = plan.Execute(context, scope);

            return 0;
        }
        catch (OperationCanceledException) when (context.IsStopping)
        {
            // Pipeline stopping is host control flow, not an AOT diagnostic or
            // a non-terminating error record.
            return CancellationExitCode;
        }
        catch (AotPublishedTerminatingException)
        {
            // Its typed TerminatingError event already reached the projector.
            return 2;
        }
        catch (AotDiagnosticException error)
        {
            projector.WriteDiagnostic(error.Diagnostic);
            return 2;
        }
        catch (Exception)
        {
            projector.WriteDiagnostic(AotDiagnostics.Internal("The host encountered an unexpected internal failure."));
            return 1;
        }
    }
}

internal static class Repl
{
    private const string ReplDocumentName = "<repl>";

    internal static void Run(AotColorMode colorMode)
    {
        Console.WriteLine("pwsh-aot-lite — AOT command prototype. Type 'help' or 'exit'.");
        AotScope sessionScope = new();
        AotReplInputBuffer input = new(ReplDocumentName);

        while (true)
        {
            Console.Write(input.Prompt);
            string? line = Console.ReadLine();
            if (line is null)
            {
                // EOF closes a partial prompt, but should not discard the
                // parser's actionable incomplete-input diagnostic.
                if (input.Drain() is { } unfinished)
                {
                    _ = ScriptRunner.Execute(unfinished, sessionScope, colorMode);
                }

                Console.WriteLine();
                return;
            }

            // Host control words are intentionally recognized only at a fresh
            // primary prompt. Inside a buffered program they remain source.
            if (!input.HasPending && (line.Equals("exit", StringComparison.OrdinalIgnoreCase) || line.Equals("quit", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine();
                return;
            }

            if (!input.HasPending && string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!input.HasPending && line.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Get-Process [-Name <pattern>] [-Id <id>] [-IncludeUserName] [-Module] [-FileVersionInfo]");
                Console.WriteLine("Get-Process -Name pwsh* | Where-Object CPU -ge 0 | Select-Object Name, Id");
                Console.WriteLine("$threshold = 10; Get-Process | Where-Object CPU -gt $threshold | Select-Object Name, Id");
                Console.WriteLine("function Get-CommonVerb($group) { Get-Verb -Group $group | Select-Object Verb }; Get-CommonVerb Common");
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

            if (!input.HasPending && line.StartsWith("complete ", StringComparison.OrdinalIgnoreCase))
            {
                CompletionWriter.Write(CompletionService.Instance.Suggest(line[9..]));
                continue;
            }

            AotParseResult submitted = input.Submit(line);
            if (submitted.RequiresMoreInput)
            {
                continue;
            }

            _ = ScriptRunner.Execute(submitted, sessionScope, colorMode);
        }
    }
}

// Console.ReadLine owns line editing, so this class owns only physical-line
// accumulation and asks the shared upstream parser when a complete program is
// available. It deliberately has no quote/brace/pipe scanner of its own.
internal sealed class AotReplInputBuffer(string documentName)
{
    private string? _source;

    internal bool HasPending => _source is not null;
    internal string Prompt => HasPending ? ">> " : "pwsh-aot> ";

    internal AotParseResult Submit(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        _source = _source is null ? line : _source + "\n" + line;
        AotParseResult parseResult = AotScriptParser.Parse(_source, documentName);
        if (!parseResult.RequiresMoreInput)
        {
            _source = null;
        }

        return parseResult;
    }

    internal AotParseResult? Drain()
    {
        if (_source is not { } source)
        {
            return null;
        }

        _source = null;
        return AotScriptParser.Parse(source, documentName);
    }
}
