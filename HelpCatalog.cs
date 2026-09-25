using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PwshAotLite;

// Help discovery deliberately has no dependency on AotCmdletRegistry. Built-in
// topics are emitted at compile time from the PowerShell source inventory;
// extension topics are declarative JSON that can be added after publishing the
// native executable.
internal interface IHelpCatalog
{
    IReadOnlyList<HelpTopic> Find(string pattern);
}

internal sealed record HelpParameter(
    string Name,
    string TypeName,
    AotParameterShape Shape,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<ParameterSetContract> ParameterSets,
    IReadOnlyList<ValidationRuleMetadata> ValidationRules,
    bool SupportsWildcards);

internal sealed record HelpTopic(
    string Name,
    string CommandType,
    string ModuleName,
    string? Version,
    string Status,
    string Origin,
    string SourceLocator,
    string? Synopsis,
    string? Description,
    string? ExecutionKind,
    string? ExecutionStatus,
    IReadOnlyList<HelpParameter> Parameters,
    IReadOnlyList<string> OutputTypes,
    IReadOnlyList<HelpExample> Examples);

internal sealed record HelpExample(string Title, string Code, string Remarks);

// One typed declaration is the authority for a built-in command's native
// availability. The help/catalog view and the executable registry both use it;
// the registry also validates that the declaration exactly matches its adapters.
internal static class BuiltInCommandAvailability
{
    internal static IReadOnlySet<string> NativeAdapterNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Get-Process", "Get-Uptime", "Get-UICulture", "Get-Culture", "Get-Verb", "Get-Random", "Get-SecureRandom", "Join-String", "Compare-Object",
        "Get-TimeZone", "Get-Date", "Get-FileHash", "Get-ChildItem", "Get-Item", "Test-Path", "Resolve-Path", "Convert-Path", "Join-Path", "Split-Path", "New-Guid", "New-TimeSpan", "Start-Sleep", "Get-Help", "Get-Command",
        "Get-Module",
    };
}

// Find-Module is a host control-plane contract rather than one of the 290
// cmdlets generated from this repository's PowerShell source tree.  Keeping it
// here makes that distinction explicit: it is discoverable and executable by
// this host, but it must never be misrepresented as a source-port.
internal static class HostControlPlaneCatalog
{
    private static readonly ParameterSetContract DefaultSet = new("Default", null, false, PipelineBindingSource.None);

    internal static CmdletDescriptor FindModuleDescriptor { get; } = new(
        "Find-Module",
        [
            new ParameterSpec("Name", AotParameterShape.Array, [], [DefaultSet with { Position = 0 }]),
            new ParameterSpec("Repository", AotParameterShape.Array, [], [DefaultSet]),
        ],
        "Name");

    internal static CmdletDescriptor InstallModuleDescriptor { get; } = new(
        "Install-Module",
        [
            new ParameterSpec("Name", AotParameterShape.Scalar, [], [DefaultSet with { Position = 0, Mandatory = true }]),
            new ParameterSpec("Repository", AotParameterShape.Scalar, [], [DefaultSet]),
        ],
        "Name");

    internal static IReadOnlyList<HelpTopic> Topics { get; } =
    [
        new HelpTopic(
            "Find-Module",
            "Cmdlet",
            "PwshAotLite.ControlPlane",
            "1",
            "native-aot control-plane query; local repository indexes only",
            "host control-plane contract",
            "PwshAotLite repository-index protocol v1",
            "Search configured declarative repository indexes without downloading, importing, or executing a package.",
            "Repository discovery is read-only. It never contacts PowerShell Gallery, sends credentials, or installs a module.",
            "in-process-native-aot",
            "implemented-current-scope",
            [
                new HelpParameter("Name", "string[]", AotParameterShape.Array, [], [DefaultSet with { Position = 0 }], [], true),
                new HelpParameter("Repository", "string[]", AotParameterShape.Array, [], [DefaultSet], [], true),
            ],
            ["PwshAotLite.RepositoryModuleRecord"],
            []),
        new HelpTopic(
            "Install-Module",
            "Cmdlet",
            "PwshAotLite.ControlPlane",
            "1",
            "native-aot local proof installer; file packages only",
            "host control-plane contract",
            "PwshAotLite installer protocol v1",
            "Stage and activate a hash-verified declarative extension package from an explicitly allowed local file URI.",
            "This intentionally does not download packages, use credentials, verify signatures, resolve dependencies, import legacy modules, or execute package code.",
            "in-process-native-aot",
            "implemented-local-file-proof-only",
            [
                new HelpParameter("Name", "string", AotParameterShape.Scalar, [], [DefaultSet with { Position = 0, Mandatory = true }], [], false),
                new HelpParameter("Repository", "string", AotParameterShape.Scalar, [], [DefaultSet], [], true),
            ],
            ["PwshAotLite.InstallModuleRecord"],
            []),
    ];

    internal static IReadOnlySet<string> NativeAdapterNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Find-Module", "Install-Module",
    };
}

internal sealed class CompositeHelpCatalog(IAotHostConfiguration? configuration = null, IAotHostDiscoveryRoots? discoveryRoots = null) : IHelpCatalog
{
    internal static CompositeHelpCatalog Instance { get; } = new();
    private readonly IAotHostConfiguration _configuration = configuration ?? new ProcessAotHostConfiguration();
    private readonly IAotHostDiscoveryRoots _discoveryRoots = discoveryRoots ?? new ProcessAotHostDiscoveryRoots();

