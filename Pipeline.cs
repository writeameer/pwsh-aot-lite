using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace PwshAotLite;

// Transitional compatibility wrapper for ports that still throw their
// historically asserted message text. The host never renders Message directly:
// it renders Diagnostic. New execution-kernel code must construct a typed
// diagnostic at the originating boundary instead of using this constructor.
internal sealed class ScriptException : AotDiagnosticException
{
    internal ScriptException(string message)
        : this(AotDiagnostics.FromLegacyMessage(message), message)
    {
    }

    internal ScriptException(AotDiagnostic diagnostic)
        : this(diagnostic, diagnostic.PrimaryMessage)
    {
    }

    private ScriptException(AotDiagnostic diagnostic, string message)
        : base(diagnostic, message)
    {
    }

    internal ScriptException WithSpan(AotSourceSpan span) => new(Diagnostic with { Span = span }, Message);
}

// Static replacement for the PowerShell Cmdlet/Parameter/WriteObject contract.
// It intentionally preserves cmdlet lifecycle and parameter-set information;
// ports do not flatten those semantics into untyped string switches.
internal enum AotParameterShape { Scalar, Array, Switch }

[Flags]
internal enum PipelineBindingSource { None = 0, ByValue = 1, ByPropertyName = 2, RemainingArguments = 4 }

internal sealed record ParameterSetContract(string Name, int? Position, bool Mandatory, PipelineBindingSource PipelineBinding);

internal sealed record ValidationRuleMetadata(string Name, IReadOnlyList<string> Arguments);

internal sealed record SourceParameterMetadata(
    string Name,
    string TypeName,
    AotParameterShape Shape,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<ParameterSetContract> ParameterSets,
    IReadOnlyList<ValidationRuleMetadata> ValidationRules,
    bool SupportsWildcards);

internal sealed record CmdletCapabilities(
    bool SupportsShouldProcess,
    bool SupportsTransactions,
    string ConfirmImpact,
    string RemotingCapability);

internal sealed record CmdletLifecycle(bool HasBeginProcessing, bool HasProcessRecord, bool HasEndProcessing, bool HasStopProcessing);

internal sealed record MigrationBlockerMetadata(string Api, string Reason);

internal sealed record ParameterSpec(
    string Name,
    AotParameterShape Shape,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<ParameterSetContract> ParameterSets)
{
    internal bool Matches(string name) => Name.Equals(name, StringComparison.OrdinalIgnoreCase)
        || Aliases.Any(alias => alias.Equals(name, StringComparison.OrdinalIgnoreCase));

    internal bool IsDefault => ParameterSets.Any(static parameterSet => parameterSet.Position == 0);
}

// Most source commands have one positional parameter.  A command with two
// parameter sets at position zero (Get-FileHash's Path/LiteralPath) must name
// the source default set here; otherwise the small parser would mistake a
// parameter-set choice for an ambiguous positional argument.
internal sealed record CmdletDescriptor(
    string Name,
    IReadOnlyList<ParameterSpec> Parameters,
    string? DefaultParameterName = null);

internal sealed record SourceCmdletMetadata(
    string Name,
    string SourceFile,
    string SourceClass,
    IReadOnlyList<string> BaseTypeChain,
    CmdletCapabilities Capabilities,
    CmdletLifecycle Lifecycle,
    IReadOnlyList<string> OutputTypes,
    IReadOnlyList<SourceParameterMetadata> Parameters,
    IReadOnlyList<MigrationBlockerMetadata> MigrationBlockers)
{
    internal CmdletDescriptor CreateAotDescriptor(params string[] supportedParameters)
    {
        List<ParameterSpec> parameters = [];
        foreach (string supported in supportedParameters)
        {
            SourceParameterMetadata? parameter = Parameters.SingleOrDefault(candidate =>
                candidate.Name.Equals(supported, StringComparison.OrdinalIgnoreCase));
            if (parameter is null)
            {
                throw new InvalidOperationException($"Generated metadata for {Name} does not include parameter '{supported}'.");
            }

            parameters.Add(new ParameterSpec(parameter.Name, parameter.Shape, parameter.Aliases, parameter.ParameterSets));
        }

        return new CmdletDescriptor(Name, parameters);
    }
}

internal sealed class CommandInvocation(
    CmdletDescriptor descriptor,
    IReadOnlyDictionary<string, string[]> parameters,
    AotSourceSpan? sourceSpan = null,
    IReadOnlyDictionary<string, AotSourceSpan?[]>? valueSpans = null)
{
    internal CmdletDescriptor Descriptor { get; } = descriptor;
    internal AotSourceSpan? SourceSpan { get; } = sourceSpan;

    internal bool TryGetValues(string name, out string[] values) => parameters.TryGetValue(name, out values!);

    internal AotSourceSpan? GetValueSpan(string name, int index) =>
        valueSpans is not null
        && valueSpans.TryGetValue(name, out AotSourceSpan?[]? spans)
        && index >= 0
        && index < spans.Length
            ? spans[index]
            : SourceSpan;
}

// Parser-independent syntax atoms. The upstream AST lowerer and the legacy
// compatibility tokenizer both feed this one generated-metadata binder; the
// binder itself remains the sole authority for aliases and parameter shapes.
internal sealed record CommandSyntaxAtom(string Text, bool IsParameter, AotSourceSpan? Span = null);

internal sealed class CommandError(AotDiagnostic diagnostic)
{
    internal AotDiagnostic Diagnostic { get; } = diagnostic;
    internal string Id => Diagnostic.Id;
    internal string Message => Diagnostic.PrimaryMessage;
}

internal sealed class AotExecutionContext
{
    private readonly List<CommandError> _errors = [];
    private AotSourceSpan? _activeCommandSpan;

    internal IReadOnlyList<CommandError> Errors => _errors;

    internal void WriteNonTerminatingError(string id, string message) =>
        _errors.Add(new CommandError(new AotDiagnostic(
            id,
            AotDiagnosticSeverity.Error,
            AotDiagnosticCategory.Runtime,
            message,
            _activeCommandSpan,
            "command reported an error")));

    internal IDisposable EnterInvocation(CommandInvocation invocation) => new InvocationScope(this, invocation.SourceSpan);

    private sealed class InvocationScope : IDisposable
    {
        private readonly AotExecutionContext _context;
        private readonly AotSourceSpan? _priorSpan;

        internal InvocationScope(AotExecutionContext context, AotSourceSpan? span)
        {
            _context = context;
            _priorSpan = context._activeCommandSpan;
            context._activeCommandSpan = span;
        }

        public void Dispose() => _context._activeCommandSpan = _priorSpan;
    }
}

internal interface IPipelineRecord
{
    double NumberFor(string property);
    string TextFor(string column);
}

internal sealed record ProcessRecord(string Name, int Id, double CpuSeconds, long WorkingSetBytes, string? UserName = null) : IPipelineRecord
{
    public double NumberFor(string property) => property switch
    {
        "CPU" => CpuSeconds,
        "Id" => Id,
        "WorkingSet" => WorkingSetBytes,
        _ => throw new ScriptException($"Where-Object does not support property '{property}'.")
    };

    public string TextFor(string column) => column switch
    {
        "Name" => Name,
        "Id" => Id.ToString(CultureInfo.InvariantCulture),
        "CPU" => CpuSeconds.ToString("0.00", CultureInfo.InvariantCulture),
        "WorkingSet" => WorkingSetBytes.ToString(CultureInfo.InvariantCulture),
        "UserName" => UserName ?? string.Empty,
        _ => throw new ScriptException($"Select-Object does not support column '{column}'.")
    };
}

internal sealed record ProcessModuleRecord(int ProcessId, string ModuleName, string FileName, string FileVersion) : IPipelineRecord
{
    public double NumberFor(string property) => property == "Id" ? ProcessId : throw new ScriptException($"Where-Object does not support property '{property}' for modules.");
    public string TextFor(string column) => column switch
    {
        "Name" or "ModuleName" => ModuleName,
        "Id" or "ProcessId" => ProcessId.ToString(CultureInfo.InvariantCulture),
        "FileName" => FileName,
        "FileVersion" => FileVersion,
        "CPU" => string.Empty,
        _ => throw new ScriptException($"Select-Object does not support column '{column}' for modules.")
    };
}

internal sealed record ProcessFileVersionRecord(int ProcessId, string FileName, string FileVersion, string ProductVersion) : IPipelineRecord
{
    public double NumberFor(string property) => property == "Id" ? ProcessId : throw new ScriptException($"Where-Object does not support property '{property}' for file versions.");
    public string TextFor(string column) => column switch
    {
        "Name" or "FileName" => FileName,
        "Id" or "ProcessId" => ProcessId.ToString(CultureInfo.InvariantCulture),
        "FileVersion" => FileVersion,
        "ProductVersion" => ProductVersion,
        "CPU" => string.Empty,
        _ => throw new ScriptException($"Select-Object does not support column '{column}' for file versions.")
    };
}

// Scalar values are still first-class pipeline records.  The initial process
// port only needed table-shaped data; this record keeps a simple cmdlet from
// acquiring a fake process-shaped output just to reach the renderer.
internal sealed record UptimeRecord(TimeSpan Value, DateTime? Since) : IPipelineRecord
{
    public double NumberFor(string property) => throw new ScriptException($"Where-Object does not support property '{property}' for uptime values.");

    public string TextFor(string column) => column switch
    {
        "Value" => Since is null ? Value.ToString() : Since.Value.ToString("O", CultureInfo.InvariantCulture),
        "Uptime" => Value.ToString(),
        "Since" => Since?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
        "Name" => "Uptime",
        _ => throw new ScriptException($"Select-Object does not support column '{column}' for uptime values.")
    };
}

internal sealed record CultureRecord(string Name, string DisplayName, string EnglishName, int Lcid) : IPipelineRecord
{
    public double NumberFor(string property) => property == "LCID" ? Lcid : throw new ScriptException($"Where-Object does not support property '{property}' for culture values.");

    public string TextFor(string column) => column switch
    {
        "Name" => Name,
        "DisplayName" => DisplayName,
        "EnglishName" => EnglishName,
        "LCID" => Lcid.ToString(CultureInfo.InvariantCulture),
        _ => throw new ScriptException($"Select-Object does not support column '{column}' for culture values.")
    };
}

// Static projection of TimeZoneInfo.  Keeping the BCL instance behind this
// typed record avoids a PSObject/format-data dependency while preserving the
// public data Get-TimeZone exposes for its common display shape.
internal sealed record TimeZoneRecord(
    string Id,
    string DisplayName,
    string StandardName,
    string DaylightName,
    TimeSpan BaseUtcOffset,
    bool SupportsDaylightSavingTime) : IPipelineRecord
{
    public double NumberFor(string property) => property switch
    {
        "BaseUtcOffsetMinutes" => BaseUtcOffset.TotalMinutes,
        _ => throw new ScriptException($"Where-Object does not support property '{property}' for time-zone values.")
    };

    public string TextFor(string column) => column switch
    {
        "Id" => Id,
        "Name" => Id,
        "DisplayName" => DisplayName,
        "StandardName" => StandardName,
        "DaylightName" => DaylightName,
        "BaseUtcOffset" => BaseUtcOffset.ToString(),
        "SupportsDaylightSavingTime" => SupportsDaylightSavingTime.ToString(),
        _ => throw new ScriptException($"Select-Object does not support column '{column}' for time-zone values.")
    };
}

// Build-time projection of System.Management.Automation.VerbInfo.  The
// generator reads Verbs.cs and VerbDescriptionStrings.resx; runtime contains
// only data and no ResourceManager/reflection path.
internal sealed record GeneratedVerbInfo(string Verb, string AliasPrefix, string Group, string Description);

internal sealed record VerbRecord(string Verb, string AliasPrefix, string Group, string Description) : IPipelineRecord
{
    public double NumberFor(string property) => throw new ScriptException($"Where-Object does not support property '{property}' for verb values.");

    public string TextFor(string column) => column switch
    {
        "Verb" => Verb,
        "AliasPrefix" => AliasPrefix,
        "Group" => Group,
        "Description" => Description,
        _ => throw new ScriptException($"Select-Object does not support column '{column}' for verb values.")
    };
}

// The source returns a DateTime decorated with the PowerShell-only
// DisplayHint note property. Keep the value and its display policy together in
// a normal static type so later ports can consume dates without ETS.
internal sealed record DateRecord(DateTime Value, string DisplayHint) : IPipelineRecord
{
    public double NumberFor(string property) => throw new ScriptException($"Where-Object does not support property '{property}' for date values.");

    public string TextFor(string column) => column switch
    {
        "Value" or "DateTime" => Value.ToString("O", CultureInfo.InvariantCulture),
        "DisplayHint" => DisplayHint,
        _ => throw new ScriptException($"Select-Object does not support column '{column}' for date values.")
    };
}

// Formatting branches of Get-Date write strings in the source command. This
// small scalar record preserves that distinction instead of pretending a
// formatted date remains a date object.
internal sealed record TextRecord(string Value) : IPipelineRecord
{
    public double NumberFor(string property) => throw new ScriptException($"Where-Object does not support property '{property}' for string values.");
    public string TextFor(string column) => column == "Value"
        ? Value
        : throw new ScriptException($"Select-Object does not support column '{column}' for string values.");
}

// Static equivalent of Microsoft.PowerShell.Commands.FileHashInfo.  It keeps
// the source command's public data (Algorithm, Hash, Path) without requiring
// PSObject formatting/type data at runtime.
internal sealed record FileHashRecord(string Algorithm, string Hash, string Path) : IPipelineRecord
{
    public double NumberFor(string property) => throw new ScriptException($"Where-Object does not support property '{property}' for file-hash values.");

    public string TextFor(string column) => column switch
    {
        "Algorithm" => Algorithm,
        "Hash" => Hash,
        "Path" => Path,
        _ => throw new ScriptException($"Select-Object does not support column '{column}' for file-hash values.")
    };
}

internal interface IAotCmdlet
{
    CmdletDescriptor Descriptor { get; }
    IReadOnlyList<string> DefaultColumns { get; }
    IEnumerable<IPipelineRecord> Invoke(CommandInvocation invocation, AotExecutionContext context);
}

// Shared execution base for ports. It mirrors the existing PowerShell cmdlet
// lifecycle and keeps buffering/error policy in one place. A command port owns
// only its business logic in ProcessRecord.
internal abstract class AotCmdletBase : IAotCmdlet
{
    public abstract CmdletDescriptor Descriptor { get; }
    public abstract IReadOnlyList<string> DefaultColumns { get; }

    public IEnumerable<IPipelineRecord> Invoke(CommandInvocation invocation, AotExecutionContext context)
    {
        using IDisposable scope = context.EnterInvocation(invocation);
        List<IPipelineRecord> output = [];
        try
        {
            output.AddRange(BeginProcessing(context));
            output.AddRange(ProcessRecord(invocation, context));
            output.AddRange(EndProcessing(context));
            return output;
        }
        catch (ScriptException error) when (error.Diagnostic.Span is null && invocation.SourceSpan is not null)
        {
            // Older ports are still migrating their origin diagnostics. Never
            // discard a known command location while that work proceeds.
            throw error.WithSpan(invocation.SourceSpan);
        }
        catch (OperationCanceledException)
        {
            StopProcessing(context);
            throw;
        }
    }

    protected virtual IEnumerable<IPipelineRecord> BeginProcessing(AotExecutionContext context) => [];
    protected abstract IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context);
    protected virtual IEnumerable<IPipelineRecord> EndProcessing(AotExecutionContext context) => [];
    protected virtual void StopProcessing(AotExecutionContext context) { }
}

internal static class AotCmdletRegistry
{
    private static readonly IAotCmdlet[] Cmdlets = [new GetProcessCmdlet(new SystemProcessCatalog()), new GetUptimeCmdlet(), new GetUICultureCmdlet(new SystemHostCulture()), new GetCultureCmdlet(new SystemHostCulture(), new SystemCultureCatalog()), new GetVerbCmdlet(), new GetTimeZoneCmdlet(new SystemTimeZoneCatalog()), new GetDateCmdlet(new SystemClock()), new GetFileHashCmdlet(new SystemPhysicalFileResolver()), new GetHelpCmdlet(CompositeHelpCatalog.Instance), new GetCommandCmdlet(CompositeHelpCatalog.Instance), new GetModuleCmdlet(CompositeModuleCatalog.Instance), new FindModuleCmdlet(RepositoryCatalog.Instance), new InstallModuleCmdlet(new LocalPackageModuleInstaller(RepositoryCatalog.Instance))];

    static AotCmdletRegistry()
    {
        HashSet<string> registryNames = Cmdlets
            .Select(cmdlet => cmdlet.Descriptor.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!registryNames.SetEquals(BuiltInCommandAvailability.NativeAdapterNames.Concat(HostControlPlaneCatalog.NativeAdapterNames)))
        {
            throw new InvalidOperationException(
                "The source-port and host-control-plane availability declarations must exactly match the registered native adapters. "
                + "Update the shared availability declaration when adding or removing an adapter.");
        }
    }

    // Compatibility entry for focused tests. It deliberately delegates source
    // parsing to the same upstream AST front end as normal execution; binding
    // remains centralized in BindCommand below.
    internal static (IAotCmdlet Cmdlet, CommandInvocation Invocation) ParseSource(string stage) =>
        UpstreamAstPipelineLowerer.BindSingleCommand(stage);

    internal static (IAotCmdlet Cmdlet, CommandInvocation Invocation) BindCommand(
        string commandName,
        IEnumerable<CommandSyntaxAtom> arguments,
        AotSourceSpan? commandSpan = null)
    {
        IAotCmdlet? cmdlet = Cmdlets.FirstOrDefault(candidate =>
            candidate.Descriptor.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase));
        if (cmdlet is null)
        {
            throw new ScriptException(AotDiagnostics.Binding("AOT2001", $"Unsupported source command '{commandName}'.", commandSpan));
        }

        Dictionary<string, List<CommandSyntaxAtom>> bound = new(StringComparer.OrdinalIgnoreCase);
        ParameterSpec? current = null;
        foreach (CommandSyntaxAtom atom in arguments)
        {
            if (atom.IsParameter)
            {
                string parameterName = atom.Text;
                current = cmdlet.Descriptor.Parameters.FirstOrDefault(parameter => parameter.Matches(parameterName));
                if (current is null)
                {
                    throw new ScriptException(AotDiagnostics.Binding("AOT2002", $"{cmdlet.Descriptor.Name} does not support parameter '-{parameterName}'.", atom.Span ?? commandSpan));
                }

                if (!bound.TryAdd(current.Name, []))
                {
                    throw new ScriptException(AotDiagnostics.Binding("AOT2003", $"{cmdlet.Descriptor.Name} parameter '-{parameterName}' was specified more than once.", atom.Span ?? commandSpan));
                }

                if (current.Shape == AotParameterShape.Switch)
                {
                    bound[current.Name].Add(new CommandSyntaxAtom("true", IsParameter: false, atom.Span));
                    current = null;
                }

                continue;
            }

            current ??= cmdlet.Descriptor.DefaultParameterName is { } defaultParameterName
                ? cmdlet.Descriptor.Parameters.Single(parameter => parameter.Name.Equals(defaultParameterName, StringComparison.OrdinalIgnoreCase))
                : cmdlet.Descriptor.Parameters.SingleOrDefault(parameter => parameter.IsDefault)
                    ?? throw new ScriptException(AotDiagnostics.Binding("AOT2005", $"{cmdlet.Descriptor.Name} does not accept positional arguments.", atom.Span ?? commandSpan));

            if (!bound.TryGetValue(current.Name, out List<CommandSyntaxAtom>? values))
            {
                values = [];
                bound.Add(current.Name, values);
            }

            // Arrays arrive as separate AST-derived value atoms. Do not split
            // a resolved string on commas here: that would turn a variable
            // value into a hidden second language parser and allow a string to
            // acquire array semantics after lowering.
            values.Add(atom);
        }

        foreach ((string name, List<CommandSyntaxAtom> values) in bound)
        {
            if (values.Count == 0)
            {
                throw new ScriptException(AotDiagnostics.Binding("AOT2004", $"{cmdlet.Descriptor.Name} parameter '-{name}' requires a value.", commandSpan));
            }
        }

        Dictionary<string, string[]> frozen = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, AotSourceSpan?[]> spans = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, List<CommandSyntaxAtom> values) in bound)
        {
            frozen.Add(name, values.Select(static atom => atom.Text).ToArray());
            spans.Add(name, values.Select(static atom => atom.Span).ToArray());
        }

        return (cmdlet, new CommandInvocation(cmdlet.Descriptor, frozen, commandSpan, spans));
    }
}

// Port boundary for Microsoft.PowerShell.Commands.GetProcessCommand:
// Cmdlet => IAotCmdlet; [Parameter] => Descriptor; WriteError => context.
// No System.Management.Automation dependency crosses this boundary.
internal sealed class GetProcessCmdlet(IProcessCatalog catalog) : AotCmdletBase
{
    // Generated from the original Process.cs [Cmdlet], [Parameter], and [Alias]
    // declarations by PwshAotPortGenerator. Every public Get-Process parameter
    // is represented here; InputObject is bound by a preceding pipeline stage.
    private static readonly CmdletDescriptor GetProcessDescriptor = GeneratedCmdletPorts.GetProcess.CreateAotDescriptor("Name", "Id", "InputObject", "IncludeUserName", "Module", "FileVersionInfo");

