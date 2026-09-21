using System.Text;

namespace PwshAotLite;

// Completion is deliberately a catalog projection, not another parser or a
// command-discovery path. It reads the same immutable/generated and
// declarative package metadata as Get-Help, Get-Command, and Get-Module; it
// never imports a module, loads an assembly, or invokes an ArgumentCompleter.
internal sealed record CompletionSuggestion(string Text, string Kind, string Description);

internal interface ICompletionService
{
    IReadOnlyList<CompletionSuggestion> Suggest(string line);
}

internal sealed class CompletionService(IHelpCatalog helpCatalog, IModuleCatalog moduleCatalog) : ICompletionService
{
    internal static CompletionService Instance { get; } = new(CompositeHelpCatalog.Instance, CompositeModuleCatalog.Instance);

    public IReadOnlyList<CompletionSuggestion> Suggest(string line)
    {
        CompletionInput input = CompletionInput.Parse(line);
        if (input.Tokens.Count == 0)
        {
            return CommandSuggestions(string.Empty);
        }

        string commandName = input.Tokens[0];
        if (input.Tokens.Count == 1 && !input.EndsWithWhitespace)
        {
            return CommandSuggestions(commandName);
        }

        HelpTopic[] commands = helpCatalog.Find(commandName)
            .Where(topic => topic.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (commands.Length == 0)
        {
            // An incomplete first token is a command-name request. Once it is
            // followed by whitespace, do not invent a command contract.
            return input.Tokens.Count == 1 ? CommandSuggestions(commandName) : [];
        }

        // Catalog queries preserve every known matching contract. Completion
        // must never pick a parameter surface by incidental source/package
        // ordering, so a collision becomes visible and parameter completion is
        // withheld until a future qualified-command syntax is designed.
        if (commands.Length > 1)
        {
            return commands
                .Select(topic => new CompletionSuggestion(topic.Name, "ambiguous-command", topic.ModuleName + " — parameter completion withheld"))
                .OrderBy(suggestion => suggestion.Description, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        HelpTopic command = commands[0];

        string current = input.EndsWithWhitespace ? string.Empty : input.Tokens[^1];
        HelpParameter? valueParameter = ValueParameter(command, input);
        if (valueParameter is not null)
        {
            return ValueSuggestions(command, valueParameter, current);
        }

        if (current.StartsWith("-", StringComparison.Ordinal))
        {
            return ParameterSuggestions(command, current);
        }

        // Commands with a positional Name contract use the same catalog as
        // their PowerShell counterparts. This is deliberately limited to the
        // three metadata control-plane commands; arbitrary values, paths, and
        // script expressions are not guessed or executed by this service.
        if (UsesTopicNames(command, out _))
        {
            return TopicSuggestions(current);
        }

        if (UsesModuleNames(command, out _))
        {
            return ModuleSuggestions(current);
        }

        return input.EndsWithWhitespace ? ParameterSuggestions(command, string.Empty) : [];
    }

    private IReadOnlyList<CompletionSuggestion> CommandSuggestions(string prefix) => helpCatalog.Find("*")
        .Where(topic => topic.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        .Select(topic => new CompletionSuggestion(topic.Name, "command", topic.ModuleName + " — " + Availability(topic)))
        .ToArray();

    private IReadOnlyList<CompletionSuggestion> ParameterSuggestions(HelpTopic command, string prefix) => DistinctAndOrder(
        command.Parameters.SelectMany(parameter => ParameterNames(parameter)
            .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(name => new CompletionSuggestion(name, "parameter", ParameterDescription(parameter)))));

    private IReadOnlyList<CompletionSuggestion> ValueSuggestions(HelpTopic command, HelpParameter parameter, string prefix)
    {
        if (command.Name.Equals("Get-Help", StringComparison.OrdinalIgnoreCase)
            && parameter.Name.Equals("Name", StringComparison.OrdinalIgnoreCase))
        {
            return TopicSuggestions(prefix);
        }

        if (command.Name.Equals("Get-Command", StringComparison.OrdinalIgnoreCase))
        {
            if (parameter.Name.Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                return TopicSuggestions(prefix);
            }

            if (parameter.Name.Equals("Module", StringComparison.OrdinalIgnoreCase))
            {
                return ModuleSuggestions(prefix);
            }
        }

        if (command.Name.Equals("Get-Module", StringComparison.OrdinalIgnoreCase)
            && parameter.Name.Equals("Name", StringComparison.OrdinalIgnoreCase))
        {
            return ModuleSuggestions(prefix);
        }

        return DistinctAndOrder(parameter.ValidationRules
            .Where(rule => rule.Name.Equals("ValidateSet", StringComparison.OrdinalIgnoreCase))
            .SelectMany(rule => rule.Arguments)
            .Select(TryGetExplicitValidateSetValue)
            .Where(value => value is not null)
            .Select(value => value!)
            .Where(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(value => new CompletionSuggestion(value, "value", "ValidateSet value for -" + parameter.Name)));
    }

    private IReadOnlyList<CompletionSuggestion> TopicSuggestions(string prefix) => helpCatalog.Find("*")
        .Where(topic => topic.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        .Select(topic => new CompletionSuggestion(topic.Name, "help-topic", topic.ModuleName + " — " + Availability(topic)))
        .ToArray();

    private IReadOnlyList<CompletionSuggestion> ModuleSuggestions(string prefix) => moduleCatalog.FindActive("*")
        .Where(module => module.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        .Select(module => new CompletionSuggestion(module.Name, "module", module.Version + " — " + module.Availability))
        .ToArray();

    private static HelpParameter? ValueParameter(HelpTopic command, CompletionInput input)
    {
        if (input.Tokens.Count < 2)
        {
            return null;
        }

        string previous = input.EndsWithWhitespace ? input.Tokens[^1] : input.Tokens.Count >= 3 ? input.Tokens[^2] : string.Empty;
        if (!previous.StartsWith("-", StringComparison.Ordinal))
        {
            return null;
        }

        HelpParameter? parameter = command.Parameters.FirstOrDefault(candidate =>
            MatchesParameter(candidate, previous[1..]));
        return parameter is { Shape: not AotParameterShape.Switch } ? parameter : null;
    }

    private static bool UsesTopicNames(HelpTopic command, out HelpParameter? parameter)
    {
        parameter = command.Parameters.FirstOrDefault(candidate => candidate.Name.Equals("Name", StringComparison.OrdinalIgnoreCase));
        return parameter is not null && (command.Name.Equals("Get-Help", StringComparison.OrdinalIgnoreCase)
            || command.Name.Equals("Get-Command", StringComparison.OrdinalIgnoreCase));
    }

    private static bool UsesModuleNames(HelpTopic command, out HelpParameter? parameter)
    {
        parameter = command.Parameters.FirstOrDefault(candidate => candidate.Name.Equals("Name", StringComparison.OrdinalIgnoreCase));
        return parameter is not null && command.Name.Equals("Get-Module", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ParameterNames(HelpParameter parameter)
    {
        yield return "-" + parameter.Name;
        foreach (string alias in parameter.Aliases)
        {
            yield return "-" + alias;
        }
    }

    private static bool MatchesParameter(HelpParameter parameter, string suppliedName) =>
        parameter.Name.Equals(suppliedName, StringComparison.OrdinalIgnoreCase)
        || parameter.Aliases.Any(alias => alias.Equals(suppliedName, StringComparison.OrdinalIgnoreCase));

    private static string ParameterDescription(HelpParameter parameter) => parameter.Shape == AotParameterShape.Switch
        ? "switch"
        : parameter.TypeName;

    private static string Availability(HelpTopic topic) => GetCommandCmdlet.AvailabilityFor(topic);

    // The source generator preserves expression text for source ValidateSet
    // attributes. Complete only direct string/identifier values, never a
    // constant/property/expression that would require evaluating source code.
    private static string? TryGetExplicitValidateSetValue(string value)
    {
        string candidate = value.Trim();
        if (candidate.Length >= 2
            && (candidate[0] == '\'' || candidate[0] == '"')
            && candidate[^1] == candidate[0])
        {
            candidate = candidate[1..^1];
        }

        if (candidate.Length == 0
            || candidate.Equals("true", StringComparison.OrdinalIgnoreCase)
            || candidate.Equals("false", StringComparison.OrdinalIgnoreCase)
            || candidate.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_' or ' ')))
        {
            return null;
        }

        return candidate;
    }

    private static IReadOnlyList<CompletionSuggestion> DistinctAndOrder(IEnumerable<CompletionSuggestion> suggestions) => suggestions
        .GroupBy(suggestion => suggestion.Text, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First())
        .OrderBy(suggestion => suggestion.Text, StringComparer.OrdinalIgnoreCase)
        .ThenBy(suggestion => suggestion.Kind, StringComparer.Ordinal)
        .ToArray();
}

internal static class CompletionWriter
{
    internal static void Write(IReadOnlyList<CompletionSuggestion> suggestions)
    {
        foreach (CompletionSuggestion suggestion in suggestions)
        {
            Console.WriteLine(EscapeTsvField(suggestion.Text) + "\t" + EscapeTsvField(suggestion.Kind) + "\t" + EscapeTsvField(suggestion.Description));
        }
    }

    // Extension metadata is untrusted text. Keep the documented TSV protocol
    // one-record-per-line even when a package contains tabs, line breaks, or
    // other terminal/control characters.
    private static string EscapeTsvField(string value)
    {
        StringBuilder escaped = new(value.Length);
        foreach (char character in value)
        {
            escaped.Append(character switch
            {
                '\\' => "\\\\",
                '\t' => "\\t",
                '\r' => "\\r",
                '\n' => "\\n",
                _ when char.IsControl(character) => "\\u" + ((int)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture),
                _ => character.ToString(),
            });
        }

        return escaped.ToString();
    }
}

// This is an intentionally permissive incomplete-line lexer. It only locates
// the active command fragment for completion; the upstream AST lowerer is the
// source of truth for executable syntax and rejects malformed input.
internal sealed record CompletionInput(IReadOnlyList<string> Tokens, bool EndsWithWhitespace)
{
    internal static CompletionInput Parse(string line)
    {
        string stage = LastPipelineStage(line);
        List<string> tokens = [];
        StringBuilder current = new();
        char quote = '\0';
        foreach (char character in stage)
        {
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(character);
                }

                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (char.IsWhiteSpace(character))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(character);
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return new CompletionInput(tokens, quote == '\0' && stage.Length > 0 && char.IsWhiteSpace(stage[^1]));
    }

    private static string LastPipelineStage(string line)
    {
        char quote = '\0';
        int start = 0;
        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (character == '|')
            {
                start = index + 1;
            }
        }

        return line[start..];
    }
}