    public IReadOnlyList<HelpTopic> Find(string pattern)
    {
        List<HelpTopic> topics = BuiltIns()
            .Concat(HostControlPlaneCatalog.Topics)
            .Concat(ExtensionPackageCatalog.LoadActive(_configuration, _discoveryRoots).SelectMany(ExtensionHelpCatalog.ToTopics))
            .Where(topic => SimpleWildcard.IsMatch(pattern, topic.Name))
            .OrderBy(topic => topic.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(topic => topic.Origin, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return topics;
    }

    // This is deliberately exposed to the module catalog as a logical built-in
    // package. The two catalog views share the same generated source contracts,
    // but neither needs to consult the executable command registry.
    internal static IEnumerable<HelpTopic> BuiltIns()
    {
        foreach (SourceCmdletMetadata source in GeneratedCmdletPorts.All)
        {
            bool implemented = BuiltInCommandAvailability.NativeAdapterNames.Contains(source.Name);
            IReadOnlySet<string>? directParameterNames = implemented
                ? AotCmdletRegistry.DirectParameterNames(source.Name)
                : null;
            string? pipelineInputSynopsis = implemented
                ? AotCmdletRegistry.StaticPipelineInputSynopsis(source.Name)
                : null;
            IEnumerable<SourceParameterMetadata> executableParameters = directParameterNames is null
                ? source.Parameters
                : source.Parameters.Where(parameter => directParameterNames.Contains(parameter.Name));
            HashSet<string> omittedMandatoryParameterSets = directParameterNames is null
                ? []
                : source.Parameters
                    .Where(parameter => !directParameterNames.Contains(parameter.Name))
                    .SelectMany(parameter => parameter.ParameterSets)
                    .Where(parameterSet => parameterSet.Mandatory)
                    .Select(parameterSet => parameterSet.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            yield return new HelpTopic(
                source.Name,
                "Cmdlet",
                "PowerShell.BuiltIn",
                null,
                implemented ? "native-aot (implemented current scope)" : "catalogued from source; no native AOT adapter",
                "built-in source catalog",
                source.SourceFile + " :: " + source.SourceClass,
                "Generated contract help from the PowerShell source declaration."
                    + (pipelineInputSynopsis is null ? string.Empty : " " + pipelineInputSynopsis),
                null,
                implemented ? "in-process-native-aot" : null,
                implemented ? "implemented-current-scope" : "catalogued-not-implemented",
                executableParameters.Select(parameter => new HelpParameter(
                    parameter.Name,
                    parameter.TypeName,
                    parameter.Shape,
                    parameter.Aliases,
                    parameter.ParameterSets.Where(parameterSet => !omittedMandatoryParameterSets.Contains(parameterSet.Name)).ToArray(),
                    parameter.ValidationRules,
                    parameter.SupportsWildcards)).ToArray(),
                source.OutputTypes,
                []);
        }
    }
}

internal sealed record ExtensionPackage(
    string PackagePath,
    ExtensionManifestDocument Manifest,
    ExtensionHelpDocument? Help,
    ExtensionAuthoredHelpState HelpState,
    ExtensionProvenanceDocument? Provenance);

// A manifest is the package's required command contract. Authored help is an
// optional overlay: a missing or malformed help.json must not make a valid
// extension invisible to command/help/completion discovery.
internal enum ExtensionAuthoredHelpState { Authored, Missing, Invalid }

// The single data-only package seam for help, command, and module discovery.
// It validates cross-document identity before exposing an extension to any
// catalog and defines deterministic active-version selection for command/help
// lookup. Module inventory intentionally uses LoadAll so users can see every
// installed version.
internal static class ExtensionPackageCatalog
{
    private const string ExtensionSchema = "https://pwsh-aot-lite.dev/schemas/extension/v1";

    internal static IReadOnlyList<ExtensionPackage> LoadAll(IAotHostConfiguration? configuration = null, IAotHostDiscoveryRoots? discoveryRoots = null)
    {
        IAotHostConfiguration hostConfiguration = configuration ?? new ProcessAotHostConfiguration();
        IAotHostDiscoveryRoots hostDiscoveryRoots = discoveryRoots ?? new ProcessAotHostDiscoveryRoots();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<ExtensionPackage> packages = [];
        string[] stagingRoots = GlobalStagingRoots(hostConfiguration).ToArray();
        foreach (string root in ExtensionRoots(hostConfiguration, hostDiscoveryRoots))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                foreach (string manifestPath in Directory.EnumerateFiles(root, "extension.json", SearchOption.AllDirectories))
                {
                    string fullPath = Path.GetFullPath(manifestPath);
                    // Install-Module stages beneath the extension filesystem
                    // so atomic rename stays same-volume. Staging is never a
                    // package discovery location, even though this catalog
                    // otherwise recursively reads extension manifests.
                    if (!stagingRoots.Any(stagingRoot => IsWithinDirectory(stagingRoot, CanonicalExistingPath(fullPath)))
                        && seen.Add(fullPath)
                        && TryLoadPackage(fullPath, out ExtensionPackage? package))
                    {
                        packages.Add(package!);
                    }
                }
            }
            catch (IOException)
            {
                // A user-controlled extension root can change mid-scan. A
                // missing/unreadable root must not break other valid roots.
            }
            catch (UnauthorizedAccessException)
            {
                // Same policy as the help catalog: skip an unreadable root.
            }
        }

        return packages;
    }

    internal static IReadOnlyList<ExtensionPackage> LoadActive(IAotHostConfiguration? configuration = null, IAotHostDiscoveryRoots? discoveryRoots = null) => LoadAll(configuration, discoveryRoots)
        .GroupBy(package => package.Manifest.Extension!.DisplayName!, StringComparer.OrdinalIgnoreCase)
        .Select(SelectActiveVersion)
        .OrderBy(package => package.Manifest.Extension!.DisplayName!, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    internal static string Availability(string? executionKind) => executionKind switch
    {
        "in-process-native-aot" => "native-aot",
        "legacy-pwsh-sidecar" => "legacy-bridge-pending",
        "declaration-only" => "blocked",
        _ => "registered-not-executable",
    };

    internal static string ExecutionStatus(ExtensionPackage package)
    {
        ExtensionExecutionDocument? execution = package.Manifest.Extension!.Execution;
        return (execution?.Kind ?? "unknown") + " / " + (execution?.Status ?? "unknown");
    }

    internal static string DescribeProvenance(ExtensionPackage package)
    {
        ExtensionProvenanceDocument? document = package.Provenance;
        if (document is null)
        {
            return "No provenance.json was found.";
        }

        string method = document.Registration?.Method
            ?? (document.Registration?.ModuleWasImportedInIsolatedSidecar == true
                ? "isolated-sidecar import"
                : "registration method not recorded");
        string sourcePath = document.SourceModule?.Path ?? "source path not recorded";
        return method + "; source: " + sourcePath;
    }

    internal static string Trust(ExtensionPackage package) => package.Provenance is null
        ? "unverified: no provenance.json"
        : "unverified registration metadata (no signature verification implemented)";

    private static ExtensionPackage SelectActiveVersion(IGrouping<string, ExtensionPackage> versions) => versions
        .OrderByDescending(package => HasDottedVersion(package.Manifest.Extension!.Version))
        .ThenByDescending(package => DottedVersionKey(package.Manifest.Extension!.Version), StringComparer.Ordinal)
        .ThenByDescending(package => package.Manifest.Extension!.Version, StringComparer.OrdinalIgnoreCase)
        .ThenBy(package => package.PackagePath, StringComparer.OrdinalIgnoreCase)
        .First();

    private static bool HasDottedVersion(string? value) => Version.TryParse(value, out _);

    // System.Version ordering without an allocation-heavy custom comparer.
    // Non-dotted versions are still deterministic via the subsequent lexical
    // comparison, but do not outrank a valid dotted version.
    private static string DottedVersionKey(string? value)
    {
        if (!Version.TryParse(value, out Version? version))
        {
            return string.Empty;
        }

        return $"{version.Major:D10}.{version.Minor:D10}.{Math.Max(version.Build, 0):D10}.{Math.Max(version.Revision, 0):D10}";
    }

    internal static bool TryLoadPackage(string manifestPath, out ExtensionPackage? package)
    {
        package = null;
        try
        {
            ExtensionManifestDocument? manifest = JsonSerializer.Deserialize(
                File.ReadAllText(manifestPath), HelpJsonContext.Default.ExtensionManifestDocument);
            if (!IsValidManifest(manifest))
            {
                return false;
            }

            string packagePath = Path.GetDirectoryName(manifestPath)!;
            (ExtensionHelpDocument? help, ExtensionAuthoredHelpState helpState) = ReadOptionalAuthoredHelp(manifest!, packagePath);

            ExtensionProvenanceDocument? provenance = ReadOptionalProvenance(packagePath);
            if (!IsValidProvenance(manifest!, provenance))
            {
                return false;
            }

            package = new ExtensionPackage(packagePath, manifest!, help, helpState, provenance);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsValidManifest(ExtensionManifestDocument? manifest) =>
        manifest is
        {
            Schema: ExtensionSchema,
            SchemaVersion: 1,
            Extension: { Id.Length: > 0, DisplayName.Length: > 0, Version.Length: > 0 },
            Commands: not null,
        }
        && UniqueNonEmpty(manifest.Commands.Select(command => command.Name));

    private static bool IsValidHelp(ExtensionManifestDocument manifest, ExtensionHelpDocument? help) =>
        help is { Module: { Name.Length: > 0 }, Commands: not null }
        && help.Module.Name!.Equals(manifest.Extension!.DisplayName, StringComparison.OrdinalIgnoreCase)
        && UniqueNonEmpty(help.Commands.Select(command => command.Name))
        && help.Commands.All(command => manifest.Commands!.Any(contract =>
            contract.Name!.Equals(command.Name, StringComparison.OrdinalIgnoreCase)));

    private static (ExtensionHelpDocument? Help, ExtensionAuthoredHelpState State) ReadOptionalAuthoredHelp(
        ExtensionManifestDocument manifest,
        string packagePath)
    {
        string helpPath = Path.Combine(packagePath, "help.json");
        if (!File.Exists(helpPath))
        {
            return (null, ExtensionAuthoredHelpState.Missing);
        }

        try
        {
            ExtensionHelpDocument? help = JsonSerializer.Deserialize(
                File.ReadAllText(helpPath), HelpJsonContext.Default.ExtensionHelpDocument);
            return IsValidHelp(manifest, help)
                ? (help, ExtensionAuthoredHelpState.Authored)
                : (null, ExtensionAuthoredHelpState.Invalid);
        }
        catch (IOException)
        {
            return (null, ExtensionAuthoredHelpState.Invalid);
        }
        catch (UnauthorizedAccessException)
        {
            return (null, ExtensionAuthoredHelpState.Invalid);
        }
        catch (JsonException)
        {
            return (null, ExtensionAuthoredHelpState.Invalid);
        }
    }

    private static bool IsValidProvenance(ExtensionManifestDocument manifest, ExtensionProvenanceDocument? provenance) =>
        provenance?.SourceModule?.Name is not { Length: > 0 } sourceName
        || sourceName.Equals(manifest.Extension!.DisplayName, StringComparison.OrdinalIgnoreCase);

    private static bool UniqueNonEmpty(IEnumerable<string?> values)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string? value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || !seen.Add(value))
            {
                return false;
            }
        }

        return true;
    }

    private static ExtensionProvenanceDocument? ReadOptionalProvenance(string packagePath)
    {
        string provenancePath = Path.Combine(packagePath, "provenance.json");
        if (!File.Exists(provenancePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(provenancePath), HelpJsonContext.Default.ExtensionProvenanceDocument);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<string> ExtensionRoots(IAotHostConfiguration configuration, IAotHostDiscoveryRoots discoveryRoots)
    {
        string? configuredRoot = configuration.Read(AotHostConfigurationKey.ExtensionsRoot);
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            yield return configuredRoot;
        }

        string? configuredPath = configuration.Read(AotHostConfigurationKey.ExtensionsPath);
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            foreach (string value in configuredPath.Split(Path.PathSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                yield return value;
            }
        }

        foreach (string root in AncestorExtensionRoots(discoveryRoots.CurrentDirectory))
        {
            yield return root;
        }

        foreach (string root in AncestorExtensionRoots(discoveryRoots.ApplicationBaseDirectory))
        {
            yield return root;
        }
    }

    private static bool IsWithinDirectory(string directory, string path)
    {
        try
        {
            string relative = Path.GetRelativePath(Path.GetFullPath(directory), Path.GetFullPath(path));
            return !Path.IsPathRooted(relative)
                && !relative.Equals("..", StringComparison.Ordinal)
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // Installation has exactly one mutable root (PWSH_AOT_EXTENSIONS_ROOT),
    // while discovery may scan that root, a parent supplied through
    // PWSH_AOT_EXTENSIONS_PATH, and development roots. Build one physical
    // exclusion set and apply it to every scan; deriving an exclusion from the
    // currently scanned root leaks staged manifests through a parent root.
    private static IEnumerable<string> GlobalStagingRoots(IAotHostConfiguration configuration)
    {
        string? configuredRoot = configuration.Read(AotHostConfigurationKey.ExtensionsRoot);
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            yield break;
        }

        string fullRoot;
        try
        {
            fullRoot = Path.GetFullPath(configuredRoot);
            if (!Directory.Exists(fullRoot))
            {
                yield break;
            }
        }
        catch (ArgumentException)
        {
            yield break;
        }

        yield return Path.Combine(CanonicalExistingDirectory(fullRoot), ".pwsh-aot-lite-staging");
    }

    private static string CanonicalExistingPath(string path)
    {
        string full = Path.GetFullPath(path);
        return Path.Combine(CanonicalExistingDirectory(Path.GetDirectoryName(full)!), Path.GetFileName(full));
    }

    private static string CanonicalExistingDirectory(string path)
    {
        Stack<string> components = new();
        for (DirectoryInfo? current = new DirectoryInfo(path); current is not null && current.Parent is not null; current = current.Parent)
        {
            components.Push(current.Name);
        }

        string resolved = Path.GetPathRoot(path)!;
        while (components.Count > 0)
        {
            string next = Path.Combine(resolved, components.Pop());
            DirectoryInfo info = new(next);
            if (info.LinkTarget is not null && info.ResolveLinkTarget(returnFinalTarget: true) is DirectoryInfo target)
            {
                resolved = target.FullName;
            }
            else
            {
                resolved = next;
            }
        }

        return Path.GetFullPath(resolved);
    }

    private static IEnumerable<string> AncestorExtensionRoots(string start)
    {
        DirectoryInfo? directory = new(Path.GetFullPath(start));
        for (int depth = 0; directory is not null && depth < 4; depth++, directory = directory.Parent)
        {
            yield return Path.Combine(directory.FullName, "extensions");
        }
    }
}

internal static class ExtensionHelpCatalog
{
    internal static IEnumerable<HelpTopic> ToTopics(ExtensionPackage package)
    {
        Dictionary<string, ExtensionCommandDocument> contracts = package.Manifest.Commands!
            .ToDictionary(command => command.Name!, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ExtensionHelpCommandDocument> authored = package.Help?.Commands?
            .ToDictionary(command => command.Name!, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, ExtensionHelpCommandDocument>(StringComparer.OrdinalIgnoreCase);
        string module = package.Manifest.Extension!.DisplayName!;
        string executionKind = package.Manifest.Extension.Execution?.Kind ?? "unknown";
        string executionStatus = package.Manifest.Extension.Execution?.Status ?? "unknown";

        return contracts.Values.Select(contract =>
        {
            authored.TryGetValue(contract.Name!, out ExtensionHelpCommandDocument? command);
            bool hasAuthoredHelp = command is not null;
            return new HelpTopic(
                contract.Name!,
                contract.CommandType ?? "Unknown",
                module,
                package.Manifest.Extension.Version,
                executionKind == "declaration-only"
                    ? "registered declaration-only extension; execution blocked pending a compatible bridge"
                    : "registered extension; execution bridge not implemented",
                "extension: " + module + HelpProvenanceSuffix(package.HelpState, hasAuthoredHelp),
                Path.Combine(package.PackagePath, hasAuthoredHelp ? "help.json" : "extension.json"),
                command?.Synopsis ?? "Generated contract help from registered extension manifest.",
                command?.Description ?? BaselineDescription(package.HelpState),
                executionKind,
                executionStatus,
                (contract.Parameters ?? []).Select(ToHelpParameter).ToArray(),
                contract.OutputTypes ?? [],
                (command?.Examples ?? []).Select(example => new HelpExample(example.Title ?? string.Empty, example.Code ?? string.Empty, example.Remarks ?? string.Empty)).ToArray());
        }).ToArray();
    }

    private static string HelpProvenanceSuffix(ExtensionAuthoredHelpState state, bool hasAuthoredHelp) => hasAuthoredHelp
        ? " (authored help overlay)"
        : state == ExtensionAuthoredHelpState.Missing
            ? " (generated manifest contract; authored help missing)"
            : " (generated manifest contract; authored help invalid or unreadable)";

    private static string BaselineDescription(ExtensionAuthoredHelpState state) => state == ExtensionAuthoredHelpState.Missing
        ? "No authored help.json was packaged; this baseline was generated from extension.json."
        : "Authored help.json was invalid or unreadable; this baseline was generated from extension.json.";

    private static HelpParameter ToHelpParameter(ExtensionParameterDocument parameter) => new(
        parameter.Name ?? "<unnamed>",
        parameter.Type ?? "System.Object",
        parameter.IsSwitch ? AotParameterShape.Switch : parameter.Type?.EndsWith("[]", StringComparison.Ordinal) == true ? AotParameterShape.Array : AotParameterShape.Scalar,
        parameter.Aliases ?? [],
        (parameter.ParameterSets ?? []).Select(set => new ParameterSetContract(
            set.Name ?? "__AllParameterSets",
            set.Position is int position && position != int.MinValue ? position : null,
            set.Mandatory,
            (set.ValueFromPipeline ? PipelineBindingSource.ByValue : PipelineBindingSource.None)
                | (set.ValueFromPipelineByPropertyName ? PipelineBindingSource.ByPropertyName : PipelineBindingSource.None)
                | (set.ValueFromRemainingArguments ? PipelineBindingSource.RemainingArguments : PipelineBindingSource.None))).ToArray(),
        (parameter.Validation?.ValidateSet ?? []).Select(value => new ValidationRuleMetadata("ValidateSet", [value])).ToArray(),
        false);
}

internal sealed class GetHelpCmdlet(IHelpCatalog catalog) : AotCmdletBase
{
    private static readonly CmdletDescriptor GetHelpDescriptor = GeneratedCmdletPorts.GetHelp.CreateAotDescriptor("Name");

    public override CmdletDescriptor Descriptor => GetHelpDescriptor;
    public override IReadOnlyList<string> DefaultColumns => ["Value"];
    public override AotTerminalPresentation TerminalPresentation => AotTerminalPresentation.Prose;

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        string pattern = invocation.TryGetValues("Name", out string[]? names)
            ? SingleValue(names)
            : "*";
        IReadOnlyList<HelpTopic> topics = catalog.Find(pattern);
        if (topics.Count == 0)
        {
            return [new HelpRecord($"No help topic matched '{pattern}'.")];
        }

        return topics.Select(HelpRenderer.Render).ToArray();
    }

    private static string SingleValue(string[] values)
    {
        if (values.Length != 1)
        {
            throw new ScriptException("Get-Help -Name accepts one topic name in this AOT prototype.");
        }

        return values[0];
    }
}

// Get-Command is the catalog counterpart to Get-Help.  It discovers source
// contracts and extension manifests, not only the small in-process adapter
// registry.  That keeps "discoverable" distinct from "currently executable"
// in an AOT host.
internal sealed class GetCommandCmdlet(IHelpCatalog catalog) : AotCmdletBase
{
    private static readonly CmdletDescriptor GetCommandDescriptor =
        GeneratedCmdletPorts.GetCommand.CreateAotDescriptor("Name", "Module", "CommandType");

    public override CmdletDescriptor Descriptor => GetCommandDescriptor;
    // Availability is deliberately a default column. A catalog query must not
    // look like a list of runnable commands when most entries remain source
    // contracts or bridge-pending extensions.
    public override IReadOnlyList<string> DefaultColumns => ["Name", "CommandType", "ModuleName", "Availability"];

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        string[] names = invocation.TryGetValues("Name", out string[]? suppliedNames) ? suppliedNames : ["*"];
        string[] modules = invocation.TryGetValues("Module", out string[]? suppliedModules) ? suppliedModules : [];
        HashSet<string>? commandTypes = invocation.TryGetValues("CommandType", out string[]? suppliedTypes)
            ? ParseCommandTypes(suppliedTypes)
            : null;

        return catalog.Find("*")
            .Where(topic => names.Any(pattern => SimpleWildcard.IsMatch(pattern, topic.Name)))
            .Where(topic => modules.Length == 0 || modules.Any(pattern => SimpleWildcard.IsMatch(pattern, topic.ModuleName)))
            .Where(topic => commandTypes is null || commandTypes.Contains(topic.CommandType))
            .OrderBy(topic => topic.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(topic => topic.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(topic => topic.Version, StringComparer.OrdinalIgnoreCase)
            .Select(topic => new CommandInfoRecord(
                topic.Name,
                topic.CommandType,
                topic.ModuleName,
                topic.Version ?? string.Empty,
                topic.Origin,
                AvailabilityFor(topic),
                topic.Status,
                string.Join(" | ", HelpRenderer.Syntax(topic))))
            .ToArray();
    }

    internal static string AvailabilityFor(HelpTopic topic) => topic.ExecutionKind is null
        ? "catalogued-only"
        : ExtensionPackageCatalog.Availability(topic.ExecutionKind);

    private static HashSet<string> ParseCommandTypes(IEnumerable<string> suppliedTypes)
    {
        HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string supplied in suppliedTypes)
        {
            if (supplied.Equals("Cmdlet", StringComparison.OrdinalIgnoreCase)
                || supplied.Equals("Function", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(supplied);
                continue;
            }

            throw new ScriptException("Get-Command -CommandType supports only Cmdlet or Function in this AOT prototype.");
        }

        return result;
    }
}

internal sealed record CommandInfoRecord(
    string Name,
    string CommandType,
    string ModuleName,
    string Version,
    string Source,
    string Availability,
    string Status,
    string Syntax) : IPipelineRecord
{
    public double NumberFor(string property) => throw new ScriptException("Where-Object does not support command metadata.");

    public string TextFor(string column) => column switch
    {
        "Name" => Name,
        "CommandType" => CommandType,
        "ModuleName" => ModuleName,
        "Version" => Version,
        "Source" => Source,
        "Availability" => Availability,
        "Status" => Status,
        "Syntax" => Syntax,
        _ => throw new ScriptException($"Select-Object does not support column '{column}' for command metadata.")
    };
}

// Get-Module needs package-level data that is not present in a per-command
// HelpTopic. It reads only declarative package files emitted by the registrar;
// it never imports a legacy module, loads an extension assembly, or invokes an
// extension command.
internal interface IModuleCatalog
{
    // Inventory intentionally exposes every valid installed version. Consumers
    // that select a module for a command/help/completion interaction must use
    // FindActive so version selection matches ExtensionPackageCatalog.LoadActive.
    IReadOnlyList<ModuleCatalogEntry> Find(string pattern);
    IReadOnlyList<ModuleCatalogEntry> FindActive(string pattern);
}

internal sealed record ModuleCatalogEntry(
    string Name,
    string Version,
    string Origin,
    string Availability,
    string Status,
    string PackagePath,
    string Trust,
    string Provenance);

internal sealed class CompositeModuleCatalog(IAotHostConfiguration? configuration = null, IAotHostDiscoveryRoots? discoveryRoots = null) : IModuleCatalog
{
    internal static CompositeModuleCatalog Instance { get; } = new();
    private readonly IAotHostConfiguration _configuration = configuration ?? new ProcessAotHostConfiguration();
    private readonly IAotHostDiscoveryRoots _discoveryRoots = discoveryRoots ?? new ProcessAotHostDiscoveryRoots();

    public IReadOnlyList<ModuleCatalogEntry> Find(string pattern) => BuiltIns()
        .Concat(ExtensionPackageCatalog.LoadAll(_configuration, _discoveryRoots).Select(ToEntry))
        .Where(module => SimpleWildcard.IsMatch(pattern, module.Name))
        .OrderBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(module => module.Version, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<ModuleCatalogEntry> FindActive(string pattern) => BuiltIns()
        .Concat(ExtensionPackageCatalog.LoadActive(_configuration, _discoveryRoots).Select(ToEntry))
        .Where(module => SimpleWildcard.IsMatch(pattern, module.Name))
        .OrderBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(module => module.Version, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static IEnumerable<ModuleCatalogEntry> BuiltIns()
    {
        HelpTopic[] commands = CompositeHelpCatalog.BuiltIns().ToArray();
        int nativeCount = commands.Count(topic => topic.ExecutionKind == "in-process-native-aot");
        int cataloguedCount = commands.Length - nativeCount;
        yield return new ModuleCatalogEntry(
            "PowerShell.BuiltIn",
            "source-" + commands.Length,
            "compiled-in source catalog",
            nativeCount == commands.Length ? "native-aot" : "mixed",
            $"{nativeCount} native-aot; {cataloguedCount} catalogued-only",
            "compiled into PwshAotLite",
            "built-in source contract; no external trust decision",
            "Generated at build time from ../../PowerShell/src; no runtime module load.");

        yield return new ModuleCatalogEntry(
            "PwshAotLite.ControlPlane",
            "1",
            "compiled host control-plane contract",
            "native-aot",
            $"{HostControlPlaneCatalog.NativeAdapterNames.Count} native-aot host control-plane command",
            "compiled into PwshAotLite",
            "host implementation; no external trust decision",
            "Hand-authored host contract, separate from the generated PowerShell-source catalog.");
    }

    private static ModuleCatalogEntry ToEntry(ExtensionPackage package) => new(
        package.Manifest.Extension!.DisplayName!,
        package.Manifest.Extension.Version!,
        "registered extension package",
        ExtensionPackageCatalog.Availability(package.Manifest.Extension.Execution?.Kind),
        ExtensionPackageCatalog.ExecutionStatus(package),
        package.PackagePath,
        ExtensionPackageCatalog.Trust(package),
        ExtensionPackageCatalog.DescribeProvenance(package));
}

internal sealed class GetModuleCmdlet(IModuleCatalog catalog) : AotCmdletBase
{
    private static readonly CmdletDescriptor GetModuleDescriptor =
        GeneratedCmdletPorts.GetModule.CreateAotDescriptor("Name");

    public override CmdletDescriptor Descriptor => GetModuleDescriptor;
    public override IReadOnlyList<string> DefaultColumns => ["Name", "Version", "Origin", "Availability"];

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        string[] names = invocation.TryGetValues("Name", out string[]? suppliedNames) ? suppliedNames : ["*"];
        return names
            .SelectMany(catalog.Find)
            .GroupBy(module => (module.Name, module.Version, module.PackagePath), StringTupleComparer.Instance)
            .Select(group => group.First())
            .OrderBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(module => module.Version, StringComparer.OrdinalIgnoreCase)
            .Select(module => new ModuleInfoRecord(
                module.Name,
                module.Version,
                module.Origin,
                module.Availability,
                module.Status,
                module.PackagePath,
                module.Trust,
                module.Provenance))
            .ToArray();
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string Name, string Version, string PackagePath)>
    {
        internal static StringTupleComparer Instance { get; } = new();

        public bool Equals((string Name, string Version, string PackagePath) x, (string Name, string Version, string PackagePath) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Name, y.Name)
            && StringComparer.OrdinalIgnoreCase.Equals(x.Version, y.Version)
            && StringComparer.OrdinalIgnoreCase.Equals(x.PackagePath, y.PackagePath);

        public int GetHashCode((string Name, string Version, string PackagePath) value) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Name),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Version),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.PackagePath));
    }
}

internal sealed record ModuleInfoRecord(
    string Name,
    string Version,
    string Origin,
    string Availability,
    string Status,
    string Path,
    string Trust,
    string Provenance) : IPipelineRecord
{
    public double NumberFor(string property) => throw new ScriptException("Where-Object does not support module metadata.");

    public string TextFor(string column) => column switch
    {
        "Name" => Name,
        "Version" => Version,
        "Origin" => Origin,
        "Availability" => Availability,
        "Status" => Status,
        "Path" => Path,
        "Trust" => Trust,
        "Provenance" => Provenance,
        _ => throw new ScriptException($"Select-Object does not support column '{column}' for module metadata.")
    };
}

// Repository discovery is intentionally separate from ExtensionPackageCatalog:
// the latter answers what is installed locally, whereas this catalog answers
// what a configured *declarative index* advertises.  The read path never
// downloads a package, loads an assembly, imports a module, or contacts a
// remote service.  That makes Find-Module safe to call in an AOT host before
// the future Install-Module trust/staging design exists.
internal interface IRepositoryCatalog
{
    IReadOnlyList<RepositoryModuleEntry> Find(string namePattern, string? repositoryPattern = null);
}

internal sealed record RepositoryModuleEntry(
    string Name,
    string Version,
    string Description,
    string Repository,
    string RepositoryUri,
    string PackageUri,
    string? PackageSha256,
    string Compatibility,
    string RegistrationMode);

internal sealed class RepositoryCatalog(IAotHostConfiguration? configuration = null, IAotHostDiscoveryRoots? discoveryRoots = null) : IRepositoryCatalog
{
    private const string RepositorySchema = "https://pwsh-aot-lite.dev/schemas/repository/v1";
    private const int MaximumIndexBytes = 1024 * 1024;
    private const int MaximumJsonDepth = 16;

    internal static RepositoryCatalog Instance { get; } = new();
    private readonly IAotHostConfiguration _configuration = configuration ?? new ProcessAotHostConfiguration();
    private readonly IAotHostDiscoveryRoots _discoveryRoots = discoveryRoots ?? new ProcessAotHostDiscoveryRoots();

    public IReadOnlyList<RepositoryModuleEntry> Find(string namePattern, string? repositoryPattern = null) => LoadAll(_configuration, _discoveryRoots)
        .Where(entry => SimpleWildcard.IsMatch(namePattern, entry.Name))
        .Where(entry => repositoryPattern is null || SimpleWildcard.IsMatch(repositoryPattern, entry.Repository))
        .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
        .ThenByDescending(entry => HasDottedVersion(entry.Version))
        .ThenByDescending(entry => DottedVersionKey(entry.Version), StringComparer.Ordinal)
        .ThenByDescending(entry => entry.Version, StringComparer.OrdinalIgnoreCase)
        .ThenBy(entry => entry.Repository, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static IReadOnlyList<RepositoryModuleEntry> LoadAll(IAotHostConfiguration configuration, IAotHostDiscoveryRoots discoveryRoots)
    {
        List<RepositoryModuleEntry> entries = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string indexPath in IndexPaths(configuration, discoveryRoots))
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(indexPath);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException)
            {
                continue;
            }

            if (!seen.Add(fullPath) || !TryLoadIndex(fullPath, out RepositoryIndexDocument? index))
            {
                continue;
            }

            RepositoryDocument repository = index!.Repository!;
            foreach (RepositoryModuleDocument module in index.Modules!)
            {
                entries.Add(new RepositoryModuleEntry(
                    module.Name!,
                    module.Version!,
                    module.Description!,
                    repository.Name!,
                    repository.Uri!,
                    module.PackageUri!,
                    module.PackageSha256,
                    module.Compatibility!,
                    module.RegistrationMode!));
            }
        }

        return entries;
    }