    public override CmdletDescriptor Descriptor => GetProcessDescriptor;
    public override IReadOnlyList<string> DefaultColumns { get; } = ["Name", "Id", "CPU"];

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        return Execute(invocation, null, context);
    }

    internal IEnumerable<IPipelineRecord> InvokeWithInput(CommandInvocation invocation, IEnumerable<IPipelineRecord> input, AotExecutionContext context)
    {
        ProcessRecord[] processes = input.OfType<ProcessRecord>().ToArray();
        if (processes.Length != input.Count())
        {
            throw new ScriptException("Get-Process accepts only process objects from the incoming pipeline.");
        }

        return Execute(invocation, processes, context);
    }

    private IEnumerable<IPipelineRecord> Execute(CommandInvocation invocation, IReadOnlyList<ProcessRecord>? input, AotExecutionContext context)
    {
        bool hasNames = invocation.TryGetValues("Name", out string[] names);
        bool hasIds = invocation.TryGetValues("Id", out string[] ids);
        bool wantsModules = invocation.TryGetValues("Module", out _);
        bool wantsFileVersion = invocation.TryGetValues("FileVersionInfo", out _);
        bool wantsUserName = invocation.TryGetValues("IncludeUserName", out _);
        if (hasNames && hasIds)
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT3001",
                "Get-Process parameters -Name and -Id cannot be combined.",
                invocation.SourceSpan,
                "conflicting parameter sets",
                "Use either -Name or -Id, not both."));
        }

        if (input is not null && (hasNames || hasIds))
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT3002",
                "Get-Process cannot combine pipeline input with -Name or -Id.",
                invocation.SourceSpan,
                "conflicting input sources",
                "Use pipeline input or an explicit -Name/-Id selection."));
        }

        if (wantsUserName && (wantsModules || wantsFileVersion))
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT3003",
                "Get-Process -IncludeUserName cannot be combined with -Module or -FileVersionInfo.",
                invocation.SourceSpan,
                "conflicting parameters",
                "Use -IncludeUserName alone, or request module/file-version detail."));
        }

        ProcessQuery query = input is not null
            ? ProcessQuery.ByInput(input)
            : hasIds
            ? ProcessQuery.ById(ids.Select((id, index) => ParseId(id, invocation.GetValueSpan("Id", index))).ToArray())
            : hasNames ? ProcessQuery.ByName(names) : ProcessQuery.All;
        IReadOnlyList<IPipelineRecord> processes = ProcessSelector.Select(catalog, query, context);
        if (wantsModules)
        {
            return processes.Cast<ProcessRecord>().SelectMany(process => catalog.Modules(process.Id, wantsFileVersion, context));
        }

        if (wantsFileVersion)
        {
            return processes.Cast<ProcessRecord>()
                .Select(process => catalog.MainFileVersion(process.Id, context))
                .Where(static info => info is not null)
                .Cast<IPipelineRecord>();
        }

        if (wantsUserName)
        {
            return processes.Cast<ProcessRecord>().Select(process => process with { UserName = catalog.UserName(process.Id, context) });
        }

        return processes;
    }

    private static int ParseId(string text, AotSourceSpan? span)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id < 0)
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT3004",
                $"Get-Process -Id expects a non-negative integer, got '{text}'.",
                span,
                "invalid process identifier",
                "Provide a non-negative integer after -Id."));
        }

        return id;
    }
}

// Port boundary for Microsoft.PowerShell.Commands.GetUptimeCommand.  This is
// deliberately a direct static evaluator of its ProcessRecord body: no AST,
// DynamicParameters, SessionState, or PowerShell formatting dependency enters
// the native binary.
internal sealed class GetUptimeCmdlet : AotCmdletBase
{
    private static readonly CmdletDescriptor GetUptimeDescriptor = GeneratedCmdletPorts.GetUptime.CreateAotDescriptor("Since");

    public override CmdletDescriptor Descriptor => GetUptimeDescriptor;
    public override IReadOnlyList<string> DefaultColumns { get; } = ["Value"];

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        if (!Stopwatch.IsHighResolution)
        {
            throw new ScriptException("Get-Uptime is unavailable because Stopwatch is not high resolution on this platform.");
        }

        TimeSpan uptime = TimeSpan.FromSeconds((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
        bool wantsSince = invocation.TryGetValues("Since", out _);
        return [new UptimeRecord(uptime, wantsSince ? DateTime.Now.Subtract(uptime) : null)];
    }
}

internal interface IClock
{
    DateTime Now { get; }
}

internal sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
}

// Port boundary for Microsoft.PowerShell.Commands.GetDateCommand. The source
// command is a deterministic BCL transformation once Cmdlet/PSObject output
// has been replaced with the static AOT lifecycle and DateRecord contract.
internal sealed class GetDateCmdlet(IClock clock) : AotCmdletBase
{
    private const long MinimumUnixTimeSecond = -62135596800;
    private const long MaximumUnixTimeSecond = 253402300799;
    private static readonly CmdletDescriptor GetDateDescriptor = GeneratedCmdletPorts.GetDate.CreateAotDescriptor(
        "Date", "UnixTimeSeconds", "Year", "Month", "Day", "Hour", "Minute", "Second", "Millisecond", "DisplayHint", "UFormat", "Format", "AsUTC");

    public override CmdletDescriptor Descriptor => GetDateDescriptor;
    public override IReadOnlyList<string> DefaultColumns { get; } = ["Value"];

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        bool hasDate = invocation.TryGetValues("Date", out string[] dateValues);
        bool hasUnixTime = invocation.TryGetValues("UnixTimeSeconds", out string[] unixTimeValues);
        bool hasFormat = invocation.TryGetValues("Format", out string[] formatValues);
        bool hasUFormat = invocation.TryGetValues("UFormat", out string[] uformatValues);
        if (hasDate && hasUnixTime)
        {
            throw new ScriptException("Get-Date parameters -Date and -UnixTimeSeconds cannot be combined.");
        }

        if (hasFormat && hasUFormat)
        {
            throw new ScriptException("Get-Date parameters -Format and -UFormat cannot be combined.");
        }

        DateTime dateToUse = hasDate
            ? ParseDate(SingleValue("Date", dateValues))
            : hasUnixTime
            ? DateTimeOffset.FromUnixTimeSeconds(ParseUnixTime(SingleValue("UnixTimeSeconds", unixTimeValues))).LocalDateTime
            : clock.Now;

        dateToUse = ApplyComponent(invocation, "Year", dateToUse, 1, 9999, static (value, offset) => value.AddYears(offset), static value => value.Year);
        dateToUse = ApplyComponent(invocation, "Month", dateToUse, 1, 12, static (value, offset) => value.AddMonths(offset), static value => value.Month);
        dateToUse = ApplyComponent(invocation, "Day", dateToUse, 1, 31, static (value, offset) => value.AddDays(offset), static value => value.Day);
        dateToUse = ApplyComponent(invocation, "Hour", dateToUse, 0, 23, static (value, offset) => value.AddHours(offset), static value => value.Hour);
        dateToUse = ApplyComponent(invocation, "Minute", dateToUse, 0, 59, static (value, offset) => value.AddMinutes(offset), static value => value.Minute);
        dateToUse = ApplyComponent(invocation, "Second", dateToUse, 0, 59, static (value, offset) => value.AddSeconds(offset), static value => value.Second);
        if (invocation.TryGetValues("Millisecond", out string[] milliseconds))
        {
            int millisecond = ParseInt("Millisecond", SingleValue("Millisecond", milliseconds), 0, 999);
            dateToUse = dateToUse.AddMilliseconds(millisecond - dateToUse.Millisecond);
            dateToUse = dateToUse.Subtract(TimeSpan.FromTicks(dateToUse.Ticks % TimeSpan.TicksPerMillisecond));
        }

        if (invocation.TryGetValues("AsUTC", out _))
        {
            dateToUse = dateToUse.ToUniversalTime();
        }

        if (hasUFormat)
        {
            string format = SingleValue("UFormat", uformatValues);
            if (format.Length == 0)
            {
                throw new ScriptException("Get-Date -UFormat requires a non-empty format string.");
            }

            return [new TextRecord(UFormatDateString(dateToUse, format))];
        }

        if (hasFormat)
        {
            string format = SingleValue("Format", formatValues);
            (dateToUse, format) = MapFileFormat(dateToUse, format);
            try
            {
                return [new TextRecord(dateToUse.ToString(format, CultureInfo.CurrentCulture))];
            }
            catch (FormatException error)
            {
                throw new ScriptException($"Get-Date -Format is invalid: {error.Message}");
            }
        }

        string hint = invocation.TryGetValues("DisplayHint", out string[] hints)
            ? ParseDisplayHint(SingleValue("DisplayHint", hints))
            : "DateTime";
        return [new DateRecord(dateToUse, hint)];
    }

    private static DateTime ApplyComponent(CommandInvocation invocation, string name, DateTime date, int minimum, int maximum, Func<DateTime, int, DateTime> apply, Func<DateTime, int> existing)
    {
        if (!invocation.TryGetValues(name, out string[] values))
        {
            return date;
        }

        int replacement = ParseInt(name, SingleValue(name, values), minimum, maximum);
        return apply(date, replacement - existing(date));
    }

    private static string SingleValue(string name, string[] values)
    {
        if (values.Length != 1)
        {
            throw new ScriptException($"Get-Date -{name} accepts exactly one value.");
        }

        return values[0];
    }

    private static DateTime ParseDate(string text)
    {
        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime result)
            || DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out result))
        {
            return result;
        }

        throw new ScriptException($"Get-Date -Date expects a date/time value, got '{text}'.");
    }

    private static int ParseInt(string name, string text, int minimum, int maximum)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < minimum || value > maximum)
        {
            throw new ScriptException($"Get-Date -{name} expects an integer from {minimum} through {maximum}, got '{text}'.");
        }

        return value;
    }

    private static long ParseUnixTime(string text)
    {
        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) || value < MinimumUnixTimeSecond || value > MaximumUnixTimeSecond)
        {
            throw new ScriptException($"Get-Date -UnixTimeSeconds expects an integer from {MinimumUnixTimeSecond} through {MaximumUnixTimeSecond}, got '{text}'.");
        }

        return value;
    }

    private static string ParseDisplayHint(string text) => text.ToLowerInvariant() switch
    {
        "date" => "Date",
        "time" => "Time",
        "datetime" => "DateTime",
        _ => throw new ScriptException($"Get-Date -DisplayHint accepts Date, Time, or DateTime. Got '{text}'.")
    };

    private static (DateTime Date, string Format) MapFileFormat(DateTime date, string format) => format.ToLowerInvariant() switch
    {
        "filedate" => (date, "yyyyMMdd"),
        "filedateuniversal" => (date.ToUniversalTime(), "yyyyMMddZ"),
        "filedatetime" => (date, "yyyyMMddTHHmmssffff"),
        "filedatetimeuniversal" => (date.ToUniversalTime(), "yyyyMMddTHHmmssffffZ"),
        _ => (date, format)
    };

    // Literal static transfer of the source's fixed strftime-style mapping.
    // The source constructs a composite format string and calls StringUtil;
    // direct appends are simpler, explicit, and Native-AOT-safe.
    private static string UFormatDateString(DateTime date, string format)
    {
        System.Text.StringBuilder result = new();
        int start = format[0] == '+' ? 1 : 0;
        for (int index = start; index < format.Length; index++)
        {
            if (format[index] != '%')
            {
                result.Append(format[index]);
                continue;
            }

            if (++index == format.Length)
            {
                throw new ScriptException("Get-Date -UFormat cannot end with '%'.");
            }

            char token = format[index];
            result.Append(token switch
            {
                'A' => date.ToString("dddd", CultureInfo.CurrentCulture),
                'a' => date.ToString("ddd", CultureInfo.CurrentCulture),
                'B' => date.ToString("MMMM", CultureInfo.CurrentCulture),
                'b' or 'h' => date.ToString("MMM", CultureInfo.CurrentCulture),
                'C' => (date.Year / 100).ToString(CultureInfo.InvariantCulture),
                'c' => date.ToString("ddd dd MMM yyyy HH:mm:ss", CultureInfo.CurrentCulture),
                'D' or 'x' => date.ToString("MM/dd/yy", CultureInfo.CurrentCulture),
                'd' => date.ToString("dd", CultureInfo.CurrentCulture),
                'e' => date.Day.ToString(CultureInfo.CurrentCulture).PadLeft(2),
                'F' => date.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture),
                'G' => ISOWeek.GetYear(date).ToString("0000", CultureInfo.InvariantCulture),
                'g' => (ISOWeek.GetYear(date) % 100).ToString("00", CultureInfo.InvariantCulture),
                'H' => date.ToString("HH", CultureInfo.CurrentCulture),
                'I' => date.ToString("hh", CultureInfo.CurrentCulture),
                'j' => date.DayOfYear.ToString("000", CultureInfo.InvariantCulture),
                'k' => date.Hour.ToString("0", CultureInfo.CurrentCulture).PadLeft(2),
                'l' => date.ToString("h", CultureInfo.CurrentCulture).PadLeft(2),
                'M' => date.ToString("mm", CultureInfo.CurrentCulture),
                'm' => date.ToString("MM", CultureInfo.CurrentCulture),
                'n' => "\n",
                'p' => date.ToString("tt", CultureInfo.CurrentCulture),
                'R' => date.ToString("HH:mm", CultureInfo.CurrentCulture),
                'r' => date.ToString("hh:mm:ss tt", CultureInfo.CurrentCulture),
                'S' => date.ToString("ss", CultureInfo.CurrentCulture),
                's' => ((long)(date.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds).ToString(CultureInfo.InvariantCulture),
                'T' or 'X' => date.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
                't' => "\t",
                'U' or 'W' => (date.DayOfYear / 7).ToString(CultureInfo.InvariantCulture),
                'u' => (date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek).ToString(CultureInfo.InvariantCulture),
                'V' => ISOWeek.GetWeekOfYear(date).ToString("00", CultureInfo.InvariantCulture),
                'w' => ((int)date.DayOfWeek).ToString(CultureInfo.InvariantCulture),
                'Y' => date.ToString("yyyy", CultureInfo.CurrentCulture),
                'y' => date.ToString("yy", CultureInfo.CurrentCulture),
                'Z' => date.ToString("zz", CultureInfo.CurrentCulture),
                _ => token.ToString()
            });
        }

        return result.ToString();
    }
}

// This is deliberately a physical-filesystem boundary, not a partial rewrite
// of SessionState.Path.  It gives Get-FileHash an honest native scope today
// and leaves PowerShell providers for a future virtual-file service.
internal interface IPhysicalFileResolver
{
    IEnumerable<string> ResolvePath(string pattern, AotExecutionContext context);
    string ResolveLiteralPath(string path);
    Stream OpenRead(string path);
}

internal sealed class SystemPhysicalFileResolver : IPhysicalFileResolver
{
    public IEnumerable<string> ResolvePath(string pattern, AotExecutionContext context)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(pattern);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            context.WriteNonTerminatingError("FileNotFound", $"File path '{pattern}' is invalid: {error.Message}");
            return [];
        }

        if (!ContainsWildcard(pattern))
        {
            if (!File.Exists(fullPath))
            {
                context.WriteNonTerminatingError("FileNotFound", $"Cannot find file '{fullPath}'.");
                return [];
            }

            return [fullPath];
        }

        string directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        string searchPattern = Path.GetFileName(fullPath);
        try
        {
            // This intentionally supports wildcards in the terminal physical
            // filename component only. Provider and recursive path matching
            // belong to the future virtual-file runtime, not this cmdlet.
            return Directory.EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly)
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray();
        }
        catch (DirectoryNotFoundException)
        {
            // Match the source Path mode: an unmatched wildcard is not an
            // error record. A non-wildcard missing file was handled above.
            return [];
        }
        catch (UnauthorizedAccessException error)
        {
            context.WriteNonTerminatingError("UnauthorizedAccessError", error.Message);
            return [];
        }
        catch (IOException error)
        {
            context.WriteNonTerminatingError("FileReadError", error.Message);
            return [];
        }
    }

    public string ResolveLiteralPath(string path) => Path.GetFullPath(path);

    public Stream OpenRead(string path) => File.OpenRead(path);

    private static bool ContainsWildcard(string path) => path.IndexOfAny(['*', '?']) >= 0;
}

// Port boundary for Microsoft.PowerShell.Commands.GetFileHashCommand:
// HashCmdletBase/PSCmdlet -> AotCmdletBase; FileHashInfo -> FileHashRecord.
// The algorithm switch is a direct BCL transfer; provider and stream binding
// are intentionally not represented as faux command-line strings.
internal sealed class GetFileHashCmdlet(IPhysicalFileResolver files) : AotCmdletBase
{
    private static readonly string[] Algorithms = ["SHA1", "SHA256", "SHA384", "SHA512", "MD5"];

    public override CmdletDescriptor Descriptor { get; } = CreateDescriptor();
    public override IReadOnlyList<string> DefaultColumns { get; } = ["Algorithm", "Hash", "Path"];

    private static CmdletDescriptor CreateDescriptor()
    {
        CmdletDescriptor generated = GeneratedCmdletPorts.GetFileHash.CreateAotDescriptor("Path", "LiteralPath", "Algorithm");
        return new CmdletDescriptor(generated.Name, generated.Parameters, "Path");
    }

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        bool hasPath = invocation.TryGetValues("Path", out string[] paths);
        bool hasLiteralPath = invocation.TryGetValues("LiteralPath", out string[] literalPaths);
        if (hasPath == hasLiteralPath)
        {
            throw new ScriptException(hasPath
                ? "Get-FileHash parameters -Path and -LiteralPath cannot be combined."
                : "Get-FileHash requires -Path or -LiteralPath.");
        }

        string algorithm = ParseAlgorithm(invocation);
        IEnumerable<string> resolvedPaths = hasPath
            ? paths.SelectMany(path => files.ResolvePath(path, context))
            : literalPaths.Select(files.ResolveLiteralPath);

        foreach (string path in resolvedPaths)
        {
            if (TryComputeFileHash(path, algorithm, context, out FileHashRecord? result))
            {
                yield return result!;
            }
        }
    }

    private string ParseAlgorithm(CommandInvocation invocation)
    {
        if (!invocation.TryGetValues("Algorithm", out string[] values))
        {
            return "SHA256";
        }

        if (values.Length != 1)
        {
            throw new ScriptException("Get-FileHash -Algorithm accepts exactly one algorithm.");
        }

        string algorithm = values[0].ToUpperInvariant();
        if (!Algorithms.Contains(algorithm, StringComparer.Ordinal))
        {
            throw new ScriptException($"Get-FileHash -Algorithm must be one of: {string.Join(", ", Algorithms)}.");
        }

        return algorithm;
    }

    private bool TryComputeFileHash(string path, string algorithm, AotExecutionContext context, out FileHashRecord? result)
    {
        result = null;
        try
        {
            using Stream stream = files.OpenRead(path);
            byte[] bytes = algorithm switch
            {
                "SHA1" => SHA1.HashData(stream),
                "SHA256" => SHA256.HashData(stream),
                "SHA384" => SHA384.HashData(stream),
                "SHA512" => SHA512.HashData(stream),
                "MD5" => MD5.HashData(stream),
                _ => throw new InvalidOperationException($"Unexpected validated hash algorithm '{algorithm}'.")
            };
            result = new FileHashRecord(algorithm, Convert.ToHexString(bytes), path);
            return true;
        }
        catch (FileNotFoundException error)
        {
            context.WriteNonTerminatingError("FileNotFound", error.Message);
        }
        catch (DirectoryNotFoundException error)
        {
            context.WriteNonTerminatingError("FileNotFound", error.Message);
        }
        catch (UnauthorizedAccessException error)
        {
            context.WriteNonTerminatingError("UnauthorizedAccessError", error.Message);
        }
        catch (IOException error)
        {
            context.WriteNonTerminatingError("FileReadError", error.Message);
        }

        return false;
    }
}

internal interface IHostCulture
{
    CultureInfo CurrentUICulture { get; }
    CultureInfo CurrentCulture { get; }
}

// PowerShell asks PSHost for this value.  The AOT executable has no dynamic
// host object, so the narrow static replacement is the .NET UI culture.
internal sealed class SystemHostCulture : IHostCulture
{
    public CultureInfo CurrentUICulture => CultureInfo.CurrentUICulture;
    public CultureInfo CurrentCulture => CultureInfo.CurrentCulture;
}

// Port boundary for Microsoft.PowerShell.Commands.GetUICultureCommand. The
// source writes during BeginProcessing; the AOT base exposes output from that
// lifecycle phase so the behavior survives without a PSHost or PSObject.
internal sealed class GetUICultureCmdlet(IHostCulture hostCulture) : AotCmdletBase
{
    private static readonly CmdletDescriptor GetUICultureDescriptor = GeneratedCmdletPorts.GetUICulture.CreateAotDescriptor();

    public override CmdletDescriptor Descriptor => GetUICultureDescriptor;
    public override IReadOnlyList<string> DefaultColumns { get; } = ["Name", "DisplayName"];

    protected override IEnumerable<IPipelineRecord> BeginProcessing(AotExecutionContext context)
    {
        CultureInfo culture = hostCulture.CurrentUICulture;
        return [new CultureRecord(culture.Name, culture.DisplayName, culture.EnglishName, culture.LCID)];
    }

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context) => [];
}

internal interface ICultureCatalog
{
    CultureInfo GetCulture(string name);
    IEnumerable<CultureInfo> AllCultures();
}

internal sealed class SystemCultureCatalog : ICultureCatalog
{
    public CultureInfo GetCulture(string name) => CultureInfo.GetCultureInfo(name);
    public IEnumerable<CultureInfo> AllCultures() => CultureInfo.GetCultures(CultureTypes.AllCultures);
}

// Port boundary for Microsoft.PowerShell.Commands.GetCultureCommand.  The
// resolver owns BCL culture lookup so the cmdlet stays a direct translation of
// the source's three parameter-set behaviours.
internal sealed class GetCultureCmdlet(IHostCulture hostCulture, ICultureCatalog cultures) : AotCmdletBase
{
    private static readonly CmdletDescriptor GetCultureDescriptor = GeneratedCmdletPorts.GetCulture.CreateAotDescriptor("Name", "NoUserOverrides", "ListAvailable");

    public override CmdletDescriptor Descriptor => GetCultureDescriptor;
    public override IReadOnlyList<string> DefaultColumns { get; } = ["Name", "DisplayName"];

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        bool hasNames = invocation.TryGetValues("Name", out string[] names);
        bool listAvailable = invocation.TryGetValues("ListAvailable", out _);
        bool noUserOverrides = invocation.TryGetValues("NoUserOverrides", out _);
        if (listAvailable && (hasNames || noUserOverrides))
        {
            throw new ScriptException("Get-Culture -ListAvailable cannot be combined with -Name or -NoUserOverrides.");
        }

        if (listAvailable)
        {
            return cultures.AllCultures().Select(ToRecord).ToArray();
        }

        if (!hasNames)
        {
            CultureInfo current = hostCulture.CurrentCulture;
            return [ToRecord(noUserOverrides ? cultures.GetCulture(current.Name) : current)];
        }

        List<IPipelineRecord> output = [];
        try
        {
            foreach (string name in names)
            {
                CultureInfo current = hostCulture.CurrentCulture;
                CultureInfo culture = !noUserOverrides && name.Equals(current.Name, StringComparison.CurrentCultureIgnoreCase)
                    ? current
                    : cultures.GetCulture(name);
                output.Add(ToRecord(culture));
            }
        }
        catch (CultureNotFoundException error)
        {
            context.WriteNonTerminatingError("ItemNotFoundException", error.Message);
        }

        return output;
    }

    private static CultureRecord ToRecord(CultureInfo culture) => new(culture.Name, culture.DisplayName, culture.EnglishName, culture.LCID);
}

internal interface ITimeZoneCatalog
{
    void Refresh();
    TimeZoneInfo Local { get; }
    IEnumerable<TimeZoneInfo> AllTimeZones();
    TimeZoneInfo FindById(string id);
}