    private static bool TryLoadIndex(string indexPath, out RepositoryIndexDocument? index)
    {
        index = null;
        try
        {
            FileInfo info = new(indexPath);
            if (!info.Exists || info.Length is < 1 or > MaximumIndexBytes)
            {
                return false;
            }

            byte[] bytes = File.ReadAllBytes(indexPath);
            if (!HasBoundedJsonStructure(bytes))
            {
                return false;
            }

            RepositoryIndexDocument? parsed = JsonSerializer.Deserialize(bytes, HelpJsonContext.Default.RepositoryIndexDocument);
            if (!IsValidIndex(parsed))
            {
                return false;
            }

            index = parsed;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasBoundedJsonStructure(ReadOnlySpan<byte> bytes)
    {
        try
        {
            Utf8JsonReader reader = new(bytes, new JsonReaderOptions { MaxDepth = MaximumJsonDepth });
            while (reader.Read())
            {
                // Iterating to the end validates both UTF-8 and the full JSON
                // structure before materializing a user-controlled document.
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsValidIndex(RepositoryIndexDocument? index) => index is
    {
        Schema: RepositorySchema,
        SchemaVersion: 1,
        Repository: { Name: not null, Uri: not null },
        Modules: not null,
    }
    && IsSafeText(index.Repository.Name, 128)
    && IsSafeUri(index.Repository.Uri)
    && index.Modules.All(IsValidModule)
    && UniqueModuleVersions(index.Modules);

    private static bool IsValidModule(RepositoryModuleDocument module) =>
        IsSafeText(module.Name, 256)
        && IsSafeText(module.Version, 64)
        && IsSafeText(module.Description, 2048)
        && IsSafeUri(module.PackageUri)
        && IsSafeText(module.Compatibility, 128)
        && IsRegistrationMode(module.RegistrationMode);

    private static bool IsSafeText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximumLength
        && value.All(static character => !char.IsControl(character));

    private static bool IsSafeUri(string? value) =>
        value is not null
        && value.Length <= 2048
        && Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
        && uri.UserInfo.Length == 0
        && (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase));

    private static bool IsRegistrationMode(string? value) => value is "isolated-sidecar-import"
        or "declaration-only"
        or "native-extension-package";

    private static bool UniqueModuleVersions(IEnumerable<RepositoryModuleDocument> modules)
    {
        HashSet<string> identities = new(StringComparer.OrdinalIgnoreCase);
        return modules.All(module => identities.Add(module.Name + "\u001f" + module.Version));
    }

    private static bool HasDottedVersion(string value) => Version.TryParse(value, out _);

    private static string DottedVersionKey(string value)
    {
        if (!Version.TryParse(value, out Version? version))
        {
            return string.Empty;
        }

        return $"{version.Major:D10}.{version.Minor:D10}.{Math.Max(version.Build, 0):D10}.{Math.Max(version.Revision, 0):D10}";
    }

    private static IEnumerable<string> IndexPaths(IAotHostConfiguration configuration, IAotHostDiscoveryRoots discoveryRoots)
    {
        string? configured = configuration.Read(AotHostConfigurationKey.RepositoriesPath);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            foreach (string configuredPath in configured.Split(Path.PathSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string path in ExpandConfiguredPath(configuredPath))
                {
                    yield return path;
                }
            }

            // An explicit deployment configuration is authoritative.  Do not
            // silently merge repositories found near the current directory or
            // executable into a user's chosen trust boundary.
            yield break;
        }

        foreach (string directory in AncestorRepositoryDirectories(discoveryRoots.CurrentDirectory))
        {
            foreach (string path in EnumerateIndexFiles(directory))
            {
                yield return path;
            }
        }

        foreach (string directory in AncestorRepositoryDirectories(discoveryRoots.ApplicationBaseDirectory))
        {
            foreach (string path in EnumerateIndexFiles(directory))
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<string> ExpandConfiguredPath(string path)
    {
        if (File.Exists(path))
        {
            yield return path;
            yield break;
        }

        foreach (string indexPath in EnumerateIndexFiles(path))
        {
            yield return indexPath;
        }
    }

    private static IEnumerable<string> AncestorRepositoryDirectories(string start)
    {
        DirectoryInfo? directory;
        try
        {
            directory = new DirectoryInfo(Path.GetFullPath(start));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            yield break;
        }

        for (int depth = 0; directory is not null && depth < 4; depth++, directory = directory.Parent)
        {
            yield return Path.Combine(directory.FullName, "repositories");
        }
    }

    private static IEnumerable<string> EnumerateIndexFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        IEnumerable<string> paths;
        try
        {
            paths = Directory.EnumerateFiles(directory, "*.repository.json", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(directory, "repository.json", SearchOption.TopDirectoryOnly));
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (string path in paths)
        {
            yield return path;
        }
    }
}

internal sealed class FindModuleCmdlet(IRepositoryCatalog catalog) : AotCmdletBase
{
    public override CmdletDescriptor Descriptor => HostControlPlaneCatalog.FindModuleDescriptor;
    public override IReadOnlyList<string> DefaultColumns => ["Name", "Version", "Repository", "Compatibility", "RegistrationMode"];

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        string[] names = invocation.TryGetValues("Name", out string[]? suppliedNames) ? suppliedNames : ["*"];
        string[] repositories = invocation.TryGetValues("Repository", out string[]? suppliedRepositories) ? suppliedRepositories : ["*"];

        return names.SelectMany(name => repositories.SelectMany(repository => catalog.Find(name, repository)))
            .GroupBy(entry => (entry.Name, entry.Version, entry.Repository, entry.PackageUri), RepositoryIdentityComparer.Instance)
            .Select(group => group.First())
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(entry => entry.Version, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Repository, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new RepositoryModuleRecord(
                entry.Name,
                entry.Version,
                entry.Description,
                entry.Repository,
                entry.RepositoryUri,
                entry.PackageUri,
                entry.Compatibility,
                entry.RegistrationMode))
            .ToArray();
    }

    private sealed class RepositoryIdentityComparer : IEqualityComparer<(string Name, string Version, string Repository, string PackageUri)>
    {
        internal static RepositoryIdentityComparer Instance { get; } = new();

        public bool Equals((string Name, string Version, string Repository, string PackageUri) x, (string Name, string Version, string Repository, string PackageUri) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Name, y.Name)
            && StringComparer.OrdinalIgnoreCase.Equals(x.Version, y.Version)
            && StringComparer.OrdinalIgnoreCase.Equals(x.Repository, y.Repository)
            && StringComparer.Ordinal.Equals(x.PackageUri, y.PackageUri);

        public int GetHashCode((string Name, string Version, string Repository, string PackageUri) value) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Name),
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Version),
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Repository),
            StringComparer.Ordinal.GetHashCode(value.PackageUri));
    }
}

internal sealed record RepositoryModuleRecord(
    string Name,
    string Version,
    string Description,
    string Repository,
    string RepositoryUri,
    string PackageUri,
    string Compatibility,
    string RegistrationMode) : IPipelineRecord
{
    public double NumberFor(string property) => throw new ScriptException("Where-Object does not support repository metadata.");

    public string TextFor(string column) => column switch
    {
        "Name" => Name,
        "Version" => Version,
        "Description" => Description,
        "Repository" => Repository,
        "RepositoryUri" => RepositoryUri,
        "PackageUri" => PackageUri,
        "Compatibility" => Compatibility,
        "RegistrationMode" => RegistrationMode,
        _ => throw new ScriptException($"Select-Object does not support column '{column}' for repository metadata.")
    };
}

internal sealed record HelpRecord(string Content) : IPipelineRecord
{
    public double NumberFor(string property) => throw new ScriptException("Where-Object does not support help text.");
    public string TextFor(string column) => column == "Value"
        ? Content
        : throw new ScriptException($"Select-Object does not support column '{column}' for help text.");
}

internal static class HelpRenderer
{
    internal static HelpRecord Render(HelpTopic topic)
    {
        StringBuilder text = new();
        text.AppendLine("NAME");
        text.AppendLine("    " + topic.Name);
        text.AppendLine();
        text.AppendLine("STATUS");
        text.AppendLine("    " + topic.Status);
        text.AppendLine();
        text.AppendLine("SYNOPSIS");
        text.AppendLine("    " + (string.IsNullOrWhiteSpace(topic.Synopsis) ? "No authored synopsis was packaged." : topic.Synopsis));
        text.AppendLine();
        text.AppendLine("SYNTAX");
        foreach (string syntax in Syntax(topic))
        {
            text.AppendLine("    " + syntax);
        }

        text.AppendLine();
        text.AppendLine("PARAMETERS");
        if (topic.Parameters.Count == 0)
        {
            text.AppendLine("    None declared in the available contract.");
        }
        else
        {
            foreach (HelpParameter parameter in topic.Parameters.OrderBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase))
            {
                text.Append("    -").Append(parameter.Name).Append(" <").Append(parameter.TypeName).Append('>');
                if (parameter.Aliases.Count > 0)
                {
                    text.Append(" (aliases: ").Append(string.Join(", ", parameter.Aliases)).Append(')');
                }

                if (parameter.SupportsWildcards)
                {
                    text.Append(" (wildcards)");
                }

                text.AppendLine();
            }
        }