// TimeZoneInfo is BCL data, and is preserved by Native AOT. The interface
// makes the platform zone database and its differing Windows/IANA identifiers
// explicit and gives the port a deterministic fixture boundary.
internal sealed class SystemTimeZoneCatalog : ITimeZoneCatalog
{
    public void Refresh() => TimeZoneInfo.ClearCachedData();
    public TimeZoneInfo Local => TimeZoneInfo.Local;
    public IEnumerable<TimeZoneInfo> AllTimeZones() => TimeZoneInfo.GetSystemTimeZones();
    public TimeZoneInfo FindById(string id) => TimeZoneInfo.FindSystemTimeZoneById(id);
}

// Port boundary for Microsoft.PowerShell.Commands.GetTimeZoneCommand. Its
// ProcessRecord has three BCL-backed parameter-set paths: local default,
// list, and individual Id/standard-or-daylight-name lookup. The source helper
// matches StandardName then DaylightName using a PowerShell WildcardPattern;
// the shared simple wildcard policy is intentionally documented separately.
internal sealed class GetTimeZoneCmdlet(ITimeZoneCatalog zones) : AotCmdletBase
{
    private static readonly CmdletDescriptor GetTimeZoneDescriptor = GeneratedCmdletPorts.GetTimeZone.CreateAotDescriptor("Id", "ListAvailable", "Name");

    public override CmdletDescriptor Descriptor => GetTimeZoneDescriptor;
    public override IReadOnlyList<string> DefaultColumns { get; } = ["Id", "DisplayName", "StandardName"];

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        zones.Refresh();
        bool hasIds = invocation.TryGetValues("Id", out string[] ids);
        bool listAvailable = invocation.TryGetValues("ListAvailable", out _);
        bool hasNames = invocation.TryGetValues("Name", out string[] names);
        if ((hasIds && hasNames) || (listAvailable && (hasIds || hasNames)))
        {
            throw new ScriptException("Get-TimeZone accepts exactly one of -Id, -Name, or -ListAvailable.");
        }

        if (listAvailable)
        {
            return zones.AllTimeZones().Select(ToRecord).ToArray();
        }

        if (hasIds)
        {
            List<IPipelineRecord> output = [];
            foreach (string id in ids)
            {
                try
                {
                    output.Add(ToRecord(zones.FindById(id)));
                }
                catch (TimeZoneNotFoundException error)
                {
                    context.WriteNonTerminatingError("TimeZoneNotFound", error.Message);
                }
                catch (InvalidTimeZoneException error)
                {
                    context.WriteNonTerminatingError("TimeZoneNotFound", error.Message);
                }
            }

            return output;
        }

        if (!hasNames)
        {
            return [ToRecord(zones.Local)];
        }

        TimeZoneInfo[] available = zones.AllTimeZones().ToArray();
        List<IPipelineRecord> matches = [];
        foreach (string name in names)
        {
            TimeZoneInfo[] namedMatches = available
                .Where(zone => SimpleWildcard.IsMatch(name, zone.StandardName) || SimpleWildcard.IsMatch(name, zone.DaylightName))
                .ToArray();
            if (namedMatches.Length == 0)
            {
                context.WriteNonTerminatingError("TimeZoneNotFound", $"The specified time zone name '{name}' was not found.");
                continue;
            }

            matches.AddRange(namedMatches.Select(ToRecord));
        }

        return matches;
    }

    private static TimeZoneRecord ToRecord(TimeZoneInfo zone) => new(
        zone.Id,
        zone.DisplayName,
        zone.StandardName,
        zone.DaylightName,
        zone.BaseUtcOffset,
        zone.SupportsDaylightSavingTime);
}

// Port boundary for Microsoft.PowerShell.Commands.GetVerbCommand.  The
// original's ProcessRecord stays a filter-and-write loop.  Its reflective
// Verbs.FilterByVerbsAndGroups helper becomes an immutable source-generated
// catalogue, keeping executable runtime code free of reflection/resources.
internal sealed class GetVerbCmdlet : AotCmdletBase
{
    private static readonly CmdletDescriptor GetVerbDescriptor = GeneratedCmdletPorts.GetVerb.CreateAotDescriptor("Verb", "Group");
    private static readonly string[] ValidGroups = ["Common", "Communications", "Data", "Diagnostic", "Lifecycle", "Other", "Security"];

    public override CmdletDescriptor Descriptor => GetVerbDescriptor;
    public override IReadOnlyList<string> DefaultColumns { get; } = ["Verb", "AliasPrefix", "Group", "Description"];

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        bool hasVerbs = invocation.TryGetValues("Verb", out string[] verbs);
        bool hasGroups = invocation.TryGetValues("Group", out string[] groups);
        if (hasGroups)
        {
            foreach (string group in groups)
            {
                if (!ValidGroups.Contains(group, StringComparer.OrdinalIgnoreCase))
                {
                    throw new ScriptException($"Get-Verb -Group accepts only: {string.Join(", ", ValidGroups)}. Got '{group}'.");
                }
            }
        }

        return GeneratedCmdletPorts.GetVerbCatalog
            .Where(info => !hasGroups || groups.Any(group => group.Equals(info.Group, StringComparison.OrdinalIgnoreCase)))
            .Where(info => !hasVerbs || verbs.Any(pattern => SimpleWildcard.IsMatch(pattern, info.Verb)))
            .Select(static info => (IPipelineRecord)new VerbRecord(info.Verb, info.AliasPrefix, info.Group, info.Description))
            .ToArray();
    }
}

internal enum ProcessMatchMode { All, ByName, ById, ByInput }

internal sealed record ProcessQuery(ProcessMatchMode Mode, IReadOnlyList<string> Names, IReadOnlyList<int> Ids)
{
    internal static ProcessQuery All { get; } = new(ProcessMatchMode.All, [], []);
    internal static ProcessQuery ByName(IReadOnlyList<string> names) => new(ProcessMatchMode.ByName, names, []);
    internal static ProcessQuery ById(IReadOnlyList<int> ids) => new(ProcessMatchMode.ById, [], ids);
    internal static ProcessQuery ByInput(IReadOnlyList<ProcessRecord> input) => new(ProcessMatchMode.ByInput, input.Select(process => process.Name).ToArray(), input.Select(process => process.Id).ToArray());
}

internal interface IProcessCatalog
{
    IEnumerable<ProcessRecord> AllProcesses();
    ProcessRecord? GetById(int id);
    IEnumerable<ProcessModuleRecord> Modules(int id, bool includeFileVersion, AotExecutionContext context);
    ProcessFileVersionRecord? MainFileVersion(int id, AotExecutionContext context);
    string? UserName(int id, AotExecutionContext context);
}

// A direct AOT adaptation of ProcessBaseCommand.MatchingProcesses: selection
// modes, duplicate removal, non-terminating errors, and sort order stay here.
internal static class ProcessSelector
{
    internal static IReadOnlyList<IPipelineRecord> Select(IProcessCatalog catalog, ProcessQuery query, AotExecutionContext context)
    {
        List<ProcessRecord> matches = [];
        HashSet<int> keys = [];

        void AddIdempotent(ProcessRecord process)
        {
            if (keys.Add(HashCode.Combine(process.Name, process.Id)))
            {
                matches.Add(process);
            }
        }

        switch (query.Mode)
        {
            case ProcessMatchMode.All:
                foreach (ProcessRecord process in catalog.AllProcesses())
                {
                    AddIdempotent(process);
                }

                break;

            case ProcessMatchMode.ByName:
                ProcessRecord[] allProcesses = catalog.AllProcesses().ToArray();
                foreach (string pattern in query.Names)
                {
                    bool found = false;
                    foreach (ProcessRecord process in allProcesses)
                    {
                        if (!SimpleWildcard.IsMatch(pattern, process.Name))
                        {
                            continue;
                        }

                        found = true;
                        AddIdempotent(process);
                    }

                    if (!found && !SimpleWildcard.ContainsWildcard(pattern))
                    {
                        string id = int.TryParse(pattern, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) && number >= 0
                            ? "RecommendIdTagForGivenName"
                            : "NoProcessFoundForGivenName";
                        context.WriteNonTerminatingError(id, $"No process was found with the name '{pattern}'.");
                    }
                }

                break;

            case ProcessMatchMode.ById:
                foreach (int id in query.Ids)
                {
                    ProcessRecord? process = catalog.GetById(id);
                    if (process is null)
                    {
                        context.WriteNonTerminatingError("NoProcessFoundForGivenId", $"No process was found with the process identifier {id}.");
                        continue;
                    }

                    AddIdempotent(process);
                }

                break;

            case ProcessMatchMode.ByInput:
                foreach (int id in query.Ids)
                {
                    ProcessRecord? process = catalog.GetById(id);
                    if (process is not null)
                    {
                        AddIdempotent(process);
                    }
                }

                break;

            default:
                throw new InvalidOperationException("Unknown process selection mode.");
        }

        matches.Sort(static (left, right) =>
        {
            int nameOrder = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            return nameOrder != 0 ? nameOrder : left.Id.CompareTo(right.Id);
        });
        return matches;
    }
}

// AOT-safe subset of PowerShell wildcard behavior used by this port: '*' and
// '?', case-insensitive. The adapter isolates this policy for later upgrade.
internal static class SimpleWildcard
{
    internal static bool ContainsWildcard(string pattern) => pattern.Contains('*') || pattern.Contains('?');

    internal static bool IsMatch(string pattern, string value)
    {
        int patternIndex = 0;
        int valueIndex = 0;
        int star = -1;
        int retryValueIndex = 0;

        while (valueIndex < value.Length)
        {
            if (patternIndex < pattern.Length &&
                (pattern[patternIndex] == '?' || char.ToUpperInvariant(pattern[patternIndex]) == char.ToUpperInvariant(value[valueIndex])))
            {
                patternIndex++;
                valueIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                star = patternIndex++;
                retryValueIndex = valueIndex;
            }
            else if (star >= 0)
            {
                patternIndex = star + 1;
                valueIndex = ++retryValueIndex;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }
}

internal sealed class SystemProcessCatalog : IProcessCatalog
{
    public IEnumerable<ProcessRecord> AllProcesses()
    {
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                if (TryRead(process, out ProcessRecord? row))
                {
                    yield return row!;
                }
            }
        }
    }

    public ProcessRecord? GetById(int id)
    {
        try
        {
            using Process process = Process.GetProcessById(id);
            return TryRead(process, out ProcessRecord? row) ? row : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public IEnumerable<ProcessModuleRecord> Modules(int id, bool includeFileVersion, AotExecutionContext context)
    {
        List<ProcessModuleRecord> modules = [];
        try
        {
            using Process process = Process.GetProcessById(id);
            foreach (ProcessModule module in process.Modules)
            {
                string version = string.Empty;
                if (includeFileVersion)
                {
                    try
                    {
                        version = module.FileVersionInfo?.FileVersion ?? string.Empty;
                    }
                    catch (Exception error) when (error is InvalidOperationException or ArgumentException or Win32Exception)
                    {
                        context.WriteNonTerminatingError("CouldNotEnumerateModuleFileVer", error.Message);
                    }
                }

                modules.Add(new ProcessModuleRecord(id, module.ModuleName ?? string.Empty, module.FileName ?? string.Empty, version));
            }
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or Win32Exception or PlatformNotSupportedException)
        {
            context.WriteNonTerminatingError("CouldNotEnumerateModules", error.Message);
        }

        return modules;
    }

    public ProcessFileVersionRecord? MainFileVersion(int id, AotExecutionContext context)
    {
        try
        {
            using Process process = Process.GetProcessById(id);
            ProcessModule? module = process.MainModule;
            if (module is null)
            {
                return null;
            }

            FileVersionInfo? version = module.FileVersionInfo;
            return version is null ? null : new ProcessFileVersionRecord(id, module.FileName ?? string.Empty, version.FileVersion ?? string.Empty, version.ProductVersion ?? string.Empty);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or Win32Exception or PlatformNotSupportedException)
        {
            context.WriteNonTerminatingError("CouldNotEnumerateFileVer", error.Message);
            return null;
        }
    }

    public string? UserName(int id, AotExecutionContext context)
    {
        if (OperatingSystem.IsWindows())
        {
            context.WriteNonTerminatingError("CouldNotRetrieveUserName", "Windows user lookup is not implemented by this AOT port.");
            return null;
        }

        try
        {
            ProcessStartInfo startInfo = new("/bin/ps")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add("user=");
            startInfo.ArgumentList.Add("-p");
            startInfo.ArgumentList.Add(id.ToString(CultureInfo.InvariantCulture));
            using Process? process = Process.Start(startInfo);
            string? user = process?.StandardOutput.ReadToEnd().Trim();
            process?.WaitForExit();
            return string.IsNullOrWhiteSpace(user) ? null : user;
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception or System.IO.IOException)
        {
            context.WriteNonTerminatingError("CouldNotRetrieveUserName", error.Message);
            return null;
        }
    }

    private static bool TryRead(Process process, out ProcessRecord? row)
    {
        try
        {
            row = new ProcessRecord(process.ProcessName, process.Id, process.TotalProcessorTime.TotalSeconds, process.WorkingSet64);
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }

        row = null;
        return false;
    }
}

internal sealed class PipelinePlan(
    IAotCmdlet source,
    CommandInvocation invocation,
    IAotCmdlet? inputCommand,
    CommandInvocation? inputInvocation,
    Filter? filter,
    IReadOnlyList<string> planColumns,
    bool projected = false,
    AotSourceSpan? projectionSpan = null)
{
    internal IReadOnlyList<string> Columns { get; } = planColumns;

    internal IReadOnlyList<IPipelineRecord> Execute(AotExecutionContext context)
    {
        IEnumerable<IPipelineRecord> rows = source.Invoke(invocation, context);
        if (inputCommand is not null)
        {
            if (inputCommand is not GetProcessCmdlet getProcess || inputInvocation is null)
            {
                throw new ScriptException("Unsupported command pipeline stage.");
            }

            rows = getProcess.InvokeWithInput(inputInvocation, rows, context);
        }
        // Ports retain their strongly typed output records.  The first generic
        // stage is the explicit, closed crossing into AotValue/AotRecord; no
        // reflection or CLR-member adaptation is available after this point.
        if (filter is null && !projected)
        {
            return rows.ToArray();
        }

        IEnumerable<AotValue> values = rows.Select(PipelineValueAdapter.ToValue);
        if (filter is not null)
        {
            values = values.Where(filter.Matches);
        }

        if (projected)
        {
            values = values.Select(value => Project(value, Columns, projectionSpan));
        }

        // Individual command ports own their source ordering. Get-Process
        // retains its original sort in ProcessSelector; this stage only filters
        // and projects the corresponding immutable AOT values.
        return values.Select(ToCompatibilityRecord).Cast<IPipelineRecord>().ToArray();
    }

    private static AotValue Project(AotValue value, IReadOnlyList<string> columns, AotSourceSpan? projectionSpan)
    {
        if (!value.TryGetRecord(out AotRecord? record))
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT4006",
                "Select-Object requires a record-shaped AOT pipeline value.",
                projectionSpan,
                "projection requires a record",
                "Select fields from a command that emits record-shaped AOT values."));
        }

        HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);
        foreach (string column in columns)
        {
            if (!selected.Add(column))
            {
                // AotRecord is immutable and case-insensitive, so preserving
                // both `id` and `ID` would create an invalid result shape.
                // Reject rather than silently choosing, renaming, or exposing
                // a CLR fallback.
                throw new ScriptException(AotDiagnostics.Runtime(
                    "AOT4007",
                    "Select-Object does not permit duplicate fields that differ only by case in the AOT subset.",
                    projectionSpan,
                    "duplicate projection field",
                    "Select each field only once."));
            }
        }

        try
        {
            return AotValue.FromRecord(record!.Project(columns));
        }
        catch (KeyNotFoundException)
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT4008",
                "Select-Object requested a column not present on this pipeline value.",
                projectionSpan,
                "unknown projection field",
                "Use a field exposed by the preceding AOT pipeline record."));
        }
    }

    private static AotPipelineRecord ToCompatibilityRecord(AotValue value)
    {
        if (!value.TryGetRecord(out AotRecord? record))
        {
            throw new ScriptException("The AOT table renderer requires a record-shaped pipeline value.");
        }

        return new AotPipelineRecord(record!);
    }
}

internal sealed class Filter(string property, Comparison comparison, AotValue value, AotSourceSpan? propertySpan = null)
{
    internal bool Matches(AotValue row)
    {
        if (!row.TryGetProperty(property, out AotValue propertyValue)
            || !AotValueComparison.TryCompare(propertyValue, value, out int result))
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT4005",
                $"Where-Object does not support property '{property}' for this pipeline value.",
                propertySpan,
                "unsupported pipeline property",
                "Use a numeric field exposed by the preceding AOT pipeline record."));
        }

        return comparison switch
        {
            Comparison.GreaterThan => result > 0,
            Comparison.GreaterThanOrEqual => result >= 0,
            Comparison.LessThan => result < 0,
            Comparison.LessThanOrEqual => result <= 0,
            Comparison.Equal => result == 0,
            Comparison.NotEqual => result != 0,
            _ => throw new InvalidOperationException("Unknown comparison.")
        };
    }
}

internal enum Comparison { GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual, Equal, NotEqual }

// Semantic helpers for the narrow generic verbs. They receive AST-derived
// atoms; they do not parse PowerShell source.
internal static class ScriptParser
{
    // Transitional source-compatible name for existing focused tests. This is
    // not a parser implementation: every call goes through the upstream AST.
    internal static PipelinePlan Parse(string script) => UpstreamAstPipelineLowerer.Parse(script);

    internal static Filter ParseFilterArguments(
        IReadOnlyList<string> tokens,
        AotSourceSpan? span = null,
        AotSourceSpan? propertySpan = null,
        AotSourceSpan? operatorSpan = null)
    {
        if (tokens.Count != 3 || !double.TryParse(tokens[2], CultureInfo.InvariantCulture, out double value))
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT4001",
                "Where-Object expects: <property> <-gt|-ge|-lt|-le|-eq|-ne> <number>.",
                span,
                "invalid predicate",
                "Provide a direct property, comparison operator, and finite numeric value."));
        }

        if (!double.IsFinite(value))
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT4002",
                "Where-Object does not accept NaN or infinity predicate literals in the AOT subset.",
                span,
                "non-finite predicate value",
                "Provide a finite numeric comparison value."));
        }

        string property = tokens[0].TrimStart('$').TrimStart('_').TrimStart('.');
        Comparison comparison = ParseComparison(tokens[1], operatorSpan ?? span);

        return new Filter(property, comparison, AotValue.FromFloatingPoint(value), propertySpan);
    }

    internal static Comparison ParseComparison(string token, AotSourceSpan? span) => token.ToLowerInvariant() switch
        {
            "-gt" => Comparison.GreaterThan,
            "-ge" => Comparison.GreaterThanOrEqual,
            "-lt" => Comparison.LessThan,
            "-le" => Comparison.LessThanOrEqual,
            "-eq" => Comparison.Equal,
            "-ne" => Comparison.NotEqual,
            _ => throw new ScriptException(AotDiagnostics.Runtime(
                "AOT4003",
                $"Unsupported comparison '{token}'.",
                span,
                "unsupported comparison",
                "Use -gt, -ge, -lt, -le, -eq, or -ne."))
        };

    private static string[] ParseColumns(string input) => ParseColumns([input]);

    internal static string[] ParseColumns(IReadOnlyList<string> rawColumns, AotSourceSpan? span = null)
    {
        string[] columns = rawColumns
            .SelectMany(static value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .ToArray();
        if (columns.Length == 0)
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT4004",
                "Select-Object requires at least one column.",
                span,
                "missing projection column",
                "Provide one or more direct field names."));
        }

        // The closed AotRecord is the property authority. A global string
        // whitelist would duplicate every adapter's contract, discard its
        // case-insensitive lookup rule, and make a newly explicit field
        // unreachable. PipelinePlan.Project validates each requested field
        // against the actual immutable record and emits a stable error.
        return columns;
    }
}

internal static class TableWriter
{
    internal static void Write(IReadOnlyList<IPipelineRecord> rows, IReadOnlyList<string> columns)
    {
        // Help is terminal prose, not a one-column table. Keeping it a typed
        // pipeline record still lets ScriptRunner share the normal execution
        // path without adding a special stdout path to Get-Help itself.
        if (columns.Count == 1 && columns[0] == "Value" && rows.All(static row => row is HelpRecord))
        {
            foreach (HelpRecord help in rows.Cast<HelpRecord>())
            {
                Console.WriteLine(help.Content);
            }

            return;
        }

        int[] widths = columns.Select(static column => column.Length).ToArray();
        foreach (IPipelineRecord row in rows)
        {
            for (int index = 0; index < columns.Count; index++)
            {
                widths[index] = Math.Max(widths[index], row.TextFor(columns[index]).Length);
            }
        }

        WriteLine(columns, widths);
        WriteLine(widths.Select(static width => new string('-', width)).ToArray(), widths);
        foreach (IPipelineRecord row in rows)
        {
            WriteLine(columns.Select(row.TextFor).ToArray(), widths);
        }
    }

    private static void WriteLine(IReadOnlyList<string> values, IReadOnlyList<int> widths)
    {
        Console.WriteLine(string.Join("  ", values.Select((value, index) => value.PadRight(widths[index]))));
    }
}