        if (topic.OutputTypes.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("OUTPUTS");
            foreach (string output in topic.OutputTypes)
            {
                text.AppendLine("    " + output);
            }
        }

        if (!string.IsNullOrWhiteSpace(topic.Description))
        {
            text.AppendLine();
            text.AppendLine("DESCRIPTION");
            text.AppendLine("    " + topic.Description);
        }

        if (!string.IsNullOrWhiteSpace(topic.ExecutionKind))
        {
            text.AppendLine();
            text.AppendLine("EXECUTION");
            text.AppendLine("    " + topic.ExecutionKind + " / " + (topic.ExecutionStatus ?? "unknown"));
        }

        if (topic.Examples.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("EXAMPLES");
            foreach (HelpExample example in topic.Examples)
            {
                text.AppendLine("    " + example.Title);
                text.AppendLine("    " + example.Code.Replace("\n", "\n    ", StringComparison.Ordinal));
            }
        }

        text.AppendLine();
        text.AppendLine("SOURCE");
        text.AppendLine("    " + topic.Origin + " — " + topic.SourceLocator);
        return new HelpRecord(text.ToString().TrimEnd());
    }

    internal static IEnumerable<string> Syntax(HelpTopic topic)
    {
        IEnumerable<IGrouping<string, HelpParameter>> sets = topic.Parameters
            .SelectMany(parameter => parameter.ParameterSets.Select(set => (Set: set.Name, Parameter: parameter)))
            .GroupBy(item => item.Set, item => item.Parameter, StringComparer.OrdinalIgnoreCase);
        bool found = false;
        foreach (IGrouping<string, HelpParameter> set in sets)
        {
            found = true;
            string suffix = set.Key == "__AllParameterSets" ? string.Empty : " (parameter set: " + set.Key + ")";
            yield return topic.Name + " " + string.Join(" ", set.Select(FormatParameter)) + suffix;
        }

        if (!found)
        {
            yield return topic.Name;
        }
    }

    private static string FormatParameter(HelpParameter parameter)
    {
        bool mandatory = parameter.ParameterSets.Any(set => set.Mandatory);
        string value = parameter.Shape == AotParameterShape.Switch ? string.Empty : " <" + parameter.TypeName + ">";
        return mandatory ? "-" + parameter.Name + value : "[-" + parameter.Name + value + "]";
    }
}

// JSON is data-only extension discovery. Source generation avoids Native-AOT
// reflection requirements and deliberately does not load the module assembly.
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ExtensionHelpDocument))]
[JsonSerializable(typeof(ExtensionManifestDocument))]
[JsonSerializable(typeof(ExtensionProvenanceDocument))]
[JsonSerializable(typeof(RepositoryIndexDocument))]
internal partial class HelpJsonContext : JsonSerializerContext;

// Versioned local repository-index protocol.  This is deliberately small and
// package-manager neutral: Find-Module needs searchable metadata, while a
// future installer owns download, verification, dependency resolution, and
// activation.  No credential field exists in this protocol.
public sealed class RepositoryIndexDocument
{
    public string? Schema { get; set; }
    public int SchemaVersion { get; set; }
    public RepositoryDocument? Repository { get; set; }
    public List<RepositoryModuleDocument>? Modules { get; set; }
}

public sealed class RepositoryDocument
{
    public string? Name { get; set; }
    public string? Uri { get; set; }
}

public sealed class RepositoryModuleDocument
{
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
    public string? PackageUri { get; set; }
    public string? PackageSha256 { get; set; }
    public string? Compatibility { get; set; }
    public string? RegistrationMode { get; set; }
}

public sealed class ExtensionHelpDocument
{
    public ExtensionModuleDocument? Module { get; set; }
    public List<ExtensionHelpCommandDocument>? Commands { get; set; }
}