internal static class SelfTest
{
    internal static void Run()
    {
        AssertExecutionKernelAndDiagnostics();
        AssertLanguageCompatibilityCore();
        AssertControlFlowCore();
        AssertTerminalPresentation();
        AssertClosedValuePlane();

        if (GeneratedCmdletPorts.Count != 290)
        {
            throw new InvalidOperationException($"Cmdlet generator discovered {GeneratedCmdletPorts.Count} cmdlets; expected 290.");
        }

        if (GeneratedCmdletPorts.All.Count != GeneratedCmdletPorts.Count
            || !GeneratedCmdletPorts.All.Any(static topic => topic.Name == "Get-ChildItem"))
        {
            throw new InvalidOperationException("Generated built-in help catalog regression.");
        }

        if (!CompositeHelpCatalog.BuiltIns()
                .Where(static topic => topic.ExecutionKind == "in-process-native-aot")
                .Select(static topic => topic.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(BuiltInCommandAvailability.NativeAdapterNames))
        {
            throw new InvalidOperationException("Built-in catalog availability diverged from the single native-adapter declaration.");
        }

        PipelinePlan builtInHelpPlan = ScriptParser.Parse("Get-Help Get-ChildItem");
        IReadOnlyList<IPipelineRecord> builtInHelpRows = builtInHelpPlan.Execute(new AotExecutionContext());
        if (builtInHelpRows.SingleOrDefault() is not HelpRecord { Content: var builtInHelp }
            || !builtInHelp.Contains("catalogued from source; no native AOT adapter", StringComparison.Ordinal)
            || !builtInHelp.Contains("-Path <string[]>", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Get-Help did not render the generated built-in contract.");
        }

        PipelinePlan extensionHelpPlan = ScriptParser.Parse("Get-Help -Name Start-ThreadJob");
        IReadOnlyList<IPipelineRecord> extensionHelpRows = extensionHelpPlan.Execute(new AotExecutionContext());
        if (extensionHelpRows.SingleOrDefault() is not HelpRecord { Content: var extensionHelp }
            || !extensionHelp.Contains("Starts a PowerShell job in a separate thread", StringComparison.Ordinal)
            || !extensionHelp.Contains("legacy-pwsh-sidecar / registered-not-invoked", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Get-Help did not discover the registered binary extension package.");
        }

        PipelinePlan archiveHelpPlan = ScriptParser.Parse("Get-Help Compress-Archive");
        IReadOnlyList<IPipelineRecord> archiveHelpRows = archiveHelpPlan.Execute(new AotExecutionContext());
        if (archiveHelpRows.SingleOrDefault() is not HelpRecord { Content: var archiveHelp }
            || !archiveHelp.Contains("Microsoft.PowerShell.Archive", StringComparison.Ordinal)
            || !archiveHelp.Contains("legacy-pwsh-sidecar / registered-not-invoked", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Get-Help did not discover the registered script extension package.");
        }

        PipelinePlan declarationHelpPlan = ScriptParser.Parse("Get-Help Get-KubeResource");
        IReadOnlyList<IPipelineRecord> declarationHelpRows = declarationHelpPlan.Execute(new AotExecutionContext());
        if (declarationHelpRows.SingleOrDefault() is not HelpRecord { Content: var declarationHelp }
            || !declarationHelp.Contains("Retrieve the available api-resources", StringComparison.Ordinal)
            || !declarationHelp.Contains("declaration-only / blocked-not-imported", StringComparison.Ordinal)
            || !declarationHelp.Contains("execution blocked", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Get-Help did not render the safe declaration-only extension package.");
        }

        PipelinePlan builtInCommandPlan = ScriptParser.Parse("Get-Command Get-ChildItem");
        if (builtInCommandPlan.Execute(new AotExecutionContext()).SingleOrDefault() is not CommandInfoRecord
            {
                Name: "Get-ChildItem", CommandType: "Cmdlet", ModuleName: "PowerShell.BuiltIn", Availability: "catalogued-only",
            })
        {
            throw new InvalidOperationException("Get-Command did not query the built-in source catalog.");
        }

        PipelinePlan nativeCommandPlan = ScriptParser.Parse("Get-Command Get-Command");
        if (nativeCommandPlan.Execute(new AotExecutionContext()).SingleOrDefault() is not CommandInfoRecord
            {
                Availability: "native-aot",
            })
        {
            throw new InvalidOperationException("Get-Command did not visibly distinguish a native adapter from catalogued-only commands.");
        }

        PipelinePlan binaryExtensionCommandPlan = ScriptParser.Parse("Get-Command Start-ThreadJob");
        if (binaryExtensionCommandPlan.Execute(new AotExecutionContext()).SingleOrDefault() is not CommandInfoRecord
            {
                CommandType: "Cmdlet", ModuleName: "Microsoft.PowerShell.ThreadJob", Version: "2.2.0", Availability: "legacy-bridge-pending",
            })
        {
            throw new InvalidOperationException("Get-Command did not query the registered binary extension catalog.");
        }

        PipelinePlan moduleCommandPlan = ScriptParser.Parse("Get-Command -Module Microsoft.PowerShell.ThreadJob");
        if (moduleCommandPlan.Execute(new AotExecutionContext()).SingleOrDefault() is not CommandInfoRecord
            {
                Name: "Start-ThreadJob",
            })
        {
            throw new InvalidOperationException("Get-Command -Module filter regression.");
        }

        PipelinePlan wildcardFunctionPlan = ScriptParser.Parse("Get-Command Get-Kube* -CommandType Function");
        IReadOnlyList<IPipelineRecord> wildcardFunctions = wildcardFunctionPlan.Execute(new AotExecutionContext());
        if (wildcardFunctions.Count != 2
            || wildcardFunctions.Any(static row => row is not CommandInfoRecord { ModuleName: "Microsoft.PowerShell.KubeCtl", CommandType: "Function" }))
        {
            throw new InvalidOperationException("Get-Command wildcard/type filtering regression.");
        }

        PipelinePlan modulePlan = ScriptParser.Parse("Get-Module");
        IReadOnlyList<IPipelineRecord> modules = modulePlan.Execute(new AotExecutionContext());
        if (modules.Count != 5
            || modules.SingleOrDefault(static row => row is ModuleInfoRecord { Name: "PowerShell.BuiltIn" }) is not ModuleInfoRecord { Availability: "mixed", Status: var builtInModuleStatus }
            || !builtInModuleStatus.Contains("native-aot", StringComparison.Ordinal)
            || modules.SingleOrDefault(static row => row is ModuleInfoRecord { Name: "PwshAotLite.ControlPlane" }) is not ModuleInfoRecord { Availability: "native-aot" }
            || modules.SingleOrDefault(static row => row is ModuleInfoRecord { Name: "Microsoft.PowerShell.ThreadJob" }) is not ModuleInfoRecord
            {
                Version: "2.2.0", Availability: "legacy-bridge-pending", Trust: var threadJobTrust, Provenance: var threadJobProvenance,
            }
            || !threadJobTrust.Contains("unverified", StringComparison.Ordinal)
            || !threadJobProvenance.Contains("isolated-sidecar import", StringComparison.Ordinal)
            || modules.SingleOrDefault(static row => row is ModuleInfoRecord { Name: "Microsoft.PowerShell.KubeCtl" }) is not ModuleInfoRecord { Availability: "blocked" })
        {
            throw new InvalidOperationException("Get-Module did not inventory built-in and registered extension package metadata.");
        }

        PipelinePlan namedModulePlan = ScriptParser.Parse("Get-Module -Name Microsoft.PowerShell.ThreadJob");
        if (namedModulePlan.Execute(new AotExecutionContext()).SingleOrDefault() is not ModuleInfoRecord
            {
                Name: "Microsoft.PowerShell.ThreadJob", Path: var threadJobPackagePath,
            }
            || !threadJobPackagePath.EndsWith(
                Path.Combine("extensions", "Microsoft.PowerShell.ThreadJob", "2.2.0"),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Get-Module -Name filtering or package-path reporting regressed.");
        }

        // Completion uses the same data-only catalogs as the control-plane
        // cmdlets. These checks cover built-in contracts, extension manifests,
        // aliases, direct ValidateSet metadata, module names, help topics, and
        // a declaration-only extension without importing or invoking it.
        if (!CompletionService.Instance.Suggest("Get-Pro").Any(static suggestion => suggestion is { Text: "Get-Process", Kind: "command" })
            || !CompletionService.Instance.Suggest("Get-Process -In").Any(static suggestion => suggestion.Text == "-IncludeUserName")
            || !CompletionService.Instance.Suggest("Get-FileHash -Al").Any(static suggestion => suggestion.Text == "-Algorithm")
            || !CompletionService.Instance.Suggest("Get-Verb -Group ").Any(static suggestion => suggestion is { Text: "Common", Kind: "value" })
            || !CompletionService.Instance.Suggest("Get-Command -Module Microsoft.PowerShell.Th").Any(static suggestion => suggestion is { Text: "Microsoft.PowerShell.ThreadJob", Kind: "module" })
            || !CompletionService.Instance.Suggest("Get-Help Start-Th").Any(static suggestion => suggestion is { Text: "Start-ThreadJob", Kind: "help-topic" })
            || !CompletionService.Instance.Suggest("Start-ThreadJob -Thr").Any(static suggestion => suggestion.Text == "-ThrottleLimit")
            || !CompletionService.Instance.Suggest("Start-ThreadJob -d").Any(static suggestion => suggestion.Text == "-db")
            || !CompletionService.Instance.Suggest("Get-KubeResource -Fo").Any(static suggestion => suggestion.Text == "-Force"))
        {
            throw new InvalidOperationException("Metadata-only completion catalog regression.");
        }

        try
        {
            _ = ScriptParser.Parse("Get-Module -ListAvailable").Execute(new AotExecutionContext());
            throw new InvalidOperationException("Get-Module accepted an unsupported live-module discovery mode.");
        }
        catch (ScriptException)
        {
            // This inventory is intentionally only a catalog query; it must not
            // pretend that a module-path scan or import model exists.
        }

        AssertRepositoryCatalogValidationAndFindModuleBehavior();
        AssertExtensionPackageCatalogValidationAndVersionSelection();
        AssertLocalPackageInstallBehavior();

        try
        {
            _ = ScriptParser.Parse("Get-Command -CommandType Alias").Execute(new AotExecutionContext());
            throw new InvalidOperationException("Get-Command accepted an unsupported original command type.");
        }
        catch (ScriptException)
        {
            // The narrow AOT subset must reject, never silently ignore, source modes it cannot model.
        }

        SourceCmdletMetadata getProcessContract = GeneratedCmdletPorts.GetProcess;
        SourceParameterMetadata idParameter = getProcessContract.Parameters.Single(parameter => parameter.Name == "Id");
        if (!getProcessContract.BaseTypeChain.Take(2).SequenceEqual(["ProcessBaseCommand", "Cmdlet"])
            || !getProcessContract.Lifecycle.HasProcessRecord
            || idParameter.Shape != AotParameterShape.Array
            || !idParameter.ParameterSets.Any(parameterSet => parameterSet.Name == "Id" && parameterSet.Mandatory && parameterSet.PipelineBinding.HasFlag(PipelineBindingSource.ByPropertyName))
            || !getProcessContract.MigrationBlockers.Any(blocker => blocker.Api == "WriteObject"))
        {
            throw new InvalidOperationException("Generated Get-Process contract regression.");
        }

        SourceCmdletMetadata getVerbContract = GeneratedCmdletPorts.GetVerb;
        if (GeneratedCmdletPorts.GetVerbCatalog.Count != 100
            || getVerbContract.Parameters.Single(parameter => parameter.Name == "Verb").Shape != AotParameterShape.Array
            || getVerbContract.Parameters.Single(parameter => parameter.Name == "Group").ValidationRules.Single().Name != "ValidateSet"
            || GeneratedCmdletPorts.GetVerbCatalog.Single(info => info.Verb == "Get") is not { AliasPrefix: "g", Group: "Common", Description.Length: > 0 })
        {
            throw new InvalidOperationException("Generated Get-Verb catalogue/contract regression.");
        }

        AotExecutionContext errors = new();
        IReadOnlyList<IPipelineRecord> selected = ProcessSelector.Select(
            new FixtureProcessCatalog(
            [
                new ProcessRecord("zeta", 3, 1, 10),
                new ProcessRecord("pwsh", 2, 11, 20),
                new ProcessRecord("pwsh-preview", 1, 12, 20),
            ]),
            ProcessQuery.ByName(["pwsh*", "pwsh"]),
            errors);

        if (errors.Errors.Count != 0 || selected.Count != 2 || ((ProcessRecord)selected[0]).Id != 2 || ((ProcessRecord)selected[1]).Id != 1)
        {
            throw new InvalidOperationException("Get-Process name-selection port regression.");
        }

        AotExecutionContext idErrors = new();
        _ = ProcessSelector.Select(new FixtureProcessCatalog([]), ProcessQuery.ById([404]), idErrors);
        if (idErrors.Errors.SingleOrDefault()?.Id != "NoProcessFoundForGivenId")
        {
            throw new InvalidOperationException("Get-Process non-terminating error regression.");
        }

        PipelinePlan plan = ScriptParser.Parse("Get-Process -Name pwsh* | Where-Object CPU -gt 10 | Select-Object Name, CPU");
        if (!plan.Columns.SequenceEqual(["Name", "CPU"]))
        {
            throw new InvalidOperationException("Pipeline parse regression.");
        }

        PipelinePlan upstreamPlan = UpstreamAstPipelineLowerer.Parse("Get-Process -Name pwsh* | Where-Object CPU -gt 10 | Select-Object Name, Id");
        if (!upstreamPlan.Columns.SequenceEqual(["Name", "Id"]))
        {
            throw new InvalidOperationException("Upstream AST lowerer did not preserve Select-Object projection.");
        }

        // The AST path must feed the same generated descriptor/binder as the
        // legacy fixture helper.  These cover alias lookup, default positional
        // binding, and rejected parameters without treating source text as a
        // second command grammar.
        _ = UpstreamAstPipelineLowerer.Parse("Get-Process -PID 12");
        _ = UpstreamAstPipelineLowerer.Parse("Get-Process pwsh*");
        try
        {
            _ = UpstreamAstPipelineLowerer.Parse("Get-Process -NotAParameter value");
            throw new InvalidOperationException("Upstream AST lowerer accepted an unsupported parameter.");
        }
        catch (ScriptException exception) when (exception.Message.Contains("does not support parameter", StringComparison.Ordinal))
        {
        }

        try
        {
            _ = UpstreamAstPipelineLowerer.Parse("Get-Process | Where-Object { $_.CPU -gt 10 }");
            throw new InvalidOperationException("Upstream AST lowerer accepted a script-block predicate.");
        }
        catch (ScriptException exception) when (exception.Diagnostic is { Id: "AOT1001", Category: AotDiagnosticCategory.UnsupportedExecution })
        {
        }

        (_, CommandInvocation aliasInvocation) = AotCmdletRegistry.ParseSource("Get-Process -PID 12");
        if (!aliasInvocation.TryGetValues("Id", out string[] ids) || !ids.SequenceEqual(["12"]))
        {
            throw new InvalidOperationException("Generated Get-Process alias metadata regression.");
        }

        GetProcessCmdlet port = new(new FixtureProcessCatalog([new ProcessRecord("pwsh", 7, 1, 2)]));
        AotExecutionContext detailContext = new();
        CommandInvocation moduleInvocation = new(port.Descriptor, new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = ["pwsh"],
            ["Module"] = ["true"],
            ["FileVersionInfo"] = ["true"],
        });
        if (port.Invoke(moduleInvocation, detailContext).SingleOrDefault() is not ProcessModuleRecord)
        {
            throw new InvalidOperationException("Get-Process module/file-version regression.");
        }

        CommandInvocation userInvocation = new(port.Descriptor, new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = ["pwsh"],
            ["IncludeUserName"] = ["true"],
        });
        if (port.Invoke(userInvocation, new AotExecutionContext()).SingleOrDefault() is not ProcessRecord { UserName: "fixture-user" })
        {
            throw new InvalidOperationException("Get-Process IncludeUserName regression.");
        }

        CommandInvocation inputInvocation = new(port.Descriptor, new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));
        if (port.InvokeWithInput(inputInvocation, [new ProcessRecord("pwsh", 7, 1, 2)], new AotExecutionContext()).SingleOrDefault() is not ProcessRecord { Id: 7 })
        {
            throw new InvalidOperationException("Get-Process InputObject regression.");
        }

        SourceCmdletMetadata getFileHashContract = GeneratedCmdletPorts.GetFileHash;
        if (!getFileHashContract.BaseTypeChain.Take(2).SequenceEqual(["HashCmdletBase", "PSCmdlet"])
            || getFileHashContract.Parameters.Single(parameter => parameter.Name == "LiteralPath").Aliases is not ["PSPath", "LP"]
            || !getFileHashContract.Parameters.Single(parameter => parameter.Name == "InputStream").ParameterSets.Any(parameterSet => parameterSet.Name == "StreamParameterSet" && parameterSet.Mandatory)
            || getFileHashContract.Parameters.Single(parameter => parameter.Name == "Algorithm").ValidationRules.Single().Name != "ValidateSet")
        {
            throw new InvalidOperationException("Generated Get-FileHash contract regression.");
        }

        string hashFixtureDirectory = Path.Combine(Path.GetTempPath(), $"pwsh-aot-lite-filehash-{Guid.NewGuid():N}");
        Directory.CreateDirectory(hashFixtureDirectory);
        try
        {
            string alphaPath = Path.Combine(hashFixtureDirectory, "alpha.txt");
            string betaPath = Path.Combine(hashFixtureDirectory, "beta.txt");
            // Windows forbids '*' in file names. The literal-path command and
            // its aliases are still exercised there with an ordinary physical
            // name; Unix additionally proves that a literal wildcard glyph is
            // not expanded by the provider-free resolver.
            string literalFileName = OperatingSystem.IsWindows() ? "literal.txt" : "literal*.txt";
            string literalStarPath = Path.Combine(hashFixtureDirectory, literalFileName);
            UTF8Encoding utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
            File.WriteAllText(alphaPath, "abc", utf8WithoutBom);
            File.WriteAllText(betaPath, "PowerShell", utf8WithoutBom);
            File.WriteAllText(literalStarPath, "literal", utf8WithoutBom);

            GetFileHashCmdlet fileHash = new(new SystemPhysicalFileResolver());
            CommandInvocation hashInvocation = new(fileHash.Descriptor, new Dictionary<string, string[]>
            {
                ["Path"] = [alphaPath, betaPath],
                ["Algorithm"] = ["sha256"],
            });
            FileHashRecord[] directHashes = fileHash.Invoke(hashInvocation, new AotExecutionContext()).Cast<FileHashRecord>().ToArray();
            if (directHashes.Length != 2
                || directHashes[0] is not { Algorithm: "SHA256", Hash: "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD" }
                || directHashes[0].Path != Path.GetFullPath(alphaPath)
                || directHashes[1].Path != Path.GetFullPath(betaPath))
            {
                throw new InvalidOperationException("Get-FileHash direct physical-path/SHA256 regression.");
            }

            CommandInvocation literalInvocation = new(fileHash.Descriptor, new Dictionary<string, string[]>
            {
                ["LiteralPath"] = [literalStarPath],
                ["Algorithm"] = ["md5"],
            });
            if (fileHash.Invoke(literalInvocation, new AotExecutionContext()).SingleOrDefault() is not FileHashRecord { Algorithm: "MD5", Hash: "F0D674F1E0ED4292267F149C5983DB02", Path: var literalResultPath }
                || literalResultPath != Path.GetFullPath(literalStarPath))
            {
                throw new InvalidOperationException("Get-FileHash literal-path/MD5 regression.");
            }

            CommandInvocation wildcardInvocation = new(fileHash.Descriptor, new Dictionary<string, string[]>
            {
                ["Path"] = [Path.Combine(hashFixtureDirectory, "*.txt")],
            });
            if (fileHash.Invoke(wildcardInvocation, new AotExecutionContext()).Cast<FileHashRecord>().Select(record => record.Path).ToArray() is not [var first, var second, var third]
                || !new[] { first, second, third }.SequenceEqual(new[] { alphaPath, betaPath, literalStarPath }.OrderBy(static path => path, StringComparer.Ordinal)))
            {
                throw new InvalidOperationException("Get-FileHash physical wildcard ordering regression.");
            }

            AotExecutionContext missingFileContext = new();
            _ = fileHash.Invoke(new CommandInvocation(fileHash.Descriptor, new Dictionary<string, string[]>
            {
                ["Path"] = [Path.Combine(hashFixtureDirectory, "missing.txt")],
            }), missingFileContext).ToArray();
            if (missingFileContext.Errors.SingleOrDefault()?.Id != "FileNotFound")
            {
                throw new InvalidOperationException("Get-FileHash missing-file non-terminating error regression.");
            }

            (_, CommandInvocation literalAliasInvocation) = AotCmdletRegistry.ParseSource($"Get-FileHash -LP '{literalStarPath}' -Algorithm SHA1");
            if (!literalAliasInvocation.TryGetValues("LiteralPath", out string[] literalAliasValues) || !literalAliasValues.SequenceEqual([literalStarPath]))
            {
                throw new InvalidOperationException("Get-FileHash generated literal-path alias regression.");
            }

            PipelinePlan fileHashPlan = ScriptParser.Parse($"Get-FileHash '{alphaPath}' -Algorithm SHA512 | Select-Object Algorithm, Hash, Path");
            if (!fileHashPlan.Columns.SequenceEqual(["Algorithm", "Hash", "Path"]))
            {
                throw new InvalidOperationException("Get-FileHash positional/parser/column regression.");
            }
        }
        finally
        {
            Directory.Delete(hashFixtureDirectory, recursive: true);
        }

        GetUptimeCmdlet uptime = new();
        if (uptime.Invoke(new CommandInvocation(uptime.Descriptor, new Dictionary<string, string[]>()), new AotExecutionContext()).SingleOrDefault() is not UptimeRecord { Since: null, Value: { Ticks: > 0 } })
        {
            throw new InvalidOperationException("Get-Uptime duration regression.");
        }

        CommandInvocation sinceInvocation = new(uptime.Descriptor, new Dictionary<string, string[]>
        {
            ["Since"] = ["true"],
        });
        if (uptime.Invoke(sinceInvocation, new AotExecutionContext()).SingleOrDefault() is not UptimeRecord { Since: not null })
        {
            throw new InvalidOperationException("Get-Uptime -Since regression.");
        }

        PipelinePlan uptimePlan = ScriptParser.Parse("Get-Uptime -Since | Select-Object Since");
        if (!uptimePlan.Columns.SequenceEqual(["Since"]))
        {
            throw new InvalidOperationException("Get-Uptime pipeline parse regression.");
        }

        SourceCmdletMetadata getDateContract = GeneratedCmdletPorts.GetDate;
        if (!getDateContract.Parameters.Single(parameter => parameter.Name == "Date").Aliases.SequenceEqual(["LastWriteTime"])
            || !getDateContract.Parameters.Single(parameter => parameter.Name == "UnixTimeSeconds").Aliases.SequenceEqual(["UnixTime"])
            || !getDateContract.Parameters.Single(parameter => parameter.Name == "UnixTimeSeconds").ValidationRules.Any(rule => rule.Name == "ValidateRange")
            || !getDateContract.Parameters.Single(parameter => parameter.Name == "UFormat").ParameterSets.Any(parameterSet => parameterSet.Name == "DateAndUFormat" && parameterSet.Mandatory))
        {
            throw new InvalidOperationException("Generated Get-Date contract regression.");
        }

        GetDateCmdlet date = new(new FixtureClock(new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Local)));
        CommandInvocation dateInvocation = new(date.Descriptor, new Dictionary<string, string[]>
        {
            ["Date"] = ["2024-02-29T12:34:56.789"],
            ["Year"] = ["2025"],
            ["Month"] = ["3"],
            ["Day"] = ["1"],
            ["Hour"] = ["1"],
            ["Minute"] = ["2"],
            ["Second"] = ["3"],
            ["Millisecond"] = ["4"],
            ["DisplayHint"] = ["Time"],
        });
        if (date.Invoke(dateInvocation, new AotExecutionContext()).SingleOrDefault() is not DateRecord { Value: { Year: 2025, Month: 3, Day: 1, Hour: 1, Minute: 2, Second: 3, Millisecond: 4 }, DisplayHint: "Time" })
        {
            throw new InvalidOperationException("Get-Date component/display-hint regression.");
        }

        CommandInvocation unixInvocation = new(date.Descriptor, new Dictionary<string, string[]>
        {
            ["UnixTimeSeconds"] = ["0"],
            ["AsUTC"] = ["true"],
            ["Format"] = ["FileDateTimeUniversal"],
        });
        if (date.Invoke(unixInvocation, new AotExecutionContext()).SingleOrDefault() is not TextRecord { Value: "19700101T0000000000Z" })
        {
            throw new InvalidOperationException("Get-Date UnixTimeSeconds/FileDateTimeUniversal regression.");
        }

        CommandInvocation uformatInvocation = new(date.Descriptor, new Dictionary<string, string[]>
        {
            ["Date"] = ["2024-02-29T12:34:56"],
            ["UFormat"] = ["+%Y-%m-%dT%H:%M:%S|%j|%V"],
        });
        if (date.Invoke(uformatInvocation, new AotExecutionContext()).SingleOrDefault() is not TextRecord { Value: "2024-02-29T12:34:56|060|09" })
        {
            throw new InvalidOperationException("Get-Date UFormat regression.");
        }

        (_, CommandInvocation unixAliasInvocation) = AotCmdletRegistry.ParseSource("Get-Date -UnixTime 0 -AsUTC -Format o");
        if (!unixAliasInvocation.TryGetValues("UnixTimeSeconds", out string[] unixValues) || !unixValues.SequenceEqual(["0"]))
        {
            throw new InvalidOperationException("Generated Get-Date UnixTime alias regression.");
        }

        PipelinePlan datePlan = ScriptParser.Parse("Get-Date -Date 2024-01-01T00:00:00 | Select-Object DateTime, DisplayHint");
        if (!datePlan.Columns.SequenceEqual(["DateTime", "DisplayHint"]))
        {
            throw new InvalidOperationException("Get-Date parser/record regression.");
        }

        PipelinePlan quotedUFormatPlan = ScriptParser.Parse("Get-Date -Date 2024-01-01T00:00:00 -UFormat '+%Y|%m' | Select-Object Value");
        if (!quotedUFormatPlan.Columns.SequenceEqual(["Value"]))
        {
            throw new InvalidOperationException("Get-Date quoted UFormat pipeline splitting regression.");
        }

        GetUICultureCmdlet uiCulture = new(new FixtureHostCulture(new CultureInfo("fr-FR")));
        if (uiCulture.Invoke(new CommandInvocation(uiCulture.Descriptor, new Dictionary<string, string[]>()), new AotExecutionContext()).SingleOrDefault() is not CultureRecord { Name: "fr-FR", Lcid: 1036 })
        {
            throw new InvalidOperationException("Get-UICulture lifecycle/output regression.");
        }

        PipelinePlan culturePlan = ScriptParser.Parse("Get-UICulture | Select-Object Name, LCID");
        if (!culturePlan.Columns.SequenceEqual(["Name", "LCID"]))
        {
            throw new InvalidOperationException("Get-UICulture pipeline parse regression.");
        }

        GetVerbCmdlet verbs = new();
        CommandInvocation verbInvocation = new(verbs.Descriptor, new Dictionary<string, string[]>
        {
            ["Verb"] = ["Get*"],
            ["Group"] = ["Common"],
        });
        if (verbs.Invoke(verbInvocation, new AotExecutionContext()).SingleOrDefault() is not VerbRecord { Verb: "Get", AliasPrefix: "g", Group: "Common", Description.Length: > 0 })
        {
            throw new InvalidOperationException("Get-Verb filtered catalogue regression.");
        }

        PipelinePlan verbPlan = ScriptParser.Parse("Get-Verb Get* -Group Common | Select-Object Verb, AliasPrefix, Group");
        if (!verbPlan.Columns.SequenceEqual(["Verb", "AliasPrefix", "Group"]))
        {
            throw new InvalidOperationException("Get-Verb parser regression.");
        }

        try
        {
            _ = verbs.Invoke(new CommandInvocation(verbs.Descriptor, new Dictionary<string, string[]> { ["Group"] = ["NoSuchGroup"] }), new AotExecutionContext()).ToArray();
            throw new InvalidOperationException("Get-Verb invalid-group validation regression.");
        }
        catch (ScriptException)
        {
        }

        CultureInfo english = new("en-US");
        CultureInfo french = new("fr-FR");
        GetCultureCmdlet culture = new(new FixtureHostCulture(english), new FixtureCultureCatalog([english, french]));
        CommandInvocation cultureNameInvocation = new(culture.Descriptor, new Dictionary<string, string[]>
        {
            ["Name"] = ["fr-FR"],
        });
        if (culture.Invoke(cultureNameInvocation, new AotExecutionContext()).SingleOrDefault() is not CultureRecord { Name: "fr-FR" })
        {
            throw new InvalidOperationException("Get-Culture name lookup regression.");
        }

        CommandInvocation cultureListInvocation = new(culture.Descriptor, new Dictionary<string, string[]>
        {
            ["ListAvailable"] = ["true"],
        });
        if (culture.Invoke(cultureListInvocation, new AotExecutionContext()).Count() != 2)
        {
            throw new InvalidOperationException("Get-Culture list regression.");
        }

        AotExecutionContext cultureErrors = new();
        _ = culture.Invoke(new CommandInvocation(culture.Descriptor, new Dictionary<string, string[]>
        {
            ["Name"] = ["not-a-culture"],
        }), cultureErrors).ToArray();
        if (cultureErrors.Errors.SingleOrDefault()?.Id != "ItemNotFoundException")
        {
            throw new InvalidOperationException("Get-Culture error regression.");
        }

        SourceCmdletMetadata timeZoneContract = GeneratedCmdletPorts.GetTimeZone;
        if (!timeZoneContract.BaseTypeChain.Take(2).SequenceEqual(["PSCmdlet", "Cmdlet"])
            || timeZoneContract.Parameters.Single(parameter => parameter.Name == "Id").Shape != AotParameterShape.Array
            || !timeZoneContract.Parameters.Single(parameter => parameter.Name == "Name").ParameterSets.Any(parameterSet => parameterSet.Name == "Name" && parameterSet.Position == 0 && parameterSet.PipelineBinding.HasFlag(PipelineBindingSource.ByValue))
            || timeZoneContract.Parameters.Single(parameter => parameter.Name == "ListAvailable").Shape != AotParameterShape.Switch)
        {
            throw new InvalidOperationException("Generated Get-TimeZone contract regression.");
        }

        TimeZoneInfo localZone = TimeZoneInfo.CreateCustomTimeZone("Fixture/Local", TimeSpan.FromHours(4), "Fixture Local", "Fixture Local Standard");
        TimeZoneInfo utcZone = TimeZoneInfo.CreateCustomTimeZone("Fixture/UTC", TimeSpan.Zero, "Fixture UTC", "Coordinated Universal Time");
        TimeZoneInfo daylightZone = TimeZoneInfo.CreateCustomTimeZone("Fixture/Daylight", TimeSpan.FromHours(-7), "Fixture Daylight", "Fixture Standard", "Fixture Daylight Time", null);
        FixtureTimeZoneCatalog timeZones = new(localZone, [utcZone, daylightZone]);
        GetTimeZoneCmdlet timeZone = new(timeZones);
        CommandInvocation timeZoneDefaultInvocation = new(timeZone.Descriptor, new Dictionary<string, string[]>());
        if (timeZone.Invoke(timeZoneDefaultInvocation, new AotExecutionContext()).SingleOrDefault() is not TimeZoneRecord { Id: "Fixture/Local" } || timeZones.RefreshCalls != 1)
        {
            throw new InvalidOperationException("Get-TimeZone local/default lifecycle regression.");
        }

        CommandInvocation timeZoneIdInvocation = new(timeZone.Descriptor, new Dictionary<string, string[]>
        {
            ["Id"] = ["Fixture/UTC"],
        });
        if (timeZone.Invoke(timeZoneIdInvocation, new AotExecutionContext()).SingleOrDefault() is not TimeZoneRecord { Id: "Fixture/UTC", BaseUtcOffset: { Ticks: 0 } })
        {
            throw new InvalidOperationException("Get-TimeZone id lookup regression.");
        }

        CommandInvocation timeZoneNameInvocation = new(timeZone.Descriptor, new Dictionary<string, string[]>
        {
            ["Name"] = ["Fixture Daylight*"],
        });
        if (timeZone.Invoke(timeZoneNameInvocation, new AotExecutionContext()).SingleOrDefault() is not TimeZoneRecord { Id: "Fixture/Daylight" })
        {
            throw new InvalidOperationException("Get-TimeZone standard/daylight name wildcard regression.");
        }

        AotExecutionContext timeZoneErrors = new();
        _ = timeZone.Invoke(new CommandInvocation(timeZone.Descriptor, new Dictionary<string, string[]>
        {
            ["Id"] = ["missing-zone"],
        }), timeZoneErrors).ToArray();
        if (timeZoneErrors.Errors.SingleOrDefault()?.Id != "TimeZoneNotFound")
        {
            throw new InvalidOperationException("Get-TimeZone non-terminating id error regression.");
        }

        PipelinePlan timeZonePlan = ScriptParser.Parse("Get-TimeZone -ListAvailable | Select-Object Id, StandardName, BaseUtcOffset");
        if (!timeZonePlan.Columns.SequenceEqual(["Id", "StandardName", "BaseUtcOffset"]))
        {
            throw new InvalidOperationException("Get-TimeZone parser/column regression.");
        }

        (_, CommandInvocation quotedTimeZoneName) = AotCmdletRegistry.ParseSource("Get-TimeZone -Name \"Fixture Daylight Time\"");
        if (!quotedTimeZoneName.TryGetValues("Name", out string[] quotedNames) || !quotedNames.SequenceEqual(["Fixture Daylight Time"]))
        {
            throw new InvalidOperationException("Quoted command parameter tokenizer regression.");
        }
    }

    private static void AssertExecutionKernelAndDiagnostics()
    {
        const string source = "Get-Process | Where-Object { $_.CPU -gt 10 }";
        AotParseResult parseResult = AotScriptParser.Parse(source, "fixture.ps1", documentVersion: 7);
        if (parseResult.DocumentName != "fixture.ps1"
            || parseResult.DocumentVersion != 7
            || parseResult.Tokens.Count == 0
            || parseResult.Diagnostics.Count != 0
            || parseResult.Ast.Extent.Text != source)
        {
            throw new InvalidOperationException("The shared upstream parser facade did not preserve the source, AST, tokens, or document identity.");
        }

        try
        {
            _ = AotExecutionKernel.Compile(source, "fixture.ps1", documentVersion: 7);
            throw new InvalidOperationException("The execution kernel accepted an unsupported script-block predicate.");
        }
        catch (ScriptException error) when (error.Diagnostic is
            {
                Id: "AOT1001",
                Category: AotDiagnosticCategory.UnsupportedExecution,
                Span: { StartLine: 1, StartColumn: 28 },
                Help: not null,
            })
        {
            string rendered = AotDiagnosticRenderer.Render(error.Diagnostic, source, "fixture.ps1", useAnsi: false);
            const string expected = """
error[AOT1001]: expression 'ScriptBlockExpressionAst' is parsed but not executable by the Native AOT structural subset.
  --> fixture.ps1:1:28
   |
1 | Get-Process | Where-Object { $_.CPU -gt 10 }
   |                            ^^^^^^^^^^^^^^^^^ unsupported execution feature
   = help: Use only the documented Native AOT execution subset until this AST node has a reviewed plan.
""";
            if (!rendered.Equals(expected.TrimEnd(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unsupported syntax diagnostic renderer snapshot regression.");
            }
        }

        try
        {
            _ = AotExecutionKernel.Compile("Get-Process -NotAParameter value", "binding.ps1")
                .Execute(new AotExecutionContext());
            throw new InvalidOperationException("The execution kernel accepted an unsupported parameter.");
        }
        catch (ScriptException error) when (error.Diagnostic is
            {
                Id: "AOT2002",
                Category: AotDiagnosticCategory.Binding,
                Span: { DocumentName: "binding.ps1", StartLine: 1, StartColumn: 13 },
            })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                "Get-Process -NotAParameter value",
                """
error[AOT2002]: Get-Process does not support parameter '-NotAParameter'.
  --> binding.ps1:1:13
   |
1 | Get-Process -NotAParameter value
   |             ^^^^^^^^^^^^^^ binding failed
""");
        }

        try
        {
            _ = AotExecutionKernel.Compile("Get-Process |", "malformed.ps1");
            throw new InvalidOperationException("The execution kernel accepted malformed pipeline input.");
        }
        catch (ScriptException error) when (error.Diagnostic is
            {
                Id: "EmptyPipeElement",
                Category: AotDiagnosticCategory.Parse,
                Span: { DocumentName: "malformed.ps1", StartLine: 1 },
            })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                "Get-Process |",
                """
error[EmptyPipeElement]: A pipeline cannot end with '|'.
  --> malformed.ps1:1:14
   |
1 | Get-Process |
   |              ^ incomplete input
   = help: Add a command after '|', or remove the trailing pipe.
""");
        }

        try
        {
            _ = AotExecutionKernel.Compile("Get-Process | Where-Object CPU -gt NaN", "predicate.ps1");
            throw new InvalidOperationException("The execution kernel accepted a non-finite predicate literal.");
        }
        catch (ScriptException error) when (error.Diagnostic is
            {
                Id: "AOT4002",
                Category: AotDiagnosticCategory.Runtime,
                Span: { DocumentName: "predicate.ps1", StartColumn: 36 },
            })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                "Get-Process | Where-Object CPU -gt NaN",
                """
error[AOT4002]: Where-Object does not accept NaN or infinity predicate literals in the AOT subset.
  --> predicate.ps1:1:36
   |
1 | Get-Process | Where-Object CPU -gt NaN
   |                                    ^^^ non-finite predicate value
   = help: Provide a finite numeric comparison value.
""");
        }

        try
        {
            _ = AotExecutionKernel.Compile("Get-Process | Where-Object CPU -ft 2", "operator.ps1");
            throw new InvalidOperationException("The execution kernel accepted an unsupported Where-Object comparison.");
        }
        catch (ScriptException error) when (error.Diagnostic is
            {
                Id: "AOT4003",
                Category: AotDiagnosticCategory.Runtime,
                Span: { DocumentName: "operator.ps1", StartColumn: 32 },
            })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                "Get-Process | Where-Object CPU -ft 2",
                """
error[AOT4003]: Unsupported comparison '-ft'.
  --> operator.ps1:1:32
   |
1 | Get-Process | Where-Object CPU -ft 2
   |                                ^^^ unsupported comparison
   = help: Use -gt, -ge, -lt, -le, -eq, or -ne.
""");
        }

        try
        {
            _ = AotExecutionKernel.Compile("Get-Process | Where-Object BadProperty -gt 2", "property.ps1")
                .Execute(new AotExecutionContext());
            throw new InvalidOperationException("The execution kernel accepted an unsupported Where-Object property.");
        }
        catch (ScriptException error) when (error.Diagnostic is
            {
                Id: "AOT4005",
                Category: AotDiagnosticCategory.Runtime,
                Span: { DocumentName: "property.ps1", StartColumn: 28 },
            })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                "Get-Process | Where-Object BadProperty -gt 2",
                """
error[AOT4005]: Where-Object does not support property 'BadProperty' for this pipeline value.
  --> property.ps1:1:28
   |
1 | Get-Process | Where-Object BadProperty -gt 2
   |                            ^^^^^^^^^^^ unsupported pipeline property
   = help: Use a numeric field exposed by the preceding AOT pipeline record.
""");
        }

        try
        {
            _ = AotExecutionKernel.Compile("Get-TimeZone -Id UTC | Select-Object NotAnAotField", "projection.ps1")
                .Execute(new AotExecutionContext());
            throw new InvalidOperationException("The execution kernel accepted an unknown Select-Object field.");
        }
        catch (ScriptException error) when (error.Diagnostic is
            {
                Id: "AOT4008",
                Category: AotDiagnosticCategory.Runtime,
                Span: { DocumentName: "projection.ps1", StartColumn: 38 },
            })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                "Get-TimeZone -Id UTC | Select-Object NotAnAotField",
                """
error[AOT4008]: Select-Object requested a column not present on this pipeline value.
  --> projection.ps1:1:38
   |
1 | Get-TimeZone -Id UTC | Select-Object NotAnAotField
   |                                      ^^^^^^^^^^^^^ unknown projection field
   = help: Use a field exposed by the preceding AOT pipeline record.
""");
        }

        try
        {
            _ = AotExecutionKernel.Compile("Get-Process | Select-Object", "empty-projection.ps1");
            throw new InvalidOperationException("The execution kernel accepted an empty Select-Object projection.");
        }
        catch (ScriptException error) when (error.Diagnostic is
            {
                Id: "AOT4004",
                Category: AotDiagnosticCategory.Runtime,
                Span: { DocumentName: "empty-projection.ps1", StartColumn: 15 },
            })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                "Get-Process | Select-Object",
                """
error[AOT4004]: Select-Object requires at least one column.
  --> empty-projection.ps1:1:15
   |
1 | Get-Process | Select-Object
   |               ^^^^^^^^^^^^^ missing projection column
   = help: Provide one or more direct field names.
""");
        }

        try
        {
            _ = AotExecutionKernel.Compile("Get-Process -Id 1 -Name launchd", "process-parameters.ps1")
                .Execute(new AotExecutionContext());
            throw new InvalidOperationException("The execution kernel accepted conflicting Get-Process parameter sets.");
        }
        catch (ScriptException error) when (error.Diagnostic is
            {
                Id: "AOT3001",
                Category: AotDiagnosticCategory.Runtime,
                Span: { DocumentName: "process-parameters.ps1", StartColumn: 1 },
            })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                "Get-Process -Id 1 -Name launchd",
                """
error[AOT3001]: Get-Process parameters -Name and -Id cannot be combined.
  --> process-parameters.ps1:1:1
   |
1 | Get-Process -Id 1 -Name launchd
   | ^^^^^^^^^^^ conflicting parameter sets
   = help: Use either -Name or -Id, not both.
""");
        }

        AotExecutionPlan runtimePlan = AotExecutionKernel.Compile("Get-Process -Id 2147483647", "runtime.ps1");
        AotExecutionContext runtimeContext = new();
        _ = runtimePlan.Execute(runtimeContext);
        if (runtimeContext.Errors.SingleOrDefault()?.Diagnostic is not { } runtimeDiagnostic
            || runtimeDiagnostic.Id != "NoProcessFoundForGivenId"
            || runtimeDiagnostic.Span is not { DocumentName: "runtime.ps1", StartColumn: 1 })
        {
            throw new InvalidOperationException("Non-terminating runtime diagnostics did not retain the active source-command span.");
        }

        AssertDiagnosticSnapshot(
            runtimeDiagnostic,
            "Get-Process -Id 2147483647",
            """
error[NoProcessFoundForGivenId]: No process was found with the process identifier 2147483647.
  --> runtime.ps1:1:1
   |
1 | Get-Process -Id 2147483647
   | ^^^^^^^^^^^ command reported an error
""");

        AotExecutionPlan successPlan = AotExecutionKernel.Compile("Get-Verb -Group Common", "success.ps1");
        AotExecutionContext successContext = new();
        if (successPlan.Execute(successContext).Outputs.Count == 0 || successContext.Errors.Count != 0)
        {
            throw new InvalidOperationException("Successful execution did not preserve an empty diagnostic stream.");
        }

        string ansi = AotDiagnosticRenderer.Render(
            runtimeDiagnostic,
            "Get-Process -Id 2147483647",
            "runtime.ps1",
            new AotDiagnosticRenderOptions(UseAnsi: true));
        if (!ansi.StartsWith("\u001b[31merror[NoProcessFoundForGivenId]", StringComparison.Ordinal)
            || !ansi.Contains("\u001b[0m", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("ANSI diagnostic renderer snapshot regression.");
        }

        string narrow = AotDiagnosticRenderer.Render(
            runtimeDiagnostic,
            "Get-Process -Id 2147483647",
            "runtime.ps1",
            new AotDiagnosticRenderOptions(Width: 20));
        if (!narrow.Contains("error[NoProcessFoundForGivenId]: No process was found with the process identifier 2147483647.", StringComparison.Ordinal)
            || !narrow.Contains("…", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Width-constrained diagnostic renderer lost the required heading or source context.");
        }
    }

    private static void AssertDiagnosticSnapshot(AotDiagnostic diagnostic, string source, string expected)
    {
        string actual = AotDiagnosticRenderer.Render(diagnostic, source, fallbackDocumentName: null, useAnsi: false);
        if (!actual.Equals(expected.TrimEnd(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Diagnostic renderer snapshot regression.");
        }
    }

    private static void AssertLanguageCompatibilityCore()
    {
        const string thresholdSource = "$threshold = -1; Get-Process | Where-Object CPU -gt $threshold | Select-Object Name, Id";
        AotExecutionResult thresholdResult = AotExecutionKernel.Compile(thresholdSource, "variables.ps1")
            .Execute(new AotExecutionContext());
        if (thresholdResult.Outputs.Count != 1
            || thresholdResult.Outputs[0].Rows.Count == 0
            || !thresholdResult.Outputs[0].Columns.SequenceEqual(["Name", "Id"]))
        {
            throw new InvalidOperationException("Block-plan variable predicates did not execute through the reviewed pipeline shape.");
        }

        AotExecutionResult listResult = AotExecutionKernel.Compile(
                "$verbs = 'Get', 'Set'; Get-Verb -Verb $verbs | Select-Object Verb",
                "list-variable.ps1")
            .Execute(new AotExecutionContext());
        if (listResult.Outputs.SingleOrDefault() is not { Rows: var verbRows, Columns: var verbColumns }
            || !verbColumns.SequenceEqual(["Verb"])
            || !verbRows.OfType<AotPipelineRecord>().Select(row => row.TextFor("Verb")).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(["Get", "Set"]))
        {
            throw new InvalidOperationException("A variable list did not expand into values for the existing generated-metadata binder.");
        }

        AotExecutionResult singleQuotedResult = AotExecutionKernel.Compile(
                "$literal = '$notInterpolation'; Get-Process -Name $literal",
                "single-quoted-variable.ps1")
            .Execute(new AotExecutionContext());
        if (singleQuotedResult.Outputs.Count != 1)
        {
            throw new InvalidOperationException("A single-quoted dollar sequence was treated as an interpolated variable.");
        }

        AotScope parent = new();
        parent.Set("Threshold", AotValue.FromInteger(7));
        AotScope child = new(parent);
        if (!child.TryGet("threshold", out AotValue inherited) || !inherited.TryGetInteger(out long inheritedValue) || inheritedValue != 7)
        {
            throw new InvalidOperationException("AOT lexical scope lookup is not case-insensitive or parent-aware.");
        }

        AotScope session = new();
        _ = AotExecutionKernel.Compile("$group = 'Common'", "repl-assignment.ps1").Execute(new AotExecutionContext(), session);
        if (AotExecutionKernel.Compile("Get-Verb -Group $GROUP", "repl-read.ps1").Execute(new AotExecutionContext(), session).Outputs.Count != 1)
        {
            throw new InvalidOperationException("An explicitly owned session scope did not preserve a variable between REPL submissions.");
        }

        try
        {
            _ = AotExecutionKernel.Compile("Get-Verb -Group $group", "fresh-scope.ps1").Execute(new AotExecutionContext());
            throw new InvalidOperationException("A fresh command execution leaked a prior scope variable.");
        }
        catch (ScriptException error) when (error.Diagnostic is { Id: "AOT5001", Span: { DocumentName: "fresh-scope.ps1", StartColumn: 17 } })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                "Get-Verb -Group $group",
                """
error[AOT5001]: Variable '$group' has not been assigned in this AOT scope.
  --> fresh-scope.ps1:1:17
   |
1 | Get-Verb -Group $group
   |                 ^^^^^^ undefined variable
   = help: Assign the variable earlier in this script, or pass a direct literal.
""");
        }

        const string completedOutputBeforeFailure = "Get-Verb -Group Common | Select-Object Verb; Get-Verb -Group $missing";
        StringWriter streamedOutput = new(CultureInfo.InvariantCulture);
        StringWriter streamedError = new(CultureInfo.InvariantCulture);
        TextWriter originalOutput = Console.Out;
        TextWriter originalError = Console.Error;
        try
        {
            Console.SetOut(streamedOutput);
            Console.SetError(streamedError);
            if (ScriptRunner.Execute(completedOutputBeforeFailure) != 2)
            {
                throw new InvalidOperationException("A terminating later statement did not return the host diagnostic exit code.");
            }
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }

        if (!streamedOutput.ToString().Contains("Verb", StringComparison.Ordinal)
            || !streamedError.ToString().Contains("error[AOT5001]", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A completed pipeline output was not streamed before a later statement failed.");
        }

        try
        {
            _ = AotExecutionKernel.Compile("$global:group = 'Common'", "scoped-variable.ps1");
            throw new InvalidOperationException("The kernel accepted a scoped variable assignment.");
        }
        catch (ScriptException error) when (error.Diagnostic is { Id: "AOT5002", Span: { DocumentName: "scoped-variable.ps1", StartColumn: 1 } })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                "$global:group = 'Common'",
                """
error[AOT5002]: assignment target '$global:group' is not supported by the Native AOT lexical scope.
  --> scoped-variable.ps1:1:1
   |
1 | $global:group = 'Common'
   | ^^^^^^^^^^^^^ unsupported variable form
   = help: Assign one ordinary unscoped variable at a time.
""");
        }

        try
        {
            _ = AotExecutionKernel.Compile("Get-Verb -Group $PSItem", "automatic-variable.ps1");
            throw new InvalidOperationException("The kernel accepted an automatic variable.");
        }
        catch (ScriptException error) when (error.Diagnostic is { Id: "AOT5002", Span: { DocumentName: "automatic-variable.ps1", StartColumn: 17 } })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                "Get-Verb -Group $PSItem",
                """
error[AOT5002]: automatic variable '$PSItem' is not supported by the Native AOT lexical scope.
  --> automatic-variable.ps1:1:17
   |
1 | Get-Verb -Group $PSItem
   |                 ^^^^^^^ unsupported variable form
   = help: Use a variable explicitly assigned in this AOT script.
""");
        }

        try
        {
            _ = AotExecutionKernel.Compile("$PID = 1", "automatic-assignment.ps1");
            throw new InvalidOperationException("The kernel accepted a PowerShell host variable assignment.");
        }
        catch (ScriptException error) when (error.Diagnostic is { Id: "AOT5002", Span: { DocumentName: "automatic-assignment.ps1", StartColumn: 1 } })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                "$PID = 1",
                """
error[AOT5002]: assignment target '$PID' is not supported by the Native AOT lexical scope.
  --> automatic-assignment.ps1:1:1
   |
1 | $PID = 1
   | ^^^^ unsupported variable form
   = help: Assign one ordinary unscoped variable at a time.
""");
        }

        try
        {
            _ = AotExecutionKernel.Compile("Get-Verb -Group $PSCulture", "automatic-host-read.ps1");
            throw new InvalidOperationException("The kernel accepted a PowerShell host variable read.");
        }
        catch (ScriptException error) when (error.Diagnostic is { Id: "AOT5002", Span: { DocumentName: "automatic-host-read.ps1", StartColumn: 17 } })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                "Get-Verb -Group $PSCulture",
                """
error[AOT5002]: automatic variable '$PSCulture' is not supported by the Native AOT lexical scope.
  --> automatic-host-read.ps1:1:17
   |
1 | Get-Verb -Group $PSCulture
   |                 ^^^^^^^^^^ unsupported variable form
   = help: Use a variable explicitly assigned in this AOT script.
""");
        }

        foreach (string reserved in new[]
        {
            "OFS", "MaximumHistoryCount", "VerboseHelpErrors", "LogCommandHealthEvent",
            "PSSessionConfigurationName", "PSSessionApplicationName", "Event", "EventArgs", "EventSubscriber", "Sender", "PROFILE",
        })
        {
            string assignment = "$" + reserved + " = 'x'";
            string read = "Get-Verb -Group $" + reserved;

            AssertReservedVariableRejected(assignment, "reserved-assignment.ps1");
            AssertReservedVariableRejected(read, "reserved-read.ps1");
        }

        try
        {
            _ = AotExecutionKernel.Compile("$value = '-Name'; Get-Process -Id $value", "injection.ps1")
                .Execute(new AotExecutionContext());
            throw new InvalidOperationException("A variable value was reclassified as a parameter token.");
        }
        catch (ScriptException error) when (error.Diagnostic is { Id: "AOT3004", Span: { DocumentName: "injection.ps1", StartColumn: 35 } })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                "$value = '-Name'; Get-Process -Id $value",
                """
error[AOT3004]: Get-Process -Id expects a non-negative integer, got '-Name'.
  --> injection.ps1:1:35
   |
1 | $value = '-Name'; Get-Process -Id $value
   |                                   ^^^^^^ invalid process identifier
   = help: Provide a non-negative integer after -Id.
""");
        }

        try
        {
            _ = AotExecutionKernel.Compile(
                    "$threshold = 'many'; Get-Process | Where-Object CPU -gt $threshold",
                    "predicate-variable.ps1")
                .Execute(new AotExecutionContext());
            throw new InvalidOperationException("A non-numeric variable predicate was accepted.");
        }
        catch (ScriptException error) when (error.Diagnostic is { Id: "AOT5004", Span: { DocumentName: "predicate-variable.ps1", StartColumn: 57 } })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                "$threshold = 'many'; Get-Process | Where-Object CPU -gt $threshold",
                """
error[AOT5004]: Where-Object requires a finite numeric predicate value in the AOT subset.
  --> predicate-variable.ps1:1:57
   |
1 | $threshold = 'many'; Get-Process | Where-Object CPU -gt $threshold
   |                                                         ^^^^^^^^^^ non-numeric predicate variable
   = help: Assign a finite numeric value before using it in this predicate.
""");
        }

        try
        {
            AotCommandArgumentConverter.Append(
                AotValue.FromBytes(new byte[] { 1 }),
                new AotSourceSpan("conversion.ps1", 0, 6, 1, 1, 1, 7),
                []);
            throw new InvalidOperationException("A closed non-scalar value reached the command binder.");
        }
        catch (ScriptException error) when (error.Diagnostic is { Id: "AOT5003", Span: { DocumentName: "conversion.ps1" } })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                "$bytes",
                """
error[AOT5003]: Value kind 'Bytes' cannot be passed to a command argument in the AOT subset.
  --> conversion.ps1:1:1
   |
1 | $bytes
   | ^^^^^^ unsupported command argument value
   = help: Use a string, Boolean, finite number, null, or a list of those values.
""");
        }

        foreach (string unsupported in new[] { "$count += 1", "$a, $b = 1, 2", "$x = @(1, 2)", "Get-Process -Name \"pwsh$x\"" })
        {
            try
            {
                _ = AotExecutionKernel.Compile(unsupported, "unsupported-variable.ps1");
                throw new InvalidOperationException($"The kernel accepted deferred variable syntax '{unsupported}'.");
            }
            catch (ScriptException error) when (error.Diagnostic.Id == "AOT1001")
            {
                // Every accepted parser node remains explicitly fail-closed
                // until it has a separate scope/evaluation plan.
            }
        }
    }

    private static void AssertReservedVariableRejected(string source, string documentName)
    {
        try
        {
            _ = AotExecutionKernel.Compile(source, documentName);
            throw new InvalidOperationException($"The kernel accepted reserved PowerShell variable source '{source}'.");
        }
        catch (ScriptException error) when (error.Diagnostic.Id == "AOT5002")
        {
            // The policy must apply symmetrically to reads and assignments.
        }
    }

    private static void AssertControlFlowCore()
    {
        const string selectedBranchSource = """
$threshold = 10
if ($threshold -gt 0) {
    $group = 'Common'
    Get-Verb -Group $group | Select-Object Verb
}
elseif ($false) {
    Get-Verb -Group Filter
}
else {
    Get-Verb -Group Filter
}
""";
        AotExecutionResult selectedBranch = AotExecutionKernel.Compile(selectedBranchSource, "if-selected.ps1")
            .Execute(new AotExecutionContext());
        if (selectedBranch.Outputs.SingleOrDefault() is not { Columns: var selectedColumns, Rows: var selectedRows }
            || !selectedColumns.SequenceEqual(["Verb"])
            || !selectedRows.OfType<AotPipelineRecord>().Any(row => row.TextFor("Verb") == "Add"))
        {
            throw new InvalidOperationException("The selected if branch did not execute through the normal pipeline plan.");
        }

        AotExecutionResult elseifBranch = AotExecutionKernel.Compile(
                "if ($false) { Get-Verb -Group Filter } elseif ($true) { Get-Verb -Group Common } else { Get-Verb -Group Filter }",
                "elseif.ps1")
            .Execute(new AotExecutionContext());
        if (elseifBranch.Outputs.Count != 1
            || !elseifBranch.Outputs[0].Rows.Any(row => row.TextFor("Verb") == "Add"))
        {
            throw new InvalidOperationException("Elseif conditions did not evaluate lazily in source order.");
        }

        AotScope ifScope = new();
        AotExecutionResult persistentAssignment = AotExecutionKernel.Compile(
                "$enabled = $true; if ($enabled) { $group = 'Common' }; Get-Verb -Group $group",
                "if-scope.ps1")
            .Execute(new AotExecutionContext(), ifScope);
        if (persistentAssignment.Outputs.Count != 1 || !ifScope.TryGet("GROUP", out AotValue group) || !group.TryGetString(out string? groupName) || groupName != "Common")
        {
            throw new InvalidOperationException("A selected if branch did not retain PowerShell's surrounding-scope assignment behavior.");
        }

        AotScope caseScope = new();
        _ = AotExecutionKernel.Compile(
                "if ('Common' -ceq 'common') { $case = 'wrong' } else { $case = 'right' }",
                "if-case.ps1")
            .Execute(new AotExecutionContext(), caseScope);
        if (!caseScope.TryGet("case", out AotValue caseValue) || !caseValue.TryGetString(out string? caseResult) || caseResult != "right")
        {
            throw new InvalidOperationException("Case-sensitive conditional comparison did not preserve the closed comparison policy.");
        }

        AotExecutionResult skippedBranch = AotExecutionKernel.Compile(
                "if ($true) { Get-Verb -Group Common } else { Get-Verb -Group $missing }",
                "if-skipped.ps1")
            .Execute(new AotExecutionContext());
        if (skippedBranch.Outputs.Count != 1)
        {
            throw new InvalidOperationException("A skipped if branch was evaluated or did not emit its selected output.");
        }

        AotExecutionResult segmentedBranch = AotExecutionKernel.Compile(
                "if ($true) { Get-Verb -Group Common | Select-Object Verb; Get-Date | Select-Object DateTime }",
                "if-segments.ps1")
            .Execute(new AotExecutionContext());
        if (segmentedBranch.Outputs.Count != 2
            || !segmentedBranch.Outputs[0].Columns.SequenceEqual(["Verb"])
            || !segmentedBranch.Outputs[1].Columns.SequenceEqual(["DateTime"]))
        {
            throw new InvalidOperationException("Nested branch pipelines did not retain ordered output segments.");
        }

        const string branchOutputBeforeFailure = "if ($true) { Get-Verb -Group Common | Select-Object Verb; Get-Verb -Group $missing }";
        StringWriter branchOutput = new(CultureInfo.InvariantCulture);
        StringWriter branchError = new(CultureInfo.InvariantCulture);
        TextWriter originalOutput = Console.Out;
        TextWriter originalError = Console.Error;
        try
        {
            Console.SetOut(branchOutput);
            Console.SetError(branchError);
            if (ScriptRunner.Execute(branchOutputBeforeFailure) != 2)
            {
                throw new InvalidOperationException("A terminating branch statement did not return the host diagnostic exit code.");
            }
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }

        if (!branchOutput.ToString().Contains("Verb", StringComparison.Ordinal)
            || !branchError.ToString().Contains("error[AOT5001]", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A completed branch pipeline was not streamed before a later branch statement failed.");
        }

        const string nonBooleanSource = "$bad = 'x'; if ($bad) { Get-Verb -Group Common }";
        try
        {
            _ = AotExecutionKernel.Compile(nonBooleanSource, "if-condition.ps1").Execute(new AotExecutionContext());
            throw new InvalidOperationException("A non-Boolean if condition was accepted.");
        }
        catch (ScriptException error) when (error.Diagnostic is { Id: "AOT5005", Span: { DocumentName: "if-condition.ps1", StartColumn: 17 } })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                nonBooleanSource,
                """
error[AOT5005]: If requires a Boolean condition in the Native AOT subset.
  --> if-condition.ps1:1:17
   |
1 | $bad = 'x'; if ($bad) { Get-Verb -Group Common }
   |                 ^^^^ non-Boolean condition
   = help: Use $true/$false or compare two supported closed values with -eq, -ne, -gt, -ge, -lt, or -le.
""");
        }

        try
        {
            _ = AotExecutionKernel.Compile("$left = 'one'; $right = 1; if ($left -gt $right) { Get-Verb -Group Common }", "if-comparison.ps1")
                .Execute(new AotExecutionContext());
            throw new InvalidOperationException("An incompatible conditional comparison was accepted.");
        }
        catch (ScriptException error) when (error.Diagnostic.Id == "AOT5005")
        {
            // Closed-value comparison intentionally has no dynamic coercion.
        }

        foreach (string unsupported in new[]
        {
            "if ($true -and $false) { Get-Verb -Group Common }",
            "if (Get-Date) { Get-Verb -Group Common }",
            "if (($true)) { Get-Verb -Group Common }",
            "if ($true) { while ($false) { Get-Verb -Group Common } }",
        })
        {
            try
            {
                _ = AotExecutionKernel.Compile(unsupported, "if-unsupported.ps1");
                throw new InvalidOperationException($"The kernel accepted deferred conditional syntax '{unsupported}'.");
            }
            catch (ScriptException error) when (error.Diagnostic.Id == "AOT1001")
            {
                // Valid upstream syntax remains fail-closed until it receives
                // a separately reviewed expression/control-flow plan.
            }
        }

        const string redirectedConditional = "if ($true) { Get-Verb -Group Common } > out.txt";
        try
        {
            _ = AotExecutionKernel.Compile(redirectedConditional, "if-redirection.ps1");
            throw new InvalidOperationException("A redirection following a conditional reached the command binder.");
        }
        catch (ScriptException error) when (error.Diagnostic is { Id: "AOT1001", Span: { DocumentName: "if-redirection.ps1", StartColumn: 39 } })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                redirectedConditional,
                """
error[AOT1001]: Redirections are parsed but not executable by the Native AOT structural subset.
  --> if-redirection.ps1:1:39
   |
1 | if ($true) { Get-Verb -Group Common } > out.txt
   |                                       ^ unsupported redirection
   = help: Remove the redirection or use the host's explicit output contract.
""");
        }

        foreach (string redirection in new[]
        {
            "if ($true) { Get-Verb -Group Common } >> out.txt",
            "if ($true) { Get-Verb -Group Common } 2>&1",
        })
        {
            try
            {
                _ = AotExecutionKernel.Compile(redirection, "if-redirection-shape.ps1");
                throw new InvalidOperationException($"A redirection pseudo-command was accepted: '{redirection}'.");
            }
            catch (ScriptException error) when (error.Diagnostic.Id == "AOT1001")
            {
                // Different upstream AST shapes still receive one lowerer
                // policy rather than being mislabeled as unknown commands.
            }
        }
    }

    private static void AssertTerminalPresentation()
    {
        if (!AotTerminalColorPolicy.Resolve(AotColorMode.Auto, new(false, "xterm-256color", null, false, false, 120))
            || AotTerminalColorPolicy.Resolve(AotColorMode.Auto, new(true, "xterm-256color", null, false, false, null))
            || AotTerminalColorPolicy.Resolve(AotColorMode.Auto, new(false, "dumb", null, false, false, 120))
            || AotTerminalColorPolicy.Resolve(AotColorMode.Auto, new(false, "xterm", "1", false, false, 120))
            || AotTerminalColorPolicy.Resolve(AotColorMode.Auto, new(false, "xterm", null, true, false, 120))
            || !AotTerminalColorPolicy.Resolve(AotColorMode.Auto, new(false, "xterm", null, true, true, 120))
            || !AotTerminalColorPolicy.Resolve(AotColorMode.Always, new(true, null, "1", true, false, null))
            || AotTerminalColorPolicy.Resolve(AotColorMode.Never, new(false, "xterm", null, false, false, 120)))
        {
            throw new InvalidOperationException("Terminal color policy regression.");
        }

        StringWriter output = new(CultureInfo.InvariantCulture);
        StringWriter error = new(CultureInfo.InvariantCulture);
        TextWriter originalOutput = Console.Out;
        TextWriter originalError = Console.Error;
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            if (ScriptRunner.Execute("Get-Verb -Group $missing", colorMode: AotColorMode.Always) != 2)
            {
                throw new InvalidOperationException("Forced ANSI diagnostic execution returned the wrong exit code.");
            }
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }

        if (output.GetStringBuilder().Length != 0
            || !error.ToString().Contains("\u001b[31merror[AOT5001]", StringComparison.Ordinal)
            || !error.ToString().Contains("\u001b[0m", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Forced ANSI diagnostics did not retain the typed terminal contract.");
        }

        AotDiagnostic hostile = AotDiagnostics.Runtime("AOT3999", "Bad\u001b[31m heading", new AotSourceSpan("bad\u001bname.ps1", 0, 4, 1, 1, 1, 5), "bad\u001b label", "bad\u001b help");
        string sanitized = AotDiagnosticRenderer.Render(hostile, "bad\u001b", options: new AotDiagnosticRenderOptions(UseAnsi: true));
        if (!sanitized.Contains("\\u001B", StringComparison.Ordinal)
            || sanitized.Count(character => character == '\u001b') != 2)
        {
            throw new InvalidOperationException("Diagnostic rendering allowed source-derived terminal control characters.");
        }
    }

    private static void AssertClosedValuePlane()
    {
        AotRecord record = new(
        [
            new AotField("Name", AotValue.FromString("pwsh")),
            new AotField("CPU", AotValue.FromDecimal(12.5m)),
        ]);
        if (!record.TryGetValue("name", out AotValue name)
            || !name.TryGetString(out string? nameText)
            || nameText != "pwsh"
            || !record.TryGetValue("CPU", out _))
        {
            throw new InvalidOperationException("AOT record case-insensitive lookup regression.");
        }

        try
        {
            _ = new AotRecord(
            [
                new AotField("Name", AotValue.FromString("first")),
                new AotField("NAME", AotValue.FromString("second")),
            ]);
            throw new InvalidOperationException("AOT records accepted duplicate case-insensitive fields.");
        }
        catch (ArgumentException)
        {
        }

        byte[] sourceBytes = [1, 2];
        AotValue bytes = AotValue.FromBytes(sourceBytes);
        sourceBytes[0] = 9;
        if (!bytes.TryGetBytes(out ReadOnlyMemory<byte> copiedBytes) || !copiedBytes.Span.SequenceEqual(new byte[] { 1, 2 }))
        {
            throw new InvalidOperationException("AOT byte values retained caller-owned storage.");
        }

        List<AotValue> sourceList = [AotValue.FromInteger(1)];
        AotValue list = AotValue.FromList(sourceList);
        sourceList[0] = AotValue.FromInteger(2);
        if (!list.TryGetItems(out IReadOnlyList<AotValue>? copiedList)
            || copiedList is null
            || !copiedList[0].TryGetInteger(out long originalListValue)
            || originalListValue != 1)
        {
            throw new InvalidOperationException("AOT list values retained caller-owned storage.");
        }

        AotRecord projected = record.Project(["CPU", "Name"]);
        AotRecord replaced = record.WithField("name", AotValue.FromString("pwsh-preview"));
        if (!projected.Fields.Select(static field => field.Name).SequenceEqual(["CPU", "Name"])
            || !replaced.GetRequiredValue("Name").TryGetString(out string? replacement)
            || replacement != "pwsh-preview"
            || !record.GetRequiredValue("Name").TryGetString(out string? original)
            || original != "pwsh")
        {
            throw new InvalidOperationException("AOT record projection/immutability regression.");
        }

        if (!AotValueComparison.TryCompare(AotValue.FromInteger(12), AotValue.FromDecimal(12m), out int equal)
            || equal != 0
            || !AotValueComparison.TryCompare(AotValue.FromFloatingPoint(12.5), AotValue.FromInteger(12), out int greater)
            || greater <= 0
            || AotValueComparison.TryCompare(AotValue.FromList([]), AotValue.FromList([]), out _)
            || AotValue.FromString("pwsh").TryGetProperty("Length", out _)
            || !AotValue.FromRecord(record).TryGetProperty("NAME", out _))
        {
            throw new InvalidOperationException("AOT value comparison/property-boundary regression.");
        }

        GetProcessCmdlet fixturePort = new(new FixtureProcessCatalog(
        [
            new ProcessRecord("pwsh-idle", 1, 1, 10),
            new ProcessRecord("pwsh-busy", 2, 12, 20),
        ]));
        CommandInvocation fixtureInvocation = new(fixturePort.Descriptor, new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = ["pwsh*"],
        });
        PipelinePlan valuePlan = new(
            fixturePort,
            fixtureInvocation,
            null,
            null,
            new Filter("CPU", Comparison.GreaterThan, AotValue.FromInteger(10)),
            ["Name", "Id"],
            projected: true);
        IReadOnlyList<IPipelineRecord> valueRows = valuePlan.Execute(new AotExecutionContext());
        if (valueRows.SingleOrDefault() is not AotPipelineRecord { Record: var result }
            || !result.Fields.Select(static field => field.Name).SequenceEqual(["Name", "Id"])
            || valueRows[0].TextFor("Name") != "pwsh-busy"
            || valueRows[0].TextFor("Id") != "2"
            || result.TryGetValue("CPU", out _))
        {
            throw new InvalidOperationException("Where-Object/Select-Object did not execute through the AOT record boundary.");
        }

        // This goes through the actual upstream AST lowerer, then uses an
        // adapter-provided field that was intentionally never part of the old
        // global Select-Object string list. Field lookup is record-authoritative
        // and case-insensitive from parser-derived command text to projection.
        PipelinePlan caseInsensitivePlan = UpstreamAstPipelineLowerer.Parse(
            "Get-TimeZone -ListAvailable | Where-Object baseutcoffsetminutes -ge -1000 | Select-Object id, baseutcoffsetminutes");
        IReadOnlyList<IPipelineRecord> caseInsensitiveRows = caseInsensitivePlan.Execute(new AotExecutionContext());
        if (caseInsensitiveRows.Count == 0
            || caseInsensitiveRows.Any(static row => row is not AotPipelineRecord
            {
                Record: { Fields: var fields },
            } || !fields.Select(static field => field.Name).SequenceEqual(["id", "baseutcoffsetminutes"])))
        {
            throw new InvalidOperationException("Upstream AST generic projection did not use case-insensitive AOT record fields.");
        }

        try
        {
            _ = UpstreamAstPipelineLowerer.Parse("Get-TimeZone -ListAvailable | Select-Object NotAnAotField")
                .Execute(new AotExecutionContext());
            throw new InvalidOperationException("Select-Object accepted a field absent from the actual AOT record.");
        }
        catch (ScriptException exception) when (exception.Message == "Select-Object requested a column not present on this pipeline value.")
        {
        }

        try
        {
            _ = UpstreamAstPipelineLowerer.Parse("Get-TimeZone -Id UTC | Select-Object id, ID")
                .Execute(new AotExecutionContext());
            throw new InvalidOperationException("Select-Object accepted case-insensitive duplicate projection fields.");
        }
        catch (ScriptException exception) when (exception.Message == "Select-Object does not permit duplicate fields that differ only by case in the AOT subset.")
        {
        }

        foreach (string literal in new[] { "NaN", "Infinity" })
        {
            try
            {
                _ = UpstreamAstPipelineLowerer.Parse($"Get-Process | Where-Object CPU -gt {literal}");
                throw new InvalidOperationException($"Where-Object accepted non-finite literal '{literal}'.");
            }
            catch (ScriptException exception) when (exception.Message == "Where-Object does not accept NaN or infinity predicate literals in the AOT subset.")
            {
            }
        }
    }

    private static void AssertRepositoryCatalogValidationAndFindModuleBehavior()
    {
        string fixtureRoot = Path.Combine(Path.GetTempPath(), $"pwsh-aot-lite-repository-catalog-{Guid.NewGuid():N}");
        string? previousPath = Environment.GetEnvironmentVariable("PWSH_AOT_REPOSITORIES_PATH");
        try
        {
            Directory.CreateDirectory(fixtureRoot);
            WriteRepositoryIndex(Path.Combine(fixtureRoot, "valid.repository.json"), "FixtureRepo", "Test.Repository.Tools", "2.0.0", "safe fixture package");
            WriteRepositoryIndex(Path.Combine(fixtureRoot, "other.repository.json"), "FixtureRepo", "Test.Repository.Tools", "1.0.0", "older safe fixture package");
            WriteRepositoryIndex(Path.Combine(fixtureRoot, "invalid-schema.repository.json"), "RejectedRepo", "Test.Repository.BadSchema", "1.0.0", "bad schema", schemaVersion: 2);
            WriteRepositoryIndex(Path.Combine(fixtureRoot, "unsafe-text.repository.json"), "RejectedRepo", "Test.Repository.Unsafe", "1.0.0", "unsafe\nterminal control");
            WriteRepositoryIndex(Path.Combine(fixtureRoot, "credential-uri.repository.json"), "RejectedRepo", "Test.Repository.Credentials", "1.0.0", "credential uri", packageUri: "https://user:password@example.invalid/package.nupkg");
            File.WriteAllText(Path.Combine(fixtureRoot, "malformed.repository.json"), "{ this is not valid JSON");

            Environment.SetEnvironmentVariable("PWSH_AOT_REPOSITORIES_PATH", fixtureRoot);
            RepositoryModuleEntry[] entries = RepositoryCatalog.Instance.Find("Test.Repository.*").ToArray();
            if (entries.Length != 2
                || !entries.Select(static entry => entry.Version).SequenceEqual(["2.0.0", "1.0.0"])
                || entries.Any(static entry => entry.Repository != "FixtureRepo" || entry.PackageUri.Contains("user:", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Repository catalog did not retain only valid, deterministic, credential-free metadata entries.");
            }

            PipelinePlan findPlan = ScriptParser.Parse("Find-Module Test.Repository.* -Repository Fixture*");
            IReadOnlyList<IPipelineRecord> rows = findPlan.Execute(new AotExecutionContext());
            if (rows.Count != 2
                || rows[0] is not RepositoryModuleRecord { Name: "Test.Repository.Tools", Version: "2.0.0", Compatibility: "legacy-pwsh-sidecar", RegistrationMode: "isolated-sidecar-import" }
                || rows[1] is not RepositoryModuleRecord { Version: "1.0.0" })
            {
                throw new InvalidOperationException("Find-Module did not project the repository catalog through its typed result contract.");
            }

            if (ScriptParser.Parse("Get-Help Find-Module").Execute(new AotExecutionContext()).SingleOrDefault() is not HelpRecord { Content: var help }
                || !help.Contains("never contacts PowerShell Gallery", StringComparison.Ordinal)
                || ScriptParser.Parse("Get-Command Find-Module").Execute(new AotExecutionContext()).SingleOrDefault() is not CommandInfoRecord { Availability: "native-aot", ModuleName: "PwshAotLite.ControlPlane" })
            {
                throw new InvalidOperationException("Find-Module host control-plane metadata was not discoverable through Get-Help/Get-Command.");
            }

            try
            {
                _ = ScriptParser.Parse("Find-Module -AllowPrerelease Test.Repository.*").Execute(new AotExecutionContext());
                throw new InvalidOperationException("Find-Module accepted an unsupported marketplace mode.");
            }
            catch (ScriptException)
            {
                // The local-index proof must not fake PowerShellGet options.
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PWSH_AOT_REPOSITORIES_PATH", previousPath);
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    private static void WriteRepositoryIndex(
        string path,
        string repository,
        string module,
        string version,
        string description,
        int schemaVersion = 1,
        string? packageUri = null)
    {
        string resolvedPackageUri = packageUri ?? "https://example.invalid/" + module + "." + version + ".nupkg";
        File.WriteAllText(path, $$"""
{
  "schema": "https://pwsh-aot-lite.dev/schemas/repository/v1",
  "schemaVersion": {{schemaVersion}},
  "repository": { "name": "{{repository}}", "uri": "https://example.invalid/{{repository}}/" },
  "modules": [
    {
      "name": "{{module}}",
      "version": "{{version}}",
      "description": "{{description}}",
      "packageUri": "{{resolvedPackageUri}}",
      "compatibility": "legacy-pwsh-sidecar",
      "registrationMode": "isolated-sidecar-import"
    }
  ]
}
""");
    }

    private static void AssertLocalPackageInstallBehavior()
    {
        // Use the real executable directory rather than macOS's logical /var
        // temp alias: installer tests intentionally reject symlinks in every
        // configured filesystem-path component.
        string fixtureRoot = Path.Combine(AppContext.BaseDirectory, $".pwsh-aot-lite-installer-{Guid.NewGuid():N}");
        string sourceRoot = Path.Combine(fixtureRoot, "approved-packages");
        string destinationRoot = Path.Combine(fixtureRoot, "extension-discovery-parent", "extensions");
        string repositoryRoot = Path.Combine(fixtureRoot, "repositories");
        string outsideRoot = Path.Combine(fixtureRoot, "outside-packages");
        string? priorRepositories = Environment.GetEnvironmentVariable("PWSH_AOT_REPOSITORIES_PATH");
        string? priorPackageRoots = Environment.GetEnvironmentVariable("PWSH_AOT_PACKAGE_ROOTS");
        string? priorExtensionRoot = Environment.GetEnvironmentVariable("PWSH_AOT_EXTENSIONS_ROOT");
        string? priorExtensionPath = Environment.GetEnvironmentVariable("PWSH_AOT_EXTENSIONS_PATH");
        try
        {
            Directory.CreateDirectory(sourceRoot);
            Directory.CreateDirectory(repositoryRoot);
            Directory.CreateDirectory(outsideRoot);
            WriteExtensionPackage(sourceRoot, "Fixture.Safe", "1.0.0", "Get-FixtureSafe", "safe installer fixture");
            WriteExtensionPackage(sourceRoot, "Fixture.HashMismatch", "1.0.0", "Get-FixtureHashMismatch", "hash mismatch fixture");
            WriteExtensionPackage(sourceRoot, "Fixture.StagedTamper", "1.0.0", "Get-FixtureStagedTamper", "staged tamper fixture");
            WriteExtensionPackage(sourceRoot, "Fixture.Cleanup", "1.0.0", "Get-FixtureCleanup", "cleanup fixture");
            WriteExtensionPackage(sourceRoot, "Fixture.Volume", "1.0.0", "Get-FixtureVolume", "volume fixture");
            WriteExtensionPackage(sourceRoot, "Fixture.SourceChanged", "1.0.0", "Get-FixtureSourceChanged", "source changed fixture");
            WriteExtensionPackage(sourceRoot, "Fixture.SpecialObject", "1.0.0", "Get-FixtureSpecialObject", "special object fixture");
            WriteExtensionPackage(sourceRoot, "Fixture.DestinationLink", "1.0.0", "Get-FixtureDestinationLink", "destination link fixture");
            WriteExtensionPackage(sourceRoot, "Fixture.Conflict", "1.0.0", "Get-FixtureConflict", "conflict fixture");
            WriteExtensionPackage(sourceRoot, "Fixture.TooManyFiles", "1.0.0", "Get-FixtureTooManyFiles", "bounds fixture");
            WriteExtensionPackage(outsideRoot, "Fixture.Outside", "1.0.0", "Get-FixtureOutside", "outside configured root fixture");
            WriteExtensionPackage(sourceRoot, "Fixture.InvalidSchema", "1.0.0", "Get-FixtureInvalidSchema", "invalid installer schema fixture", schemaVersion: 2);

            string tooManyFilesPackage = Path.Combine(sourceRoot, "Fixture.TooManyFiles", "1.0.0");
            for (int file = 0; file < 30; file++)
            {
                File.WriteAllText(Path.Combine(tooManyFilesPackage, "extra" + file + ".txt"), "fixture");
            }

            string safePackage = Path.Combine(sourceRoot, "Fixture.Safe", "1.0.0");
            string hashMismatchPackage = Path.Combine(sourceRoot, "Fixture.HashMismatch", "1.0.0");
            string outsidePackage = Path.Combine(outsideRoot, "Fixture.Outside", "1.0.0");
            string invalidSchemaPackage = Path.Combine(sourceRoot, "Fixture.InvalidSchema", "1.0.0");
            string stagedTamperPackage = Path.Combine(sourceRoot, "Fixture.StagedTamper", "1.0.0");
            string cleanupPackage = Path.Combine(sourceRoot, "Fixture.Cleanup", "1.0.0");
            string volumePackage = Path.Combine(sourceRoot, "Fixture.Volume", "1.0.0");
            string sourceChangedPackage = Path.Combine(sourceRoot, "Fixture.SourceChanged", "1.0.0");
            string specialObjectPackage = Path.Combine(sourceRoot, "Fixture.SpecialObject", "1.0.0");
            bool supportsFifoFixture = OperatingSystem.IsMacOS() || OperatingSystem.IsLinux();
            if (supportsFifoFixture)
            {
                CreateFifo(Path.Combine(specialObjectPackage, "named-pipe"));
            }
            string destinationLinkPackage = Path.Combine(sourceRoot, "Fixture.DestinationLink", "1.0.0");
            string conflictPackage = Path.Combine(sourceRoot, "Fixture.Conflict", "1.0.0");
            WriteInstallRepositoryIndex(
                Path.Combine(repositoryRoot, "installer.repository.json"),
                ("Fixture.Safe", "1.0.0", safePackage, LocalPackageModuleInstaller.CalculateContentSha256(safePackage)),
                ("Fixture.HashMismatch", "1.0.0", hashMismatchPackage, new string('0', 64)),
                ("Fixture.Outside", "1.0.0", outsidePackage, LocalPackageModuleInstaller.CalculateContentSha256(outsidePackage)),
                ("Fixture.InvalidSchema", "1.0.0", invalidSchemaPackage, LocalPackageModuleInstaller.CalculateContentSha256(invalidSchemaPackage)),
                ("Fixture.StagedTamper", "1.0.0", stagedTamperPackage, LocalPackageModuleInstaller.CalculateContentSha256(stagedTamperPackage)),
                ("Fixture.Cleanup", "1.0.0", cleanupPackage, LocalPackageModuleInstaller.CalculateContentSha256(cleanupPackage)),
                ("Fixture.Volume", "1.0.0", volumePackage, LocalPackageModuleInstaller.CalculateContentSha256(volumePackage)),
                ("Fixture.SourceChanged", "1.0.0", sourceChangedPackage, LocalPackageModuleInstaller.CalculateContentSha256(sourceChangedPackage)),
                ("Fixture.SpecialObject", "1.0.0", specialObjectPackage, new string('0', 64)),
                ("Fixture.DestinationLink", "1.0.0", destinationLinkPackage, LocalPackageModuleInstaller.CalculateContentSha256(destinationLinkPackage)),
                ("Fixture.Conflict", "1.0.0", conflictPackage, LocalPackageModuleInstaller.CalculateContentSha256(conflictPackage)),
                ("Fixture.TooManyFiles", "1.0.0", tooManyFilesPackage, new string('0', 64)));

            Environment.SetEnvironmentVariable("PWSH_AOT_REPOSITORIES_PATH", repositoryRoot);
            Environment.SetEnvironmentVariable("PWSH_AOT_PACKAGE_ROOTS", sourceRoot);
            Environment.SetEnvironmentVariable("PWSH_AOT_EXTENSIONS_ROOT", destinationRoot);
            // Scan the parent as well as the actual extension root. This is
            // the adversarial shape that used to reveal in-root staging.
            Environment.SetEnvironmentVariable("PWSH_AOT_EXTENSIONS_PATH", Path.GetDirectoryName(destinationRoot));

            if (ScriptParser.Parse("Install-Module Fixture.Safe -Repository FixtureRepo").Execute(new AotExecutionContext()).SingleOrDefault() is not InstallModuleRecord
                {
                    Name: "Fixture.Safe", Version: "1.0.0", Status: "installed", Path: var activatedPath,
                }
                || !activatedPath.EndsWith(Path.Combine("Fixture.Safe", "1.0.0"), StringComparison.OrdinalIgnoreCase)
                || !File.Exists(Path.Combine(activatedPath, "extension.json"))
                || CompositeModuleCatalog.Instance.Find("Fixture.Safe").SingleOrDefault() is not { Version: "1.0.0" }
                || CompositeHelpCatalog.Instance.Find("Get-FixtureSafe").SingleOrDefault() is not { Synopsis: "safe installer fixture" }
                || ScriptParser.Parse("Get-Command Get-FixtureSafe").Execute(new AotExecutionContext()).SingleOrDefault() is not CommandInfoRecord { ModuleName: "Fixture.Safe" })
            {
                throw new InvalidOperationException("Install-Module did not activate a validated package into the shared extension catalog.");
            }

            if (ScriptParser.Parse("Install-Module Fixture.Safe -Repository FixtureRepo").Execute(new AotExecutionContext()).SingleOrDefault() is not InstallModuleRecord { Status: "already-installed" })
            {
                throw new InvalidOperationException("Install-Module idempotence policy regressed for identical existing content.");
            }

            // The staging seam asks the live catalogs while the copy exists;
            // staging is a sibling of extensions, so unvalidated content must
            // not leak into discovery. It then mutates the staged bytes to
            // prove the post-copy digest stops a source-copy TOCTOU change.
            AssertInstallerRejected(
                new LocalPackageModuleInstaller(RepositoryCatalog.Instance, new ProbingTamperingStager("Get-FixtureStagedTamper")),
                "Fixture.StagedTamper",
                "InstallStagedHashMismatch");

            if (new LocalPackageModuleInstaller(RepositoryCatalog.Instance, cleaner: new ThrowingStagingCleaner())
                    .Install("Fixture.Cleanup", "FixtureRepo") is not { Status: "installed" }
                || !Directory.Exists(Path.Combine(destinationRoot, "Fixture.Cleanup", "1.0.0")))
            {
                throw new InvalidOperationException("A staging cleanup failure was allowed to mask a successful atomic activation.");
            }

            AssertInstallerRejected(
                new LocalPackageModuleInstaller(
                    RepositoryCatalog.Instance,
                    volumeVerifier: new RejectingVolumeVerifier()),
                "Fixture.Volume",
                "InstallCrossVolumeStaging");

            AssertInstallerRejected(
                new LocalPackageModuleInstaller(RepositoryCatalog.Instance, new SourceMutatingStager()),
                "Fixture.SourceChanged",
                "InstallSourceChanged");

            Directory.CreateDirectory(Path.Combine(destinationRoot, "destination-link-target"));
            Directory.CreateSymbolicLink(
                Path.Combine(destinationRoot, "Fixture.DestinationLink"),
                Path.Combine(destinationRoot, "destination-link-target"));
            AssertInstallRejected("Fixture.DestinationLink", "InstallDestinationSymlink");

            string sourceLink = Path.Combine(sourceRoot, "untrusted-child-link");
            Directory.CreateSymbolicLink(sourceLink, sourceRoot);
            string sourceLinkPackage = Path.Combine(sourceLink, "Fixture.Safe", "1.0.0");
            AssertInstallerRejected(
                new LocalPackageModuleInstaller(new StaticRepositoryCatalog(new RepositoryModuleEntry(
                    "Fixture.Safe", "1.0.0", "source-link", "FixtureRepo", "https://example.invalid/fixture/", new Uri(sourceLinkPackage).AbsoluteUri,
                    LocalPackageModuleInstaller.CalculateContentSha256(safePackage), "legacy-pwsh-sidecar", "isolated-sidecar-import"))),
                "Fixture.Safe",
                "InstallSourceSymlink");

            AssertInstallerRejected(
                new LocalPackageModuleInstaller(new StaticRepositoryCatalog(new RepositoryModuleEntry(
                    "Fixture.UriHost", "1.0.0", "uri-host", "FixtureRepo", "https://example.invalid/fixture/", "file://localhost" + new Uri(safePackage).AbsolutePath,
                    LocalPackageModuleInstaller.CalculateContentSha256(safePackage), "legacy-pwsh-sidecar", "isolated-sidecar-import"))),
                "Fixture.UriHost",
                "InstallSourceUriNotAllowed");

            if (ScriptParser.Parse("Install-Module Fixture.Conflict -Repository FixtureRepo").Execute(new AotExecutionContext()).SingleOrDefault() is not InstallModuleRecord { Status: "installed" })
            {
                throw new InvalidOperationException("Installer conflict fixture did not establish an existing package.");
            }
            File.WriteAllText(Path.Combine(conflictPackage, "changed.txt"), "different content");
            WriteInstallRepositoryIndex(
                Path.Combine(repositoryRoot, "installer.repository.json"),
                ("Fixture.Conflict", "1.0.0", conflictPackage, LocalPackageModuleInstaller.CalculateContentSha256(conflictPackage)));
            AssertInstallRejected("Fixture.Conflict", "InstallConflict");

            // Restore the repository fixture needed by the remaining parser
            // tests after exercising the overwrite policy.
            WriteInstallRepositoryIndex(
                Path.Combine(repositoryRoot, "installer.repository.json"),
                ("Fixture.HashMismatch", "1.0.0", hashMismatchPackage, new string('0', 64)),
                ("Fixture.Outside", "1.0.0", outsidePackage, LocalPackageModuleInstaller.CalculateContentSha256(outsidePackage)),
                ("Fixture.InvalidSchema", "1.0.0", invalidSchemaPackage, LocalPackageModuleInstaller.CalculateContentSha256(invalidSchemaPackage)),
                ("Fixture.TooManyFiles", "1.0.0", tooManyFilesPackage, new string('0', 64)));

            AssertInstallRejected("Fixture.HashMismatch", "InstallHashMismatch");
            AssertInstallRejected("Fixture.Outside", "InstallSourceOutsideRoot");
            AssertInstallRejected("Fixture.InvalidSchema", "InstallPackageInvalid");
            AssertInstallRejected("Fixture.TooManyFiles", "InstallPackageTooLarge");
            // A FIFO is the reviewed Unix special-file fixture. Windows builds
            // still exercise the installer suite, but must not pretend this
            // Unix-only object test ran on a different filesystem model.
            if (supportsFifoFixture)
            {
                AssertInstallerRejected(
                    new LocalPackageModuleInstaller(new StaticRepositoryCatalog(new RepositoryModuleEntry(
                        "Fixture.SpecialObject", "1.0.0", "special object", "FixtureRepo", "https://example.invalid/fixture/", new Uri(specialObjectPackage).AbsoluteUri,
                        new string('0', 64), "legacy-pwsh-sidecar", "isolated-sidecar-import"))),
                    "Fixture.SpecialObject",
                    "InstallPackageObjectNotRegular");
            }
            if (Directory.Exists(Path.Combine(destinationRoot, "Fixture.HashMismatch"))
                || Directory.Exists(Path.Combine(destinationRoot, "Fixture.Outside"))
                || Directory.Exists(Path.Combine(destinationRoot, "Fixture.InvalidSchema"))
                || Directory.Exists(Path.Combine(destinationRoot, "Fixture.StagedTamper"))
                || Directory.Exists(Path.Combine(destinationRoot, "Fixture.TooManyFiles")))
            {
                throw new InvalidOperationException("Rejected installer fixtures left a partially activated extension package.");
            }

            AssertPlatformRootAliasInstallBehavior();

            if (ScriptParser.Parse("Get-Help Install-Module").Execute(new AotExecutionContext()).SingleOrDefault() is not HelpRecord { Content: var help }
                || !help.Contains("hash-verified declarative extension package", StringComparison.Ordinal)
                || ScriptParser.Parse("Get-Command Install-Module").Execute(new AotExecutionContext()).SingleOrDefault() is not CommandInfoRecord { Availability: "native-aot" })
            {
                throw new InvalidOperationException("Install-Module control-plane discovery metadata regressed.");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PWSH_AOT_REPOSITORIES_PATH", priorRepositories);
            Environment.SetEnvironmentVariable("PWSH_AOT_PACKAGE_ROOTS", priorPackageRoots);
            Environment.SetEnvironmentVariable("PWSH_AOT_EXTENSIONS_ROOT", priorExtensionRoot);
            Environment.SetEnvironmentVariable("PWSH_AOT_EXTENSIONS_PATH", priorExtensionPath);
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    private static void AssertInstallRejected(string name, string errorId)
        => AssertInstallerRejected(new LocalPackageModuleInstaller(RepositoryCatalog.Instance), name, errorId);

    private static void AssertInstallerRejected(IModuleInstaller installer, string name, string errorId)
    {
        try
        {
            _ = installer.Install(name, "FixtureRepo");
            throw new InvalidOperationException($"Install-Module accepted the deliberately rejected fixture '{name}'.");
        }
        catch (ScriptException error) when (error.Message.StartsWith(errorId + ":", StringComparison.Ordinal))
        {
            // Expected: the package must not reach the activation directory.
        }
    }

    private static void AssertPlatformRootAliasInstallBehavior()
    {
        // macOS commonly exposes the same physical directory through /tmp and
        // /var aliases. Explicitly configuring one is trusted-root intent, not
        // an untrusted child symlink traversal.
        string aliasRoot = Path.Combine(Path.GetTempPath(), "pwsh-aot-lite-root-alias-" + Guid.NewGuid().ToString("N"));
        string? priorPackageRoots = Environment.GetEnvironmentVariable("PWSH_AOT_PACKAGE_ROOTS");
        string? priorExtensionRoot = Environment.GetEnvironmentVariable("PWSH_AOT_EXTENSIONS_ROOT");
        try
        {
            WriteExtensionPackage(aliasRoot, "Fixture.RootAlias", "1.0.0", "Get-FixtureRootAlias", "trusted root alias fixture");
            string package = Path.Combine(aliasRoot, "Fixture.RootAlias", "1.0.0");
            Environment.SetEnvironmentVariable("PWSH_AOT_PACKAGE_ROOTS", aliasRoot);
            Environment.SetEnvironmentVariable("PWSH_AOT_EXTENSIONS_ROOT", Path.Combine(aliasRoot, "extensions"));
            string physicalAliasRoot = aliasRoot.StartsWith("/var/", StringComparison.Ordinal)
                ? "/private" + aliasRoot
                : aliasRoot;
            string physicalChildLink = Path.Combine(physicalAliasRoot, "untrusted-child-link");
            Directory.CreateSymbolicLink(physicalChildLink, physicalAliasRoot);
            AssertInstallerRejected(
                new LocalPackageModuleInstaller(new StaticRepositoryCatalog(new RepositoryModuleEntry(
                    "Fixture.RootAlias", "1.0.0", "mixed alias child link", "FixtureRepo", "https://example.invalid/fixture/", new Uri(Path.Combine(physicalChildLink, "Fixture.RootAlias", "1.0.0")).AbsoluteUri,
                    LocalPackageModuleInstaller.CalculateContentSha256(package), "legacy-pwsh-sidecar", "isolated-sidecar-import"))),
                "Fixture.RootAlias",
                "InstallSourceSymlink");
            ModuleInstallResult result = new LocalPackageModuleInstaller(new StaticRepositoryCatalog(new RepositoryModuleEntry(
                "Fixture.RootAlias", "1.0.0", "root alias", "FixtureRepo", "https://example.invalid/fixture/", new Uri(package).AbsoluteUri,
                LocalPackageModuleInstaller.CalculateContentSha256(package), "legacy-pwsh-sidecar", "isolated-sidecar-import")))
                .Install("Fixture.RootAlias", "FixtureRepo");
            if (result.Status != "installed" || !Directory.Exists(result.PackagePath))
            {
                throw new InvalidOperationException("An explicit platform root alias was not accepted as a trusted physical root.");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PWSH_AOT_PACKAGE_ROOTS", priorPackageRoots);
            Environment.SetEnvironmentVariable("PWSH_AOT_EXTENSIONS_ROOT", priorExtensionRoot);
            if (Directory.Exists(aliasRoot))
            {
                Directory.Delete(aliasRoot, recursive: true);
            }
        }
    }

    private static void WriteInstallRepositoryIndex(string path, params (string Name, string Version, string PackagePath, string Sha256)[] packages)
    {
        string modules = string.Join(",\n", packages.Select(package => $$"""
    {
      "name": "{{package.Name}}",
      "version": "{{package.Version}}",
      "description": "installer fixture",
      "packageUri": "{{new Uri(package.PackagePath).AbsoluteUri}}",
      "packageSha256": "{{package.Sha256}}",
      "compatibility": "legacy-pwsh-sidecar",
      "registrationMode": "isolated-sidecar-import"
    }
"""));
        File.WriteAllText(path, $$"""
{
  "schema": "https://pwsh-aot-lite.dev/schemas/repository/v1",
  "schemaVersion": 1,
  "repository": { "name": "FixtureRepo", "uri": "https://example.invalid/fixture/" },
  "modules": [
{{modules}}
  ]
}
""");
    }

    private static void AssertExtensionPackageCatalogValidationAndVersionSelection()
    {
        string fixtureRoot = Path.Combine(Path.GetTempPath(), $"pwsh-aot-lite-extension-catalog-{Guid.NewGuid():N}");
        string? previousRoot = Environment.GetEnvironmentVariable("PWSH_AOT_EXTENSIONS_ROOT");
        try
        {
            Directory.CreateDirectory(fixtureRoot);
            WriteExtensionPackage(fixtureRoot, "Catalog.Probe", "1.0.0", "Get-CatalogProbe", "old active candidate");
            WriteExtensionPackage(fixtureRoot, "Catalog.Probe", "2.0.0", "Get-CatalogProbe", "new active candidate");
            WriteExtensionPackage(fixtureRoot, "Catalog.Duplicate", "1.0.0", "Get-CatalogDuplicate", "invalid duplicate", duplicateCommand: true);
            WriteExtensionPackage(fixtureRoot, "Catalog.IdentityMismatch", "1.0.0", "Get-CatalogIdentityMismatch", "invalid identity", sourceModuleName: "Some.Other.Module");
            WriteExtensionPackage(fixtureRoot, "Catalog.SchemaMismatch", "1.0.0", "Get-CatalogSchemaMismatch", "invalid schema", schemaVersion: 2);
            WriteExtensionPackage(fixtureRoot, "Catalog.CollisionOne", "1.0.0", "Get-CatalogCollision", "first collision candidate");
            WriteExtensionPackage(fixtureRoot, "Catalog.CollisionTwo", "1.0.0", "Get-CatalogCollision", "second collision candidate");
            WriteControlCharacterExtensionPackage(fixtureRoot);
            WriteManifestOnlyExtensionPackage(fixtureRoot, "Catalog.ManifestOnly", "Get-ManifestOnly", corruptHelp: false);
            WriteManifestOnlyExtensionPackage(fixtureRoot, "Catalog.CorruptHelp", "Get-CorruptHelp", corruptHelp: true);

            Environment.SetEnvironmentVariable("PWSH_AOT_EXTENSIONS_ROOT", fixtureRoot);
            IReadOnlyList<ExtensionPackage> allPackages = ExtensionPackageCatalog.LoadAll();
            if (allPackages.Count(package => package.Manifest.Extension!.DisplayName == "Catalog.Probe") != 2
                || allPackages.Any(package => package.Manifest.Extension!.DisplayName is "Catalog.Duplicate" or "Catalog.IdentityMismatch" or "Catalog.SchemaMismatch"))
            {
                throw new InvalidOperationException("Extension package validation accepted duplicate, schema, or identity-invalid metadata.");
            }

            if (CompositeModuleCatalog.Instance.Find("Catalog.ManifestOnly").SingleOrDefault() is not { Version: "1.0.0" }
                || CompositeModuleCatalog.Instance.Find("Catalog.CorruptHelp").SingleOrDefault() is not { Version: "1.0.0" }
                || CompositeHelpCatalog.Instance.Find("Get-ManifestOnly") is not [
                    { Synopsis: "Generated contract help from registered extension manifest.", Origin: var missingHelpOrigin, Parameters: [ { Name: "Mode" } ] },
                ]
                || !missingHelpOrigin.Contains("authored help missing", StringComparison.Ordinal)
                || CompositeHelpCatalog.Instance.Find("Get-CorruptHelp") is not [
                    { Origin: var invalidHelpOrigin, Description: var invalidHelpDescription },
                ]
                || !invalidHelpOrigin.Contains("authored help invalid", StringComparison.Ordinal)
                || invalidHelpDescription is null
                || !invalidHelpDescription.Contains("generated from extension.json", StringComparison.Ordinal)
                || ScriptParser.Parse("Get-Help Get-ManifestOnly").Execute(new AotExecutionContext()).SingleOrDefault() is not HelpRecord { Content: var manifestHelp }
                || !manifestHelp.Contains("-Mode <System.String>", StringComparison.Ordinal)
                || !manifestHelp.Contains("generated manifest contract", StringComparison.Ordinal)
                || ScriptParser.Parse("Get-Command Get-ManifestOnly").Execute(new AotExecutionContext()).SingleOrDefault() is not CommandInfoRecord { ModuleName: "Catalog.ManifestOnly" }
                || !CompletionService.Instance.Suggest("Get-Manifest").Any(static suggestion => suggestion is { Text: "Get-ManifestOnly", Kind: "command" })
                || !CompletionService.Instance.Suggest("Get-Corrupt").Any(static suggestion => suggestion is { Text: "Get-CorruptHelp", Kind: "command" }))
            {
                throw new InvalidOperationException("A valid extension manifest without usable authored help disappeared from help, command, module, or completion discovery.");
            }

            HelpTopic[] activeTopics = CompositeHelpCatalog.Instance.Find("Get-CatalogProbe").ToArray();
            if (activeTopics is not [{ Version: "2.0.0", Synopsis: "new active candidate" }])
            {
                throw new InvalidOperationException("Extension command/help lookup did not deterministically select the highest active package version.");
            }

            ModuleCatalogEntry[] inventory = CompositeModuleCatalog.Instance.Find("Catalog.Probe").ToArray();
            if (inventory.Length != 2 || !inventory.Select(module => module.Version).SequenceEqual(["1.0.0", "2.0.0"]))
            {
                throw new InvalidOperationException("Get-Module catalog did not retain every valid installed package version.");
            }

            ModuleCatalogEntry[] activeModules = CompositeModuleCatalog.Instance.FindActive("Catalog.Probe").ToArray();
            if (activeModules is not [{ Version: "2.0.0" }]
                || !CompletionService.Instance.Suggest("Get-Command -Module Catalog.Pro")
                    .Any(static suggestion => suggestion is { Text: "Catalog.Probe", Description: var description } && description.StartsWith("2.0.0", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("Completion module suggestions did not use the catalog's deterministic active-version policy.");
            }

            HelpTopic[] collisions = CompositeHelpCatalog.Instance.Find("Get-CatalogCollision").ToArray();
            if (collisions.Length != 2
                || ScriptParser.Parse("Get-Help Get-CatalogCollision").Execute(new AotExecutionContext()).Count != 2
                || ScriptParser.Parse("Get-Command Get-CatalogCollision").Execute(new AotExecutionContext()).Count != 2
                || CompletionService.Instance.Suggest("Get-CatalogCollision ") is not [
                    { Kind: "ambiguous-command", Description: var firstCollision },
                    { Kind: "ambiguous-command", Description: var secondCollision },
                ]
                || !firstCollision.Contains("parameter completion withheld", StringComparison.Ordinal)
                || !secondCollision.Contains("parameter completion withheld", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Catalog command collision policy regressed: queries must retain every candidate and completion must not select a parameter surface.");
            }

            IReadOnlyList<CompletionSuggestion> unsafeSuggestions = CompletionService.Instance.Suggest("Get-Control");
            StringWriter captured = new(CultureInfo.InvariantCulture);
            TextWriter originalOutput = Console.Out;
            try
            {
                Console.SetOut(captured);
                CompletionWriter.Write(unsafeSuggestions);
            }
            finally
            {
                Console.SetOut(originalOutput);
            }

            string completionOutput = captured.ToString();
            if (!completionOutput.Contains("Get-Control\\nCommand", StringComparison.Ordinal)
                || !completionOutput.Contains("Catalog.Control\\tModule", StringComparison.Ordinal)
                || completionOutput.Count(character => character == '\n') != 1
                || completionOutput.Contains("Get-Control\nCommand", StringComparison.Ordinal)
                || completionOutput.Contains("Catalog.Control\tModule", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("CompletionWriter did not safely escape control characters from extension metadata.");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PWSH_AOT_EXTENSIONS_ROOT", previousRoot);
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    private static void WriteExtensionPackage(
        string root,
        string module,
        string version,
        string command,
        string synopsis,
        bool duplicateCommand = false,
        string? sourceModuleName = null,
        int schemaVersion = 1)
    {
        string packagePath = Path.Combine(root, module, version);
        Directory.CreateDirectory(packagePath);
        string duplicate = duplicateCommand
            ? ",\n    { \"name\": \"" + command + "\", \"commandType\": \"Function\", \"parameters\": [], \"outputTypes\": [] }"
            : string.Empty;
        File.WriteAllText(Path.Combine(packagePath, "extension.json"), $$"""
{
  "schema": "https://pwsh-aot-lite.dev/schemas/extension/v1",
  "schemaVersion": {{schemaVersion}},
  "extension": {
    "id": "test/{{module}}",
    "displayName": "{{module}}",
    "version": "{{version}}",
    "execution": { "kind": "declaration-only", "status": "blocked-not-imported" }
  },
  "commands": [
    { "name": "{{command}}", "commandType": "Function", "parameters": [], "outputTypes": [] }{{duplicate}}
  ]
}
""");
        File.WriteAllText(Path.Combine(packagePath, "help.json"), $$"""
{
  "module": { "name": "{{module}}" },
  "commands": [
    { "name": "{{command}}", "synopsis": "{{synopsis}}", "description": "fixture" }
  ]
}
""");
        File.WriteAllText(Path.Combine(packagePath, "provenance.json"), $$"""
{
  "registration": { "method": "fixture" },
  "sourceModule": { "name": "{{sourceModuleName ?? module}}", "path": "/fixture/{{module}}" }
}
""");
    }

    private static void WriteControlCharacterExtensionPackage(string root)
    {
        string packagePath = Path.Combine(root, "Catalog.Control", "1.0.0");
        Directory.CreateDirectory(packagePath);
        File.WriteAllText(Path.Combine(packagePath, "extension.json"), """
{
  "schema": "https://pwsh-aot-lite.dev/schemas/extension/v1",
  "schemaVersion": 1,
  "extension": {
    "id": "test/Catalog.Control",
    "displayName": "Catalog.Control\tModule",
    "version": "1.0.0",
    "execution": { "kind": "declaration-only", "status": "blocked-not-imported" }
  },
  "commands": [
    { "name": "Get-Control\nCommand", "commandType": "Function", "parameters": [], "outputTypes": [] }
  ]
}
""");
        File.WriteAllText(Path.Combine(packagePath, "help.json"), """
{
  "module": { "name": "Catalog.Control\tModule" },
  "commands": [
    { "name": "Get-Control\nCommand", "synopsis": "fixture", "description": "fixture" }
  ]
}
""");
        File.WriteAllText(Path.Combine(packagePath, "provenance.json"), """
{
  "registration": { "method": "fixture" },
  "sourceModule": { "name": "Catalog.Control\tModule", "path": "/fixture/Catalog.Control" }
}
""");
    }

    private static void WriteManifestOnlyExtensionPackage(string root, string module, string command, bool corruptHelp)
    {
        string packagePath = Path.Combine(root, module, "1.0.0");
        Directory.CreateDirectory(packagePath);
        File.WriteAllText(Path.Combine(packagePath, "extension.json"), $$"""
{
  "schema": "https://pwsh-aot-lite.dev/schemas/extension/v1",
  "schemaVersion": 1,
  "extension": {
    "id": "test/{{module}}",
    "displayName": "{{module}}",
    "version": "1.0.0",
    "execution": { "kind": "declaration-only", "status": "blocked-not-imported" }
  },
  "commands": [
    {
      "name": "{{command}}",
      "commandType": "Function",
      "parameters": [
        {
          "name": "Mode",
          "type": "System.String",
          "aliases": ["M"],
          "isSwitch": false,
          "validation": { "validateSet": ["Fast", "Safe"] },
          "parameterSets": [
            { "name": "__AllParameterSets", "position": null, "mandatory": false, "valueFromPipeline": false, "valueFromPipelineByPropertyName": false, "valueFromRemainingArguments": false }
          ]
        }
      ],
      "outputTypes": ["Test.Output"]
    }
  ]
}
""");
        if (corruptHelp)
        {
            File.WriteAllText(Path.Combine(packagePath, "help.json"), "{ this is deliberately invalid JSON");
        }
        File.WriteAllText(Path.Combine(packagePath, "provenance.json"), $$"""
{
  "registration": { "method": "fixture" },
  "sourceModule": { "name": "{{module}}", "path": "/fixture/{{module}}" }
}
""");
    }

    private sealed class StaticRepositoryCatalog(params RepositoryModuleEntry[] entries) : IRepositoryCatalog
    {
        public IReadOnlyList<RepositoryModuleEntry> Find(string namePattern, string? repositoryPattern = null) => entries
            .Where(entry => SimpleWildcard.IsMatch(namePattern, entry.Name))
            .Where(entry => repositoryPattern is null || SimpleWildcard.IsMatch(repositoryPattern, entry.Repository))
            .ToArray();
    }

    private sealed class ProbingTamperingStager(string stagedCommand) : IPackageStager
    {
        public void Copy(string source, string stagedPackage)
        {
            new PhysicalPackageStager().Copy(source, stagedPackage);
            if (CompositeModuleCatalog.Instance.Find("Fixture.StagedTamper").Count != 0
                || CompositeHelpCatalog.Instance.Find(stagedCommand).Count != 0
                || ScriptParser.Parse("Get-Command " + stagedCommand).Execute(new AotExecutionContext()).Count != 0
                || CompletionService.Instance.Suggest("Get-FixtureStagedTamper").Any())
            {
                throw new InvalidOperationException("An unvalidated staged package leaked into an extension catalog projection.");
            }

            File.AppendAllText(Path.Combine(stagedPackage, "extension.json"), "\n");
        }
    }

    private sealed class ThrowingStagingCleaner : IStagingAreaCleaner
    {
        public void Cleanup(string stagingRoot) => throw new IOException("fixture cleanup failure");
    }

    private sealed class SourceMutatingStager : IPackageStager
    {
        public void Copy(string source, string stagedPackage)
        {
            new PhysicalPackageStager().Copy(source, stagedPackage);
            File.WriteAllText(Path.Combine(source, "changed-during-stage.txt"), "source changed after copy");
        }
    }

    private sealed class RejectingVolumeVerifier : IActivationVolumeVerifier
    {
        public void EnsureSameVolume(string stagingRoot, string extensionRoot) => throw new ScriptException("InstallCrossVolumeStaging: fixture mountpoint rejection");
    }

    private static void CreateFifo(string path)
    {
        int result = OperatingSystem.IsMacOS()
            ? DarwinMkfifo(path, 0x1A4)
            : OperatingSystem.IsLinux()
                ? LinuxMkfifo(path, 0x1A4)
                : throw new InvalidOperationException("The self-test FIFO fixture requires a reviewed Unix host.");
        if (result != 0)
        {
            throw new InvalidOperationException("Could not create FIFO fixture.");
        }
    }

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "mkfifo", CharSet = CharSet.Ansi)]
    private static extern int DarwinMkfifo(string path, uint mode);

    [DllImport("libc.so.6", EntryPoint = "mkfifo", CharSet = CharSet.Ansi)]
    private static extern int LinuxMkfifo(string path, uint mode);

    private sealed class FixtureProcessCatalog(IReadOnlyList<ProcessRecord> processes) : IProcessCatalog
    {
        public IEnumerable<ProcessRecord> AllProcesses() => processes;
        public ProcessRecord? GetById(int id) => processes.SingleOrDefault(process => process.Id == id);
        public IEnumerable<ProcessModuleRecord> Modules(int id, bool includeFileVersion, AotExecutionContext context) => [new ProcessModuleRecord(id, "fixture-module", "/fixture", includeFileVersion ? "1.0" : string.Empty)];
        public ProcessFileVersionRecord? MainFileVersion(int id, AotExecutionContext context) => new ProcessFileVersionRecord(id, "/fixture", "1.0", "1.0");
        public string? UserName(int id, AotExecutionContext context) => "fixture-user";
    }

    private sealed class FixtureHostCulture(CultureInfo currentUICulture) : IHostCulture
    {
        public CultureInfo CurrentUICulture { get; } = currentUICulture;
        public CultureInfo CurrentCulture { get; } = currentUICulture;
    }

    private sealed class FixtureClock(DateTime now) : IClock
    {
        public DateTime Now { get; } = now;
    }

    private sealed class FixtureCultureCatalog(IEnumerable<CultureInfo> cultures) : ICultureCatalog
    {
        private readonly CultureInfo[] _cultures = [.. cultures];
        public CultureInfo GetCulture(string name) => _cultures.SingleOrDefault(culture => culture.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? throw new CultureNotFoundException(nameof(name), name, "Fixture culture was not found.");
        public IEnumerable<CultureInfo> AllCultures() => _cultures;
    }

    private sealed class FixtureTimeZoneCatalog(TimeZoneInfo local, IEnumerable<TimeZoneInfo> zones) : ITimeZoneCatalog
    {
        private readonly TimeZoneInfo[] _zones = [.. zones];
        public int RefreshCalls { get; private set; }
        public TimeZoneInfo Local { get; } = local;
        public void Refresh() => RefreshCalls++;
        public IEnumerable<TimeZoneInfo> AllTimeZones() => _zones;
        public TimeZoneInfo FindById(string id) => _zones.SingleOrDefault(zone => zone.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            ?? throw new TimeZoneNotFoundException($"Fixture time zone '{id}' was not found.");
    }
}