public sealed class ExtensionModuleDocument
{
    public string? Name { get; set; }
}

public sealed class ExtensionHelpCommandDocument
{
    public string? Name { get; set; }
    public string? Synopsis { get; set; }
    public string? Description { get; set; }
    public List<ExtensionExampleDocument>? Examples { get; set; }
}

public sealed class ExtensionExampleDocument
{
    public string? Title { get; set; }
    public string? Code { get; set; }
    public string? Remarks { get; set; }
}

public sealed class ExtensionManifestDocument
{
    public string? Schema { get; set; }
    public int SchemaVersion { get; set; }
    public ExtensionMetadataDocument? Extension { get; set; }
    public List<ExtensionCommandDocument>? Commands { get; set; }
}

public sealed class ExtensionMetadataDocument
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Version { get; set; }
    public ExtensionExecutionDocument? Execution { get; set; }
}

public sealed class ExtensionExecutionDocument
{
    public string? Kind { get; set; }
    public string? Status { get; set; }
    public string? Reason { get; set; }
}

public sealed class ExtensionProvenanceDocument
{
    public ExtensionRegistrationDocument? Registration { get; set; }
    public ExtensionSourceModuleDocument? SourceModule { get; set; }
}

public sealed class ExtensionRegistrationDocument
{
    public string? Method { get; set; }
    public bool? ModuleWasImportedInIsolatedSidecar { get; set; }
}

public sealed class ExtensionSourceModuleDocument
{
    public string? Name { get; set; }
    public string? Path { get; set; }
}

public sealed class ExtensionCommandDocument
{
    public string? Name { get; set; }
    public string? CommandType { get; set; }
    public List<ExtensionParameterDocument>? Parameters { get; set; }
    public List<string>? OutputTypes { get; set; }
}

public sealed class ExtensionParameterDocument
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public List<string>? Aliases { get; set; }
    public bool IsSwitch { get; set; }
    public ExtensionValidationDocument? Validation { get; set; }
    public List<ExtensionParameterSetDocument>? ParameterSets { get; set; }
}

public sealed class ExtensionValidationDocument
{
    public List<string>? ValidateSet { get; set; }
    public List<string>? ValidatePattern { get; set; }
}

public sealed class ExtensionParameterSetDocument
{
    public string? Name { get; set; }
    public int? Position { get; set; }
    public bool Mandatory { get; set; }
    public bool ValueFromPipeline { get; set; }
    public bool ValueFromPipelineByPropertyName { get; set; }
    public bool ValueFromRemainingArguments { get; set; }
}
