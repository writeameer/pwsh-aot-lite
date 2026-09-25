using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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

// ErrorAction Stop has already emitted its terminating transcript event. This
// carrier prevents ScriptRunner from rendering the same diagnostic again.
internal sealed class AotPublishedTerminatingException(AotDiagnostic diagnostic) : AotDiagnosticException(diagnostic)
{
}

// Static replacement for the PowerShell Cmdlet/Parameter/WriteObject contract.
// It intentionally preserves cmdlet lifecycle and parameter-set information;
// ports do not flatten those semantics into untyped string switches.
internal enum AotParameterShape { Scalar, Array, Switch }

// Only J1's two reviewed lexical profiles select this branch.  It is a
// descriptor-owned closed mapping, not a second/general PowerShell binder.
internal enum StaticBindingMode
{
    Legacy,
    JoinPathSequential,
    SplitPathSelector,
    J2WhereStaticNumeric,
    J2SelectStaticFields,
}

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
    string? DefaultParameterName = null,
    StaticBindingMode BindingMode = StaticBindingMode.Legacy);

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
    IReadOnlyDictionary<string, AotSourceSpan?[]>? valueSpans = null,
    AotCommonParameters? commonParameters = null,
    IReadOnlyDictionary<string, AotSourceSpan?>? parameterSpans = null)
{
    internal CmdletDescriptor Descriptor { get; } = descriptor;
    internal AotSourceSpan? SourceSpan { get; } = sourceSpan;
    internal AotCommonParameters CommonParameters { get; } = commonParameters ?? AotCommonParameters.Default;

    internal bool TryGetValues(string name, out string[] values) => parameters.TryGetValue(name, out values!);

    internal AotSourceSpan? GetValueSpan(string name, int index) =>
        valueSpans is not null
        && valueSpans.TryGetValue(name, out AotSourceSpan?[]? spans)
        && index >= 0
        && index < spans.Length
            ? spans[index]
            : SourceSpan;
    internal AotSourceSpan? GetParameterSpan(string name) =>
        parameterSpans is not null && parameterSpans.TryGetValue(name, out AotSourceSpan? span) ? span : SourceSpan;

    internal CommandInvocation WithCommonParameters(AotCommonParameters commonParameters) =>
        new(Descriptor, parameters, SourceSpan, valueSpans, commonParameters, parameterSpans);
}

// Parser-independent syntax atoms. The upstream AST lowerer and the legacy
// compatibility tokenizer both feed this one generated-metadata binder; the
// binder itself remains the sole authority for aliases and parameter shapes.
// Preserve the upstream CommandParameterAst.Argument association. In
// particular, `-Empty:$false` is not a bare `-Empty` followed by a positional
// `$false`; the binder never reparses command text to recreate this fact.
internal sealed record CommandSyntaxAtom(
    string Text,
    bool IsParameter,
    AotSourceSpan? Span = null,
    bool IsAttachedParameterValue = false,
    bool? AttachedDirectBoolean = null,
    int? GroupId = null);

internal sealed class CommandError(AotDiagnostic diagnostic)
{
    internal AotDiagnostic Diagnostic { get; } = diagnostic;
    internal string Id => Diagnostic.Id;
    internal string Message => Diagnostic.PrimaryMessage;
}

internal sealed class AotExecutionContext(CancellationToken cancellationToken = default)
{
    private readonly List<CommandError> _errors = [];
    private readonly List<AotRuntimeEvent> _events = [];
    private readonly List<Action<AotRuntimeEvent>> _observers = [];
    private readonly HashSet<string> _activeFunctions = new(StringComparer.OrdinalIgnoreCase);
    private AotInvocationFrame? _activeInvocation;
    private AotCommonParameters _activeCommonParameters = AotCommonParameters.Default;
    private long _nextSequence;

    internal IReadOnlyList<CommandError> Errors => _errors;
    internal IReadOnlyList<AotRuntimeEvent> Events => _events;

    // The engine accepts a host-owned token instead of owning a mutable
    // cancellation source. This keeps embedding deterministic and gives ports
    // one AOT-safe cooperative stop signal.
    internal CancellationToken StopToken { get; } = cancellationToken;
    internal bool IsStopping => StopToken.IsCancellationRequested;

    internal void ThrowIfCancellationRequested() => StopToken.ThrowIfCancellationRequested();

    // Hosts and programmatic callers observe the same ordered transcript.
    // A subscription is scoped so a nested execution cannot leak a host sink.
    internal IDisposable Subscribe(Action<AotRuntimeEvent> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        _observers.Add(observer);
        return new ObserverScope(this, observer);
    }

    internal void WriteOutput(AotExecutionOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        // A completed empty batch has no user-visible data. Suppressing it at
        // the one transcript ingress prevents blank table headers and makes
        // the factory invariant true for every producer.
        if (output.Batch.Records.Count != 0)
        {
            Publish(AotRuntimeEvent.Success(NextSequence(), output));
        }
    }

    internal void WriteNonTerminatingError(string id, string message) =>
        WriteNonTerminatingError(AotDiagnostics.Runtime(id, message, _activeInvocation?.SourceSpan, "command reported an error"));

    internal void WriteNonTerminatingError(AotDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        AotDiagnostic sourceAwareDiagnostic = diagnostic.Span is null && _activeInvocation?.SourceSpan is not null
            ? diagnostic with { Span = _activeInvocation.SourceSpan, Label = diagnostic.Label ?? "command reported an error" }
            : diagnostic;
        _errors.Add(new CommandError(sourceAwareDiagnostic));
        switch (_activeCommonParameters.ErrorAction)
        {
            case AotErrorAction.Continue:
                Publish(AotRuntimeEvent.Error(NextSequence(), _activeInvocation, sourceAwareDiagnostic));
                return;
            case AotErrorAction.SilentlyContinue:
                return;
            case AotErrorAction.Stop:
                Publish(AotRuntimeEvent.TerminatingError(NextSequence(), _activeInvocation, sourceAwareDiagnostic));
                throw new AotPublishedTerminatingException(sourceAwareDiagnostic);
            default:
                throw new InvalidOperationException("Unknown static ErrorAction policy.");
        }
    }

    // Ports opt into these typed side streams explicitly.  Existing ports do
    // not manufacture verbose/debug messages merely because a common switch
    // was supplied; that would be a fake compatibility behavior.
    internal void WriteVerbose(string message) => WriteSideStream(AotRuntimeEventKind.Verbose, message, _activeCommonParameters.Verbose);
    internal void WriteDebug(string message) => WriteSideStream(AotRuntimeEventKind.Debug, message, _activeCommonParameters.Debug);

    internal IDisposable EnterInvocation(CommandInvocation invocation, int pipelinePosition = 0, int pipelineLength = 1) =>
        new InvocationScope(this, new AotInvocationFrame(
            invocation.Descriptor.Name,
            invocation.SourceSpan,
            pipelinePosition,
            pipelineLength,
            _activeInvocation), invocation.CommonParameters);

    private void WriteSideStream(AotRuntimeEventKind kind, string message, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (enabled)
        {
            ThrowIfCancellationRequested();
            Publish(AotRuntimeEvent.Stream(NextSequence(), kind, _activeInvocation, message));
        }
    }

    // Local functions deliberately reject recursive re-entry. It prevents a
    // static-AOT host stack overflow while recursive control flow remains
    // outside the reviewed subset; bare local-function return is supported.
    internal IDisposable EnterFunction(string name, AotSourceSpan callSpan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_activeFunctions.Add(name))
        {
            throw new ScriptException(AotDiagnostics.Scope(
                "AOT5007",
                $"Recursive invocation of local function '{name}' is not supported by the Native AOT subset.",
                callSpan,
                "unsupported recursive function call",
                "Use a non-recursive function body until return and recursive control flow receive a reviewed plan."));
        }

        return new FunctionScope(this, name);
    }

    private long NextSequence() => checked(++_nextSequence);

    private void Publish(AotRuntimeEvent runtimeEvent)
    {
        _events.Add(runtimeEvent);
        // Copy protects the current delivery from an observer unsubscribing.
        foreach (Action<AotRuntimeEvent> observer in _observers.ToArray())
        {
            observer(runtimeEvent);
        }
    }

    private sealed class InvocationScope : IDisposable
    {
        private readonly AotExecutionContext _context;
        private readonly AotInvocationFrame? _priorInvocation;
        private readonly AotCommonParameters _priorCommonParameters;

        internal InvocationScope(AotExecutionContext context, AotInvocationFrame invocation, AotCommonParameters commonParameters)
        {
            _context = context;
            _priorInvocation = context._activeInvocation;
            _priorCommonParameters = context._activeCommonParameters;
            context._activeInvocation = invocation;
            context._activeCommonParameters = commonParameters;
        }

        public void Dispose()
        {
            _context._activeInvocation = _priorInvocation;
            _context._activeCommonParameters = _priorCommonParameters;
        }
    }

    private sealed class FunctionScope(AotExecutionContext context, string name) : IDisposable
    {
        public void Dispose() => context._activeFunctions.Remove(name);
    }

    private sealed class ObserverScope(AotExecutionContext context, Action<AotRuntimeEvent> observer) : IDisposable
    {
        public void Dispose() => context._observers.Remove(observer);
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

// Static projection of the source TimeSpan output. The BCL value remains
// private to this record: structural pipelines can observe only this explicit
// finite field set, never arbitrary CLR TimeSpan members.
internal sealed record TimeSpanRecord(TimeSpan Value) : IPipelineRecord
{
    public int Days => Value.Days;
    public int Hours => Value.Hours;
    public int Minutes => Value.Minutes;
    public int Seconds => Value.Seconds;
    public int Milliseconds => Value.Milliseconds;
    public long Ticks => Value.Ticks;
    public double TotalDays => Value.TotalDays;
    public double TotalHours => Value.TotalHours;
    public double TotalMinutes => Value.TotalMinutes;
    public double TotalSeconds => Value.TotalSeconds;
    public double TotalMilliseconds => Value.TotalMilliseconds;

    public double NumberFor(string property) => property switch
    {
        "Days" => Days,
        "Hours" => Hours,
        "Minutes" => Minutes,
        "Seconds" => Seconds,
        "Milliseconds" => Milliseconds,
        "Ticks" => Ticks,
        "TotalDays" => TotalDays,
        "TotalHours" => TotalHours,
        "TotalMinutes" => TotalMinutes,
        "TotalSeconds" => TotalSeconds,
        "TotalMilliseconds" => TotalMilliseconds,
        _ => throw new ScriptException($"Where-Object does not support property '{property}' for TimeSpan values.")
    };

    public string TextFor(string column) => column switch
    {
        "Value" => Value.ToString("c", CultureInfo.InvariantCulture),
        "Days" => Days.ToString(CultureInfo.InvariantCulture),
        "Hours" => Hours.ToString(CultureInfo.InvariantCulture),
        "Minutes" => Minutes.ToString(CultureInfo.InvariantCulture),
        "Seconds" => Seconds.ToString(CultureInfo.InvariantCulture),
        "Milliseconds" => Milliseconds.ToString(CultureInfo.InvariantCulture),
        "Ticks" => Ticks.ToString(CultureInfo.InvariantCulture),
        "TotalDays" => TotalDays.ToString("R", CultureInfo.InvariantCulture),
        "TotalHours" => TotalHours.ToString("R", CultureInfo.InvariantCulture),
        "TotalMinutes" => TotalMinutes.ToString("R", CultureInfo.InvariantCulture),
        "TotalSeconds" => TotalSeconds.ToString("R", CultureInfo.InvariantCulture),
        "TotalMilliseconds" => TotalMilliseconds.ToString("R", CultureInfo.InvariantCulture),
        _ => throw new ScriptException($"Select-Object does not support column '{column}' for TimeSpan values.")
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

// A scalar Boolean remains a typed pipeline value. It is separate from
// TextRecord so Test-Path does not stringify a result merely to reach the
// terminal; the one-field closed record is also reusable by later boolean
// cmdlets without introducing an object/ETS boundary.
internal sealed record BooleanRecord(bool Value) : IPipelineRecord
{
    public double NumberFor(string property) => throw new ScriptException($"Where-Object does not support property '{property}' for Boolean values.");
    public string TextFor(string column) => column == "Value"
        ? Value.ToString()
        : throw new ScriptException($"Select-Object does not support column '{column}' for Boolean values.");
}

// Closed output shape of CompareObjectCommand's default (non-PassThru) route.
// It is intentionally a static two-field record, never a PSObject note-property
// bag or a dynamically formatted comparison result.
internal sealed record CompareObjectRecord(string InputObject, string SideIndicator) : IPipelineRecord
{
    public double NumberFor(string property) => throw new ScriptException($"Where-Object does not support property '{property}' for Compare-Object values.");

    public string TextFor(string column) => column switch
    {
        "InputObject" => InputObject,
        "SideIndicator" => SideIndicator,
        _ => throw new ScriptException($"Select-Object does not support column '{column}' for Compare-Object values."),
    };
}

// Closed TextMeasureInfo projection for Measure-Object's text parameter set.
// Null source fields remain explicit nullability, rather than being invented
// as zeroes or surfaced through a generic CLR object boundary.
internal sealed record MeasureTextRecord(int? Lines, int? Words, int? Characters) : IPipelineRecord
{
    public double NumberFor(string property) => throw new ScriptException($"Where-Object does not support property '{property}' for Measure-Object text values.");

    public string TextFor(string column) => column switch
    {
        "Lines" => Lines?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        "Words" => Words?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        "Characters" => Characters?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        "Property" => string.Empty,
        _ => throw new ScriptException($"Select-Object does not support column '{column}' for Measure-Object text values."),
    };
}

// Closed GroupInfoNoElement projection for Group-Object's static TextRecord
// route. It intentionally contains no source-object list or ETS state.
internal sealed record GroupTextRecord(string Name, int Count) : IPipelineRecord
{
    public string TextFor(string property) => property switch
    {
        "Name" => Name,
        "Count" => Count.ToString(CultureInfo.InvariantCulture),
        _ => throw new ScriptException($"Select-Object does not support column '{property}' for Group-Object text values."),
    };

    public double NumberFor(string property) => property switch
    {
        "Count" => Count,
        _ => throw new ScriptException($"Where-Object does not support property '{property}' for Group-Object text values."),
    };
}

// Port boundary for Microsoft.PowerShell.Commands.NewGuidCommand. The
// upstream process body is one BCL decision after generated binding: emit a
// UUID v7 normally, or Guid.Empty when -Empty is true. The existing closed
// TextRecord prose shape renders the canonical D-format value. InputObject is
// deliberately not admitted until a typed Guid boundary is separately
// reviewed.
internal sealed class NewGuidCmdlet : AotCmdletBase
{
    private static readonly CmdletDescriptor NewGuidDescriptor =
        GeneratedCmdletPorts.NewGuid.CreateAotDescriptor("Empty");

    public override CmdletDescriptor Descriptor => NewGuidDescriptor;
    public override IReadOnlyList<string> DefaultColumns { get; } = ["Value"];
    public override AotTerminalPresentation TerminalPresentation => AotTerminalPresentation.Prose;

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        bool empty = invocation.TryGetValues("Empty", out string[] values)
            && values.Single() is "true";
        Guid value = empty ? Guid.Empty : Guid.CreateVersion7();
        return [new TextRecord(value.ToString("D"))];
    }
}

// Narrow source-derived slice of Microsoft.PowerShell.Commands.GetRandomCommand.
// The descriptor admits only the seeded Int32 numeric parameter set. The
// upstream unseeded crypto, object/pipeline, Count, Shuffle, floating-point,
// and Int64 paths remain unavailable until independently reviewed.
internal sealed class GetRandomCmdlet : AotCmdletBase
{
    private static readonly CmdletDescriptor GetRandomDescriptor = CreateDescriptor();

    public override CmdletDescriptor Descriptor => GetRandomDescriptor;
    public override IReadOnlyList<string> DefaultColumns { get; } = ["Value"];
    public override AotTerminalPresentation TerminalPresentation => AotTerminalPresentation.Prose;

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        int seed = RequiredInt32(invocation, "SetSeed");
        int minimum = RequiredInt32(invocation, "Minimum");
        int maximum = RequiredInt32(invocation, "Maximum");
        if (minimum >= maximum)
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT6702",
                "Get-Random -Minimum must be less than -Maximum in the seeded Int32 subset.",
                invocation.GetValueSpan("Minimum", 0),
                "invalid random bounds",
                "Use named Int32 values where Minimum is less than Maximum."));
        }

        int value = UpstreamSeededRandomInt32.Next(new Random(seed), minimum, maximum);
        return [new TextRecord(value.ToString(CultureInfo.InvariantCulture))];
    }

    private static CmdletDescriptor CreateDescriptor()
    {
        CmdletDescriptor source = GeneratedCmdletPorts.GetRandom.CreateAotDescriptor("SetSeed", "Minimum", "Maximum");
        ParameterSpec[] namedOnly = source.Parameters
            .Select(parameter => parameter with
            {
                ParameterSets = parameter.ParameterSets
                    .Select(parameterSet => parameterSet with { Position = null })
                    .ToArray(),
            })
            .ToArray();
        return new CmdletDescriptor(source.Name, namedOnly);
    }

    private static int RequiredInt32(CommandInvocation invocation, string name)
    {
        if (!invocation.TryGetValues(name, out string[] values) || values.Length != 1
            || !int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT6701",
                $"Get-Random requires named -{name} with exactly one invariant Int32 value in the seeded subset.",
                invocation.GetValueSpan(name, 0),
                "invalid seeded random parameter",
                "Use -SetSeed, -Minimum, and -Maximum with whole-number values."));
        }

        return value;
    }
}

// Narrow source-derived slice of GetSecureRandomCommand. The upstream base
// shares its bounded Int32 algorithm with Get-Random but owns a runspace map;
// this stateless AOT adapter deliberately admits one direct cryptographic draw
// and does not introduce runspace/session state.
internal sealed class GetSecureRandomCmdlet : AotCmdletBase
{
    private static readonly CmdletDescriptor GetSecureRandomDescriptor = CreateDescriptor();

    public override CmdletDescriptor Descriptor => GetSecureRandomDescriptor;
    public override IReadOnlyList<string> DefaultColumns { get; } = ["Value"];
    public override AotTerminalPresentation TerminalPresentation => AotTerminalPresentation.Prose;

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        int minimum = RequiredInt32(invocation, "Minimum");
        int maximum = RequiredInt32(invocation, "Maximum");
        if (minimum >= maximum)
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT6712",
                "Get-SecureRandom -Minimum must be less than -Maximum in the Int32 subset.",
                invocation.GetValueSpan("Minimum", 0),
                "invalid secure random bounds",
                "Use named Int32 values where Minimum is less than Maximum."));
        }

        using RandomNumberGenerator generator = RandomNumberGenerator.Create();
        int value = UpstreamSeededRandomInt32.Next(generator, minimum, maximum);
        return [new TextRecord(value.ToString(CultureInfo.InvariantCulture))];
    }

    private static CmdletDescriptor CreateDescriptor()
    {
        CmdletDescriptor source = GeneratedCmdletPorts.GetSecureRandom.CreateAotDescriptor("Minimum", "Maximum");
        ParameterSpec[] namedOnly = source.Parameters
            .Select(parameter => parameter with
            {
                ParameterSets = parameter.ParameterSets
                    .Select(parameterSet => parameterSet with { Position = null })
                    .ToArray(),
            })
            .ToArray();
        return new CmdletDescriptor(source.Name, namedOnly);
    }

    private static int RequiredInt32(CommandInvocation invocation, string name)
    {
        if (!invocation.TryGetValues(name, out string[] values) || values.Length != 1
            || !int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT6711",
                $"Get-SecureRandom requires named -{name} with exactly one invariant Int32 value in the subset.",
                invocation.GetValueSpan(name, 0),
                "invalid secure random parameter",
                "Use -Minimum and -Maximum with whole-number values."));
        }

        return value;
    }
}

// Closed direct-string slice of JoinStringCommand. Source conversion/property
// semantics remain outside this adapter; explicit strings and separator map
// directly to the source's StringBuilder separator behavior.
internal sealed class JoinStringCmdlet : AotCmdletBase
{
    private static readonly CmdletDescriptor JoinStringDescriptor = CreateDescriptor();

    public override CmdletDescriptor Descriptor => JoinStringDescriptor;
    public override IReadOnlyList<string> DefaultColumns { get; } = ["Value"];
    public override AotTerminalPresentation TerminalPresentation => AotTerminalPresentation.Prose;

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        if (!invocation.TryGetValues("InputObject", out string[] values) || values.Length == 0)
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT6721",
                "Join-String requires named -InputObject with one or more direct string values in this subset.",
                invocation.SourceSpan,
                "missing direct string input",
                "Use -InputObject followed by one or more string literals."));
        }

        if (!invocation.TryGetValues("Separator", out string[] separators) || separators.Length != 1)
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT6722",
                "Join-String requires named -Separator with exactly one direct string value in this subset.",
                invocation.SourceSpan,
                "missing direct separator",
                "Use -Separator followed by one string literal."));
        }

        return [new TextRecord(string.Join(separators[0], values))];
    }

    private static CmdletDescriptor CreateDescriptor()
    {
        CmdletDescriptor source = GeneratedCmdletPorts.JoinString.CreateAotDescriptor("InputObject", "Separator");
        ParameterSpec[] namedOnly = source.Parameters
            .Select(parameter => parameter with
            {
                ParameterSets = parameter.ParameterSets
                    .Select(parameterSet => parameterSet with { Position = null })
                    .ToArray(),
            })
            .ToArray();
        return new CmdletDescriptor(source.Name, namedOnly);
    }
}

// Direct-string, SyncWindow=0 extraction of CompareObjectCommand.Process.
// The source's OrderByProperty/PSObject comparer is deliberately not crossed:
// this route has no property expression, conversion, culture override, or
// dynamic output surface.
internal sealed class CompareObjectCmdlet : AotCmdletBase
{
    private static readonly CmdletDescriptor CompareObjectDescriptor = CreateDescriptor();

    public override CmdletDescriptor Descriptor => CompareObjectDescriptor;
    public override IReadOnlyList<string> DefaultColumns { get; } = ["InputObject", "SideIndicator"];

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        if (!invocation.TryGetValues("ReferenceObject", out string[] reference) || reference.Length == 0)
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT6723",
                "Compare-Object requires named -ReferenceObject with one or more direct string values in this subset.",
                invocation.SourceSpan,
                "missing direct reference input",
                "Use -ReferenceObject followed by one or more string literals."));
        }

        if (!invocation.TryGetValues("DifferenceObject", out string[] difference) || difference.Length == 0)
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT6724",
                "Compare-Object requires named -DifferenceObject with one or more direct string values in this subset.",
                invocation.SourceSpan,
                "missing direct difference input",
                "Use -DifferenceObject followed by one or more string literals."));
        }

        if (!invocation.TryGetValues("SyncWindow", out string[] syncWindow)
            || syncWindow.Length != 1
            || !string.Equals(syncWindow[0], "0", StringComparison.Ordinal))
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT6725",
                "Compare-Object requires named -SyncWindow 0 in this direct-string subset.",
                invocation.SourceSpan,
                "unsupported comparison window",
                "Use -SyncWindow 0 with direct string inputs."));
        }

        List<IPipelineRecord> output = [];
        int common = Math.Min(reference.Length, difference.Length);
        StringComparer comparer = invocation.TryGetValues("CaseSensitive", out _)
            ? StringComparer.CurrentCulture
            : StringComparer.CurrentCultureIgnoreCase;
        for (int index = 0; index < common; index++)
        {
            if (comparer.Equals(reference[index], difference[index]))
            {
                continue;
            }

            output.Add(new CompareObjectRecord(difference[index], "=>"));
            output.Add(new CompareObjectRecord(reference[index], "<="));
        }

        for (int index = common; index < difference.Length; index++)
        {
            output.Add(new CompareObjectRecord(difference[index], "=>"));
        }

        for (int index = common; index < reference.Length; index++)
        {
            output.Add(new CompareObjectRecord(reference[index], "<="));
        }

        return output;
    }

    private static CmdletDescriptor CreateDescriptor()
    {
        CmdletDescriptor source = GeneratedCmdletPorts.CompareObject.CreateAotDescriptor(
            "ReferenceObject", "DifferenceObject", "SyncWindow", "CaseSensitive");
        ParameterSpec[] namedOnly = source.Parameters
            .Select(parameter => parameter with
            {
                ParameterSets = parameter.ParameterSets
                    .Select(parameterSet => parameterSet with { Position = null })
                    .ToArray(),
            })
            .ToArray();
        return new CmdletDescriptor(source.Name, namedOnly);
    }
}

// Closed Raw/SimpleMatch file route of SelectStringCommand. It deliberately
// reuses the existing physical-file capability and emits strings only, rather
// than importing MatchInfo, regex compilation, provider resolution, or ETS.
internal sealed class SelectStringCmdlet(IPhysicalFileResolver files) : AotCmdletBase
{
    private static readonly CmdletDescriptor SelectStringDescriptor = CreateDescriptor();

    public override CmdletDescriptor Descriptor => SelectStringDescriptor;
    public override IReadOnlyList<string> DefaultColumns { get; } = ["Value"];
    public override AotTerminalPresentation TerminalPresentation => AotTerminalPresentation.Prose;

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        if (!invocation.TryGetValues("Path", out string[] paths) || paths.Length != 1 || ContainsWildcard(paths[0]))
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT6726",
                "Select-String requires named -Path with exactly one direct non-wildcard file path in this subset.",
                invocation.SourceSpan,
                "unsupported path route",
                "Use one literal file path with -Path."));
        }

        if (!invocation.TryGetValues("Pattern", out string[] patterns) || patterns.Length != 1)
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT6727",
                "Select-String requires named -Pattern with exactly one direct string value in this subset.",
                invocation.SourceSpan,
                "unsupported pattern route",
                "Use one literal pattern with -Pattern."));
        }

        if (!invocation.TryGetValues("SimpleMatch", out _) || !invocation.TryGetValues("Raw", out _))
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT6728",
                "Select-String requires both -SimpleMatch and -Raw in this subset.",
                invocation.SourceSpan,
                "missing static output or match mode",
                "Use -SimpleMatch -Raw with direct Path and Pattern values."));
        }

        string[] resolved = files.ResolvePath(paths[0], context).ToArray();
        if (resolved.Length != 1)
        {
            return [];
        }

        StringComparison comparison = invocation.TryGetValues("CaseSensitive", out _)
            ? StringComparison.CurrentCulture
            : StringComparison.CurrentCultureIgnoreCase;
        List<IPipelineRecord> output = [];
        try
        {
            using Stream stream = files.OpenRead(resolved[0]);
            using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                context.ThrowIfCancellationRequested();
                if (line.IndexOf(patterns[0], comparison) >= 0)
                {
                    output.Add(new TextRecord(line));
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            context.WriteNonTerminatingError("ProcessingFile", error.Message);
        }

        return output;
    }

    private static CmdletDescriptor CreateDescriptor()
    {
        CmdletDescriptor source = GeneratedCmdletPorts.SelectString.CreateAotDescriptor(
            "Path", "Pattern", "SimpleMatch", "Raw", "CaseSensitive");
        ParameterSpec[] namedOnly = source.Parameters
            .Select(parameter => parameter with
            {
                ParameterSets = parameter.ParameterSets
                    .Select(parameterSet => parameterSet with { Position = null })
                    .ToArray(),
            })
            .ToArray();
        return new CmdletDescriptor(source.Name, namedOnly);
    }

    private static bool ContainsWildcard(string value) => value.IndexOfAny(['*', '?', '[']) >= 0;
}

// Static direct-string extraction of MeasureObjectCommand's TextMeasure set.
// The source helpers CountChar/CountWord/CountLine are structurally retained;
// PSObject/property expressions and generic numeric statistics remain out.
internal sealed class MeasureObjectCmdlet : AotCmdletBase
{
    private static readonly CmdletDescriptor MeasureObjectDescriptor = CreateDescriptor();

    public override CmdletDescriptor Descriptor => MeasureObjectDescriptor;
    public override IReadOnlyList<string> DefaultColumns { get; } = ["Lines", "Words", "Characters", "Property"];

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        if (!invocation.TryGetValues("InputObject", out string[] values) || values.Length != 1)
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT6729",
                "Measure-Object requires named -InputObject with exactly one direct string value in this subset.",
                invocation.SourceSpan,
                "unsupported direct input",
                "Use one string literal with -InputObject."));
        }

        bool characters = invocation.TryGetValues("Character", out _);
        bool words = invocation.TryGetValues("Word", out _);
        bool lines = invocation.TryGetValues("Line", out _);
        if (!characters && !words && !lines)
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT6730",
                "Measure-Object requires at least one text statistic switch in this subset.",
                invocation.SourceSpan,
                "unsupported generic measurement route",
                "Use one or more of -Character, -Word, and -Line."));
        }

        string value = values[0];
        int? characterCount = characters ? CountCharacters(value, invocation.TryGetValues("IgnoreWhiteSpace", out _)) : null;
        int? wordCount = words ? CountWords(value) : null;
        int? lineCount = lines ? CountLines(value) : null;
        return [new MeasureTextRecord(lineCount, wordCount, characterCount)];
    }

    private static CmdletDescriptor CreateDescriptor()
    {
        CmdletDescriptor source = GeneratedCmdletPorts.MeasureObject.CreateAotDescriptor(
            "InputObject", "Character", "Word", "Line", "IgnoreWhiteSpace");
        ParameterSpec[] namedOnly = source.Parameters
            .Select(parameter => parameter with
            {
                ParameterSets = parameter.ParameterSets
                    .Select(parameterSet => parameterSet with { Position = null })
                    .ToArray(),
            })
            .ToArray();
        return new CmdletDescriptor(source.Name, namedOnly);
    }

    private static int CountCharacters(string value, bool ignoreWhiteSpace) =>
        ignoreWhiteSpace ? value.Count(static character => !char.IsWhiteSpace(character)) : value.Length;

    private static int CountWords(string value)
    {
        int count = 0;
        bool previousWasWhiteSpace = true;
        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                previousWasWhiteSpace = true;
            }
            else
            {
                if (previousWasWhiteSpace)
                {
                    count++;
                }

                previousWasWhiteSpace = false;
            }
        }

        return count;
    }

    private static int CountLines(string value)
    {
        if (value.Length == 0)
        {
            return 0;
        }

        int count = value.Count(static character => character == '\n');
        return value[^1] == '\n' ? count : count + 1;
    }
}

// Closed TextRecord extraction of GetUniqueCommand's explicit -AsString path.
// The source compares each input with its immediate predecessor.  Its PSObject
// and InternalTypeNames routes remain deliberately outside this adapter.
internal sealed class GetUniqueCmdlet : AotPipelineInputCmdletBase<TextRecord>
{
    private static readonly CmdletDescriptor GetUniqueDescriptor = CreateDescriptor();

    public override CmdletDescriptor Descriptor => GetUniqueDescriptor;
    public override IReadOnlyList<string> DefaultColumns { get; } = ["Value"];
    public override AotTerminalPresentation TerminalPresentation => AotTerminalPresentation.Prose;

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        RequireAsString(invocation);
        // GetUniqueCommand emits nothing when its pipeline input is absent.
        return [];
    }

    protected override IEnumerable<IPipelineRecord> ProcessPipelineInput(
        CommandInvocation invocation,
        IReadOnlyList<TextRecord> input,
        AotExecutionContext context)
    {
        RequireAsString(invocation);
        StringComparison comparison = invocation.TryGetValues("CaseInsensitive", out _)
            ? StringComparison.CurrentCultureIgnoreCase
            : StringComparison.CurrentCulture;

        List<IPipelineRecord> output = [];
        string? previous = null;
        bool hasPrevious = false;
        foreach (TextRecord record in input)
        {
            context.ThrowIfCancellationRequested();
            if (!hasPrevious || !string.Equals(record.Value, previous, comparison))
            {
                output.Add(record);
                previous = record.Value;
                hasPrevious = true;
            }
        }

        return output;
    }

    private static void RequireAsString(CommandInvocation invocation)
    {
        if (!invocation.TryGetValues("AsString", out _))
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT6731",
                "Get-Unique requires explicit -AsString in the static TextRecord subset.",
                invocation.SourceSpan,
                "unsupported object-comparison route",
                "Use -AsString with a preceding AOT command that emits TextRecord values."));
        }
    }

    private static CmdletDescriptor CreateDescriptor()
    {
        CmdletDescriptor source = GeneratedCmdletPorts.GetUnique.CreateAotDescriptor("AsString", "CaseInsensitive");
        ParameterSpec[] namedOnly = source.Parameters
            .Select(parameter => parameter with
            {
                ParameterSets = parameter.ParameterSets
                    .Select(parameterSet => parameterSet with { Position = null })
                    .ToArray(),
            })
            .ToArray();
        return new CmdletDescriptor(source.Name, namedOnly);
    }
}

// Closed TextRecord/NoElement extraction of GroupObjectCommand. The source
// buffers then sorts group keys; this adapter preserves that behavior only for
// static text values and emits no GroupInfo element collection.
internal sealed class GroupObjectCmdlet : AotPipelineInputCmdletBase<TextRecord>
{
    private static readonly CmdletDescriptor GroupObjectDescriptor = CreateDescriptor();

    public override CmdletDescriptor Descriptor => GroupObjectDescriptor;
    public override IReadOnlyList<string> DefaultColumns { get; } = ["Count", "Name"];

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        RequireNoElement(invocation);
        return [];
    }

    protected override IEnumerable<IPipelineRecord> ProcessPipelineInput(
        CommandInvocation invocation,
        IReadOnlyList<TextRecord> input,
        AotExecutionContext context)
    {
        RequireNoElement(invocation);
        StringComparer comparer = invocation.TryGetValues("CaseSensitive", out _)
            ? StringComparer.CurrentCulture
            : StringComparer.CurrentCultureIgnoreCase;
        Dictionary<string, (string Name, int Count)> groups = new(comparer);
        foreach (TextRecord record in input)
        {
            context.ThrowIfCancellationRequested();
            if (groups.TryGetValue(record.Value, out (string Name, int Count) group))
            {
                groups[record.Value] = (group.Name, group.Count + 1);
            }
            else
            {
                groups.Add(record.Value, (record.Value, 1));
            }
        }

        return groups.Values
            .OrderBy(static group => group.Name, comparer)
            .Select(static group => (IPipelineRecord)new GroupTextRecord(group.Name, group.Count))
            .ToArray();
    }

    private static void RequireNoElement(CommandInvocation invocation)
    {
        if (!invocation.TryGetValues("NoElement", out _))
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT6732",
                "Group-Object requires explicit -NoElement in the static TextRecord subset.",
                invocation.SourceSpan,
                "unsupported dynamic group-element route",
                "Use -NoElement with a preceding AOT command that emits TextRecord values."));
        }
    }

    private static CmdletDescriptor CreateDescriptor()
    {
        CmdletDescriptor source = GeneratedCmdletPorts.GroupObject.CreateAotDescriptor("NoElement", "CaseSensitive");
        ParameterSpec[] namedOnly = source.Parameters
            .Select(parameter => parameter with
            {
                ParameterSets = parameter.ParameterSets
                    .Select(parameterSet => parameterSet with { Position = null })
                    .ToArray(),
            })
            .ToArray();
        return new CmdletDescriptor(source.Name, namedOnly);
    }
}

// Closed TextRecord extraction of SortObjectCommand's default value ordering.
// Property expressions, PSObject comparison, and source's broader sort modes
// remain outside this static text-only adapter.
internal sealed class SortObjectCmdlet : AotPipelineInputCmdletBase<TextRecord>
{
    private static readonly CmdletDescriptor SortObjectDescriptor = CreateDescriptor();

    public override CmdletDescriptor Descriptor => SortObjectDescriptor;
    public override IReadOnlyList<string> DefaultColumns { get; } = ["Value"];
    public override AotTerminalPresentation TerminalPresentation => AotTerminalPresentation.Prose;

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context) => [];

    protected override IEnumerable<IPipelineRecord> ProcessPipelineInput(
        CommandInvocation invocation,
        IReadOnlyList<TextRecord> input,
        AotExecutionContext context)
    {
        StringComparer comparer = invocation.TryGetValues("CaseSensitive", out _)
            ? StringComparer.CurrentCulture
            : StringComparer.CurrentCultureIgnoreCase;
        bool descending = invocation.TryGetValues("Descending", out _);

        // Enumerable.OrderBy is stable, matching the source's indexed comparer
        // behavior for equal text values.
        IEnumerable<TextRecord> sorted = descending
            ? input.OrderByDescending(static record => record.Value, comparer)
            : input.OrderBy(static record => record.Value, comparer);
        return sorted.ToArray();
    }

    private static CmdletDescriptor CreateDescriptor()
    {
        CmdletDescriptor source = GeneratedCmdletPorts.SortObject.CreateAotDescriptor("Descending", "CaseSensitive");
        ParameterSpec[] namedOnly = source.Parameters
            .Select(parameter => parameter with
            {
                ParameterSets = parameter.ParameterSets
                    .Select(parameterSet => parameterSet with { Position = null })
                    .ToArray(),
            })
            .ToArray();
        return new CmdletDescriptor(source.Name, namedOnly);
    }
}

// Exact seeded-only extraction of PolymorphicRandomNumberGenerator's helper
// path from upstream GetRandomCommandBase. It deliberately contains no
// cryptographic generator, runspace map, reflection, or PSObject behavior.
internal static class UpstreamSeededRandomInt32
{
    internal static int Next(Random random, int minimum, int maximum)
    {
        long range = (long)maximum - minimum;
        return (int)(NextDouble(random) * range) + minimum;
    }

    internal static int Next(RandomNumberGenerator random, int minimum, int maximum)
    {
        long range = (long)maximum - minimum;
        return (int)(NextDouble(random) * range) + minimum;
    }

    private static double NextDouble(Random random) => NextNonNegative(random) * (1.0 / int.MaxValue);

    private static double NextDouble(RandomNumberGenerator random) => NextNonNegative(random) * (1.0 / int.MaxValue);

    private static int NextNonNegative(Random random)
    {
        int value;
        do
        {
            byte[] bytes = new byte[sizeof(int)];
            random.NextBytes(bytes);
            value = BitConverter.ToInt32(bytes, 0);
        }
        while (value == int.MaxValue);

        return value < 0 ? value + int.MaxValue : value;
    }

    private static int NextNonNegative(RandomNumberGenerator random)
    {
        int value;
        do
        {
            byte[] bytes = new byte[sizeof(int)];
            random.GetBytes(bytes);
            value = BitConverter.ToInt32(bytes, 0);
        }
        while (value == int.MaxValue);

        return value < 0 ? value + int.MaxValue : value;
    }
}

// Port boundary for Microsoft.PowerShell.Commands.NewTimeSpanCommand. The
// admitted Time parameter set maps directly to the static BCL constructor.
// Date/LastWriteTime/Start/End and pipeline modes stay outside the descriptor
// until a reviewed typed DateTime input contract exists.
internal sealed class NewTimeSpanCmdlet : AotCmdletBase
{
    private static readonly CmdletDescriptor NewTimeSpanDescriptor =
        GeneratedCmdletPorts.NewTimeSpan.CreateAotDescriptor("Days", "Hours", "Minutes", "Seconds", "Milliseconds");

    public override CmdletDescriptor Descriptor => NewTimeSpanDescriptor;
    public override IReadOnlyList<string> DefaultColumns { get; } = ["Value"];
    public override AotTerminalPresentation TerminalPresentation => AotTerminalPresentation.Prose;

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        int days = Component(invocation, "Days");
        int hours = Component(invocation, "Hours");
        int minutes = Component(invocation, "Minutes");
        int seconds = Component(invocation, "Seconds");
        int milliseconds = Component(invocation, "Milliseconds");

        try
        {
            return [new TimeSpanRecord(new TimeSpan(days, hours, minutes, seconds, milliseconds))];
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT3006",
                "New-TimeSpan components produce a TimeSpan outside the supported range.",
                FirstComponentSpan(invocation),
                "TimeSpan overflow",
                "Use component values whose combined duration fits in TimeSpan."));
        }
    }

    private static int Component(CommandInvocation invocation, string name)
    {
        if (!invocation.TryGetValues(name, out string[] values))
        {
            return 0;
        }

        if (values.Length != 1 || !int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT3005",
                $"New-TimeSpan -{name} expects one invariant integer value, got '{string.Join(", ", values)}'.",
                invocation.GetValueSpan(name, 0),
                "invalid TimeSpan component",
                "Use an integer literal in the range supported by TimeSpan."));
        }

        return value;
    }

    private static AotSourceSpan? FirstComponentSpan(CommandInvocation invocation)
    {
        foreach (string name in new[] { "Days", "Hours", "Minutes", "Seconds", "Milliseconds" })
        {
            if (invocation.TryGetValues(name, out _))
            {
                return invocation.GetValueSpan(name, 0);
            }
        }

        return invocation.SourceSpan;
    }
}

// Port boundary for Microsoft.PowerShell.Commands.StartSleepCommand. The W2
// slice intentionally admits only the generated Milliseconds parameter (and
// its generated ms alias). Waiting remains a host capability: this adapter
// neither owns a cancellation source nor creates a timer/scheduler.
internal sealed class StartSleepCmdlet(IAotDelay delay) : AotCmdletBase
{
    private static readonly CmdletDescriptor StartSleepDescriptor =
        GeneratedCmdletPorts.StartSleep.CreateAotDescriptor("Milliseconds");

    public override CmdletDescriptor Descriptor => StartSleepDescriptor;
    public override IReadOnlyList<string> DefaultColumns { get; } = [];
    public override AotTerminalPresentation TerminalPresentation => AotTerminalPresentation.Prose;

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        if (!invocation.TryGetValues("Milliseconds", out string[] values))
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT3007",
                "Start-Sleep requires generated parameter '-Milliseconds <non-negative invariant integer>'.",
                invocation.SourceSpan,
                "missing sleep duration",
                "Use -Milliseconds followed by a whole number from 0 through 2147483647."));
        }

        if (values.Length != 1)
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT3008",
                "Start-Sleep -Milliseconds accepts exactly one invariant integer value.",
                invocation.GetValueSpan("Milliseconds", 0),
                "invalid sleep duration",
                "Use one whole number from 0 through 2147483647."));
        }

        string text = values[0];
        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT3008",
                $"Start-Sleep -Milliseconds expects an invariant integer, got '{text}'.",
                invocation.GetValueSpan("Milliseconds", 0),
                "invalid sleep duration",
                "Use one whole number from 0 through 2147483647."));
        }

        if (parsed < 0 || parsed > int.MaxValue)
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT3009",
                $"Start-Sleep -Milliseconds must be between 0 and {int.MaxValue.ToString(CultureInfo.InvariantCulture)}, got '{text}'.",
                invocation.GetValueSpan("Milliseconds", 0),
                "sleep duration outside supported range",
                "Use a non-negative whole number no larger than 2147483647."));
        }

        context.ThrowIfCancellationRequested();
        delay.DelayMilliseconds((int)parsed, context.StopToken);
        context.ThrowIfCancellationRequested();
        return [];
    }
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
    AotTerminalPresentation TerminalPresentation { get; }
    AotTableLayout? DefaultTableLayout { get; }
    IEnumerable<IPipelineRecord> Invoke(
        CommandInvocation invocation,
        AotExecutionContext context,
        int pipelinePosition = 0,
        int pipelineLength = 1);
}

// A downstream command may opt in to one concrete input-record contract. The
// non-generic surface permits the already-bound pipeline plan to dispatch it;
// the generic base below performs the only type crossing. Neither surface
// accepts object, Type, PSObject, reflection, or conversion services.
internal interface IAotPipelineInputCmdlet : IAotCmdlet
{
    string PipelineInputSynopsis { get; }

    IEnumerable<IPipelineRecord> InvokeWithPipelineInput(
        CommandInvocation invocation,
        IReadOnlyList<IPipelineRecord> input,
        AotExecutionContext context,
        int pipelinePosition,
        int pipelineLength);
}

internal sealed record AotPipelineInputStage(IAotPipelineInputCmdlet Cmdlet, CommandInvocation Invocation);

// Shared execution base for ports. It mirrors the existing PowerShell cmdlet
// lifecycle and keeps buffering/error policy in one place. A command port owns
// only its business logic in ProcessRecord.
internal abstract class AotCmdletBase : IAotCmdlet
{
    public abstract CmdletDescriptor Descriptor { get; }
    public abstract IReadOnlyList<string> DefaultColumns { get; }
    public virtual AotTerminalPresentation TerminalPresentation => AotTerminalPresentation.Table;
    public virtual AotTableLayout? DefaultTableLayout => null;

    public IEnumerable<IPipelineRecord> Invoke(
        CommandInvocation invocation,
        AotExecutionContext context,
        int pipelinePosition = 0,
        int pipelineLength = 1) =>
        InvokeLifecycle(invocation, context, () => ProcessRecord(invocation, context), pipelinePosition, pipelineLength);

    // Input-capable ports use this exact runner too. That keeps lifecycle,
    // origin span repair, cancellation, and output materialization shared.
    protected IEnumerable<IPipelineRecord> InvokeLifecycle(
        CommandInvocation invocation,
        AotExecutionContext context,
        Func<IEnumerable<IPipelineRecord>> process,
        int pipelinePosition,
        int pipelineLength)
    {
        using IDisposable scope = context.EnterInvocation(invocation, pipelinePosition, pipelineLength);
        List<IPipelineRecord> output = [];
        // A pre-cancelled context never starts the command. Once lifecycle
        // work starts, an observed cancellation calls StopProcessing once.
        context.ThrowIfCancellationRequested();
        bool lifecycleStarted = true;
        bool stopCalled = false;

        void StopOnce()
        {
            if (stopCalled)
            {
                return;
            }

            stopCalled = true;
            try
            {
                StopProcessing(context);
            }
            catch (Exception)
            {
                // Upstream PipelineProcessor.Stop ignores failures from a
                // stop hook: cancellation is the result that must propagate.
            }
        }

        try
        {
            AppendOutput(output, BeginProcessing(context), context);
            AppendOutput(output, process(), context);
            AppendOutput(output, EndProcessing(context), context);
            return output;
        }
        catch (ScriptException error) when (error.Diagnostic.Span is null && invocation.SourceSpan is not null)
        {
            // Older ports are still migrating their origin diagnostics. Never
            // discard a known command location while that work proceeds.
            throw error.WithSpan(invocation.SourceSpan);
        }
        catch (OperationCanceledException) when (lifecycleStarted && context.IsStopping)
        {
            StopOnce();
            throw;
        }
    }

    private static void AppendOutput(
        List<IPipelineRecord> output,
        IEnumerable<IPipelineRecord> records,
        AotExecutionContext context)
    {
        context.ThrowIfCancellationRequested();
        foreach (IPipelineRecord record in records)
        {
            context.ThrowIfCancellationRequested();
            output.Add(record);
        }

        context.ThrowIfCancellationRequested();
    }

    protected virtual IEnumerable<IPipelineRecord> BeginProcessing(AotExecutionContext context) => [];
    protected abstract IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context);
    protected virtual IEnumerable<IPipelineRecord> EndProcessing(AotExecutionContext context) => [];
    protected virtual void StopProcessing(AotExecutionContext context) { }
}

// The generic type parameter is statically closed by each port. It replaces a
// command-specific cast in PipelinePlan, while preserving a concrete record
// handoff rather than recreating PowerShell's dynamic object binder.
internal abstract class AotPipelineInputCmdletBase<TInput> : AotCmdletBase, IAotPipelineInputCmdlet
    where TInput : IPipelineRecord
{
    public virtual string PipelineInputSynopsis =>
        $"Accepts static {typeof(TInput).Name} input only from one preceding registered AOT pipeline adapter.";

    IEnumerable<IPipelineRecord> IAotPipelineInputCmdlet.InvokeWithPipelineInput(
        CommandInvocation invocation,
        IReadOnlyList<IPipelineRecord> input,
        AotExecutionContext context,
        int pipelinePosition,
        int pipelineLength)
    {
        List<TInput> typedInput = [];
        foreach (IPipelineRecord record in input)
        {
            if (record is not TInput typedRecord)
            {
                throw new ScriptException(AotDiagnostics.Runtime(
                    "AOT4010",
                    $"{Descriptor.Name} does not accept this AOT pipeline record shape.",
                    invocation.SourceSpan,
                    "incompatible static pipeline input",
                    "Use a preceding AOT command that emits the record shape accepted by this command."));
            }

            typedInput.Add(typedRecord);
        }

        return InvokeLifecycle(
            invocation,
            context,
            () => ProcessPipelineInput(invocation, typedInput, context),
            pipelinePosition,
            pipelineLength);
    }

    protected abstract IEnumerable<IPipelineRecord> ProcessPipelineInput(
        CommandInvocation invocation,
        IReadOnlyList<TInput> input,
        AotExecutionContext context);
}

internal static class AotCmdletRegistry
{
    private static readonly AotHostSubstrate Host = AotHostComposition.Substrate;
    private static readonly IAotCmdlet[] Cmdlets = [new GetProcessCmdlet(Host.Processes), new GetUptimeCmdlet(), new GetUICultureCmdlet(Host.Culture), new GetCultureCmdlet(Host.Culture, Host.Cultures), new GetVerbCmdlet(), new GetTimeZoneCmdlet(Host.TimeZones), new GetDateCmdlet(Host.Clock), new GetFileHashCmdlet(Host.PhysicalFiles), new GetChildItemCmdlet(Host.PhysicalChildItems), new GetItemCmdlet(Host.PhysicalChildItems), new TestPathCmdlet(Host.PhysicalChildItems), new ResolvePathCmdlet(Host.PhysicalChildItems), new ConvertPathCmdlet(Host.PhysicalChildItems), new JoinPathCmdlet(), new SplitPathCmdlet(), new NewGuidCmdlet(), new GetRandomCmdlet(), new GetSecureRandomCmdlet(), new JoinStringCmdlet(), new CompareObjectCmdlet(), new SelectStringCmdlet(Host.PhysicalFiles), new MeasureObjectCmdlet(), new GetUniqueCmdlet(), new GroupObjectCmdlet(), new SortObjectCmdlet(), new NewTimeSpanCmdlet(), new StartSleepCmdlet(Host.Delay), new GetHelpCmdlet(AotHostComposition.Help), new GetCommandCmdlet(AotHostComposition.Help), new GetModuleCmdlet(AotHostComposition.Modules), new FindModuleCmdlet(AotHostComposition.Repositories), new InstallModuleCmdlet(new LocalPackageModuleInstaller(AotHostComposition.Repositories, configuration: Host.Configuration))];

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

    // This is deliberately a registry capability query, not a generated
    // metadata query. Generated metadata describes upstream declarations for
    // every command; only an installed native adapter may be executable.
    internal static bool IsStaticPipelineInputCmdlet(string commandName) =>
        Cmdlets.Any(candidate => candidate.Descriptor.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase)
            && candidate is IAotPipelineInputCmdlet);

    // This is a static registry availability query, not metadata discovery.
    // It guards the common-parameter extraction boundary: generated catalog
    // entries and unknown names must reach the normal AOT2001 binder path.
    internal static bool IsStaticNativeCommand(string commandName) =>
        Cmdlets.Any(candidate => candidate.Descriptor.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase));

    // Record stages are executable only after a preceding source has crossed
    // the one-way typed-record boundary. They deliberately are not IAotCmdlet
    // entries: source-position execution would imply an object/input binder
    // that this slice does not provide.
    internal static bool IsStaticRecordStage(string commandName) =>
        AotStaticRecordStageRegistry.IsRegistered(commandName);

    internal static AotPipelineTailStagePlan BindStaticRecordStage(AotCommandPlan command) =>
        AotStaticRecordStageRegistry.Bind(command);

    internal static IReadOnlySet<string>? DirectParameterNames(string commandName)
    {
        IAotCmdlet? cmdlet = Cmdlets.FirstOrDefault(candidate =>
            candidate.Descriptor.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase));
        return cmdlet is null
            ? null
            : cmdlet.Descriptor.Parameters.Select(static parameter => parameter.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    internal static string? StaticPipelineInputSynopsis(string commandName) => Cmdlets
        .FirstOrDefault(candidate => candidate.Descriptor.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase))
        is IAotPipelineInputCmdlet inputCmdlet
            ? inputCmdlet.PipelineInputSynopsis
            : null;

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

        if (cmdlet.Descriptor.BindingMode is StaticBindingMode.JoinPathSequential or StaticBindingMode.SplitPathSelector)
        {
            return BindJ1(cmdlet, arguments.ToArray(), commandSpan);
        }

        Dictionary<string, List<CommandSyntaxAtom>> bound = new(StringComparer.OrdinalIgnoreCase);
        ParameterSpec? current = null;
        CommandSyntaxAtom[] atoms = arguments.ToArray();
        for (int index = 0; index < atoms.Length; index++)
        {
            CommandSyntaxAtom atom = atoms[index];
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
                    if (index + 1 < atoms.Length && atoms[index + 1].IsAttachedParameterValue)
                    {
                        CommandSyntaxAtom attached = atoms[++index];
                        if (attached.AttachedDirectBoolean is not bool switchValue)
                        {
                            throw new ScriptException(AotDiagnostics.Unsupported(
                                $"non-Boolean attached -{parameterName} value for {cmdlet.Descriptor.Name}",
                                attached.Span ?? atom.Span ?? commandSpan,
                                $"Use bare -{parameterName} or attach $true/$false directly (for example, -{parameterName}:$false)."));
                        }

                        bound[current.Name].Add(new CommandSyntaxAtom(
                            switchValue ? "true" : "false",
                            IsParameter: false,
                            attached.Span));
                    }
                    else
                    {
                        bound[current.Name].Add(new CommandSyntaxAtom("true", IsParameter: false, atom.Span));
                    }

                    current = null;
                }

                continue;
            }

            if (atom.IsAttachedParameterValue && current is null)
            {
                throw new ScriptException(AotDiagnostics.Unsupported(
                    "an attached command parameter value without its parameter",
                    atom.Span ?? commandSpan,
                    "Use an admitted static parameter form."));
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

    private static (IAotCmdlet Cmdlet, CommandInvocation Invocation) BindJ1(
        IAotCmdlet cmdlet,
        CommandSyntaxAtom[] atoms,
        AotSourceSpan? commandSpan)
    {
        return cmdlet.Descriptor.BindingMode == StaticBindingMode.JoinPathSequential
            ? BindJoinPath(cmdlet, atoms, commandSpan)
            : BindSplitPath(cmdlet, atoms, commandSpan);
    }

    private static (IAotCmdlet Cmdlet, CommandInvocation Invocation) BindJoinPath(IAotCmdlet cmdlet, CommandSyntaxAtom[] atoms, AotSourceSpan? span)
    {
        Dictionary<string, List<CommandSyntaxAtom>> bound = NewBound();
        Dictionary<string, AotSourceSpan?> parameterSpans = new(StringComparer.OrdinalIgnoreCase);
        List<CommandSyntaxAtom> positional = [];
        ParameterSpec? active = null;
        foreach (CommandSyntaxAtom atom in atoms)
        {
            if (atom.IsParameter)
            {
                active = FindJ1Parameter(cmdlet, atom, span);
                if (bound.ContainsKey(active.Name)) throw J1("AOT6212", $"Join-Path parameter '-{active.Name}' was specified more than once.", atom.Span ?? span);
                bound.Add(active.Name, []);
                parameterSpans[active.Name] = atom.Span;
                continue;
            }

            if (active is not null) bound[active.Name].Add(atom); else positional.Add(atom);
        }

        if (bound.Count == 0)
        {
            AssignJoinPositional(positional, bound, span);
        }
        else if (!bound.ContainsKey("Path") && positional.Count > 0)
        {
            bound["Path"] = positional;
        }
        else if (positional.Count > 0)
        {
            throw J1("AOT6212", "Join-Path does not admit positional values after named J1 parameters.", positional[0].Span ?? span);
        }

        if (!bound.TryGetValue("ChildPath", out List<CommandSyntaxAtom>? children) || children.Count == 0)
        {
            throw J1("AOT6211", "Join-Path requires ChildPath.", span);
        }

        if (!bound.TryGetValue("Path", out List<CommandSyntaxAtom>? paths) || paths.Count == 0)
        {
            if (cmdlet is not IAotPipelineInputCmdlet) throw J1("AOT6211", "Join-Path requires Path.", span);
        }

        return (cmdlet, Freeze(cmdlet.Descriptor, bound, span, parameterSpans));
    }

    private static void AssignJoinPositional(List<CommandSyntaxAtom> positional, Dictionary<string, List<CommandSyntaxAtom>> bound, AotSourceSpan? span)
    {
        if (positional.Count < 2) throw J1("AOT6211", "Join-Path requires Path and ChildPath.", positional.FirstOrDefault()?.Span ?? span);
        int firstGroup = positional[0].GroupId ?? -1;
        int index = 0;
        List<CommandSyntaxAtom> paths = [];
        if (firstGroup >= 0)
        {
            while (index < positional.Count && positional[index].GroupId == firstGroup) paths.Add(positional[index++]);
        }
        else paths.Add(positional[index++]);
        if (index >= positional.Count) throw J1("AOT6211", "Join-Path requires ChildPath.", span);
        int childGroup = positional[index].GroupId ?? -1;
        List<CommandSyntaxAtom> children = [];
        if (childGroup >= 0) while (index < positional.Count && positional[index].GroupId == childGroup) children.Add(positional[index++]);
        else children.Add(positional[index++]);
        bound["Path"] = paths;
        bound["ChildPath"] = children;
        if (index < positional.Count) bound["AdditionalChildPath"] = positional.Skip(index).ToList();
    }

    private static (IAotCmdlet Cmdlet, CommandInvocation Invocation) BindSplitPath(IAotCmdlet cmdlet, CommandSyntaxAtom[] atoms, AotSourceSpan? span)
    {
        Dictionary<string, List<CommandSyntaxAtom>> bound = NewBound();
        Dictionary<string, AotSourceSpan?> parameterSpans = new(StringComparer.OrdinalIgnoreCase);
        List<CommandSyntaxAtom> positional = [];
        ParameterSpec? active = null;
        foreach (CommandSyntaxAtom atom in atoms)
        {
            if (!atom.IsParameter)
            {
                if (active is not null) bound[active.Name].Add(atom); else positional.Add(atom);
                continue;
            }
            ParameterSpec parameter = FindJ1Parameter(cmdlet, atom, span);
            if (bound.ContainsKey(parameter.Name)) throw J1("AOT6212", $"Split-Path parameter '-{parameter.Name}' was specified more than once.", atom.Span ?? span);
            bound.Add(parameter.Name, parameter.Shape == AotParameterShape.Switch ? [new CommandSyntaxAtom("true", false, atom.Span)] : []);
            parameterSpans[parameter.Name] = atom.Span;
            active = parameter.Shape == AotParameterShape.Switch ? null : parameter;
        }

        string[] selectors = ["Parent", "Leaf", "LeafBase", "Extension", "IsAbsolute"];
        string? selected = null;
        foreach (CommandSyntaxAtom atom in atoms.Where(static atom => atom.IsParameter))
        {
            string? selector = selectors.FirstOrDefault(name => name.Equals(atom.Text, StringComparison.OrdinalIgnoreCase));
            if (selector is null) continue;
            if (selected is not null) throw J1("AOT6213", "Split-Path accepts exactly one static selector.", atom.Span ?? span);
            selected = selector;
        }
        bool literal = bound.ContainsKey("LiteralPath");
        if (literal && selected is not null)
        {
            CommandSyntaxAtom conflict = atoms.Last(atom => atom.IsParameter && (atom.Text.Equals("LiteralPath", StringComparison.OrdinalIgnoreCase) || atom.Text.Equals("PSPath", StringComparison.OrdinalIgnoreCase) || atom.Text.Equals("LP", StringComparison.OrdinalIgnoreCase) || selectors.Any(name => name.Equals(atom.Text, StringComparison.OrdinalIgnoreCase))));
            throw J1("AOT6213", "LiteralPath is only admitted by the default parent route.", conflict.Span ?? span);
        }
        if (literal && positional.Count > 0) throw J1("AOT6212", "LiteralPath cannot be combined with positional paths.", positional[0].Span ?? span);
        if (!literal)
        {
            if (positional.Count == 0)
            {
                if (cmdlet is not IAotPipelineInputCmdlet) throw J1("AOT6211", "Split-Path requires Path.", span);
            }
            else bound["Path"] = positional;
        }
        else if (bound["LiteralPath"].Count == 0) throw J1("AOT6211", "Split-Path LiteralPath requires a value.", span);
        return (cmdlet, Freeze(cmdlet.Descriptor, bound, span, parameterSpans));
    }

    private static ParameterSpec FindJ1Parameter(IAotCmdlet cmdlet, CommandSyntaxAtom atom, AotSourceSpan? span) =>
        cmdlet.Descriptor.Parameters.FirstOrDefault(parameter => parameter.Matches(atom.Text))
        ?? throw J1("AOT6212", $"{cmdlet.Descriptor.Name} does not admit parameter '-{atom.Text}'.", atom.Span ?? span);

    private static Dictionary<string, List<CommandSyntaxAtom>> NewBound() => new(StringComparer.OrdinalIgnoreCase);

    private static CommandInvocation Freeze(CmdletDescriptor descriptor, Dictionary<string, List<CommandSyntaxAtom>> bound, AotSourceSpan? span, IReadOnlyDictionary<string, AotSourceSpan?>? parameterSpans = null) =>
        new(descriptor,
            bound.ToDictionary(static pair => pair.Key, static pair => pair.Value.Select(static atom => atom.Text).ToArray(), StringComparer.OrdinalIgnoreCase),
            span,
            bound.ToDictionary(static pair => pair.Key, static pair => pair.Value.Select(static atom => (AotSourceSpan?)atom.Span).ToArray(), StringComparer.OrdinalIgnoreCase),
            parameterSpans: parameterSpans);

    private static ScriptException J1(string id, string message, AotSourceSpan? span) =>
        new(AotDiagnostics.Binding(id, message, span));
}

// Static adapters for the reviewed J1 lexical profile.  They deliberately use
// only PathText; no host capability is injected because lexical text has zero
// filesystem/provider authority.
internal sealed class JoinPathCmdlet : AotPipelineInputCmdletBase<TextRecord>
{
    private static readonly CmdletDescriptor JoinPathDescriptor = new(
        GeneratedCmdletPorts.JoinPath.Name,
        GeneratedCmdletPorts.JoinPath.CreateAotDescriptor("Path", "ChildPath", "AdditionalChildPath").Parameters,
        "Path",
        StaticBindingMode.JoinPathSequential);

    public override CmdletDescriptor Descriptor => JoinPathDescriptor;
    public override IReadOnlyList<string> DefaultColumns { get; } = ["Value"];

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
        => Execute(invocation, null, context);

    protected override IEnumerable<IPipelineRecord> ProcessPipelineInput(CommandInvocation invocation, IReadOnlyList<TextRecord> input, AotExecutionContext context)
        => Execute(invocation, input.Select(static record => (record.Value, (AotSourceSpan?)null)).ToArray(), context);

    private static IEnumerable<IPipelineRecord> Execute(CommandInvocation invocation, IReadOnlyList<(string Value, AotSourceSpan? Span)>? pipelinePaths, AotExecutionContext context)
    {
        if (pipelinePaths is not null && invocation.TryGetValues("Path", out _))
            throw new ScriptException(AotDiagnostics.Binding("AOT6212", "Join-Path cannot combine pipeline Path input with explicit Path.", invocation.GetParameterSpan("Path")));
        IReadOnlyList<(string Value, AotSourceSpan? Span)> paths = pipelinePaths ?? Values(invocation, "Path", required: true);
        List<(string Value, AotSourceSpan? Span)> childValues = [.. Values(invocation, "ChildPath", required: true)];
        childValues.AddRange(Values(invocation, "AdditionalChildPath", required: false));

        // Validate the entire invocation before producing an output record.
        List<PathText> children = childValues.Select(value => PathText.Parse(value.Value, LexicalPathDialect.PosixV1, value.Span, child: true)).ToList();
        List<TextRecord> output = [];
        foreach ((string value, AotSourceSpan? valueSpan) in paths)
        {
            context.ThrowIfCancellationRequested();
            output.Add(new TextRecord(PathText.Parse(value, LexicalPathDialect.PosixV1, valueSpan).Compose(children, valueSpan).Value));
        }

        return output;
    }

    internal static IReadOnlyList<(string Value, AotSourceSpan? Span)> Values(CommandInvocation invocation, string name, bool required)
    {
        if (!invocation.TryGetValues(name, out string[] values) || values.Length == 0)
        {
            if (required) throw new ScriptException(AotDiagnostics.Binding("AOT6211", $"{invocation.Descriptor.Name} requires '{name}'.", invocation.SourceSpan));
            return [];
        }

        return values.Select((value, index) => (value, invocation.GetValueSpan(name, index))).ToArray();
    }
}

internal sealed class SplitPathCmdlet : AotPipelineInputCmdletBase<TextRecord>
{
    private static readonly CmdletDescriptor SplitPathDescriptor = new(
        GeneratedCmdletPorts.SplitPath.Name,
        GeneratedCmdletPorts.SplitPath.CreateAotDescriptor("Path", "LiteralPath", "Parent", "Leaf", "LeafBase", "Extension", "IsAbsolute").Parameters,
        "Path",
        StaticBindingMode.SplitPathSelector);

    public override CmdletDescriptor Descriptor => SplitPathDescriptor;
    public override IReadOnlyList<string> DefaultColumns { get; } = ["Value"];

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
        => Execute(invocation, null, context);

    protected override IEnumerable<IPipelineRecord> ProcessPipelineInput(CommandInvocation invocation, IReadOnlyList<TextRecord> input, AotExecutionContext context)
        => Execute(invocation, input.Select(static record => (record.Value, (AotSourceSpan?)null)).ToArray(), context);

    private static IEnumerable<IPipelineRecord> Execute(CommandInvocation invocation, IReadOnlyList<(string Value, AotSourceSpan? Span)>? pipelineValues, AotExecutionContext context)
    {
        if (pipelineValues is not null && (invocation.TryGetValues("Path", out _) || invocation.TryGetValues("LiteralPath", out _)))
            throw new ScriptException(AotDiagnostics.Binding("AOT6212", "Split-Path cannot combine pipeline Path input with explicit Path or LiteralPath.", invocation.GetParameterSpan(invocation.TryGetValues("Path", out _) ? "Path" : "LiteralPath")));
        string pathName = invocation.TryGetValues("LiteralPath", out _) ? "LiteralPath" : "Path";
        IReadOnlyList<(string Value, AotSourceSpan? Span)> values = pipelineValues ?? JoinPathCmdlet.Values(invocation, pathName, required: true);
        string mode = invocation.TryGetValues("Leaf", out _) ? "Leaf"
            : invocation.TryGetValues("LeafBase", out _) ? "LeafBase"
            : invocation.TryGetValues("Extension", out _) ? "Extension"
            : invocation.TryGetValues("IsAbsolute", out _) ? "IsAbsolute"
            : "Parent";

        // Parse all values first: a mixed invalid batch has no partial output.
        List<(PathText Path, AotSourceSpan? Span)> parsed = values.Select(value => (PathText.Parse(value.Value, LexicalPathDialect.PosixV1, value.Span), value.Span)).ToList();
        List<IPipelineRecord> output = [];
        foreach ((PathText path, AotSourceSpan? valueSpan) in parsed)
        {
            context.ThrowIfCancellationRequested();
            output.Add(mode switch
            {
                "Leaf" => new TextRecord(path.Leaf()),
                "LeafBase" => new TextRecord(path.LeafBase()),
                "Extension" => new TextRecord(path.Extension()),
                "IsAbsolute" => new BooleanRecord(path.IsAbsolute),
                _ => new TextRecord(path.Parent(valueSpan)),
            });
        }

        return output;
    }
}

// Port boundary for Microsoft.PowerShell.Commands.GetProcessCommand:
// Cmdlet => IAotCmdlet; [Parameter] => Descriptor; WriteError => context.
// No System.Management.Automation dependency crosses this boundary.
internal sealed class GetProcessCmdlet(IProcessCatalog catalog) : AotPipelineInputCmdletBase<ProcessRecord>
{
    // Generated from the original Process.cs [Cmdlet], [Parameter], and [Alias]
    // declarations by PwshAotPortGenerator. InputObject is intentionally not
    // a direct command argument: only the typed static stage contract can
    // supply ProcessRecord input, so a string cannot masquerade as Process.
    private static readonly CmdletDescriptor GetProcessDescriptor = GeneratedCmdletPorts.GetProcess.CreateAotDescriptor("Name", "Id", "IncludeUserName", "Module", "FileVersionInfo");

    public override CmdletDescriptor Descriptor => GetProcessDescriptor;
    public override IReadOnlyList<string> DefaultColumns { get; } = ["Name", "Id", "CPU"];

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        return Execute(invocation, null, context);
    }

    protected override IEnumerable<IPipelineRecord> ProcessPipelineInput(
        CommandInvocation invocation,
        IReadOnlyList<ProcessRecord> input,
        AotExecutionContext context) => Execute(invocation, input, context);

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
    private readonly IAotHostPlatform _platform;
    private readonly IProcessOwnerReader _owners;

    internal SystemProcessCatalog(IAotHostPlatform? platform = null, IProcessOwnerReader? owners = null)
    {
        _platform = platform ?? new SystemAotHostPlatform();
        _owners = owners ?? new UnixPsProcessOwnerReader(_platform);
    }
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
        return _owners.TryGetOwner(id, context);
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
    AotPipelineInputStage? inputStage,
    IReadOnlyList<IAotRecordBatchStage> stages,
    AotRecordShape shape,
    AotSourceSpan? boundarySpan,
    int pipelineLength = 1)
{
    internal AotRecordShape Shape { get; } = shape;
    internal IReadOnlyList<string> Columns => Shape.Fields;
    // Match the previous terminal policy exactly without relying on a record
    // runtime type: only a direct, untransformed cmdlet may request prose.
    // A typed input stage or structural transform produces a table contract.
    internal AotTerminalPresentation TerminalPresentation =>
        inputStage is null && stages.Count == 0
            ? source.TerminalPresentation
            : AotTerminalPresentation.Table;

    // An upstream-derived static view only applies to the command's direct
    // result. A typed input stage or structural projection owns a new shape,
    // so retaining the source view there would mislabel transformed data.
    internal AotTableLayout? TerminalTableLayout =>
        inputStage is null && stages.Count == 0
            ? source.DefaultTableLayout
            : null;

    // Production execution carries this canonical batch through the runtime
    // event; it never creates compatibility rows in the engine.
    internal AotRecordBatch ExecuteBatch(AotExecutionContext context)
    {
        context.ThrowIfCancellationRequested();
        IReadOnlyList<IPipelineRecord> rows = Materialize(
            context,
            source.Invoke(invocation, context, pipelinePosition: 0, pipelineLength));
        if (inputStage is not null)
        {
            context.ThrowIfCancellationRequested();
            rows = Materialize(
                context,
                inputStage.Cmdlet.InvokeWithPipelineInput(
                    inputStage.Invocation,
                    rows,
                    context,
                    pipelinePosition: 1,
                    pipelineLength));
        }
        return ApplyStages(context, rows, stages, boundarySpan);
    }

    // Transitional fixture seam retained for pre-Phase-7 port tests. The host
    // execution path uses ExecuteBatch and terminal projection exclusively.
    // It preserves raw typed rows when no structural boundary is requested.
    internal IReadOnlyList<IPipelineRecord> Execute(AotExecutionContext context)
    {
        context.ThrowIfCancellationRequested();
        IReadOnlyList<IPipelineRecord> rows = Materialize(
            context,
            source.Invoke(invocation, context, pipelinePosition: 0, pipelineLength));
        if (inputStage is not null)
        {
            context.ThrowIfCancellationRequested();
            rows = Materialize(
                context,
                inputStage.Cmdlet.InvokeWithPipelineInput(
                    inputStage.Invocation,
                    rows,
                    context,
                    pipelinePosition: 1,
                    pipelineLength));
        }

        return stages.Count == 0
            ? rows
            : ApplyStages(context, rows, stages, boundarySpan).ToTerminalRows(context);
    }

    // The sole reusable record-transform seam. Native sources, static input
    // adapters, and function producers hand it concrete typed rows; no result
    // is flattened, rendered, or adapted through object.
    internal static AotRecordBatch ApplyStages(
        AotExecutionContext context,
        IReadOnlyList<IPipelineRecord> rows,
        IReadOnlyList<IAotRecordBatchStage> stages,
        AotSourceSpan? boundarySpan = null)
    {
        AotRecordBatch batch = AotRecordBatch.FromTypedRows(context, rows, boundarySpan);
        return batch.Apply(context, stages);
    }

    private static IReadOnlyList<IPipelineRecord> Materialize(
        AotExecutionContext context,
        IEnumerable<IPipelineRecord> records)
    {
        List<IPipelineRecord> materialized = [];
        foreach (IPipelineRecord record in records)
        {
            context.ThrowIfCancellationRequested();
            materialized.Add(record);
        }

        context.ThrowIfCancellationRequested();
        return materialized;
    }

}

internal sealed class Filter(string property, Comparison comparison, AotValue value, AotSourceSpan? propertySpan = null)
{
    internal bool Matches(AotValue row)
    {
        if (!row.TryGetProperty(property, out AotValue propertyValue)
            || propertyValue.Kind is not (AotValueKind.Integer or AotValueKind.Decimal or AotValueKind.FloatingPoint)
            || !AotValueComparison.TryCompare(propertyValue, value, out int result))
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT6402",
                $"Where-Object Property '{property}' is not a numeric field on this AOT record batch.",
                propertySpan,
                "unsupported numeric record field",
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
    internal static void Write(
        IReadOnlyList<IPipelineRecord> rows,
        IReadOnlyList<string> columns,
        TextWriter? writer = null,
        AotExecutionContext? projectionContext = null,
        Action? afterRowRendered = null,
        AotTableLayout? layout = null)
    {
        writer ??= Console.Out;
        IReadOnlyList<AotTableColumn> tableColumns = ResolveColumns(columns, layout);
        string columnSeparator = layout?.ColumnSeparator ?? "  ";
        int[] widths = tableColumns.Select(static column => Math.Max(column.Header.Length, column.MinimumWidth)).ToArray();
        foreach (IPipelineRecord row in rows)
        {
            projectionContext?.ThrowIfCancellationRequested();
            for (int index = 0; index < tableColumns.Count; index++)
            {
                widths[index] = Math.Max(widths[index], RenderCell(row, tableColumns[index]).Length);
            }
        }

        AotTableGroup? group = layout?.Group;
        string? activeGroup = null;
        bool wroteGroup = false;
        bool wroteLeadingBlankLines = false;
        foreach (IPipelineRecord row in rows)
        {
            projectionContext?.ThrowIfCancellationRequested();
            if (group is not null)
            {
                string rowGroup = row.TextFor(group.Field);
                if (!string.Equals(activeGroup, rowGroup, StringComparison.Ordinal))
                {
                    if (wroteGroup)
                    {
                        writer.WriteLine();
                    }

                    writer.WriteLine();
                    writer.WriteLine($"    {group.Header}: {rowGroup}");
                    writer.WriteLine();
                    WriteLine(tableColumns.Select(static column => column.Header).ToArray(), widths, tableColumns, columnSeparator, writer);
                    WriteSeparator(tableColumns, widths, columnSeparator, writer);
                    activeGroup = rowGroup;
                    wroteGroup = true;
                }
            }
            else if (!wroteGroup)
            {
                if (!wroteLeadingBlankLines)
                {
                    for (int index = 0; index < layout?.LeadingBlankLines; index++)
                    {
                        writer.WriteLine();
                    }

                    wroteLeadingBlankLines = true;
                }

                WriteLine(tableColumns.Select(static column => column.Header).ToArray(), widths, tableColumns, columnSeparator, writer);
                WriteSeparator(tableColumns, widths, columnSeparator, writer);
                wroteGroup = true;
            }

            WriteLine(tableColumns.Select(column => RenderCell(row, column)).ToArray(), widths, tableColumns, columnSeparator, writer);
            afterRowRendered?.Invoke();
        }

        if (wroteGroup && group is null)
        {
            for (int index = 0; index < layout?.TrailingBlankLines; index++)
            {
                writer.WriteLine();
            }
        }

        projectionContext?.ThrowIfCancellationRequested();
    }

    private static IReadOnlyList<AotTableColumn> ResolveColumns(IReadOnlyList<string> columns, AotTableLayout? layout)
    {
        if (layout is null)
        {
            return columns.Select(static column => new AotTableColumn(column, column)).ToArray();
        }

        if (!layout.Columns.Select(static column => column.Field).SequenceEqual(columns, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("A static terminal table layout did not match the output record shape.");
        }

        return layout.Columns;
    }

    private static string RenderCell(IPipelineRecord row, AotTableColumn column)
    {
        if (column.ValueFormat == AotTableValueFormat.FileSystemLastWriteTime
            && row is AotPipelineRecord { Record: var record }
            && record.TryGetValue(column.Field, out AotValue value)
            && value.TryGetDateTime(out DateTime dateTime))
        {
            // Static transcription of FileSystem_format_ps1xml.cs:
            // '{0:d} {0:HH}:{0:mm}' -f $_.LastWriteTime.
            return string.Format(CultureInfo.CurrentCulture, "{0:d} {0:HH}:{0:mm}", dateTime);
        }

        return row.TextFor(column.Field);
    }

    private static void WriteLine(
        IReadOnlyList<string> values,
        IReadOnlyList<int> widths,
        IReadOnlyList<AotTableColumn> columns,
        string columnSeparator,
        TextWriter writer)
    {
        string line = string.Join(columnSeparator, values.Select((value, index) => columns[index].Alignment == AotTableAlignment.Right
            ? value.PadLeft(widths[index])
            : value.PadRight(widths[index])));
        // Console table controls do not serialize the padding past the final
        // cell. Keep column geometry intact while matching that terminal fact.
        writer.WriteLine(line.TrimEnd());
    }

    private static void WriteSeparator(IReadOnlyList<AotTableColumn> columns, IReadOnlyList<int> widths, string columnSeparator, TextWriter writer)
    {
        // PowerShell table controls retain declared display widths for column
        // positioning but underline the header text itself, not all padding;
        // the underline follows the source header's declared alignment.
        WriteLine(columns.Select(static column => new string('-', column.Header.Length)).ToArray(), widths, columns, columnSeparator, writer);
    }
}

internal static class SelfTest
{
    internal static void Run()
    {
        AssertHostSubstrate();
        AssertNewGuidPort();
        AssertNewTimeSpanPort();
        AssertStartSleepPort();
        AssertExecutionKernelAndDiagnostics();
        AssertRuntimeEventContract();
        AssertStaticCommonParameterPolicy();
        AssertTypedStageComposition();
        AssertCancellationLifecycle();
        AssertLanguageCompatibilityCore();
        AssertNamedLocalFunctions();
        AssertFunctionReturnControlFlow();
        AssertStaticFunctionComposition();
        AssertGeneralTypedDataPipeline();
        AssertControlFlowCore();
        AssertForEachCore();
        AssertTerminalPresentation();
        AssertHostReplProjection();
        AssertClosedValuePlane();
        AssertJ2DescriptorRedirectAndTransportContract();
        AssertClosedJsonCodec();
        AssertJ1LexicalPaths();

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
            || !builtInHelp.Contains("native-aot (implemented current scope)", StringComparison.Ordinal)
            || !builtInHelp.Contains("-Path <string[]>", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Get-Help did not render the generated built-in contract.");
        }

        PipelinePlan getItemHelpPlan = ScriptParser.Parse("Get-Help Get-Item");
        if (getItemHelpPlan.Execute(new AotExecutionContext()).SingleOrDefault() is not HelpRecord { Content: var getItemHelp }
            || !getItemHelp.Contains("native-aot (implemented current scope)", StringComparison.Ordinal)
            || !getItemHelp.Contains("-Path <string[]>", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Get-Help did not render the Get-Item generated contract.");
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
                Name: "Get-ChildItem", CommandType: "Cmdlet", ModuleName: "PowerShell.BuiltIn", Availability: "native-aot",
            })
        {
            throw new InvalidOperationException("Get-Command did not query the built-in source catalog.");
        }

        PipelinePlan getItemCommandPlan = ScriptParser.Parse("Get-Command Get-Item");
        if (getItemCommandPlan.Execute(new AotExecutionContext()).SingleOrDefault() is not CommandInfoRecord
            {
                Name: "Get-Item", CommandType: "Cmdlet", ModuleName: "PowerShell.BuiltIn", Availability: "native-aot",
            })
        {
            throw new InvalidOperationException("Get-Command did not query the native Get-Item adapter.");
        }

        PipelinePlan nativeCommandPlan = ScriptParser.Parse("Get-Command Get-Command");
        if (nativeCommandPlan.Execute(new AotExecutionContext()).SingleOrDefault() is not CommandInfoRecord
            {
                Availability: "native-aot",
            })
        {
            throw new InvalidOperationException("Get-Command did not visibly distinguish a native adapter from catalogued-only commands.");
        }

        if (ScriptParser.Parse("Get-Help Get-Process").Execute(new AotExecutionContext()).SingleOrDefault() is not HelpRecord { Content: var processHelp }
            || processHelp.Contains("-InputObject", StringComparison.Ordinal)
            || processHelp.Contains("parameter set: InputObject", StringComparison.Ordinal)
            || !processHelp.Contains("Accepts static ProcessRecord input only from one preceding registered AOT pipeline adapter.", StringComparison.Ordinal)
            || CompletionService.Instance.Suggest("Get-Process -In").Any(static suggestion => suggestion.Text.Equals("-InputObject", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Get-Process help or completion advertised direct InputObject binding outside the static typed-stage contract.");
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
        SourceParameterMetadata inputParameter = getProcessContract.Parameters.Single(parameter => parameter.Name == "InputObject");
        if (!getProcessContract.BaseTypeChain.Take(2).SequenceEqual(["ProcessBaseCommand", "Cmdlet"])
            || !getProcessContract.Lifecycle.HasProcessRecord
            || idParameter.Shape != AotParameterShape.Array
            || !idParameter.ParameterSets.Any(parameterSet => parameterSet.Name == "Id" && parameterSet.Mandatory && parameterSet.PipelineBinding.HasFlag(PipelineBindingSource.ByPropertyName))
            || inputParameter.TypeName != "Process[]"
            || !inputParameter.ParameterSets.Any(parameterSet => parameterSet.Mandatory && parameterSet.PipelineBinding.HasFlag(PipelineBindingSource.ByValue))
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
        catch (ScriptException exception) when (exception.Diagnostic is { Id: "AOT6406", Category: AotDiagnosticCategory.Binding })
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
        if (((IAotPipelineInputCmdlet)port).InvokeWithPipelineInput(
                inputInvocation,
                [new ProcessRecord("pwsh", 7, 1, 2)],
                new AotExecutionContext(),
                pipelinePosition: 1,
                pipelineLength: 2).SingleOrDefault() is not ProcessRecord { Id: 7 })
        {
            throw new InvalidOperationException("Get-Process InputObject regression.");
        }

        SourceCmdletMetadata getChildItemContract = GeneratedCmdletPorts.GetChildItem;
        if (!getChildItemContract.BaseTypeChain.Take(2).SequenceEqual(["CoreCommandBase", "PSCmdlet"])
            || getChildItemContract.Parameters.Single(parameter => parameter.Name == "Path").ParameterSets.Single().Position != 0
            || !getChildItemContract.Parameters.Single(parameter => parameter.Name == "LiteralPath").Aliases.SequenceEqual(["PSPath", "LP"])
            || !getChildItemContract.Parameters.Single(parameter => parameter.Name == "Recurse").Aliases.SequenceEqual(["s", "r"]))
        {
            throw new InvalidOperationException("Generated Get-ChildItem contract regression.");
        }

        // macOS exposes /tmp and /var through intentional system symlinks.
        // This fixture must exercise the strict all-components direct-path
        // policy itself, so locate it below the caller's checked-out working
        // directory rather than an alias-rooted temporary directory.
        string childItemFixtureDirectory = Path.Combine(Environment.CurrentDirectory, $"pwsh-aot-lite-childitems-{Guid.NewGuid():N}");
        Directory.CreateDirectory(childItemFixtureDirectory);
        try
        {
            string childAlphaPath = Path.Combine(childItemFixtureDirectory, "alpha.txt");
            string childBetaPath = Path.Combine(childItemFixtureDirectory, "beta.txt");
            string childDirectoryPath = Path.Combine(childItemFixtureDirectory, "folder");
            string hiddenChildPath = Path.Combine(childItemFixtureDirectory, ".hidden.txt");
            File.WriteAllText(childAlphaPath, "alpha");
            File.WriteAllText(childBetaPath, "beta");
            Directory.CreateDirectory(childDirectoryPath);
            File.WriteAllText(hiddenChildPath, "hidden");

            IAotHostPlatform macOsChildItemPlatform = new FixtureHostPlatform(new AotHostPlatformSnapshot(AotHostOperatingSystem.MacOS, System.Runtime.InteropServices.Architecture.Arm64));
            GetChildItemCmdlet childItems = new(new SystemPhysicalChildItemCatalog(new FixtureDiscoveryRoots(childItemFixtureDirectory), macOsChildItemPlatform));
            PhysicalChildItemRecord[] defaultChildItems = childItems.Invoke(
                new CommandInvocation(childItems.Descriptor, new Dictionary<string, string[]>()),
                new AotExecutionContext()).Cast<PhysicalChildItemRecord>().ToArray();
            string[] expectedChildPaths = [childDirectoryPath, childAlphaPath, childBetaPath];
            if (!defaultChildItems.Select(static item => item.Path).SequenceEqual(expectedChildPaths)
                || defaultChildItems.Single(item => item.Path == childDirectoryPath) is not { Kind: PhysicalChildItemKind.Directory, Length: null }
                || defaultChildItems.Single(item => item.Path == childAlphaPath) is not { Kind: PhysicalChildItemKind.File, Length: 5, Size: 5, UnixMode: var alphaMode }
                || alphaMode.Length != 10
                || defaultChildItems.Any(static item => string.IsNullOrWhiteSpace(item.User) || string.IsNullOrWhiteSpace(item.Group)))
            {
                throw new InvalidOperationException("Get-ChildItem captured-root/default immediate-child enumeration regression.");
            }

            if (!childItems.DefaultColumns.SequenceEqual(["UnixMode", "User", "Group", "LastWriteTime", "Size", "Name"])
                || childItems.DefaultTableLayout is not { Group: { Field: "ParentPath", Header: "Directory" }, ColumnSeparator: " ", Columns: var childColumns }
                || !childColumns.Select(static column => column.Field).SequenceEqual(childItems.DefaultColumns)
                || !childColumns.Select(static column => column.Header).SequenceEqual(["UnixMode", "User", "Group", "LastWriteTime", "Size", "Name"]))
            {
                throw new InvalidOperationException("Get-ChildItem static upstream Unix display-view transcription regression.");
            }

            // The catalog owns direct physical enumeration, so it must observe
            // a host cancellation even when called below the cmdlet lifecycle.
            using (CancellationTokenSource cancelledChildEnumeration = new())
            {
                cancelledChildEnumeration.Cancel();
                AotExecutionContext cancelledChildContext = new(cancelledChildEnumeration.Token);
                AssertCancellation(() => ((IPhysicalChildItemCatalog)new SystemPhysicalChildItemCatalog(
                    new FixtureDiscoveryRoots(childItemFixtureDirectory), macOsChildItemPlatform))
                    .GetImmediateChildren(childItemFixtureDirectory, cancelledChildContext, span: null)
                    .ToArray());
                if (cancelledChildContext.Errors.Count != 0 || cancelledChildContext.Events.Count != 0)
                {
                    throw new InvalidOperationException("Get-ChildItem catalog cancellation leaked output or an error event.");
                }
            }

            AotExecutionContext unsupportedDarwinArchitecture = new();
            _ = ((IPhysicalChildItemCatalog)new SystemPhysicalChildItemCatalog(
                new FixtureDiscoveryRoots(childItemFixtureDirectory),
                new FixtureHostPlatform(new AotHostPlatformSnapshot(AotHostOperatingSystem.MacOS, System.Runtime.InteropServices.Architecture.X64))))
                .GetImmediateChildren(childItemFixtureDirectory, unsupportedDarwinArchitecture, span: null)
                .ToArray();
            if (unsupportedDarwinArchitecture.Errors.SingleOrDefault()?.Id != "AOT6209")
            {
                throw new InvalidOperationException("Get-ChildItem Darwin ABI gate regression.");
            }

            PhysicalChildItemRecord[] exactFile = childItems.Invoke(
                new CommandInvocation(childItems.Descriptor, new Dictionary<string, string[]> { ["Path"] = [childAlphaPath] }),
                new AotExecutionContext()).Cast<PhysicalChildItemRecord>().ToArray();
            if (exactFile is not [var singleFile]
                || singleFile is not { Name: "alpha.txt", Path: var exactPath, Kind: PhysicalChildItemKind.File, Length: 5, Size: 5 }
                || exactPath != childAlphaPath)
            {
                throw new InvalidOperationException("Get-ChildItem exact direct-file projection regression.");
            }

            string childLinkPath = Path.Combine(childItemFixtureDirectory, "alpha-link.txt");
            File.CreateSymbolicLink(childLinkPath, childAlphaPath);
            AotExecutionContext symbolicLinkContext = new();
            if (childItems.Invoke(new CommandInvocation(childItems.Descriptor, new Dictionary<string, string[]> { ["Path"] = [childLinkPath] }), symbolicLinkContext).Any()
                || symbolicLinkContext.Errors.SingleOrDefault()?.Id != "AOT6205")
            {
                throw new InvalidOperationException("Get-ChildItem no-follow direct-link regression.");
            }

            // The all-components resolver must not accept a syntactically
            // ordinary file that reaches its target through a linked parent.
            // This is distinct from the final-link check above: accepting it
            // would reintroduce traversal before the final O_NOFOLLOW flag.
            string linkedAncestorTarget = Path.Combine(childItemFixtureDirectory, "linked-ancestor-target");
            string linkedAncestorChild = Path.Combine(linkedAncestorTarget, "descendant.txt");
            string linkedAncestor = Path.Combine(childItemFixtureDirectory, "linked-ancestor");
            Directory.CreateDirectory(linkedAncestorTarget);
            File.WriteAllText(linkedAncestorChild, "must-not-traverse");
            Directory.CreateSymbolicLink(linkedAncestor, linkedAncestorTarget);
            AotExecutionContext ancestorLinkContext = new();
            if (childItems.Invoke(new CommandInvocation(childItems.Descriptor, new Dictionary<string, string[]> { ["Path"] = [Path.Combine(linkedAncestor, "descendant.txt")] }), ancestorLinkContext).Any()
                || ancestorLinkContext.Errors.SingleOrDefault()?.Id != "AOT6205")
            {
                throw new InvalidOperationException("Get-ChildItem no-follow ancestor-link regression.");
            }

            (_, CommandInvocation positionalChildItem) = AotCmdletRegistry.ParseSource($"Get-ChildItem '{childAlphaPath}'");
            (_, CommandInvocation namedChildItem) = AotCmdletRegistry.ParseSource($"Get-ChildItem -Path '{childBetaPath}'");
            if (!positionalChildItem.TryGetValues("Path", out string[] positionalChildPaths)
                || !positionalChildPaths.SequenceEqual([childAlphaPath])
                || !namedChildItem.TryGetValues("Path", out string[] namedChildPaths)
                || !namedChildPaths.SequenceEqual([childBetaPath]))
            {
                throw new InvalidOperationException("Get-ChildItem generated Path positional/named binding regression.");
            }

            AotSourceSpan childItemSpan = new("childitem-fixture.ps1", 14, 29, 1, 15, 1, 30);
            AotExecutionContext providerContext = new();
            _ = childItems.Invoke(new CommandInvocation(childItems.Descriptor, new Dictionary<string, string[]>
            {
                ["Path"] = ["FileSystem::/tmp"],
            }, childItemSpan, new Dictionary<string, AotSourceSpan?[]> { ["Path"] = [childItemSpan] }), providerContext).ToArray();
            if (providerContext.Errors.SingleOrDefault()?.Diagnostic is not { Id: "AOT6201", Span: var providerSpan }
                || !ReferenceEquals(providerSpan, childItemSpan))
            {
                throw new InvalidOperationException("Get-ChildItem provider rejection/source-span regression.");
            }

            AotExecutionContext wildcardContext = new();
            _ = childItems.Invoke(new CommandInvocation(childItems.Descriptor, new Dictionary<string, string[]> { ["Path"] = [Path.Combine(childItemFixtureDirectory, "*.txt")] }), wildcardContext).ToArray();
            if (wildcardContext.Errors.SingleOrDefault()?.Id != "AOT6202")
            {
                throw new InvalidOperationException("Get-ChildItem wildcard rejection regression.");
            }

            try
            {
                _ = AotCmdletRegistry.ParseSource($"Get-ChildItem -LP '{childAlphaPath}'");
                throw new InvalidOperationException("Get-ChildItem accepted unsupported LiteralPath.");
            }
            catch (ScriptException exception) when (exception.Diagnostic.Id == "AOT2002")
            {
            }

            try
            {
                _ = AotCmdletRegistry.ParseSource($"Get-ChildItem -Recurse '{childItemFixtureDirectory}'");
                throw new InvalidOperationException("Get-ChildItem accepted unsupported Recurse.");
            }
            catch (ScriptException exception) when (exception.Diagnostic.Id == "AOT2002")
            {
            }

            SourceCmdletMetadata getItemContract = GeneratedCmdletPorts.GetItem;
            if (!getItemContract.BaseTypeChain.Take(2).SequenceEqual(["CoreCommandWithCredentialsBase", "CoreCommandBase"])
                || getItemContract.Parameters.Single(parameter => parameter.Name == "Path").ParameterSets.Single() is not { Position: 0, Mandatory: true }
                || !getItemContract.Parameters.Single(parameter => parameter.Name == "LiteralPath").Aliases.SequenceEqual(["PSPath", "LP"]))
            {
                throw new InvalidOperationException("Generated Get-Item contract regression.");
            }

            GetItemCmdlet getItem = new(new SystemPhysicalChildItemCatalog(new FixtureDiscoveryRoots(childItemFixtureDirectory), macOsChildItemPlatform));
            PhysicalChildItemRecord[] directFile = getItem.Invoke(
                new CommandInvocation(getItem.Descriptor, new Dictionary<string, string[]> { ["Path"] = [childAlphaPath] }),
                new AotExecutionContext()).Cast<PhysicalChildItemRecord>().ToArray();
            PhysicalChildItemRecord[] directDirectory = getItem.Invoke(
                new CommandInvocation(getItem.Descriptor, new Dictionary<string, string[]> { ["Path"] = [childDirectoryPath] }),
                new AotExecutionContext()).Cast<PhysicalChildItemRecord>().ToArray();
            if (directFile is not [var directFileItem]
                || directFileItem is not { Name: "alpha.txt", Path: var directFilePath, Kind: PhysicalChildItemKind.File, Length: 5 }
                || directFilePath != childAlphaPath
                || directDirectory is not [var directDirectoryItem]
                || directDirectoryItem is not { Name: "folder", Path: var directDirectoryPath, Kind: PhysicalChildItemKind.Directory, Length: null }
                || directDirectoryPath != childDirectoryPath
                || directDirectory.Any(item => item.Name is "alpha.txt" or "beta.txt")
                || !getItem.DefaultColumns.SequenceEqual(childItems.DefaultColumns)
                || !ReferenceEquals(getItem.DefaultTableLayout, childItems.DefaultTableLayout))
            {
                throw new InvalidOperationException("Get-Item direct file-or-directory/no-enumeration/presentation reuse regression.");
            }

            using (CancellationTokenSource cancelledDirectItem = new())
            {
                cancelledDirectItem.Cancel();
                AotExecutionContext cancelledDirectContext = new(cancelledDirectItem.Token);
                AssertCancellation(() => ((IPhysicalChildItemCatalog)new SystemPhysicalChildItemCatalog(
                    new FixtureDiscoveryRoots(childItemFixtureDirectory), macOsChildItemPlatform))
                    .GetDirectPhysicalItem(childAlphaPath, cancelledDirectContext, span: null));
                if (cancelledDirectContext.Errors.Count != 0 || cancelledDirectContext.Events.Count != 0)
                {
                    throw new InvalidOperationException("Get-Item direct catalog cancellation leaked output or an error event.");
                }
            }

            AotExecutionContext getItemProviderContext = new();
            _ = getItem.Invoke(new CommandInvocation(getItem.Descriptor, new Dictionary<string, string[]>
            {
                ["Path"] = ["FileSystem::/tmp"],
            }, childItemSpan, new Dictionary<string, AotSourceSpan?[]> { ["Path"] = [childItemSpan] }), getItemProviderContext).ToArray();
            if (getItemProviderContext.Errors.SingleOrDefault()?.Diagnostic is not { Id: "AOT6201", Span: var getItemProviderSpan }
                || !ReferenceEquals(getItemProviderSpan, childItemSpan))
            {
                throw new InvalidOperationException("Get-Item shared provider rejection/source-span regression.");
            }

            AotExecutionContext getItemLinkContext = new();
            if (getItem.Invoke(new CommandInvocation(getItem.Descriptor, new Dictionary<string, string[]>
                { ["Path"] = [Path.Combine(linkedAncestor, "descendant.txt")] }), getItemLinkContext).Any()
                || getItemLinkContext.Errors.SingleOrDefault()?.Id != "AOT6205")
            {
                throw new InvalidOperationException("Get-Item shared no-follow ancestor-link regression.");
            }

            (_, CommandInvocation positionalGetItem) = AotCmdletRegistry.ParseSource($"Get-Item '{childAlphaPath}'");
            (_, CommandInvocation namedGetItem) = AotCmdletRegistry.ParseSource($"Get-Item -Path '{childDirectoryPath}'");
            if (!positionalGetItem.TryGetValues("Path", out string[] positionalGetItemPaths)
                || !positionalGetItemPaths.SequenceEqual([childAlphaPath])
                || !namedGetItem.TryGetValues("Path", out string[] namedGetItemPaths)
                || !namedGetItemPaths.SequenceEqual([childDirectoryPath]))
            {
                throw new InvalidOperationException("Get-Item generated Path positional/named binding regression.");
            }

            SourceCmdletMetadata resolvePathContract = GeneratedCmdletPorts.ResolvePath;
            if (!resolvePathContract.BaseTypeChain.Take(2).SequenceEqual(["CoreCommandWithCredentialsBase", "CoreCommandBase"])
                || resolvePathContract.Parameters.Single(parameter => parameter.Name == "Path").ParameterSets.Single() is not { Position: 0, Mandatory: true }
                || !resolvePathContract.Parameters.Single(parameter => parameter.Name == "LiteralPath").Aliases.SequenceEqual(["PSPath", "LP"]))
            {
                throw new InvalidOperationException("Generated Resolve-Path contract regression.");
            }

            IPhysicalChildItemCatalog resolvePathCatalog = new SystemPhysicalChildItemCatalog(
                new FixtureDiscoveryRoots(childItemFixtureDirectory), macOsChildItemPlatform);
            ResolvePathCmdlet resolvePath = new(resolvePathCatalog);
            DirectPhysicalPathRecord[] resolvedRows = resolvePath.Invoke(new CommandInvocation(resolvePath.Descriptor,
                new Dictionary<string, string[]> { ["Path"] = ["alpha.txt", childDirectoryPath] }), new AotExecutionContext())
                .Cast<DirectPhysicalPathRecord>().ToArray();
            if (!resolvedRows.Select(static row => row.Path).SequenceEqual([childAlphaPath, childDirectoryPath])
                || !resolvePath.DefaultColumns.SequenceEqual(["Path"])
                || !ReferenceEquals(resolvePath.DefaultTableLayout, ResolvePathPresentation.PathTable))
            {
                throw new InvalidOperationException("Resolve-Path captured-root direct record/layout regression.");
            }

            // The catalog captures its discovery root at composition. A later
            // process CWD mutation must not become a hidden resolution input.
            string originalWorkingDirectory = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(childDirectoryPath);
                if (resolvePath.Invoke(new CommandInvocation(resolvePath.Descriptor,
                    new Dictionary<string, string[]> { ["Path"] = ["alpha.txt"] }), new AotExecutionContext())
                    .Cast<DirectPhysicalPathRecord>().SingleOrDefault() is not { Path: var capturedRootPath }
                    || capturedRootPath != childAlphaPath)
                {
                    throw new InvalidOperationException("Resolve-Path consulted invocation current directory instead of its captured root.");
                }
            }
            finally
            {
                Directory.SetCurrentDirectory(originalWorkingDirectory);
            }

            using (StringWriter resolveTable = new(CultureInfo.InvariantCulture))
            {
                TableWriter.Write(resolvedRows, resolvePath.DefaultColumns, resolveTable, layout: resolvePath.DefaultTableLayout);
                string expected = $"{Environment.NewLine}Path{Environment.NewLine}----{Environment.NewLine}{childAlphaPath}{Environment.NewLine}{childDirectoryPath}{Environment.NewLine}{Environment.NewLine}";
                if (resolveTable.ToString() != expected)
                {
                    throw new InvalidOperationException("Resolve-Path layout-owned leading blank-line regression.");
                }
            }

            AotSourceSpan resolveAcceptedFirstSpan = new("resolvepath-mixed.ps1", 13, 31, 1, 14, 1, 32);
            AotSourceSpan resolveMissingSpan = new("resolvepath-mixed.ps1", 32, 50, 1, 33, 1, 51);
            AotSourceSpan resolveRejectedSpan = new("resolvepath-mixed.ps1", 51, 69, 1, 52, 1, 70);
            AotSourceSpan resolveAcceptedLastSpan = new("resolvepath-mixed.ps1", 70, 89, 1, 53, 1, 90);
            AotExecutionContext resolveMixedContext = new();
            DirectPhysicalPathRecord[] resolveMixedRows = resolvePath.Invoke(new CommandInvocation(resolvePath.Descriptor,
                new Dictionary<string, string[]> { ["Path"] = [childAlphaPath, Path.Combine(childItemFixtureDirectory, "missing.txt"), "FileSystem::/tmp", childDirectoryPath] },
                childItemSpan,
                new Dictionary<string, AotSourceSpan?[]> { ["Path"] = [resolveAcceptedFirstSpan, resolveMissingSpan, resolveRejectedSpan, resolveAcceptedLastSpan] }),
                resolveMixedContext).Cast<DirectPhysicalPathRecord>().ToArray();
            if (!resolveMixedRows.Select(static row => row.Path).SequenceEqual([childAlphaPath, childDirectoryPath])
                || resolveMixedContext.Errors.Count != 2
                || resolveMixedContext.Errors[0].Diagnostic is not { Id: "AOT6206", Span: var resolveMissingErrorSpan }
                || !ReferenceEquals(resolveMissingErrorSpan, resolveMissingSpan)
                || resolveMixedContext.Errors[1].Diagnostic is not { Id: "AOT6201", Span: var resolveRejectedErrorSpan }
                || !ReferenceEquals(resolveRejectedErrorSpan, resolveRejectedSpan))
            {
                throw new InvalidOperationException("Resolve-Path Missing/Rejected continuation or per-value-span regression.");
            }

            AotExecutionContext resolveWhitespaceContext = new();
            if (resolvePath.Invoke(new CommandInvocation(resolvePath.Descriptor, new Dictionary<string, string[]> { ["Path"] = ["  "] }), resolveWhitespaceContext).Any()
                || resolveWhitespaceContext.Errors.SingleOrDefault()?.Id != "AOT6206")
            {
                throw new InvalidOperationException("Resolve-Path whitespace literal/missing regression.");
            }

            AotExecutionContext resolveEmptyContext = new();
            if (resolvePath.Invoke(new CommandInvocation(resolvePath.Descriptor, new Dictionary<string, string[]> { ["Path"] = [string.Empty] }), resolveEmptyContext).Any()
                || resolveEmptyContext.Errors.SingleOrDefault()?.Diagnostic is not { Id: "AOT6213", Span: null })
            {
                throw new InvalidOperationException("Resolve-Path empty direct input diagnostic regression.");
            }

            AotExecutionContext resolveLinkContext = new();
            if (resolvePath.Invoke(new CommandInvocation(resolvePath.Descriptor, new Dictionary<string, string[]>
                { ["Path"] = [Path.Combine(linkedAncestor, "descendant.txt")] }), resolveLinkContext).Any()
                || resolveLinkContext.Errors.SingleOrDefault()?.Id != "AOT6205")
            {
                throw new InvalidOperationException("Resolve-Path no-follow ancestor-link regression.");
            }

            (_, CommandInvocation positionalResolvePath) = AotCmdletRegistry.ParseSource("Resolve-Path alpha.txt");
            (_, CommandInvocation namedResolvePath) = AotCmdletRegistry.ParseSource($"Resolve-Path -Path '{childDirectoryPath}'");
            if (!positionalResolvePath.TryGetValues("Path", out string[] positionalResolveValues)
                || !positionalResolveValues.SequenceEqual(["alpha.txt"])
                || !namedResolvePath.TryGetValues("Path", out string[] namedResolveValues)
                || !namedResolveValues.SequenceEqual([childDirectoryPath]))
            {
                throw new InvalidOperationException("Resolve-Path generated Path positional/named binding regression.");
            }

            foreach (string rejectedParameter in new[] { "-LP", "-Relative", "-RelativeBasePath" })
            {
                try
                {
                    _ = AotCmdletRegistry.ParseSource($"Resolve-Path {rejectedParameter} '{childAlphaPath}'");
                    throw new InvalidOperationException($"Resolve-Path accepted deferred parameter {rejectedParameter}.");
                }
                catch (ScriptException exception) when (exception.Diagnostic.Id == "AOT2002")
                {
                }
            }

            // Convert-Path deliberately has a stricter empty-input contract
            // than the shared resolver: it must inspect the *entire* bound
            // collection before making its first catalog call.  Keep this
            // observable rather than inferring it from terminal text; a
            // counting catalog proves an empty value at every possible first
            // empty position prevents rows, resolver calls, and side errors.
            SourceCmdletMetadata convertPathContract = GeneratedCmdletPorts.ConvertPath;
            if (!convertPathContract.BaseTypeChain.Take(2).SequenceEqual(["CoreCommandBase", "PSCmdlet"])
                || convertPathContract.Parameters.Single(parameter => parameter.Name == "Path").ParameterSets.Single() is not { Position: 0, Mandatory: true })
            {
                throw new InvalidOperationException("Generated Convert-Path contract regression.");
            }

            AotSourceSpan convertResolvedFirstSpan = new("convertpath-mixed.ps1", 14, 29, 1, 15, 1, 30);
            AotSourceSpan convertMissingSpan = new("convertpath-mixed.ps1", 31, 40, 1, 32, 1, 41);
            AotSourceSpan convertRejectedSpan = new("convertpath-mixed.ps1", 42, 52, 1, 43, 1, 53);
            AotSourceSpan convertResolvedLastSpan = new("convertpath-mixed.ps1", 54, 68, 1, 55, 1, 69);
            AotSourceSpan[] emptySpans =
            [
                new("convertpath-empty.ps1", 14, 16, 1, 15, 1, 17),
                new("convertpath-empty.ps1", 31, 33, 1, 32, 1, 34),
                new("convertpath-empty.ps1", 48, 50, 1, 49, 1, 51),
            ];
            string[][] pathsWithFirstEmpty =
            [
                [string.Empty, "resolved-first", "missing"],
                ["resolved-first", string.Empty, "rejected"],
                ["resolved-first", "missing", string.Empty, "resolved-last"],
            ];
            for (int emptyIndex = 0; emptyIndex < pathsWithFirstEmpty.Length; emptyIndex++)
            {
                CountingConvertPathCatalog emptyCatalog = new();
                ConvertPathCmdlet convertPath = new(emptyCatalog);
                AotExecutionContext emptyContext = new();
                AotSourceSpan?[] valueSpans = pathsWithFirstEmpty[emptyIndex]
                    .Select((_, index) => index == emptyIndex ? emptySpans[emptyIndex] : convertResolvedFirstSpan)
                    .Cast<AotSourceSpan?>()
                    .ToArray();
                int observedAot6213 = 0;
                try
                {
                    _ = convertPath.Invoke(new CommandInvocation(convertPath.Descriptor,
                        new Dictionary<string, string[]> { ["Path"] = pathsWithFirstEmpty[emptyIndex] },
                        convertResolvedFirstSpan,
                        new Dictionary<string, AotSourceSpan?[]> { ["Path"] = valueSpans }), emptyContext).ToArray();
                    throw new InvalidOperationException("Convert-Path accepted an exact empty Path value.");
                }
                catch (ScriptException exception) when (exception.Diagnostic is { Id: "AOT6213", Span: var emptySpan }
                    && ReferenceEquals(emptySpan, emptySpans[emptyIndex]))
                {
                    observedAot6213++;
                }

                if (observedAot6213 != 1
                    || emptyCatalog.ResolveCalls != 0
                    || emptyContext.Errors.Count != 0
                    || emptyContext.Events.Count != 0)
                {
                    throw new InvalidOperationException("Convert-Path whole-collection empty preflight leaked a resolver call, row, or secondary diagnostic.");
                }
            }

            CountingConvertPathCatalog continuationCatalog = new();
            ConvertPathCmdlet continuationConvertPath = new(continuationCatalog);
            AotExecutionContext convertContinuationContext = new();
            TextRecord[] convertContinuationRows = continuationConvertPath.Invoke(new CommandInvocation(continuationConvertPath.Descriptor,
                new Dictionary<string, string[]> { ["Path"] = ["resolved-first", "missing", "rejected", "resolved-last"] },
                convertResolvedFirstSpan,
                new Dictionary<string, AotSourceSpan?[]> { ["Path"] = [convertResolvedFirstSpan, convertMissingSpan, convertRejectedSpan, convertResolvedLastSpan] }),
                convertContinuationContext).Cast<TextRecord>().ToArray();
            if (!convertContinuationRows.Select(static row => row.Value).SequenceEqual(["/fixture/convert-first", "/fixture/convert-last"])
                || !continuationCatalog.Inputs.SequenceEqual(["resolved-first", "missing", "rejected", "resolved-last"])
                || convertContinuationContext.Errors.Count != 2
                || convertContinuationContext.Errors[0].Diagnostic is not { Id: "AOT6206", Span: var convertMissingErrorSpan }
                || !ReferenceEquals(convertMissingErrorSpan, convertMissingSpan)
                || convertContinuationContext.Errors[1].Diagnostic is not { Id: "AOT6201", Span: var convertRejectedErrorSpan }
                || !ReferenceEquals(convertRejectedErrorSpan, convertRejectedSpan))
            {
                throw new InvalidOperationException("Convert-Path non-empty Missing/Rejected continuation, ordering, or per-value-span regression.");
            }

            CountingConvertPathCatalog whitespaceCatalog = new();
            ConvertPathCmdlet whitespaceConvertPath = new(whitespaceCatalog);
            AotExecutionContext convertWhitespaceContext = new();
            if (whitespaceConvertPath.Invoke(new CommandInvocation(whitespaceConvertPath.Descriptor,
                new Dictionary<string, string[]> { ["Path"] = ["  "] }), convertWhitespaceContext).Any()
                || !whitespaceCatalog.Inputs.SequenceEqual(["  "])
                || convertWhitespaceContext.Errors.SingleOrDefault()?.Id != "AOT6206")
            {
                throw new InvalidOperationException("Convert-Path whitespace literal/missing regression.");
            }

            SourceCmdletMetadata testPathContract = GeneratedCmdletPorts.TestPath;
            if (!testPathContract.BaseTypeChain.Take(2).SequenceEqual(["CoreCommandWithCredentialsBase", "CoreCommandBase"])
                || testPathContract.Parameters.Single(parameter => parameter.Name == "Path").ParameterSets.Single() is not { Position: 0, Mandatory: true }
                || !testPathContract.Parameters.Single(parameter => parameter.Name == "PathType").Aliases.SequenceEqual(["Type"])
                || !testPathContract.OutputTypes.SequenceEqual(["typeof(bool)"]))
            {
                throw new InvalidOperationException("Generated Test-Path contract regression.");
            }

            IPhysicalChildItemCatalog testPathCatalog = new SystemPhysicalChildItemCatalog(
                new FixtureDiscoveryRoots(childItemFixtureDirectory), macOsChildItemPlatform);
            TestPathCmdlet testPath = new(testPathCatalog);
            BooleanRecord[] anyResults = testPath.Invoke(new CommandInvocation(testPath.Descriptor, new Dictionary<string, string[]>
            {
                ["Path"] = [childAlphaPath, childDirectoryPath, Path.Combine(childItemFixtureDirectory, "missing.txt"), "  "],
            }), new AotExecutionContext()).Cast<BooleanRecord>().ToArray();
            if (!anyResults.Select(static row => row.Value).SequenceEqual([true, true, false, false]))
            {
                throw new InvalidOperationException("Test-Path direct Any/missing/whitespace regression.");
            }

            BooleanRecord[] containerResults = testPath.Invoke(new CommandInvocation(testPath.Descriptor, new Dictionary<string, string[]>
            {
                ["Path"] = [childAlphaPath, childDirectoryPath],
                ["PathType"] = ["container"],
            }), new AotExecutionContext()).Cast<BooleanRecord>().ToArray();
            BooleanRecord[] leafResults = testPath.Invoke(new CommandInvocation(testPath.Descriptor, new Dictionary<string, string[]>
            {
                ["Path"] = [childAlphaPath, childDirectoryPath],
                ["PathType"] = ["Leaf"],
            }), new AotExecutionContext()).Cast<BooleanRecord>().ToArray();
            if (!containerResults.Select(static row => row.Value).SequenceEqual([false, true])
                || !leafResults.Select(static row => row.Value).SequenceEqual([true, false])
                || testPath.TerminalPresentation != AotTerminalPresentation.Prose
                || !testPath.DefaultColumns.SequenceEqual(["Value"]))
            {
                throw new InvalidOperationException("Test-Path closed PathType/Boolean presentation regression.");
            }

            PhysicalItemProbeResult missingProbe = testPathCatalog.ProbeDirectPhysicalItem(
                Path.Combine(childItemFixtureDirectory, "missing.txt"), new AotExecutionContext(), span: null);
            if (missingProbe != PhysicalItemProbeResult.Missing)
            {
                throw new InvalidOperationException("Test-Path catalog missing probe regression.");
            }

            AotExecutionContext testPathProviderContext = new();
            if (testPath.Invoke(new CommandInvocation(testPath.Descriptor, new Dictionary<string, string[]>
                { ["Path"] = ["FileSystem::/tmp"] }, childItemSpan, new Dictionary<string, AotSourceSpan?[]> { ["Path"] = [childItemSpan] }), testPathProviderContext).Any()
                || testPathProviderContext.Errors.SingleOrDefault()?.Diagnostic is not { Id: "AOT6201", Span: var testPathProviderSpan }
                || !ReferenceEquals(testPathProviderSpan, childItemSpan))
            {
                throw new InvalidOperationException("Test-Path rejected provider/source-span regression.");
            }

            // A rejected value must retain its own source span and must not
            // suppress successfully probed sibling values. Exercise every
            // closed catalog rejection class that Test-Path claims here rather
            // than letting the single-value cases hide cardinality regressions.
            AotSourceSpan testPathAcceptedFirstSpan = new("testpath-mixed.ps1", 15, 31, 1, 16, 1, 32);
            AotSourceSpan testPathProviderValueSpan = new("testpath-mixed.ps1", 33, 51, 1, 34, 1, 52);
            AotSourceSpan testPathAcceptedLastSpan = new("testpath-mixed.ps1", 53, 72, 1, 54, 1, 73);
            AotExecutionContext testPathMixedProviderContext = new();
            BooleanRecord[] testPathMixedProviderResults = testPath.Invoke(new CommandInvocation(testPath.Descriptor,
                new Dictionary<string, string[]> { ["Path"] = [childAlphaPath, "FileSystem::/tmp", childDirectoryPath] },
                childItemSpan,
                new Dictionary<string, AotSourceSpan?[]> { ["Path"] = [testPathAcceptedFirstSpan, testPathProviderValueSpan, testPathAcceptedLastSpan] }),
                testPathMixedProviderContext).Cast<BooleanRecord>().ToArray();
            if (!testPathMixedProviderResults.Select(static result => result.Value).SequenceEqual([true, true])
                || testPathMixedProviderContext.Errors.SingleOrDefault()?.Diagnostic is not { Id: "AOT6201", Span: var mixedProviderSpan }
                || !ReferenceEquals(mixedProviderSpan, testPathProviderValueSpan))
            {
                throw new InvalidOperationException("Test-Path mixed provider rejection lost per-value span or Boolean cardinality.");
            }

            AotSourceSpan testPathWildcardValueSpan = new("testpath-mixed.ps1", 74, 92, 1, 75, 1, 93);
            AotExecutionContext testPathMixedWildcardContext = new();
            BooleanRecord[] testPathMixedWildcardResults = testPath.Invoke(new CommandInvocation(testPath.Descriptor,
                new Dictionary<string, string[]> { ["Path"] = [childAlphaPath, Path.Combine(childItemFixtureDirectory, "*.txt")] },
                childItemSpan,
                new Dictionary<string, AotSourceSpan?[]> { ["Path"] = [testPathAcceptedFirstSpan, testPathWildcardValueSpan] }),
                testPathMixedWildcardContext).Cast<BooleanRecord>().ToArray();
            if (!testPathMixedWildcardResults.Select(static result => result.Value).SequenceEqual([true])
                || testPathMixedWildcardContext.Errors.SingleOrDefault()?.Diagnostic is not { Id: "AOT6202", Span: var mixedWildcardSpan }
                || !ReferenceEquals(mixedWildcardSpan, testPathWildcardValueSpan))
            {
                throw new InvalidOperationException("Test-Path mixed wildcard rejection lost per-value span or Boolean cardinality.");
            }

            AotExecutionContext testPathLinkContext = new();
            if (testPath.Invoke(new CommandInvocation(testPath.Descriptor, new Dictionary<string, string[]>
                { ["Path"] = [Path.Combine(linkedAncestor, "descendant.txt")] }), testPathLinkContext).Any()
                || testPathLinkContext.Errors.SingleOrDefault()?.Id != "AOT6205")
            {
                throw new InvalidOperationException("Test-Path no-follow ancestor-link regression.");
            }

            AotSourceSpan testPathLinkValueSpan = new("testpath-mixed.ps1", 94, 117, 1, 95, 1, 118);
            AotExecutionContext testPathMixedLinkContext = new();
            BooleanRecord[] testPathMixedLinkResults = testPath.Invoke(new CommandInvocation(testPath.Descriptor,
                new Dictionary<string, string[]> { ["Path"] = [childDirectoryPath, Path.Combine(linkedAncestor, "descendant.txt")] },
                childItemSpan,
                new Dictionary<string, AotSourceSpan?[]> { ["Path"] = [testPathAcceptedLastSpan, testPathLinkValueSpan] }),
                testPathMixedLinkContext).Cast<BooleanRecord>().ToArray();
            if (!testPathMixedLinkResults.Select(static result => result.Value).SequenceEqual([true])
                || testPathMixedLinkContext.Errors.SingleOrDefault()?.Diagnostic is not { Id: "AOT6205", Span: var mixedLinkSpan }
                || !ReferenceEquals(mixedLinkSpan, testPathLinkValueSpan))
            {
                throw new InvalidOperationException("Test-Path mixed link rejection lost per-value span or Boolean cardinality.");
            }

            (_, CommandInvocation positionalTestPath) = AotCmdletRegistry.ParseSource($"Test-Path '{childAlphaPath}' -Type Leaf");
            if (!positionalTestPath.TryGetValues("Path", out string[] positionalTestPaths)
                || !positionalTestPaths.SequenceEqual([childAlphaPath])
                || !positionalTestPath.TryGetValues("PathType", out string[] testPathTypes)
                || !testPathTypes.SequenceEqual(["Leaf"]))
            {
                throw new InvalidOperationException("Test-Path generated Path/Type binding regression.");
            }

            try
            {
                _ = testPath.Invoke(new CommandInvocation(testPath.Descriptor, new Dictionary<string, string[]>
                    { ["Path"] = [childAlphaPath], ["PathType"] = ["Invalid"] }), new AotExecutionContext()).ToArray();
                throw new InvalidOperationException("Test-Path accepted an unsupported PathType.");
            }
            catch (ScriptException exception) when (exception.Diagnostic.Id == "AOT6212")
            {
            }

            try
            {
                _ = AotCmdletRegistry.ParseSource($"Test-Path -LP '{childAlphaPath}'");
                throw new InvalidOperationException("Test-Path accepted unsupported LiteralPath.");
            }
            catch (ScriptException exception) when (exception.Diagnostic.Id == "AOT2002")
            {
            }

            try
            {
                _ = AotCmdletRegistry.ParseSource($"Test-Path -IsValid '{childAlphaPath}'");
                throw new InvalidOperationException("Test-Path accepted unsupported IsValid.");
            }
            catch (ScriptException exception) when (exception.Diagnostic.Id == "AOT2002")
            {
            }

            try
            {
                _ = ScriptParser.Parse("Get-Item").Execute(new AotExecutionContext());
                throw new InvalidOperationException("Get-Item accepted a missing generated mandatory Path.");
            }
            catch (ScriptException exception) when (exception.Diagnostic.Id == "AOT6211")
            {
            }

            try
            {
                _ = AotCmdletRegistry.ParseSource($"Get-Item -LP '{childAlphaPath}'");
                throw new InvalidOperationException("Get-Item accepted unsupported LiteralPath.");
            }
            catch (ScriptException exception) when (exception.Diagnostic.Id == "AOT2002")
            {
            }
        }
        finally
        {
            Directory.Delete(childItemFixtureDirectory, recursive: true);
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

    private static void AssertNewGuidPort()
    {
        SourceCmdletMetadata contract = GeneratedCmdletPorts.NewGuid;
        SourceParameterMetadata empty = contract.Parameters.Single(parameter => parameter.Name == "Empty");
        SourceParameterMetadata inputObject = contract.Parameters.Single(parameter => parameter.Name == "InputObject");
        if (!contract.BaseTypeChain.Take(2).SequenceEqual(["PSCmdlet", "Cmdlet"])
            || !contract.OutputTypes.SequenceEqual(["typeof(Guid)"])
            || empty.Shape != AotParameterShape.Switch
            || !empty.ParameterSets.Any(parameterSet => parameterSet.Name == "Empty")
            || inputObject.Shape != AotParameterShape.Scalar
            || !inputObject.ParameterSets.Any(parameterSet => parameterSet.Name == "InputObject" && parameterSet.Position == 0 && parameterSet.PipelineBinding.HasFlag(PipelineBindingSource.ByValue)))
        {
            throw new InvalidOperationException("Generated New-Guid contract regression.");
        }

        NewGuidCmdlet cmdlet = new();
        if (cmdlet.Invoke(new CommandInvocation(cmdlet.Descriptor, new Dictionary<string, string[]>()), new AotExecutionContext())
            .SingleOrDefault() is not TextRecord { Value: var generated }
            || !Guid.TryParse(generated, out Guid generatedGuid)
            || generatedGuid == Guid.Empty
            || generated.Length != 36
            || generated[14] != '7')
        {
            throw new InvalidOperationException("New-Guid did not emit a UUID v7 through the closed prose record path.");
        }

        if (cmdlet.Invoke(new CommandInvocation(cmdlet.Descriptor, new Dictionary<string, string[]>
            {
                ["Empty"] = ["true"],
            }), new AotExecutionContext()).SingleOrDefault() is not TextRecord { Value: "00000000-0000-0000-0000-000000000000" })
        {
            throw new InvalidOperationException("New-Guid -Empty did not emit Guid.Empty.");
        }

        (_, CommandInvocation falseInvocation) = AotCmdletRegistry.ParseSource("New-Guid -Empty:$false");
        if (!falseInvocation.TryGetValues("Empty", out string[] falseValues)
            || !falseValues.SequenceEqual(["false"], StringComparer.OrdinalIgnoreCase)
            || cmdlet.Invoke(falseInvocation, new AotExecutionContext()).SingleOrDefault() is not TextRecord { Value: var falseGuid }
            || !Guid.TryParse(falseGuid, out Guid parsedFalseGuid)
            || parsedFalseGuid == Guid.Empty
            || falseGuid.Length != 36
            || falseGuid[14] != '7')
        {
            throw new InvalidOperationException("An attached Boolean switch value was detached or New-Guid -Empty:$false did not emit UUID v7.");
        }

        if (ScriptParser.Parse("New-Guid").TerminalPresentation is not AotTerminalPresentation.Prose)
        {
            throw new InvalidOperationException("New-Guid did not retain its direct prose terminal contract.");
        }

        AssertNewGuidFailure("New-Guid -Empty:'false'", "new-guid-attached-string.ps1", "AOT1001");
        AssertNewGuidFailure("New-Guid -Empty:$null", "new-guid-attached-null.ps1", "AOT1001");
        AssertNewGuidFailure("New-Guid 00000000-0000-0000-0000-000000000000", "new-guid-positional.ps1", "AOT2005");
        AssertNewGuidFailure("New-Guid -InputObject 00000000-0000-0000-0000-000000000000", "new-guid-input-object.ps1", "AOT2002");
        AssertNewGuidFailure("New-Guid -Empty -InputObject 00000000-0000-0000-0000-000000000000", "new-guid-conflict.ps1", "AOT2002");
    }

    private static void AssertNewGuidFailure(string source, string documentName, string diagnosticId)
    {
        try
        {
            _ = AotExecutionKernel.Compile(source, documentName).Execute(new AotExecutionContext());
            throw new InvalidOperationException($"Expected {diagnosticId} for New-Guid source '{source}'.");
        }
        catch (ScriptException error) when (error.Diagnostic.Id == diagnosticId)
        {
        }
    }

    private static void AssertNewTimeSpanPort()
    {
        SourceCmdletMetadata contract = GeneratedCmdletPorts.NewTimeSpan;
        if (!contract.BaseTypeChain.Take(2).SequenceEqual(["PSCmdlet", "Cmdlet"])
            || !contract.OutputTypes.SequenceEqual(["typeof(TimeSpan)"])
            || contract.Parameters.Single(parameter => parameter.Name == "Start") is not { Aliases: var startAliases }
            || !startAliases.SequenceEqual(["LastWriteTime"])
            || contract.Parameters.Where(parameter => parameter.Name is "Days" or "Hours" or "Minutes" or "Seconds" or "Milliseconds")
                .Any(parameter => parameter.Shape != AotParameterShape.Scalar || !parameter.ParameterSets.Any(set => set.Name == "Time")))
        {
            throw new InvalidOperationException("Generated New-TimeSpan contract regression.");
        }

        NewTimeSpanCmdlet cmdlet = new();
        if (cmdlet.Invoke(new CommandInvocation(cmdlet.Descriptor, new Dictionary<string, string[]>()), new AotExecutionContext())
            .SingleOrDefault() is not TimeSpanRecord { Value: var zero }
            || zero != TimeSpan.Zero)
        {
            throw new InvalidOperationException("New-TimeSpan did not emit the zero TimeSpan without components.");
        }

        (_, CommandInvocation componentInvocation) = AotCmdletRegistry.ParseSource("New-TimeSpan -Days 1 -Hours 2 -Minutes 3 -Seconds 4 -Milliseconds 5");
        if (cmdlet.Invoke(componentInvocation, new AotExecutionContext()).SingleOrDefault() is not TimeSpanRecord value
            || value.Value != new TimeSpan(1, 2, 3, 4, 5)
            || value.TextFor("Value") != "1.02:03:04.0050000")
        {
            throw new InvalidOperationException("New-TimeSpan did not preserve Time components or invariant c-format presentation.");
        }

        AotRecord record = PipelineValueAdapter.ToRecord(value!);
        if (record.TextFor("Value") != "1.02:03:04.0050000"
            || record.TextFor("Ticks") != "937840050000"
            || record.TextFor("TotalMilliseconds") != "93784005.00")
        {
            throw new InvalidOperationException("New-TimeSpan explicit value-plane adapter regression.");
        }

        if (ScriptParser.Parse("New-TimeSpan").TerminalPresentation is not AotTerminalPresentation.Prose
            || ScriptParser.Parse("New-TimeSpan -Seconds 2 | Select-Object TotalSeconds").TerminalPresentation is not AotTerminalPresentation.Table)
        {
            throw new InvalidOperationException("New-TimeSpan terminal presentation contract regression.");
        }

        AssertNewTimeSpanFailure("New-TimeSpan -Seconds nope", "new-timespan-invalid.ps1", "AOT3005", 23);
        AssertNewTimeSpanFailure("New-TimeSpan -Seconds 1.2", "new-timespan-fractional.ps1", "AOT3005", 23);
        AssertNewTimeSpanFailure("New-TimeSpan -Days 2147483647", "new-timespan-overflow.ps1", "AOT3006", 20);
        AssertNewTimeSpanFailure("New-TimeSpan -Start 2020-01-01", "new-timespan-start.ps1", "AOT2002", 14);
        AssertNewTimeSpanFailure("New-TimeSpan -End 2020-01-01", "new-timespan-end.ps1", "AOT2002", 14);
        AssertNewTimeSpanFailure("New-TimeSpan -LastWriteTime 2020-01-01", "new-timespan-last-write-time.ps1", "AOT2002", 14);
        AssertNewTimeSpanFailure("New-TimeSpan 2020-01-01", "new-timespan-positional.ps1", "AOT2005", 14);
        AssertNewTimeSpanFailure("Get-Date | New-TimeSpan", "new-timespan-pipeline.ps1", "AOT1001", 12);
    }

    private static void AssertNewTimeSpanFailure(string source, string documentName, string diagnosticId, int startColumn)
    {
        try
        {
            _ = AotExecutionKernel.Compile(source, documentName).Execute(new AotExecutionContext());
            throw new InvalidOperationException($"Expected {diagnosticId} for New-TimeSpan source '{source}'.");
        }
        catch (ScriptException error) when (error.Diagnostic is { Id: var id, Span: { DocumentName: var document, StartColumn: var column } }
            && id == diagnosticId && document == documentName && column == startColumn)
        {
        }
    }

    private static void AssertStartSleepPort()
    {
        SourceCmdletMetadata contract = GeneratedCmdletPorts.StartSleep;
        SourceParameterMetadata milliseconds = contract.Parameters.Single(parameter => parameter.Name == "Milliseconds");
        if (!contract.BaseTypeChain.Take(2).SequenceEqual(["PSCmdlet", "Cmdlet"])
            || contract.OutputTypes.Count != 0
            || milliseconds is not { TypeName: "int", Shape: AotParameterShape.Scalar, Aliases: var aliases }
            || !aliases.SequenceEqual(["ms"])
            || !milliseconds.ParameterSets.Any(set => set is { Name: "Milliseconds", Mandatory: true }))
        {
            throw new InvalidOperationException("Generated Start-Sleep contract regression.");
        }

        FixtureDelay delay = new();
        StartSleepCmdlet cmdlet = new(delay);
        (_, CommandInvocation aliasInvocation) = AotCmdletRegistry.ParseSource("Start-Sleep -ms 17");
        AotExecutionContext context = new();
        if (cmdlet.Invoke(aliasInvocation, context).Any()
            || delay is not { Calls: 1, LastMilliseconds: 17, LastTokenCanBeCanceled: false }
            || context.Events.Count != 0
            || context.Errors.Count != 0)
        {
            throw new InvalidOperationException("Start-Sleep did not invoke the injected delay exactly once without producing output or stream events.");
        }

        using CancellationTokenSource cancellationSource = new();
        FixtureDelay cancellingDelay = new(cancellationSource);
        StartSleepCmdlet cancellingCmdlet = new(cancellingDelay);
        (_, CommandInvocation cancellationInvocation) = AotCmdletRegistry.ParseSource("Start-Sleep -Milliseconds 5");
        AotExecutionContext cancellationContext = new(cancellationSource.Token);
        AssertCancellation(() => cancellingCmdlet.Invoke(cancellationInvocation, cancellationContext).ToArray());
        if (cancellingDelay is not { Calls: 1, LastMilliseconds: 5, LastTokenCanBeCanceled: true }
            || cancellationContext.Events.Count != 0
            || cancellationContext.Errors.Count != 0)
        {
            throw new InvalidOperationException("Start-Sleep cancellation did not remain host-token-owned and stream-silent.");
        }

        using CancellationTokenSource hostCancellation = new();
        hostCancellation.Cancel();
        if (ScriptRunner.Execute("Start-Sleep -Milliseconds 0", cancellationToken: hostCancellation.Token) != ScriptRunner.CancellationExitCode)
        {
            throw new InvalidOperationException("Start-Sleep did not retain the host cancellation exit contract.");
        }

        if (ScriptParser.Parse("Start-Sleep -Milliseconds 0").TerminalPresentation is not AotTerminalPresentation.Prose)
        {
            throw new InvalidOperationException("Start-Sleep did not retain the no-output prose terminal contract.");
        }

        AssertStartSleepFailure("Start-Sleep", "start-sleep-missing.ps1", "AOT3007", 1);
        AssertStartSleepFailure("Start-Sleep -Milliseconds", "start-sleep-missing-value.ps1", "AOT2004", 1);
        AssertStartSleepFailure("Start-Sleep -Milliseconds nope", "start-sleep-invalid.ps1", "AOT3008", 27);
        AssertStartSleepFailure("Start-Sleep -Milliseconds -1", "start-sleep-negative.ps1", "AOT3009", 27);
        AssertStartSleepFailure("Start-Sleep -Milliseconds 2147483648", "start-sleep-too-large.ps1", "AOT3009", 27);
        AssertStartSleepFailure("Start-Sleep -Milliseconds 1 -ms 2", "start-sleep-duplicate.ps1", "AOT2003", 29);
        AssertStartSleepFailure("Start-Sleep 1", "start-sleep-positional.ps1", "AOT2005", 13);
        AssertStartSleepFailure("Start-Sleep -Seconds 1", "start-sleep-seconds.ps1", "AOT2002", 13);
        AssertStartSleepFailure("Start-Sleep -Duration 00:00:01", "start-sleep-duration.ps1", "AOT2002", 13);
        AssertStartSleepFailure("Start-Sleep -ts 00:00:01", "start-sleep-duration-alias.ps1", "AOT2002", 13);
        AssertStartSleepFailure("sleep -Milliseconds 1", "start-sleep-command-alias.ps1", "AOT2001", 1);
        AssertStartSleepFailure("Get-Date | Start-Sleep -Milliseconds 0", "start-sleep-pipeline.ps1", "AOT1001", 12);
    }

    private static void AssertStartSleepFailure(string source, string documentName, string diagnosticId, int startColumn)
    {
        try
        {
            _ = AotExecutionKernel.Compile(source, documentName).Execute(new AotExecutionContext());
            throw new InvalidOperationException($"Expected {diagnosticId} for Start-Sleep source '{source}'.");
        }
        catch (ScriptException error) when (error.Diagnostic is { Id: var id, Span: { DocumentName: var document, StartColumn: var column } }
            && id == diagnosticId && document == documentName && column == startColumn)
        {
        }
    }

    private static void AssertHostSubstrate()
    {
        AotHostSubstrate local = AotHostSubstrate.CreateLocal();
        if (local.PhysicalFiles is not SystemPhysicalFileResolver
            || local.Processes is not SystemProcessCatalog
            || local.Clock is not SystemClock
            || local.Delay is not CancellationTokenDelay
            || local.Culture is not SystemHostCulture
            || local.Cultures is not SystemCultureCatalog
            || local.TimeZones is not SystemTimeZoneCatalog
            || local.Configuration is not ProcessAotHostConfiguration
            || local.Platform is not SystemAotHostPlatform
            || local.Credentials.Unavailable is not { Capability: AotUnavailableCapability.Credentials, DiagnosticId: "AOT6101" }
            || local.Network.Unavailable is not { Capability: AotUnavailableCapability.Network, DiagnosticId: "AOT6102" })
        {
            throw new InvalidOperationException("AOT host substrate local composition regression.");
        }

        IAotHostConfiguration fixtureConfiguration = new FixtureHostConfiguration(
            (AotHostConfigurationKey.Term, "xterm-256color"),
            (AotHostConfigurationKey.NoColor, "1"),
            (AotHostConfigurationKey.WindowsTerminalSession, "fixture"));
        IAotHostPlatform fixturePlatform = new FixtureHostPlatform(new AotHostPlatformSnapshot(AotHostOperatingSystem.Windows, System.Runtime.InteropServices.Architecture.Arm64));
        AotTerminalInfo terminal = new SystemTerminalInfoSource(fixtureConfiguration, fixturePlatform).Capture();
        if (terminal.Term != "xterm-256color" || terminal.NoColor != "1" || !terminal.IsWindows || !terminal.HasWindowsAnsiHost)
        {
            throw new InvalidOperationException("AOT host terminal capability injection regression.");
        }

        AotTerminalInfo conservativePlatformFallback = new SystemTerminalInfoSource(fixtureConfiguration, new ThrowingHostPlatform()).Capture();
        AotTerminalInfo conservativeConfigurationFallback = new SystemTerminalInfoSource(new ThrowingHostConfiguration(), fixturePlatform).Capture();
        if (conservativePlatformFallback is not { IsErrorRedirected: true, IsWindows: false, HasWindowsAnsiHost: false, Term: null, NoColor: null }
            || conservativeConfigurationFallback is not { IsErrorRedirected: true, IsWindows: false, HasWindowsAnsiHost: false, Term: null, NoColor: null })
        {
            throw new InvalidOperationException("AOT host terminal failure fallback regression.");
        }

        AotExecutionContext ownerErrors = new();
        IProcessOwnerReader unavailableOwner = new UnixPsProcessOwnerReader(new FixtureHostPlatform(new AotHostPlatformSnapshot(AotHostOperatingSystem.Windows, System.Runtime.InteropServices.Architecture.X64)));
        if (unavailableOwner.TryGetOwner(1, ownerErrors) is not null || ownerErrors.Errors.SingleOrDefault()?.Id != "CouldNotRetrieveUserName")
        {
            throw new InvalidOperationException("AOT host process-ownership platform boundary regression.");
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
                Id: "AOT6406",
                Category: AotDiagnosticCategory.Binding,
                Span: { StartLine: 1, StartColumn: 28 },
                Help: not null,
            })
        {
            string rendered = AotDiagnosticRenderer.Render(error.Diagnostic, source, "fixture.ps1", useAnsi: false);
            const string expected = """
error[AOT6406]: Where-Object ScriptBlock binding is outside the Native AOT static record subset.
  --> fixture.ps1:1:28
   |
1 | Get-Process | Where-Object { $_.CPU -gt 10 }
   |                            ^^^^^^^^^^^^^^^^^ unsupported static stage form
   = help: Use the admitted direct Property/Value or Property field form; script blocks are not evaluated.
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
                Id: "AOT6403",
                Category: AotDiagnosticCategory.Binding,
                Span: { DocumentName: "predicate.ps1", StartColumn: 36 },
            })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                "Get-Process | Where-Object CPU -gt NaN",
                """
error[AOT6403]: Where-Object requires a finite numeric Value in this Native AOT subset.
  --> predicate.ps1:1:36
   |
1 | Get-Process | Where-Object CPU -gt NaN
   |                                    ^^^ invalid numeric predicate
   = help: Supply an integer, decimal, finite floating-point literal, or a reviewed closed-scope numeric value.
""");
        }

        try
        {
            _ = AotExecutionKernel.Compile("Get-Process | Where-Object CPU -ft 2", "operator.ps1");
            throw new InvalidOperationException("The execution kernel accepted an unsupported Where-Object comparison.");
        }
        catch (ScriptException error) when (error.Diagnostic is
            {
                Id: "AOT6406",
                Category: AotDiagnosticCategory.Binding,
                Span: { DocumentName: "operator.ps1", StartColumn: 32 },
            })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                "Get-Process | Where-Object CPU -ft 2",
                """
error[AOT6406]: Where-Object parameter '-ft' is outside the Native AOT static numeric subset.
  --> operator.ps1:1:32
   |
1 | Get-Process | Where-Object CPU -ft 2
   |                                ^^^ unsupported static stage parameter
   = help: Use Property, Value, and one admitted case-insensitive numeric operator.
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
                Id: "AOT6402",
                Category: AotDiagnosticCategory.Runtime,
                Span: { DocumentName: "property.ps1", StartColumn: 28 },
            })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                "Get-Process | Where-Object BadProperty -gt 2",
                """
error[AOT6402]: Where-Object Property 'BadProperty' is not a numeric field on this AOT record batch.
  --> property.ps1:1:28
   |
1 | Get-Process | Where-Object BadProperty -gt 2
   |                            ^^^^^^^^^^^ unsupported numeric record field
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
                Id: "AOT6404",
                Category: AotDiagnosticCategory.Runtime,
                Span: { DocumentName: "projection.ps1", StartColumn: 38 },
            })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                "Get-TimeZone -Id UTC | Select-Object NotAnAotField",
                """
error[AOT6404]: Select-Object requested a column not present on this pipeline value.
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
                Id: "AOT6404",
                Category: AotDiagnosticCategory.Binding,
                Span: { DocumentName: "empty-projection.ps1", StartColumn: 15 },
            })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                "Get-Process | Select-Object",
                """
error[AOT6404]: Select-Object requires one or more direct Property fields.
  --> empty-projection.ps1:1:15
   |
1 | Get-Process | Select-Object
   |               ^^^^^^^^^^^^^ missing projection field
   = help: Supply direct literal field names, for example 'Select-Object Name, Id'.
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

        // Get-Item deliberately preserves the source-mandatory Path metadata,
        // but this noninteractive static host does not invent PowerShell's
        // mandatory-parameter prompt. Keep the intentional AOT6211 variance
        // source-spanned and snapshot its user-facing terminal contract.
        try
        {
            _ = AotExecutionKernel.Compile("Get-Item", "getitem-missing-path.ps1")
                .Execute(new AotExecutionContext());
            throw new InvalidOperationException("Get-Item accepted a missing mandatory Path.");
        }
        catch (ScriptException error) when (error.Diagnostic is
            {
                Id: "AOT6211",
                Category: AotDiagnosticCategory.Runtime,
                Span: { DocumentName: "getitem-missing-path.ps1", StartLine: 1, StartColumn: 1, EndLine: 1, EndColumn: 9 },
            })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                "Get-Item",
                """
error[AOT6211]: Get-Item requires a direct physical -Path value in the current Native AOT slice.
  --> getitem-missing-path.ps1:1:1
   |
1 | Get-Item
   | ^^^^^^^^ required direct path missing
   = help: Supply one existing direct physical file or directory path.
""");
        }

        // Resolve-Path preserves the source-mandatory Path metadata but does
        // not synthesize the upstream host's interactive prompt.  Keep that
        // separate from its catalog-owned empty-string AOT6213 diagnostic.
        try
        {
            _ = AotExecutionKernel.Compile("Resolve-Path", "resolvepath-missing-path.ps1")
                .Execute(new AotExecutionContext());
            throw new InvalidOperationException("Resolve-Path accepted a missing mandatory Path.");
        }
        catch (ScriptException error) when (error.Diagnostic is
            {
                Id: "AOT6211",
                Category: AotDiagnosticCategory.Runtime,
                Span: { DocumentName: "resolvepath-missing-path.ps1", StartLine: 1, StartColumn: 1, EndLine: 1, EndColumn: 13 },
            })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                "Resolve-Path",
                """
error[AOT6211]: Resolve-Path requires a direct physical -Path value in the current Native AOT slice.
  --> resolvepath-missing-path.ps1:1:1
   |
1 | Resolve-Path
   | ^^^^^^^^^^^^ required direct path missing
   = help: Supply one existing direct physical file or directory path.
""");
        }

        // Test-Path has the same source-mandatory metadata. The static host
        // deliberately declines an interactive prompt, so retain the exact
        // command-specific AOT6211 rendering as a compatibility variance.
        try
        {
            _ = AotExecutionKernel.Compile("Test-Path", "testpath-missing-path.ps1")
                .Execute(new AotExecutionContext());
            throw new InvalidOperationException("Test-Path accepted a missing mandatory Path.");
        }
        catch (ScriptException error) when (error.Diagnostic is
            {
                Id: "AOT6211",
                Category: AotDiagnosticCategory.Runtime,
                Span: { DocumentName: "testpath-missing-path.ps1", StartLine: 1, StartColumn: 1, EndLine: 1, EndColumn: 10 },
            })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                "Test-Path",
                """
error[AOT6211]: Test-Path requires a direct physical -Path value in the current Native AOT slice.
  --> testpath-missing-path.ps1:1:1
   |
1 | Test-Path
   | ^^^^^^^^^ required direct path missing
   = help: Supply one direct physical file or directory path.
""");
        }

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

    private static void AssertRuntimeEventContract()
    {
        const string source = "Get-Verb -Verb Add; Get-TimeZone -Id AotRuntimeEventMissingZone; Get-Verb -Verb Get";
        AotExecutionContext context = new();
        List<AotRuntimeEvent> observed = [];
        using IDisposable subscription = context.Subscribe(observed.Add);
        AotExecutionResult result = AotExecutionKernel.Compile(source, "runtime-events.ps1").Execute(context);

        if (result.Outputs.Count != 2
            || context.Events.Count != 3
            || !context.Events.Select(static runtimeEvent => runtimeEvent.Kind).SequenceEqual(
                [AotRuntimeEventKind.Success, AotRuntimeEventKind.Error, AotRuntimeEventKind.Success])
            || !observed.SequenceEqual(context.Events)
            || !context.Events.Select(static runtimeEvent => runtimeEvent.Sequence).SequenceEqual([1L, 2L, 3L])
            || context.Events[1] is not
            {
                Diagnostic: { Id: "TimeZoneNotFound", Span: { DocumentName: "runtime-events.ps1" } },
                Invocation: { CommandName: "Get-TimeZone", PipelinePosition: 0, PipelineLength: 1 },
            }
            || context.Errors.SingleOrDefault()?.Diagnostic != context.Events[1].Diagnostic)
        {
            throw new InvalidOperationException("The Runtime Core event bridge did not preserve one ordered typed output/error transcript.");
        }

        // A source command can write an error before its completed table
        // segment is available. The event stream reports that true runtime
        // ordering rather than inventing per-record streaming semantics.
        AotExecutionContext mixedContext = new();
        using IDisposable mixedSubscription = mixedContext.Subscribe(static _ => { });
        AotExecutionOutput output = AotExecutionOutput.FromTypedRows(
            mixedContext,
            [new VerbRecord("Get", "g", "Common", "fixture")],
            ["Verb"]);
        mixedContext.WriteNonTerminatingError(AotDiagnostics.Runtime("FixtureError", "fixture error"));
        mixedContext.WriteOutput(output);
        if (!mixedContext.Events.Select(static runtimeEvent => runtimeEvent.Kind).SequenceEqual(
                [AotRuntimeEventKind.Error, AotRuntimeEventKind.Success])
            || mixedContext.Events[0].Diagnostic?.Id != "FixtureError"
            || mixedContext.Events[1].Output != output)
        {
            throw new InvalidOperationException("The Runtime Core event contract did not preserve an error before a completed output segment.");
        }

        AotSourceSpan secondStageSpan = new("input-stage.ps1", 14, 31, 1, 15, 1, 32);
        GetProcessCmdlet inputStage = new(new FixtureProcessCatalog([new ProcessRecord("present", 7, 0, 0)], emitUserError: true));
        CommandInvocation inputStageInvocation = new(inputStage.Descriptor, new Dictionary<string, string[]>
        {
            ["IncludeUserName"] = ["true"],
        }, secondStageSpan);
        AotExecutionContext inputStageContext = new();
        _ = ((IAotPipelineInputCmdlet)inputStage).InvokeWithPipelineInput(
            inputStageInvocation,
            [new ProcessRecord("present", 7, 0, 0)],
            inputStageContext,
            pipelinePosition: 1,
            pipelineLength: 2).ToArray();
        if (inputStageContext.Events.SingleOrDefault() is not
            {
                Kind: AotRuntimeEventKind.Error,
                Diagnostic: { Id: "FixtureInputUserError", Span: var eventSpan },
                Invocation: { CommandName: "Get-Process", PipelinePosition: 1, PipelineLength: 2, SourceSpan: var frameSpan },
            }
            || eventSpan != secondStageSpan
            || frameSpan != secondStageSpan)
        {
            throw new InvalidOperationException("The special-case input cmdlet did not retain its own invocation frame.");
        }

        AotExecutionOutput emptyOutput = new(new AotRecordBatch([]), new AotRecordShape(["Value"]));
        AssertArgumentException(() => AotRuntimeEvent.Success(1, emptyOutput));
        AssertArgumentException(() => AotRuntimeEvent.Stream(1, AotRuntimeEventKind.Success, null, "not-a-stream"));
        AssertArgumentException(() => AotRuntimeEvent.Stream(1, AotRuntimeEventKind.Verbose, null, " "));
        AssertArgumentException(() => AotRuntimeEvent.Error(0, null, AotDiagnostics.Runtime("Fixture", "fixture")));
    }

    private static void AssertStaticCommonParameterPolicy()
    {
        const string missingZone = "AotCommonParameterMissingZone";

        // Common parameters are extracted from upstream AST nodes before the
        // sole generated-metadata cmdlet binder. Both the canonical spelling
        // and its reviewed -ea alias preserve Continue's normal event path.
        AotExecutionContext continueContext = new();
        _ = AotExecutionKernel.Compile($"Get-TimeZone -Id {missingZone} -ErrorAction Continue", "common-continue.ps1")
            .Execute(continueContext);
        if (continueContext.Errors.Count != 1
            || continueContext.Events.Count(static runtimeEvent => runtimeEvent.Kind == AotRuntimeEventKind.Error) != 1)
        {
            throw new InvalidOperationException("-ErrorAction Continue did not retain the typed non-terminating error event.");
        }

        AotExecutionContext aliasContext = new();
        _ = AotExecutionKernel.Compile($"Get-TimeZone -Id {missingZone} -ea Continue", "common-ea.ps1")
            .Execute(aliasContext);
        if (aliasContext.Errors.Count != 1
            || aliasContext.Events.Count(static runtimeEvent => runtimeEvent.Kind == AotRuntimeEventKind.Error) != 1)
        {
            throw new InvalidOperationException("The -ea common-parameter alias did not use the typed Continue policy.");
        }

        AotExecutionContext silentContext = new();
        _ = AotExecutionKernel.Compile($"Get-TimeZone -Id {missingZone} -ErrorAction SilentlyContinue", "common-silent.ps1")
            .Execute(silentContext);
        if (silentContext.Errors.Count != 1
            || silentContext.Events.Any(static runtimeEvent => runtimeEvent.Kind == AotRuntimeEventKind.Error))
        {
            throw new InvalidOperationException("-ErrorAction SilentlyContinue did not retain the record while suppressing terminal error projection.");
        }

        AotExecutionContext stopContext = new();
        try
        {
            _ = AotExecutionKernel.Compile($"Get-TimeZone -Id {missingZone} -ErrorAction Stop", "common-stop.ps1")
                .Execute(stopContext);
            throw new InvalidOperationException("-ErrorAction Stop did not terminate the current script.");
        }
        catch (AotPublishedTerminatingException error) when (error.Diagnostic.Id == "TimeZoneNotFound")
        {
            if (stopContext.Errors.Count != 1
                || !stopContext.Events.Select(static runtimeEvent => runtimeEvent.Kind).SequenceEqual([AotRuntimeEventKind.TerminatingError])
                || stopContext.Events.Single().Diagnostic != error.Diagnostic)
            {
                throw new InvalidOperationException("-ErrorAction Stop did not publish the exact typed terminating context event before throwing it.");
            }
        }

        TextWriter originalStopOutput = Console.Out;
        TextWriter originalStopError = Console.Error;
        StringWriter stopOutput = new();
        StringWriter stopError = new();
        try
        {
            Console.SetOut(stopOutput);
            Console.SetError(stopError);
            if (ScriptRunner.Execute($"Get-TimeZone -Id {missingZone} -ea Stop", colorMode: AotColorMode.Never) != 2)
            {
                throw new InvalidOperationException("-ErrorAction Stop did not preserve the terminating host exit path.");
            }
        }
        finally
        {
            Console.SetOut(originalStopOutput);
            Console.SetError(originalStopError);
        }

        const string terminatingHeading = "error[TimeZoneNotFound]";
        int firstTerminatingHeading = stopError.ToString().IndexOf(terminatingHeading, StringComparison.Ordinal);
        if (firstTerminatingHeading < 0
            || firstTerminatingHeading != stopError.ToString().LastIndexOf(terminatingHeading, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A typed terminating event was rendered zero or multiple times by the static host.");
        }

        // The attached AST argument is essential: a bare switch enables a
        // side stream, while `-Verbose:$false` / `-Debug:$false` disables it.
        (_, CommandInvocation enabledInvocation) = AotCmdletRegistry.ParseSource("Get-Verb -Verbose -Debug");
        AotExecutionContext enabledContext = new();
        using (enabledContext.EnterInvocation(enabledInvocation))
        {
            enabledContext.WriteVerbose("fixture verbose");
            enabledContext.WriteDebug("fixture debug");
        }

        if (!enabledContext.Events.Select(static runtimeEvent => runtimeEvent.Kind)
                .SequenceEqual([AotRuntimeEventKind.Verbose, AotRuntimeEventKind.Debug])
            || enabledContext.Events[0] is not { Message: "fixture verbose", Invocation: { CommandName: "Get-Verb" } }
            || enabledContext.Events[1] is not { Message: "fixture debug", Invocation: { CommandName: "Get-Verb" } })
        {
            throw new InvalidOperationException("Bare -Verbose/-Debug did not enable the closed typed side streams.");
        }

        (_, CommandInvocation disabledInvocation) = AotCmdletRegistry.ParseSource("Get-Verb -Verbose:$false -Debug:$false");
        AotExecutionContext disabledContext = new();
        using (disabledContext.EnterInvocation(disabledInvocation))
        {
            disabledContext.WriteVerbose("must remain hidden");
            disabledContext.WriteDebug("must remain hidden");
        }

        if (disabledContext.Events.Count != 0)
        {
            throw new InvalidOperationException("Attached $false common-switch values were detached from their upstream parameter ASTs.");
        }

        (_, CommandInvocation detachedInvocation) = AotCmdletRegistry.ParseSource("Get-Verb -Verbose $false");
        if (!detachedInvocation.CommonParameters.Verbose
            || !detachedInvocation.TryGetValues("Verb", out string[] detachedValues)
            || !detachedValues.SequenceEqual(["False"], StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A bare -Verbose incorrectly consumed the following upstream command element as an attached switch value.");
        }

        AssertCommonParameterFailure("Get-Verb -ErrorAction", "common-missing-error-action.ps1", "AOT2004");
        AssertCommonParameterFailure("Get-Verb -ErrorAction Ignore", "common-unsupported-error-action.ps1", "AOT1001");
        AssertCommonParameterFailure("Get-Verb -WarningAction SilentlyContinue", "common-unsupported-common.ps1", "AOT1001");
        AssertCommonParameterFailure("Get-Verb -Verbose:'false'", "common-string-switch.ps1", "AOT1001");
        AssertCommonParameterFailure("Get-ChildItem -ErrorAction Ignore", "common-get-childitem.ps1", "AOT1001");
        AssertCommonParameterFailure("No-SuchCommand -ErrorAction Ignore", "common-unknown.ps1", "AOT2001");

        StringWriter stdout = new();
        StringWriter stderr = new();
        AotTerminalEventProjector projector = new(
            "",
            "common-projector.ps1",
            new AotDiagnosticRenderOptions(UseAnsi: false),
            stdout,
            stderr,
            new AotExecutionContext());
        projector.Project(AotRuntimeEvent.Stream(1, AotRuntimeEventKind.Verbose, null, "fixture verbose\n\u001b[2J"));
        projector.Project(AotRuntimeEvent.Stream(2, AotRuntimeEventKind.Debug, null, "fixture debug"));
        if (stdout.ToString().Length != 0
            || !stderr.ToString().Equals("VERBOSE: fixture verbose\\n\\u001B[2J" + Environment.NewLine + "DEBUG: fixture debug" + Environment.NewLine, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The static terminal projector did not sanitize admitted side streams or keep them out of success output.");
        }

        using CancellationTokenSource cancelledSideStream = new();
        cancelledSideStream.Cancel();
        AotExecutionContext cancelledContext = new(cancelledSideStream.Token);
        CommandInvocation cancelledInvocation = new(
            LifecycleFixtureCmdlet.DescriptorContract,
            new Dictionary<string, string[]>(),
            commonParameters: new AotCommonParameters(AotErrorAction.Continue, Verbose: true, Debug: false));
        using (cancelledContext.EnterInvocation(cancelledInvocation))
        {
            AssertCancellation(() => cancelledContext.WriteVerbose("must not publish"));
        }
        if (cancelledContext.Events.Count != 0)
        {
            throw new InvalidOperationException("Cancellation published a side-stream event.");
        }

        AotExecutionContext emptyContext = new();
        emptyContext.WriteOutput(new AotExecutionOutput(new AotRecordBatch([]), new AotRecordShape(["Value"])));
        if (emptyContext.Events.Count != 0)
        {
            throw new InvalidOperationException("A zero-row output batch became a blank success segment.");
        }
    }

    private static void AssertCommonParameterFailure(string source, string documentName, string diagnosticId)
    {
        try
        {
            _ = AotExecutionKernel.Compile(source, documentName).Execute(new AotExecutionContext());
            throw new InvalidOperationException($"Expected {diagnosticId} for common-parameter source '{source}'.");
        }
        catch (ScriptException error) when (error.Diagnostic.Id == diagnosticId)
        {
        }
    }

    private static void AssertArgumentException(Action action)
    {
        try
        {
            action();
            throw new InvalidOperationException("Expected a runtime-event factory invariant failure.");
        }
        catch (ArgumentException)
        {
        }
    }

    private static void AssertCancellationLifecycle()
    {
        CommandInvocation invocation = new(
            LifecycleFixtureCmdlet.DescriptorContract,
            new Dictionary<string, string[]>(),
            new AotSourceSpan("cancellation.ps1", 0, 18, 1, 1, 1, 19));

        using CancellationTokenSource preCancelledSource = new();
        preCancelledSource.Cancel();
        LifecycleFixtureCmdlet preCancelled = new();
        AotExecutionContext preCancelledContext = new(preCancelledSource.Token);
        AssertCancellation(() => preCancelled.Invoke(invocation, preCancelledContext).ToArray());
        if (preCancelled.BeginCalls != 0
            || preCancelled.ProcessCalls != 0
            || preCancelled.EndCalls != 0
            || preCancelled.StopCalls != 0
            || preCancelledContext.Events.Count != 0
            || preCancelledContext.Errors.Count != 0)
        {
            throw new InvalidOperationException("A pre-cancelled execution context started a cmdlet lifecycle or emitted a runtime record.");
        }

        LifecycleFixtureCmdlet completed = new();
        IReadOnlyList<IPipelineRecord> completedRows = completed.Invoke(invocation, new AotExecutionContext()).ToArray();
        if (completedRows.Count != 1
            || completed.BeginCalls != 1
            || completed.ProcessCalls != 1
            || completed.EndCalls != 1
            || completed.StopCalls != 0)
        {
            throw new InvalidOperationException("The normal AOT cmdlet lifecycle regressed while adding cancellation.");
        }

        using CancellationTokenSource processCancellation = new();
        LifecycleFixtureCmdlet cancelledDuringProcess = new(processCancellation, cancelDuringProcess: true);
        AotExecutionContext processContext = new(processCancellation.Token);
        // This represents a previously completed output segment. Cancellation
        // does not erase transcript history or synthesize an error record.
        processContext.WriteOutput(AotExecutionOutput.FromTypedRows(
            processContext,
            [new TextRecord("completed-before-cancellation")],
            ["Value"]));
        AssertCancellation(() => cancelledDuringProcess.Invoke(invocation, processContext).ToArray());
        if (cancelledDuringProcess.BeginCalls != 1
            || cancelledDuringProcess.ProcessCalls != 1
            || cancelledDuringProcess.EndCalls != 0
            || cancelledDuringProcess.StopCalls != 1
            || processContext.Errors.Count != 0
            || processContext.Events.Count != 1
            || processContext.Events.Single().Kind != AotRuntimeEventKind.Success)
        {
            throw new InvalidOperationException("Cancellation did not stop one active cmdlet exactly once while preserving prior runtime events.");
        }

        using CancellationTokenSource stopFailureCancellation = new();
        LifecycleFixtureCmdlet stopFailure = new(stopFailureCancellation, cancelDuringProcess: true, stopThrows: true);
        AssertCancellation(() => stopFailure.Invoke(invocation, new AotExecutionContext(stopFailureCancellation.Token)).ToArray());
        if (stopFailure.StopCalls != 1)
        {
            throw new InvalidOperationException("A StopProcessing failure replaced or repeated the cancellation path.");
        }

        using CancellationTokenSource inputCancellation = new();
        LifecycleFixtureInputCmdlet inputStage = new(inputCancellation);
        CommandInvocation inputInvocation = new(
            inputStage.Descriptor,
            new Dictionary<string, string[]>(),
            new AotSourceSpan("typed-cancellation.ps1", 14, 33, 1, 15, 1, 34));
        AotExecutionContext inputContext = new(inputCancellation.Token);
        AssertCancellation(() => ((IAotPipelineInputCmdlet)inputStage).InvokeWithPipelineInput(
            inputInvocation,
            [new TextRecord("input")],
            inputContext,
            pipelinePosition: 1,
            pipelineLength: 2).ToArray());
        if (inputStage.BeginCalls != 1
            || inputStage.ProcessCalls != 1
            || inputStage.EndCalls != 0
            || inputStage.StopCalls != 1
            || inputContext.Errors.Count != 0
            || inputContext.Events.Count != 0)
        {
            throw new InvalidOperationException("Typed input-stage cancellation did not retain the shared lifecycle contract.");
        }

        using CancellationTokenSource hostCancellation = new();
        hostCancellation.Cancel();
        if (ScriptRunner.Execute("Get-Verb", cancellationToken: hostCancellation.Token) != ScriptRunner.CancellationExitCode)
        {
            throw new InvalidOperationException("The script host did not return the stable cancellation exit code.");
        }
    }

    private static void AssertCancellation(Action action)
    {
        try
        {
            action();
        }
        catch (OperationCanceledException)
        {
            return;
        }

        throw new InvalidOperationException("Expected cooperative cancellation to stop execution.");
    }

    private static void AssertTypedStageComposition()
    {
        // The upstream AST is still the only syntax authority. This succeeds
        // only because the second command is a registered typed-input port;
        // Where/Select remain the existing closed value-plane transforms.
        AotExecutionResult composed = AotExecutionKernel.Compile(
                "Get-Process | Get-Process | Select-Object Name, Id",
                "typed-stage.ps1")
            .Execute(new AotExecutionContext());
        if (composed.Outputs.SingleOrDefault() is not { Columns: var columns, Rows: var rows }
            || !columns.SequenceEqual(["Name", "Id"])
            || rows.Count == 0
            || rows.Any(static row => row.Fields.Count != 2))
        {
            throw new InvalidOperationException("A registered typed input stage did not compose with the existing source and projection stages.");
        }

        try
        {
            _ = AotExecutionKernel.Compile("Get-Process -InputObject not-a-process", "direct-input.ps1")
                .Execute(new AotExecutionContext());
            throw new InvalidOperationException("Get-Process accepted direct InputObject text outside the typed stage boundary.");
        }
        catch (ScriptException error) when (error.Diagnostic.Id == "AOT2002" && error.Diagnostic.Span is { DocumentName: "direct-input.ps1" })
        {
        }

        try
        {
            _ = AotExecutionKernel.Compile("Get-Uptime | Get-Process", "input-shape.ps1")
                .Execute(new AotExecutionContext());
            throw new InvalidOperationException("A static input stage accepted an incompatible AOT record shape.");
        }
        catch (ScriptException error) when (error.Diagnostic.Id == "AOT4010" && error.Diagnostic.Span is { DocumentName: "input-shape.ps1" })
        {
        }

        foreach (string unsupported in new[]
        {
            "Get-Process | Get-Uptime",
            "Get-Process | Where-Object CPU -gt 0 | Get-Process",
            "Get-Process | Get-Process | Get-Process",
        })
        {
            try
            {
                _ = AotExecutionKernel.Compile(unsupported, "invalid-typed-stage.ps1");
                throw new InvalidOperationException($"The lowerer accepted an out-of-contract typed stage pipeline: {unsupported}");
            }
            catch (ScriptException error) when (error.Diagnostic.Id == "AOT1001")
            {
            }
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
            || !verbRows.Select(row => row.TextFor("Verb")).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(["Get", "Set"]))
        {
            throw new InvalidOperationException("A variable list did not expand into values for the existing generated-metadata binder.");
        }

        AotExecutionContext singleQuotedContext = new();
        AotScope singleQuotedScope = new();
        AotExecutionResult singleQuotedResult = AotExecutionKernel.Compile(
                "$literal = '$notInterpolation'; Get-Process -Name $literal",
                "single-quoted-variable.ps1")
            .Execute(singleQuotedContext, singleQuotedScope);
        if (!singleQuotedScope.TryGet("literal", out AotValue literalValue)
            || !literalValue.TryGetString(out string? literalText)
            || !string.Equals(literalText, "$notInterpolation", StringComparison.Ordinal)
            || singleQuotedResult.Outputs.Count > 1)
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
        catch (ScriptException error) when (error.Diagnostic is { Id: "AOT6403", Category: AotDiagnosticCategory.Binding, Span: { DocumentName: "predicate-variable.ps1", StartColumn: 57 } })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                "$threshold = 'many'; Get-Process | Where-Object CPU -gt $threshold",
                """
error[AOT6403]: Where-Object requires a finite numeric Value in this Native AOT subset.
  --> predicate-variable.ps1:1:57
   |
1 | $threshold = 'many'; Get-Process | Where-Object CPU -gt $threshold
   |                                                         ^^^^^^^^^^ invalid numeric predicate
   = help: Supply an integer, decimal, finite floating-point literal, or a reviewed closed-scope numeric value.
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

    private static void AssertNamedLocalFunctions()
    {
        const string basicSource = """
function Get-CommonVerb($group) {
    Get-Verb -Group $group | Select-Object Verb
}
Get-CommonVerb Common
""";
        AotExecutionResult basicResult = AotExecutionKernel.Compile(basicSource, "function-basic.ps1")
            .Execute(new AotExecutionContext());
        if (basicResult.Outputs.SingleOrDefault() is not { Columns: var basicColumns, Rows: var basicRows }
            || !basicColumns.SequenceEqual(["Verb"])
            || !basicRows.Any(row => row.TextFor("Verb") == "Add"))
        {
            throw new InvalidOperationException("A declared local function did not lower, bind its closed positional parameter, and forward body output.");
        }

        const string scopeSource = """
$group = 'Common'
$outer = 'Other'
function Read-Group($ignored) {
    $outer = 'Security'
    Get-Verb -Group $group | Select-Object Verb
}
Read-Group $null
""";
        AotScope scope = new();
        AotExecutionResult scopeResult = AotExecutionKernel.Compile(scopeSource, "function-scope.ps1")
            .Execute(new AotExecutionContext(), scope);
        if (scopeResult.Outputs.Count != 1
            || !scope.TryGet("outer", out AotValue outer)
            || !outer.TryGetString(out string? outerText)
            || outerText != "Other")
        {
            throw new InvalidOperationException("A local function did not read its caller scope or isolate its assignment to a child scope.");
        }

        AotScope session = new();
        _ = AotExecutionKernel.Compile(
                "function Session-Verb($group) { Get-Verb -Group $group | Select-Object Verb }",
                "function-repl-definition.ps1")
            .Execute(new AotExecutionContext(), session);
        AotExecutionResult sessionResult = AotExecutionKernel.Compile("session-verb Common", "function-repl-call.ps1")
            .Execute(new AotExecutionContext(), session);
        if (sessionResult.Outputs.Count != 1 || !sessionResult.Outputs[0].Rows.Any(row => row.TextFor("Verb") == "Add"))
        {
            throw new InvalidOperationException("An explicitly owned REPL scope did not retain a local function between submissions.");
        }

        AotExecutionResult shadowResult = AotExecutionKernel.Compile(
                "function Get-Verb() { Get-Date | Select-Object DateTime }; get-verb",
                "function-shadow.ps1")
            .Execute(new AotExecutionContext());
        if (shadowResult.Outputs.SingleOrDefault() is not { Columns: var shadowColumns }
            || !shadowColumns.SequenceEqual(["DateTime"]))
        {
            throw new InvalidOperationException("A case-insensitive local function did not shadow the native command registry.");
        }

        AotExecutionResult orderedOutput = AotExecutionKernel.Compile(
                "function Two-Outputs() { Get-Verb -Verb Add | Select-Object Verb; Get-Date | Select-Object DateTime }; Two-Outputs; Get-Verb -Verb Get | Select-Object Verb",
                "function-output-order.ps1")
            .Execute(new AotExecutionContext());
        if (orderedOutput.Outputs.Count != 3
            || !orderedOutput.Outputs[0].Columns.SequenceEqual(["Verb"])
            || !orderedOutput.Outputs[1].Columns.SequenceEqual(["DateTime"])
            || !orderedOutput.Outputs[2].Columns.SequenceEqual(["Verb"]))
        {
            throw new InvalidOperationException("Local-function body output did not retain the existing ordered segment sink.");
        }

        AssertFunctionFailure("Before-Definition Common; function Before-Definition($group) { Get-Verb -Group $group }", "function-order.ps1", "AOT2001");
        AssertFunctionFailure("function Needs-One($group) { Get-Verb -Group $group }; Needs-One", "function-arity.ps1", "AOT5008");
        AssertFunctionFailure("function Loop() { Loop }; Loop", "function-recursion.ps1", "AOT5007");

        foreach (string unsupported in new[]
        {
            "if ($true) { function Nested() { Get-Verb -Verb Add } }",
            "function With-BodyParam() { param($group) Get-Verb -Group $group }",
            "function With-Type([string]$group) { Get-Verb -Group $group }",
            "filter Stream-Verb { Get-Verb -Verb Add }",
        })
        {
            try
            {
                _ = AotExecutionKernel.Compile(unsupported, "function-unsupported.ps1");
                throw new InvalidOperationException($"The kernel accepted deferred local-function syntax '{unsupported}'.");
            }
            catch (ScriptException error) when (error.Diagnostic.Id == "AOT1001")
            {
                // The parser may see rich function syntax, but the native plan
                // is intentionally limited to the documented local subset.
            }
        }
    }

    private static void AssertFunctionFailure(string source, string documentName, string diagnosticId)
    {
        try
        {
            _ = AotExecutionKernel.Compile(source, documentName).Execute(new AotExecutionContext());
            throw new InvalidOperationException($"The local-function failure '{diagnosticId}' did not occur for '{source}'.");
        }
        catch (ScriptException error) when (error.Diagnostic.Id == diagnosticId)
        {
            // The test names the stable boundary without accepting a raw host exception.
        }
    }

    private static void AssertFunctionReturnControlFlow()
    {
        AotExecutionResult directReturn = AotExecutionKernel.Compile(
                "function Early() { Get-Verb -Verb Add | Select-Object Verb; return; Get-Date | Select-Object DateTime }; Early; Get-Verb -Verb Get | Select-Object Verb",
                "return-direct.ps1")
            .Execute(new AotExecutionContext());
        if (directReturn.Outputs.Count != 2
            || !directReturn.Outputs[0].Columns.SequenceEqual(["Verb"])
            || !directReturn.Outputs[1].Columns.SequenceEqual(["Verb"]))
        {
            throw new InvalidOperationException("A bare local-function return did not retain earlier output, suppress its tail, and continue the caller.");
        }

        AotExecutionResult conditionalReturn = AotExecutionKernel.Compile(
                "function Pick($enabled) { if ($enabled) { return }; Get-Verb -Verb Add | Select-Object Verb }; Pick $true; Pick $false",
                "return-if.ps1")
            .Execute(new AotExecutionContext());
        if (conditionalReturn.Outputs.Count != 1 || !conditionalReturn.Outputs[0].Rows.Any(row => row.TextFor("Verb") == "Add"))
        {
            throw new InvalidOperationException("A return from a selected local-function if branch did not stay local or the false branch did not fall through.");
        }

        AotExecutionResult foreachReturn = AotExecutionKernel.Compile(
                "function First($values) { foreach ($value in $values) { Get-Verb -Verb $value | Select-Object Verb; return }; Get-Date | Select-Object DateTime }; $values = 'Add', 'Get'; First $values",
                "return-foreach.ps1")
            .Execute(new AotExecutionContext());
        if (foreachReturn.Outputs.Count != 1
            || !foreachReturn.Outputs[0].Rows.Any(row => row.TextFor("Verb") == "Add"))
        {
            throw new InvalidOperationException("A return from a local-function foreach body did not exit the function and suppress later iterations/tail output.");
        }

        AotExecutionResult calleeReturn = AotExecutionKernel.Compile(
                "function Inner() { return }; function Outer() { Inner; Get-Verb -Verb Add | Select-Object Verb }; Outer",
                "return-callee.ps1")
            .Execute(new AotExecutionContext());
        if (calleeReturn.Outputs.Count != 1 || !calleeReturn.Outputs[0].Rows.Any(row => row.TextFor("Verb") == "Add"))
        {
            throw new InvalidOperationException("A local-function return escaped into its caller instead of being consumed by the callee invocation.");
        }

        foreach (string unsupported in new[]
        {
            "return",
            "if ($true) { return }",
            "foreach ($value in 'Add', 'Get') { return }",
            "function Value() { return 'value' }",
            "function Pipeline() { return Get-Verb -Verb Add }",
        })
        {
            try
            {
                _ = AotExecutionKernel.Compile(unsupported, "return-unsupported.ps1");
                throw new InvalidOperationException($"The kernel accepted unsupported return shape '{unsupported}'.");
            }
            catch (ScriptException error) when (error.Diagnostic.Id == "AOT1001")
            {
                // Return values need the later typed value-to-output contract;
                // root returns are never allowed to escape into the host.
            }
        }

        try
        {
            _ = AotExecutionKernel.Compile("function Stop() { return }; Stop", "return-cancelled.ps1")
                .Execute(new AotExecutionContext(new CancellationToken(canceled: true)));
            throw new InvalidOperationException("A pre-cancelled local function ran its return plan.");
        }
        catch (OperationCanceledException)
        {
            // Host cancellation remains control flow and wins before any local return.
        }
    }

    private static void AssertStaticFunctionComposition()
    {
        AotExecutionResult namedAndDefault = AotExecutionKernel.Compile(
                "function Pick-Verb($group = 'Common', $verb = 'Add') { Get-Verb -Group $group -Verb $verb | Select-Object Verb, Group }; Pick-Verb -GROUP:Common -VERB Add; Pick-Verb",
                "function-named-default.ps1")
            .Execute(new AotExecutionContext());
        if (namedAndDefault.Outputs.Count != 2
            || namedAndDefault.Outputs.Any(static output => !output.Columns.SequenceEqual(["Verb", "Group"])
                || output.Rows.SingleOrDefault()?.TextFor("Verb") != "Add"))
        {
            throw new InvalidOperationException("Local function named/default parameter binding did not retain the closed body-output contract.");
        }

        AotExecutionResult producer = AotExecutionKernel.Compile(
                "function Get-Zones() { Get-TimeZone -ListAvailable }; Get-Zones | Where-Object BaseUtcOffsetMinutes -ge -1000 | Select-Object Id, BaseUtcOffsetMinutes",
                "function-producer.ps1")
            .Execute(new AotExecutionContext());
        if (producer.Outputs.SingleOrDefault() is not { Columns: var producerColumns, Rows: var producerRows }
            || !producerColumns.SequenceEqual(["Id", "BaseUtcOffsetMinutes"])
            || producerRows.Count == 0
            || producerRows.Any(static row => row.Fields.Count != 2))
        {
            throw new InvalidOperationException("A transparent local-function producer did not reuse the typed Where/Select tail.");
        }

        AssertFunctionFailure("function Needs-One($group) { Get-Verb -Group $group }; Needs-One", "function-missing-required.ps1", "AOT5008");
        AssertFunctionFailure("function Needs-One($group) { Get-Verb -Group $group }; Needs-One -missing Common", "function-unknown-named.ps1", "AOT5009");
        AssertFunctionFailure("function Needs-One($group) { Get-Verb -Group $group }; Needs-One -group", "function-named-missing-value.ps1", "AOT5011");
        AssertFunctionFailure("function Needs-One($group) { Get-Verb -Group $group }; Needs-One Common -group Data", "function-duplicate-named.ps1", "AOT5010");
        AssertFunctionFailure("function Needs-Two($one, $two) { Get-Verb -Group $one }; Needs-Two -one Common Data", "function-positional-after-named.ps1", "AOT5012");
        AssertFunctionFailure("function Too-Many($group) { Get-Verb -Group $group }; Too-Many Common Data", "function-extra-positional.ps1", "AOT5008");
        AssertFunctionFailure("function Nested-Producer() { function Inner() { Get-Verb -Verb Add }; Inner }; Nested-Producer | Select-Object Verb", "function-producer-nested.ps1", "AOT1001");
        AssertFunctionFailure("function Get-Zones() { Get-TimeZone -ListAvailable }; Get-Zones | Get-Process", "function-producer-native-input.ps1", "AOT1001");

        AssertFunctionDiagnosticSnapshot(
            "function One($value) { Get-Verb -Verb $value }; One -missing Add",
            "function-unknown-snapshot.ps1",
            "AOT5009",
            """
error[AOT5009]: Function 'One' does not declare parameter '-missing'.
  --> function-unknown-snapshot.ps1:1:53
   |
1 | function One($value) { Get-Verb -Verb $value }; One -missing Add
   |                                                     ^^^^^^^^ unknown local-function parameter
   = help: Use an exact parameter name declared by the local function.
""");
        AssertFunctionDiagnosticSnapshot(
            "function One($value) { Get-Verb -Verb $value }; One -value",
            "function-missing-value-snapshot.ps1",
            "AOT5011",
            """
error[AOT5011]: Function 'One' parameter '-value' requires a closed value.
  --> function-missing-value-snapshot.ps1:1:53
   |
1 | function One($value) { Get-Verb -Verb $value }; One -value
   |                                                     ^^^^^^ local-function parameter requires a value
   = help: Supply one closed value immediately after the named parameter.
""");
        try
        {
            _ = AotExecutionKernel.Compile(
                    "function Get-Zones($ignored = 'closed') { Get-TimeZone -ListAvailable }; Get-Zones -ignored caller | Select-Object Id",
                    "function-producer-cancelled.ps1")
                .Execute(new AotExecutionContext(new CancellationToken(canceled: true)));
            throw new InvalidOperationException("A pre-cancelled local-function producer bound arguments or executed its source.");
        }
        catch (OperationCanceledException)
        {
            // Cancellation remains host control flow and wins before binding.
        }

        foreach (string unsupported in new[]
        {
            "function Dynamic-Default($group = $outer) { Get-Verb -Group $group }",
            "function NonTrailing($optional = 'Common', $required) { Get-Verb -Group $optional }",
        })
        {
            try
            {
                _ = AotExecutionKernel.Compile(unsupported, "function-binding-unsupported.ps1");
                throw new InvalidOperationException($"The kernel accepted deferred local-function binding syntax '{unsupported}'.");
            }
            catch (ScriptException error) when (error.Diagnostic.Id == "AOT1001")
            {
                // Header syntax remains parser-authoritative but fails closed
                // until it has a specifically reviewed binding contract.
            }
        }
    }

    private static void AssertGeneralTypedDataPipeline()
    {
        const string repeatedTransforms = "Get-TimeZone -ListAvailable | Where-Object BaseUtcOffsetMinutes -ge -1000 | Select-Object Id, BaseUtcOffsetMinutes | Where-Object BaseUtcOffsetMinutes -ge -1000 | Select-Object Id";
        AotExecutionResult transformed = AotExecutionKernel.Compile(repeatedTransforms, "typed-record-transforms.ps1")
            .Execute(new AotExecutionContext());
        if (transformed.Outputs.SingleOrDefault() is not { Columns: var transformColumns, Rows: var transformRows }
            || !transformColumns.SequenceEqual(["Id"])
            || transformRows.Count == 0
            || transformRows.Any(static row => row.Fields.Count != 1))
        {
            throw new InvalidOperationException("Repeated mixed Where-Object/Select-Object transforms did not retain one explicit record shape.");
        }

        const string producerSource = "function Get-TwoVerbs() { $group = 'Common'; Get-Verb -Verb Add | Select-Object Verb; Get-Verb -Verb Get | Select-Object Verb }; Get-TwoVerbs | Select-Object Verb";
        AotExecutionResult producer = AotExecutionKernel.Compile(producerSource, "function-record-batch.ps1")
            .Execute(new AotExecutionContext());
        if (producer.Outputs.SingleOrDefault() is not { Columns: var producerColumns, Rows: var producerRows }
            || !producerColumns.SequenceEqual(["Verb"])
            || !producerRows.Select(row => row.TextFor("Verb")).SequenceEqual(["Add", "Get"]))
        {
            throw new InvalidOperationException("Function pipeline composition did not collect ordered typed output into one record batch.");
        }

        AssertFunctionFailure(
            "Get-TimeZone -ListAvailable | Where-Object BaseUtcOffsetMinutes -ge -1000 | Select-Object Id | Where-Object Id -ne 0 | Select-Object Id | Where-Object Id -ne 0",
            "typed-record-transform-cap.ps1",
            "AOT1001");

        AssertFunctionFailure(
            "Get-TimeZone -ListAvailable | Select-Object Id | Where-Object BaseUtcOffsetMinutes -ge -1000",
            "typed-record-dropped-field.ps1",
            "AOT6402");
        AssertFunctionFailure(
            "function Mixed() { Get-Verb -Verb Add | Select-Object Verb; Get-Date | Select-Object DateTime }; Mixed | Where-Object Verb -eq 0",
            "function-producer-heterogeneous-filter.ps1",
            "AOT1001");

        AotExecutionResult returnedProducer = AotExecutionKernel.Compile(
                "function First-Verb() { Get-Verb -Verb Add | Select-Object Verb; return; Get-Verb -Verb Get | Select-Object Verb }; First-Verb | Select-Object Verb",
                "function-producer-return.ps1")
            .Execute(new AotExecutionContext());
        if (returnedProducer.Outputs.SingleOrDefault() is not { Rows: var returnedRows }
            || !returnedRows.Select(row => row.TextFor("Verb")).SequenceEqual(["Add"]))
        {
            throw new InvalidOperationException("A bare return in a function producer did not preserve pre-return typed batch rows.");
        }

        AssertTerminalPresentationContracts();

        const string shapeContractSource = "Get-Verb | Select-Object Verb";
        AotSourceSpan shapeContractSpan = new("shape-contract.ps1", 0, 8, 1, 1, 1, 9);
        try
        {
            new AotRecordShape(["Missing"]).ValidateForProjection(
                new AotRecord([new AotField("Verb", AotValue.FromString("Get"))]),
                shapeContractSpan);
            throw new InvalidOperationException("A record shape contract accepted a record without its required field.");
        }
        catch (ScriptException error) when (error.Diagnostic.Id == "AOT4011")
        {
            AssertDiagnosticSnapshot(error.Diagnostic, shapeContractSource, """
error[AOT4011]: AOT record output does not expose required field 'Missing'.
  --> shape-contract.ps1:1:1
   |
1 | Get-Verb | Select-Object Verb
   | ^^^^^^^^ record shape contract mismatch
   = help: Use a last direct Select-Object that names fields emitted by every record.
""");
        }

        AssertBatchAndProjectionCancellation();

        const string unregisteredSource = "Get-Unknown | Select-Object Name";
        AotSourceSpan unregisteredSpan = new("unregistered-record.ps1", 0, 11, 1, 1, 1, 12);
        try
        {
            _ = AotRecordBatch.FromTypedRows(new AotExecutionContext(), [new UnregisteredPipelineRecord()], unregisteredSpan);
            throw new InvalidOperationException("An unregistered pipeline record crossed the AOT record boundary.");
        }
        catch (ScriptException error) when (error.Diagnostic.Id == "AOT4009")
        {
            AssertDiagnosticSnapshot(error.Diagnostic, unregisteredSource, """
error[AOT4009]: This pipeline record type is not registered for the Native AOT record boundary.
  --> unregistered-record.ps1:1:1
   |
1 | Get-Unknown | Select-Object Name
   | ^^^^^^^^^^^ unregistered pipeline record
   = help: Add an explicit reviewed record adapter before using this cmdlet in a structural pipeline.
""");
        }
    }

    // Terminal prose is a static pipeline-plan contract. These go through the
    // same ScriptRunner/event/projector path as the CLI rather than calling a
    // cmdlet renderer or inspecting a HelpRecord at the terminal boundary.
    private static void AssertTerminalPresentationContracts()
    {
        string help = CaptureHostOutput("Get-Help NoSuchTopic");
        string expectedHelp = $"No help topic matched 'NoSuchTopic'.{Environment.NewLine}";
        if (help != expectedHelp)
        {
            throw new InvalidOperationException("Direct Get-Help did not retain its static terminal-prose contract.");
        }

        string functionHelp = CaptureHostOutput("function Show-Help { Get-Help NoSuchTopic }; Show-Help");
        if (functionHelp != expectedHelp)
        {
            throw new InvalidOperationException("A direct local function did not retain its contained Get-Help terminal-prose contract.");
        }

        string transformedHelp = CaptureHostOutput("Get-Help NoSuchTopic | Select-Object Value");
        if (!HasValueTableHeader(transformedHelp)
            || !transformedHelp.Contains("No help topic matched 'NoSuchTopic'.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A structural transform did not reset Get-Help to the table presentation contract.");
        }

        string ordinaryValue = CaptureHostOutput("Get-Date -UFormat +%Y");
        if (!HasValueTableHeader(ordinaryValue))
        {
            throw new InvalidOperationException("An ordinary Value-shaped cmdlet was incorrectly rendered as terminal prose.");
        }
    }

    private static string CaptureHostOutput(string source)
    {
        StringWriter output = new(CultureInfo.InvariantCulture);
        StringWriter error = new(CultureInfo.InvariantCulture);
        TextWriter originalOutput = Console.Out;
        TextWriter originalError = Console.Error;
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            if (ScriptRunner.Execute(source, colorMode: AotColorMode.Never) != 0 || error.GetStringBuilder().Length != 0)
            {
                throw new InvalidOperationException($"The terminal presentation host fixture failed: {source}");
            }
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }

        return output.ToString();
    }

    private static bool HasValueTableHeader(string output)
    {
        string[] lines = output.Split([Environment.NewLine], StringSplitOptions.None);
        return lines.Length >= 2
            && lines[0].TrimEnd().Equals("Value", StringComparison.Ordinal)
            && lines[1].TrimEnd().All(static character => character == '-');
    }

    private static void AssertBatchAndProjectionCancellation()
    {
        using CancellationTokenSource conversionCancellation = new();
        AotExecutionContext conversionContext = new(conversionCancellation.Token);
        AssertCancellation(() => AotRecordBatch.FromTypedRows(conversionContext, YieldThenCancel(conversionCancellation)));
        if (conversionContext.Events.Count != 0 || conversionContext.Errors.Count != 0)
        {
            throw new InvalidOperationException("Cancelled record-batch conversion emitted a success or error event.");
        }

        AotRecordBatch batch = new(
        [
            new AotRecord([new AotField("Value", AotValue.FromString("first"))]),
            new AotRecord([new AotField("Value", AotValue.FromString("second"))]),
        ]);
        using CancellationTokenSource transformCancellation = new();
        AotExecutionContext transformContext = new(transformCancellation.Token);
        CancelAfterFirstTransform transform = new(transformCancellation);
        AssertCancellation(() => batch.ApplyTransforms(transformContext, [transform]));
        if (transform.ApplyCount != 1)
        {
            throw new InvalidOperationException("Record-batch transformation did not cancel after its first row.");
        }
        if (transformContext.Events.Count != 0 || transformContext.Errors.Count != 0)
        {
            throw new InvalidOperationException("Cancelled record-batch transformation emitted a success or error event.");
        }

        using CancellationTokenSource projectionCancellation = new();
        AotExecutionContext projectionContext = new(projectionCancellation.Token);
        StringWriter output = new(CultureInfo.InvariantCulture);
        StringWriter error = new(CultureInfo.InvariantCulture);
        int renderedRows = 0;
        AotTerminalEventProjector projector = new(
            "Get-Help NoSuchTopic",
            "projection-cancellation.ps1",
            new AotDiagnosticRenderOptions(UseAnsi: false),
            output,
            error,
            projectionContext,
            () =>
            {
                renderedRows++;
                projectionCancellation.Cancel();
            });
        AssertCancellation(() => projector.Project(AotRuntimeEvent.Success(
            1,
            new AotExecutionOutput(batch, new AotRecordShape(["Value"]), AotTerminalPresentation.Prose))));
        if (renderedRows != 1)
        {
            throw new InvalidOperationException("Terminal projection did not cancel after buffering its first row.");
        }
        if (output.GetStringBuilder().Length != 0
            || error.GetStringBuilder().Length != 0
            || projectionContext.Events.Count != 0
            || projectionContext.Errors.Count != 0)
        {
            throw new InvalidOperationException("Cancelled terminal projection emitted a success or error result.");
        }

        static IEnumerable<IPipelineRecord> YieldThenCancel(CancellationTokenSource cancellation)
        {
            yield return new TextRecord("first");
            cancellation.Cancel();
            yield return new TextRecord("second");
        }
    }

    private sealed class CancelAfterFirstTransform(CancellationTokenSource cancellation) : AotRecordTransform(null)
    {
        internal int ApplyCount { get; private set; }

        internal override bool TryApply(AotRecord record, out AotRecord? transformed)
        {
            ApplyCount++;
            transformed = record;
            cancellation.Cancel();
            return true;
        }
    }

    private sealed class UnregisteredPipelineRecord : IPipelineRecord
    {
        public double NumberFor(string property) => throw new NotSupportedException();

        public string TextFor(string column) => throw new NotSupportedException();
    }

    private static void AssertFunctionDiagnosticSnapshot(string source, string documentName, string id, string expected)
    {
        try
        {
            _ = AotExecutionKernel.Compile(source, documentName).Execute(new AotExecutionContext());
            throw new InvalidOperationException($"The local-function diagnostic snapshot '{id}' did not fail.");
        }
        catch (ScriptException error) when (error.Diagnostic.Id == id)
        {
            AssertDiagnosticSnapshot(error.Diagnostic, source, expected);
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
            || !selectedRows.Any(row => row.TextFor("Verb") == "Add"))
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

    private static void AssertForEachCore()
    {
        const string source = """
$verbs = 'Add', 'Get'
foreach ($verb in $verbs) {
    $last = $verb
    Get-Verb -Verb $verb | Select-Object Verb
}
""";
        AotScope scope = new();
        AotExecutionResult result = AotExecutionKernel.Compile(source, "foreach-basic.ps1")
            .Execute(new AotExecutionContext(), scope);
        if (result.Outputs.Count != 2
            || !result.Outputs.All(output => output.Columns.SequenceEqual(["Verb"]))
            || !result.Outputs.SelectMany(output => output.Rows).Select(row => row.TextFor("Verb")).SequenceEqual(["Add", "Get"])
            || !scope.TryGet("VERB", out AotValue finalVerb)
            || !finalVerb.TryGetString(out string? finalVerbText)
            || finalVerbText != "Get"
            || !scope.TryGet("last", out AotValue finalAssignment)
            || !finalAssignment.TryGetString(out string? finalAssignmentText)
            || finalAssignmentText != "Get")
        {
            throw new InvalidOperationException("Foreach did not execute each closed-list item in order or retain its final loop variable.");
        }

        AotScope emptyScope = new();
        emptyScope.Set("items", AotValue.FromList([]));
        emptyScope.Set("item", AotValue.FromString("before"));
        AotExecutionResult emptyResult = AotExecutionKernel.Compile(
                "foreach ($item in $items) { Get-Verb -Verb $item }",
                "foreach-empty.ps1")
            .Execute(new AotExecutionContext(), emptyScope);
        if (emptyResult.Outputs.Count != 0
            || !emptyScope.TryGet("item", out AotValue itemAfterEmpty)
            || !itemAfterEmpty.TryGetString(out string? itemAfterEmptyText)
            || itemAfterEmptyText != "before")
        {
            throw new InvalidOperationException("An empty foreach changed its pre-existing loop variable or emitted output.");
        }

        AotExecutionResult directListResult = AotExecutionKernel.Compile(
                "foreach ($verb in 'Add', 'Get') { Get-Verb -Verb $verb | Select-Object Verb }",
                "foreach-direct-list.ps1")
            .Execute(new AotExecutionContext());
        if (!directListResult.Outputs.SelectMany(output => output.Rows).Select(row => row.TextFor("Verb")).SequenceEqual(["Add", "Get"]))
        {
            throw new InvalidOperationException("A direct comma-list foreach collection did not preserve source order.");
        }

        AotScope snapshotScope = new();
        AotExecutionResult snapshotResult = AotExecutionKernel.Compile(
                "$values = 'Add', 'Get'; foreach ($verb in $values) { $values = 'Add'; Get-Verb -Verb $verb | Select-Object Verb }",
                "foreach-snapshot.ps1")
            .Execute(new AotExecutionContext(), snapshotScope);
        if (!snapshotResult.Outputs.SelectMany(output => output.Rows).Select(row => row.TextFor("Verb")).SequenceEqual(["Add", "Get"]))
        {
            throw new InvalidOperationException("Foreach re-evaluated its source after a body assignment instead of using the initial closed-list snapshot.");
        }

        AotExecutionResult nestedResult = AotExecutionKernel.Compile(
                "$outerValues = 'Add', 'Get'; $innerValues = 'Add', 'Get'; foreach ($outer in $outerValues) { foreach ($inner in $innerValues) { Get-Verb -Verb $inner | Select-Object Verb } }",
                "foreach-nested.ps1")
            .Execute(new AotExecutionContext());
        if (nestedResult.Outputs.Count != 4 || !nestedResult.Outputs.All(output => output.Columns.SequenceEqual(["Verb"])))
        {
            throw new InvalidOperationException("Nested foreach plans did not preserve each body output segment.");
        }

        AotScope sameNameScope = new();
        _ = AotExecutionKernel.Compile(
                "$outerValues = 'Add', 'Get'; $innerValues = 'Add', 'Add'; foreach ($verb in $outerValues) { foreach ($verb in $innerValues) { $last = $verb } }",
                "foreach-same-name.ps1")
            .Execute(new AotExecutionContext(), sameNameScope);
        if (!sameNameScope.TryGet("verb", out AotValue sameNameValue)
            || !sameNameValue.TryGetString(out string? sameNameText)
            || sameNameText != "Add")
        {
            throw new InvalidOperationException("A nested foreach restored an outer loop variable instead of preserving PowerShell's shared-scope result.");
        }

        AotScope nullItemScope = new();
        AotExecutionResult nullItemResult = AotExecutionKernel.Compile(
                "$values = $null, $null; foreach ($verb in $values) { Get-Verb -Verb $verb }",
                "foreach-null-item.ps1")
            .Execute(new AotExecutionContext(), nullItemScope);
        if (nullItemResult.Outputs.Count != 0
            || !nullItemScope.TryGet("verb", out AotValue nullItem)
            || nullItem.Kind != AotValueKind.Null)
        {
            throw new InvalidOperationException("Null entries in a closed foreach list were not retained as iteration items.");
        }

        const string outputBeforeFailure = """
$groups = 'Common', 'Communications'
foreach ($group in $groups) {
    Get-Verb -Group $group | Select-Object Verb
    if ($group -eq 'Communications') { Get-Verb -Group $missing }
}
""";
        StringWriter streamedOutput = new(CultureInfo.InvariantCulture);
        StringWriter streamedError = new(CultureInfo.InvariantCulture);
        TextWriter originalOutput = Console.Out;
        TextWriter originalError = Console.Error;
        try
        {
            Console.SetOut(streamedOutput);
            Console.SetError(streamedError);
            if (ScriptRunner.Execute(outputBeforeFailure) != 2)
            {
                throw new InvalidOperationException("A later foreach iteration failure did not return the host diagnostic exit code.");
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
            throw new InvalidOperationException("Completed foreach body output was not streamed before a later iteration failed.");
        }

        const string scalarCollection = "$value = 'Add'; foreach ($verb in $value) { Get-Verb -Verb $verb }";
        try
        {
            _ = AotExecutionKernel.Compile(scalarCollection, "foreach-scalar.ps1").Execute(new AotExecutionContext());
            throw new InvalidOperationException("Foreach accepted a scalar collection outside the closed list subset.");
        }
        catch (ScriptException error) when (error.Diagnostic is { Id: "AOT5006", Span: { DocumentName: "foreach-scalar.ps1", StartColumn: 35 } })
        {
            AssertDiagnosticSnapshot(
                error.Diagnostic,
                scalarCollection,
                """
error[AOT5006]: Foreach requires a closed list value in the Native AOT subset.
  --> foreach-scalar.ps1:1:35
   |
1 | $value = 'Add'; foreach ($verb in $value) { Get-Verb -Verb $verb }
   |                                   ^^^^^^ unsupported foreach collection
   = help: Assign a comma-list of supported closed values, then iterate that variable.
""");
        }

        const string undefinedCollection = "foreach ($verb in $values) { Get-Verb -Verb $verb }";
        try
        {
            _ = AotExecutionKernel.Compile(undefinedCollection, "foreach-undefined.ps1").Execute(new AotExecutionContext());
            throw new InvalidOperationException("Foreach accepted an undefined collection variable.");
        }
        catch (ScriptException error) when (error.Diagnostic is { Id: "AOT5001", Span: { DocumentName: "foreach-undefined.ps1", StartColumn: 19 } })
        {
            // A header expression must retain its use-site diagnostic span.
        }

        foreach (string invalidTarget in new[]
        {
            "foreach ($PID in $verbs) { Get-Verb -Verb $PID }",
            "foreach ($foreach in $verbs) { Get-Verb -Verb $foreach }",
            "foreach ($global:verb in $verbs) { Get-Verb -Verb $verb }",
            "foreach (@verb in $verbs) { Get-Verb -Verb $verb }",
        })
        {
            try
            {
                _ = AotExecutionKernel.Compile(invalidTarget, "foreach-target.ps1");
                throw new InvalidOperationException($"The kernel accepted an invalid foreach target '{invalidTarget}'.");
            }
            catch (ScriptException error) when (error.Diagnostic is { Id: "AOT5002", Span: not null })
            {
                // Iterator targets share normal assignment validation.
            }
        }

        foreach (string unsupported in new[]
        {
            "foreach ($verb in Get-Verb) { Get-Verb -Verb $verb }",
            "foreach ($verb in 1..3) { Get-Verb -Verb $verb }",
            ":label foreach ($verb in $verbs) { Get-Verb -Verb $verb }",
            "foreach ($verb in $verbs) { break }",
        })
        {
            try
            {
                _ = AotExecutionKernel.Compile(unsupported, "foreach-unsupported.ps1");
                throw new InvalidOperationException($"The kernel accepted deferred foreach syntax '{unsupported}'.");
            }
            catch (ScriptException error) when (error.Diagnostic.Id == "AOT1001")
            {
                // Upstream parses these forms; the closed foreach plan must
                // reject them instead of invoking dynamic pipeline semantics.
            }
        }

        foreach (string parserRejected in new[]
        {
            "foreach -parallel ($verb in $verbs) { Get-Verb -Verb $verb }",
            "foreach -throttlelimit 2 ($verb in $verbs) { Get-Verb -Verb $verb }",
        })
        {
            try
            {
                _ = AotExecutionKernel.Compile(parserRejected, "foreach-parser-rejected.ps1");
                throw new InvalidOperationException($"The parser accepted an upstream-rejected foreach option '{parserRejected}'.");
            }
            catch (ScriptException error) when (error.Diagnostic is { Id: "KeywordParameterReservedForFutureUse", Span: { DocumentName: "foreach-parser-rejected.ps1" } })
            {
                // These upstream semantic diagnostics precede lowering and
                // therefore must not be relabeled as AOT1001.
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

    private static void AssertHostReplProjection()
    {
        // This fact originates in the pinned parser's ParseError.IncompleteInput
        // bit. The REPL does not scan punctuation itself to infer continuation.
        AotParseResult trailingPipe = AotScriptParser.Parse("Get-Verb |", "repl-pipe.ps1");
        if (!trailingPipe.HasIncompleteInput
            || trailingPipe.HasBlockingDiagnostics
            || !trailingPipe.RequiresMoreInput
            || trailingPipe.Diagnostics.SingleOrDefault()?.Id != "EmptyPipeElement")
        {
            throw new InvalidOperationException("The shared parser did not expose an incomplete trailing pipeline for REPL continuation.");
        }

        AotReplInputBuffer input = new("repl-buffer.ps1");
        AotParseResult firstLine = input.Submit("Get-Verb |");
        if (!firstLine.RequiresMoreInput || !input.HasPending || input.Prompt != ">> ")
        {
            throw new InvalidOperationException("The REPL input buffer did not retain an upstream-incomplete first line.");
        }

        AotParseResult completed = input.Submit("Select-Object Verb");
        if (completed.Diagnostics.Count != 0
            || input.HasPending
            || input.Prompt != "pwsh-aot> "
            || completed.Source != "Get-Verb |\nSelect-Object Verb")
        {
            throw new InvalidOperationException("The REPL input buffer did not preserve multi-line source or reset after a complete parse.");
        }

        AotExecutionPlan plan = AotExecutionKernel.Compile(completed);
        if (!ReferenceEquals(plan.ParseResult, completed))
        {
            throw new InvalidOperationException("A parser-approved REPL buffer was reparsed instead of lowering its shared parse result.");
        }

        AotReplInputBuffer malformedBuffer = new("repl-malformed.ps1");
        AotParseResult malformed = malformedBuffer.Submit("Get-Verb | | Get-Verb");
        if (!malformed.HasBlockingDiagnostics || malformed.RequiresMoreInput || malformedBuffer.HasPending)
        {
            throw new InvalidOperationException("A malformed REPL line was treated as an endlessly incomplete continuation.");
        }

        StringWriter output = new(CultureInfo.InvariantCulture);
        StringWriter error = new(CultureInfo.InvariantCulture);
        AotExecutionContext projectionContext = new();
        AotTerminalEventProjector projector = new(
            "Get-Verb",
            "host-projection.ps1",
            new AotDiagnosticRenderOptions(UseAnsi: true),
            output,
            error,
            projectionContext);
        AotExecutionOutput segment = AotExecutionOutput.FromTypedRows(
            new AotExecutionContext(),
            [new VerbRecord("Get", "g", "Common", "fixture")],
            ["Verb", "AliasPrefix"]);
        projector.Project(AotRuntimeEvent.Success(1, segment));
        if (!output.ToString().Contains("Verb", StringComparison.Ordinal)
            || error.GetStringBuilder().Length != 0)
        {
            throw new InvalidOperationException("The terminal event projector did not send a success segment exclusively to stdout.");
        }

        projector.Project(AotRuntimeEvent.Error(
            2,
            null,
            AotDiagnostics.Runtime("AOT3998", "fixture runtime error", new AotSourceSpan("host-projection.ps1", 0, 8, 1, 1, 1, 9))));
        if (!error.ToString().Contains("\u001b[31merror[AOT3998]", StringComparison.Ordinal)
            || !error.ToString().Contains("host-projection.ps1:1:1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The terminal event projector did not render an event diagnostic on stderr with the configured policy.");
        }

        StringWriter runnerOutput = new(CultureInfo.InvariantCulture);
        StringWriter runnerError = new(CultureInfo.InvariantCulture);
        TextWriter originalOutput = Console.Out;
        TextWriter originalError = Console.Error;
        try
        {
            Console.SetOut(runnerOutput);
            Console.SetError(runnerError);
            if (ScriptRunner.Execute(trailingPipe, colorMode: AotColorMode.Never) != 2)
            {
                throw new InvalidOperationException("An incomplete buffer drained at host EOF returned the wrong diagnostic exit code.");
            }
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }

        if (runnerOutput.GetStringBuilder().Length != 0
            || !runnerError.ToString().Contains("error[EmptyPipeElement]", StringComparison.Ordinal)
            || !runnerError.ToString().Contains("repl-pipe.ps1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The same parser result did not produce an EOF-safe REPL diagnostic through the host projector.");
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
            [
                new AotWhereStaticNumericStage(
                    new CmdletDescriptor("Where-Object", []),
                    "CPU",
                    Comparison.GreaterThan,
                    AotValue.FromInteger(10),
                    null!,
                    null!),
                new AotSelectStaticFieldsStage(
                    new CmdletDescriptor("Select-Object", []),
                    ["Name", "Id"],
                    null!),
            ],
            new AotRecordShape(["Name", "Id"]),
            null,
            pipelineLength: 3);
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
            catch (ScriptException exception) when (exception.Diagnostic.Id == "AOT6403")
            {
            }
        }
    }

    // J2 redirect evidence: each spelling is lowered only to its descriptor
    // stage. These fixture helpers are intentionally private to the self-test;
    // they are not a transport API, listener, or endpoint implementation.
    private static void AssertJ2DescriptorRedirectAndTransportContract()
    {
        AssertJ2FixtureManifest();
        AssertJ2TailPlan(
            "Get-TimeZone -ListAvailable | Where-Object BaseUtcOffsetMinutes -GE -1000 | Select-Object Id, BaseUtcOffsetMinutes",
            "j2-positional-redirect.ps1");
        AssertJ2TailPlan(
            "Get-TimeZone -ListAvailable | Where-Object -Property BaseUtcOffsetMinutes -GE -Value -1000 | Select-Object -Property Id, BaseUtcOffsetMinutes",
            "j2-named-redirect.ps1");

        AssertJ2DiagnosticSnapshot(
            "Get-TimeZone -Id UTC | Select-Object Id -Property BaseUtcOffsetMinutes",
            "j2-mixed-positional-named.ps1",
            """
error[AOT6404]: Select-Object does not permit a named -Property group after positional Property values.
  --> j2-mixed-positional-named.ps1:1:41
   |
1 | Get-TimeZone -Id UTC | Select-Object Id -Property BaseUtcOffsetMinutes
   |                                         ^^^^^^^^^ mixed projection binding
   = help: Use either positional fields or one generated -Property field group, not both.
""");
        AssertJ2DiagnosticSnapshot(
            "Get-TimeZone -Id UTC | Select-Object -Property Id BaseUtcOffsetMinutes",
            "j2-mixed-named-positional.ps1",
            """
error[AOT6404]: Select-Object does not permit positional Property values after a named -Property group.
  --> j2-mixed-named-positional.ps1:1:51
   |
1 | Get-TimeZone -Id UTC | Select-Object -Property Id BaseUtcOffsetMinutes
   |                                                   ^^^^^^^^^^^^^^^^^^^^ mixed projection binding
   = help: Use either positional fields or one generated -Property field group, not both.
""");
        AssertJ2DiagnosticSnapshot(
            "Get-TimeZone -Id UTC | Select-Object I*",
            "j2-wildcard-property.ps1",
            """
error[AOT6404]: Select-Object requires one direct non-wildcard Property field in this Native AOT subset.
  --> j2-wildcard-property.ps1:1:38
   |
1 | Get-TimeZone -Id UTC | Select-Object I*
   |                                      ^^ invalid projection field
   = help: Use one literal field exposed by the preceding AOT record batch.
""");

        foreach ((string source, string id) in new[]
        {
            ("Where-Object CPU -GT 10", "AOT6401"),
            ("Select-Object Name", "AOT6401"),
            ("Get-Process | Where-Object { $_.CPU -GT 10 }", "AOT6406"),
            ("Get-Process | Where-Object CPU -GT '10'", "AOT6403"),
            ("Get-Process | Select-Object", "AOT6404"),
            ("Get-Process | Select-Object -ExcludeProperty Name", "AOT6406"),
            ("Get-Process | Select-Object Name, NAME", "AOT6405"),
            ("Get-TimeZone -Id UTC | Select-Object Id -Property BaseUtcOffsetMinutes", "AOT6404"),
            ("Get-TimeZone -Id UTC | Select-Object -Property Id BaseUtcOffsetMinutes", "AOT6404"),
            ("Get-TimeZone -Id UTC | Select-Object Id BaseUtcOffsetMinutes", "AOT6404"),
            ("Get-TimeZone -Id UTC | Select-Object I*", "AOT6404"),
        })
        {
            try
            {
                _ = AotExecutionKernel.Compile(source, "j2-redirect-boundary.ps1");
                throw new InvalidOperationException($"J2 redirect fixture unexpectedly accepted '{source}'.");
            }
            catch (ScriptException error) when (error.Diagnostic.Id == id && !error.Diagnostic.Id.StartsWith("AOT400", StringComparison.Ordinal))
            {
                // A single descriptor-owned AOT640* route is the contract.
            }
        }

        AotRecordBatch original = new(
        [
            new AotRecord(
            [
                new AotField("Name", AotValue.FromString("pwsh")),
                new AotField("CPU", AotValue.FromDecimal(12.5m)),
                new AotField("Bytes", AotValue.FromBytes(new byte[] { 1, 2, 3 })),
                new AotField("Nested", AotValue.FromList([AotValue.FromBoolean(true), AotValue.FromRecord(new AotRecord([new AotField("Inner", AotValue.FromInteger(7))]))])),
            ]),
            new AotRecord([new AotField("ID", AotValue.FromInteger(2)), new AotField("Enabled", AotValue.FromBoolean(false))]),
        ]);

        AotValue payload = J2FixtureEncodeBatch(original);
        AotRecordBatch reconstructed = J2FixtureReconstructBatch(payload);
        if (!J2FixtureBatchesEquivalent(original, reconstructed))
        {
            throw new InvalidOperationException("J2 fixture-only list-of-record transport contract did not preserve row/field order, field casing, or closed values.");
        }

        try
        {
            _ = J2FixtureReconstructBatch(AotValue.FromRecord(original.Records[0]));
            throw new InvalidOperationException("J2 fixture-only transport contract accepted a non-list root.");
        }
        catch (ArgumentException)
        {
            // The future contract accepts exactly List(Record...).
        }
    }

    private static void AssertJ2TailPlan(string source, string documentName)
    {
        AotExecutionPlan plan = AotExecutionKernel.Compile(source, documentName);
        AotPipelineStatementPlan pipeline = plan.Block.Statements.OfType<AotPipelineStatementPlan>().Single();
        IReadOnlyList<AotPipelineTailStagePlan> stages = pipeline.GetTailStagesForEvidence();
        if (stages.Count != 2
            || stages[0] is not AotWhereStaticNumericStagePlan
            || stages[1] is not AotSelectStaticFieldsStagePlan)
        {
            throw new InvalidOperationException("J2 command spelling did not lower to the sole generated descriptor-owned record stages.");
        }

        _ = plan.Execute(new AotExecutionContext());
    }

    private static void AssertJ2DiagnosticSnapshot(string source, string documentName, string expected)
    {
        try
        {
            _ = AotExecutionKernel.Compile(source, documentName);
            throw new InvalidOperationException($"J2 diagnostic snapshot unexpectedly accepted '{source}'.");
        }
        catch (ScriptException error) when (error.Diagnostic.Id == "AOT6404")
        {
            AssertDiagnosticSnapshot(error.Diagnostic, source, expected);
        }
    }

    // The approved readiness packet requires a fixed 52-ID evidence corpus.
    // Keep the inventory embedded in the artifact so a native self-test catches
    // an accidental deleted/renamed fixture before reviewers read the ledger.
    private static void AssertJ2FixtureManifest()
    {
        using Stream stream = typeof(SelfTest).Assembly.GetManifestResourceStream("PwshAotLite.J2StaticRecordTransformFixtures")
            ?? throw new InvalidOperationException("J2 static record-transform fixture manifest was not embedded in the executable.");
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1
            || root.GetProperty("authority").GetString() != "j2-static-record-transform-implementation")
        {
            throw new InvalidOperationException("J2 static record-transform fixture manifest schema/authority changed unexpectedly.");
        }

        IReadOnlyDictionary<string, int> required = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["grammar-baseline"] = 12,
            ["binder-diagnostic"] = 14,
            ["batch-semantics"] = 12,
            ["stock-oracle"] = 8,
            ["native-aot-smoke"] = 6,
        };
        JsonElement categories = root.GetProperty("categories");
        JsonElement[] fixtures = root.GetProperty("fixtures").EnumerateArray().ToArray();
        if (fixtures.Length != 52)
        {
            throw new InvalidOperationException("J2 static record-transform fixture corpus must retain exactly 52 IDs.");
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, int> category in required)
        {
            if (categories.GetProperty(category.Key).GetInt32() != category.Value
                || fixtures.Count(fixture => fixture.GetProperty("category").GetString() == category.Key) != category.Value)
            {
                throw new InvalidOperationException($"J2 fixture corpus category '{category.Key}' drifted from its approved count.");
            }
        }

        foreach (JsonElement fixture in fixtures)
        {
            string? id = fixture.GetProperty("id").GetString();
            string? evidence = fixture.GetProperty("evidence").GetString();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(evidence) || !ids.Add(id))
            {
                throw new InvalidOperationException("J2 fixture corpus contains an empty or duplicate ID/evidence record.");
            }
        }
    }

    private static AotValue J2FixtureEncodeBatch(AotRecordBatch batch) =>
        AotValue.FromList(batch.Records.Select(AotValue.FromRecord));

    private static AotRecordBatch J2FixtureReconstructBatch(AotValue payload)
    {
        if (!payload.TryGetItems(out IReadOnlyList<AotValue>? rows) || rows is null)
        {
            throw new ArgumentException("J2 fixture transport payload root must be List(Record...).", nameof(payload));
        }

        List<AotRecord> records = [];
        foreach (AotValue row in rows)
        {
            if (!row.TryGetRecord(out AotRecord? record) || record is null)
            {
                throw new ArgumentException("J2 fixture transport payload items must be Record values.", nameof(payload));
            }

            records.Add(record);
        }

        return new AotRecordBatch(records);
    }

    private static bool J2FixtureBatchesEquivalent(AotRecordBatch left, AotRecordBatch right) =>
        left.Records.Count == right.Records.Count
        && left.Records.Zip(right.Records).All(static pair => J2FixtureRecordsEquivalent(pair.First, pair.Second));

    private static bool J2FixtureRecordsEquivalent(AotRecord left, AotRecord right) =>
        left.Fields.Count == right.Fields.Count
        && left.Fields.Zip(right.Fields).All(static pair => pair.First.Name.Equals(pair.Second.Name, StringComparison.Ordinal)
            && J2FixtureValuesEquivalent(pair.First.Value, pair.Second.Value));

    private static bool J2FixtureValuesEquivalent(AotValue left, AotValue right)
    {
        if (left.Kind != right.Kind)
        {
            return false;
        }

        if (left.Kind == AotValueKind.Record)
        {
            return left.TryGetRecord(out AotRecord? leftRecord)
                && right.TryGetRecord(out AotRecord? rightRecord)
                && leftRecord is not null
                && rightRecord is not null
                && J2FixtureRecordsEquivalent(leftRecord, rightRecord);
        }

        if (left.Kind == AotValueKind.List)
        {
            return left.TryGetItems(out IReadOnlyList<AotValue>? leftItems)
                && right.TryGetItems(out IReadOnlyList<AotValue>? rightItems)
                && leftItems is not null
                && rightItems is not null
                && leftItems.Count == rightItems.Count
                && leftItems.Zip(rightItems).All(static pair => J2FixtureValuesEquivalent(pair.First, pair.Second));
        }

        return left.Equals(right);
    }

    private static void AssertJ1LexicalPaths()
    {
        static IReadOnlyList<IPipelineRecord> Execute(string script) => UpstreamAstPipelineLowerer.Parse(script).Execute(new AotExecutionContext());
        AssertJ1Corpus(Execute);
        if (!Execute("Join-Path alpha,beta gamma").Cast<TextRecord>().Select(static row => row.Value).SequenceEqual(["alpha/gamma", "beta/gamma"]))
        {
            throw new InvalidOperationException("J1 Join-Path did not preserve its upstream AST argument group.");
        }

        if (!Execute("Join-Path alpha child | Join-Path -ChildPath child2").Cast<TextRecord>().Select(static row => row.Value).SequenceEqual(["alpha/child/child2"]))
        {
            throw new InvalidOperationException("J1 did not preserve the admitted TextRecord pipeline path.");
        }

        if (!Execute("Join-Path alpha beta | Split-Path -Leaf").Cast<TextRecord>().Select(static row => row.Value).SequenceEqual(["beta"]))
        {
            throw new InvalidOperationException("J1 lexical path adapters did not compose through the typed TextRecord pipeline.");
        }

        if (Execute("Split-Path alpha/beta -IsAbsolute").SingleOrDefault() is not BooleanRecord { Value: false })
        {
            throw new InvalidOperationException("J1 Split-Path did not preserve the closed Boolean IsAbsolute output.");
        }

        try
        {
            _ = Execute("Join-Path alpha /rooted-child");
            throw new InvalidOperationException("J1 Join-Path accepted a rooted child.");
        }
        catch (ScriptException exception) when (exception.Diagnostic.Id == "AOT6210") { }

        try
        {
            _ = Execute("Split-Path alpha/beta -Leaf -Extension");
            throw new InvalidOperationException("J1 Split-Path accepted conflicting selectors.");
        }
        catch (ScriptException exception) when (exception.Diagnostic.Id == "AOT6213") { }

        try
        {
            _ = Execute("Join-Path alpha child | Join-Path -Path beta -ChildPath child2");
            throw new InvalidOperationException("J1 Join-Path silently combined pipeline and explicit Path input.");
        }
        catch (ScriptException exception) when (exception.Diagnostic.Id == "AOT6212") { }

        // Keep the required fragment corpus mechanical: every admitted form is
        // pure text and must not require an item to exist.
        string[] fragments = ["a", "a/", "a-b", "a_b", "a.b", "0", "01", "a1", "abc", "x-y", "x.y", "x_y", "one", "two", "three", "four", "five", "six", "seven", "eight"];
        if (fragments.Length != 20 || fragments.Any(fragment => Execute($"Join-Path 'base' '{fragment}'").SingleOrDefault() is not TextRecord { Value: var value } || value != $"base/{fragment}"))
        {
            throw new InvalidOperationException("J1 lexical child-fragment corpus regressed.");
        }

        foreach ((string script, string id) in new[]
        {
            ("Join-Path good,bad /rooted", "AOT6210"),
            ("Split-Path good/path,/ -Parent", "AOT6214"),
        })
        {
            try { _ = Execute(script); throw new InvalidOperationException($"J1 atomic corpus accepted '{script}'."); }
            catch (ScriptException exception) when (exception.Diagnostic.Id == id) { }
        }

        string[] propertyRoutes =
        [
            "Join-Path -PathByPropertyName alpha beta",
            "Join-Path -ChildPathByPropertyName beta",
            "Split-Path -PathByPropertyName alpha/beta",
            "Split-Path -LiteralPathByPropertyName alpha/beta",
            "Split-Path -LeafByPropertyName alpha/beta",
        ];
        if (propertyRoutes.Length != 5) throw new InvalidOperationException("J1 property rejection corpus count drifted.");
        foreach (string script in propertyRoutes)
        {
            try { _ = Execute(script); throw new InvalidOperationException($"J1 accepted property route '{script}'."); }
            catch (ScriptException exception) when (exception.Diagnostic.Id == "AOT6212") { }
        }
    }

    private static void AssertJ1Corpus(Func<string, IReadOnlyList<IPipelineRecord>> execute)
    {
        using Stream stream = typeof(SelfTest).Assembly.GetManifestResourceStream("PwshAotLite.J1LexicalPathCorpus")
            ?? throw new InvalidOperationException("J1 lexical corpus was not embedded in the executable.");
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("authority").GetString() != "none")
            throw new InvalidOperationException("J1 lexical corpus schema/authority changed unexpectedly.");
        JsonElement[] cases = root.GetProperty("cases").EnumerateArray().ToArray();
        if (cases.Length != 27
            || cases.Count(@case => @case.GetProperty("kind").GetString() == "child") != 20
            || cases.Count(@case => @case.GetProperty("kind").GetString() == "mixed-invalid") != 2
            || cases.Count(@case => @case.GetProperty("kind").GetString() == "property-rejection") != 5)
            throw new InvalidOperationException("J1 lexical corpus must contain the reviewed 20/2/5 case distribution.");

        foreach (JsonElement @case in cases)
        {
            string kind = @case.GetProperty("kind").GetString()!;
            if (kind == "property-rejection")
            {
                AssertJ1PropertyBindingRejected(
                    @case.GetProperty("command").GetString()!,
                    @case.GetProperty("property").GetString()!,
                    @case.GetProperty("value").GetString()!,
                    @case.GetProperty("diagnostic").GetString()!);
                continue;
            }

            string script = @case.GetProperty("script").GetString()!;
            if (kind == "child")
            {
                string expected = @case.GetProperty("output").GetString()!;
                if (execute(script).SingleOrDefault() is not TextRecord { Value: var actual } || actual != expected)
                    throw new InvalidOperationException($"J1 child corpus output drifted for '{@case.GetProperty("id").GetString()}'.");
                continue;
            }

            string diagnostic = @case.GetProperty("diagnostic").GetString()!;
            try
            {
                _ = execute(script);
                throw new InvalidOperationException($"J1 negative corpus accepted '{@case.GetProperty("id").GetString()}'.");
            }
            catch (ScriptException exception) when (exception.Diagnostic.Id == diagnostic)
            {
                // A direct command throws before terminal materialization; this
                // proves the corpus's zero-output atomic outcome.
            }
        }
    }

    // A record with a field named like an upstream ByPropertyName target is
    // deliberately not converted. The J1 input gate accepts TextRecord only;
    // this is a real typed-pipeline property-binding attempt, not a fabricated
    // parameter spelling or an object/reflection simulation.
    private static void AssertJ1PropertyBindingRejected(string command, string property, string value, string diagnostic)
    {
        (IAotCmdlet cmdlet, CommandInvocation invocation) = AotCmdletRegistry.ParseSource(command);
        if (cmdlet is not IAotPipelineInputCmdlet inputCmdlet)
            throw new InvalidOperationException($"J1 property corpus command '{command}' is not a typed input adapter.");
        try
        {
            _ = inputCmdlet.InvokeWithPipelineInput(invocation, [new J1NamedFieldRecord(property, value)], new AotExecutionContext(), 1, 2).ToArray();
            throw new InvalidOperationException($"J1 property corpus bound '{property}' for '{command}'.");
        }
        catch (ScriptException exception) when (exception.Diagnostic.Id == diagnostic) { }
    }

    private sealed record J1NamedFieldRecord(string Field, string Value) : IPipelineRecord
    {
        public double NumberFor(string property) => throw new ScriptException(AotDiagnostics.Runtime("AOT4010", "J1 property test records have no numeric fields."));
        public string TextFor(string column) => column.Equals(Field, StringComparison.OrdinalIgnoreCase) ? Value : throw new ScriptException(AotDiagnostics.Runtime("AOT4010", "J1 property test field is absent."));
    }

    private static void AssertClosedJsonCodec()
    {
        AotSourceSpan span = new("json-fixture.ps1", 11, 28, 1, 12, 1, 29);
        AotValue decoded = AotJsonCodec.Decode(
            "{\"name\":\"pwsh\",\"count\":2,\"items\":[true,null,1.5]}"u8,
            AotJsonReadLimits.J0,
            span,
            CancellationToken.None);
        if (!decoded.TryGetRecord(out AotRecord? decodedRecord)
            || decodedRecord is null
            || !decodedRecord.Fields.Select(static field => field.Name).SequenceEqual(["name", "count", "items"])
            || !decodedRecord.GetRequiredValue("count").TryGetInteger(out long count)
            || count != 2
            || !decodedRecord.GetRequiredValue("items").TryGetItems(out IReadOnlyList<AotValue>? items)
            || items is null
            || items.Count != 3
            || !items[2].TryGetDecimal(out decimal decimalValue)
            || decimalValue != 1.5m)
        {
            throw new InvalidOperationException("Closed JSON decoding did not preserve the approved ordered AOT shape.");
        }

        byte[] encoded = AotJsonCodec.Encode(decoded, AotJsonWriteLimits.J0, span, CancellationToken.None);
        if (!encoded.AsSpan().SequenceEqual("{\"name\":\"pwsh\",\"count\":2,\"items\":[true,null,1.5]}"u8))
        {
            throw new InvalidOperationException("Closed JSON encoding did not produce the stable compact J0 representation.");
        }

        AotValue floatingPoint = AotJsonCodec.Decode("1e30"u8, AotJsonReadLimits.J0, span, CancellationToken.None);
        if (!floatingPoint.TryGetFloatingPoint(out double finiteFloatingPoint) || finiteFloatingPoint != 1e30d)
        {
            throw new InvalidOperationException("JSON numeric precedence did not fall back from Int64/Decimal to a finite floating point value.");
        }

        byte[] exactNull = AotJsonCodec.Encode(AotValue.Null, new AotJsonWriteLimits(4, 16, 32, 8, 64), span, CancellationToken.None);
        byte[] exactString = AotJsonCodec.Encode(AotValue.FromString("a"), new AotJsonWriteLimits(3, 16, 32, 8, 64), span, CancellationToken.None);
        if (!exactNull.AsSpan().SequenceEqual("null"u8) || !exactString.AsSpan().SequenceEqual("\"a\""u8))
        {
            throw new InvalidOperationException("Closed JSON encoding rejected an exact-fit small output budget.");
        }

        AssertJsonDiagnostic("{\"value\":"u8, "AOT6301", span);
        AssertJsonDiagnostic("{\"value\":1} trailing"u8, "AOT6301", span);
        AssertJsonDiagnostic("{\"Name\":1,\"Name\":2}"u8, "AOT6304", span);
        AssertJsonDiagnostic("{\"Name\":1,\"name\":2}"u8, "AOT6304", span);
        AssertJsonDiagnostic("{\"\":1}"u8, "AOT6304", span);
        // Stock pwsh accepts this property name. J0 deliberately fails closed
        // under AOT6304 because AotRecord cannot carry whitespace-only fields.
        AssertJsonDiagnostic("{\"   \":1}"u8, "AOT6304", span);
        AssertJsonDiagnostic("1e400"u8, "AOT6305", span);

        AotJsonReadLimits tinyInput = new(4, 16, 32, 8, 64);
        AssertJsonDiagnostic("12345"u8, "AOT6302", span, tinyInput);
        AotJsonReadLimits shallow = new(1024, 1, 32, 8, 64);
        AssertJsonDiagnostic("[[]]"u8, "AOT6302", span, shallow);
        AotJsonReadLimits fewMembers = new(1024, 16, 32, 1, 64);
        AssertJsonDiagnostic("[1,2]"u8, "AOT6302", span, fewMembers);
        AssertJsonDiagnostic("{\"one\":1,\"two\":2}"u8, "AOT6302", span, fewMembers);

        AssertJsonEncodeDiagnostic(AotValue.FromDateTime(DateTime.UnixEpoch), "AOT6306", span, AotJsonWriteLimits.J0);
        AssertJsonEncodeDiagnostic(AotValue.FromBytes(new byte[] { 1, 2 }), "AOT6306", span, AotJsonWriteLimits.J0);
        AssertJsonEncodeDiagnostic(AotValue.FromString("output"), "AOT6303", span, new AotJsonWriteLimits(4, 16, 32, 8, 64));
        AssertJsonEncodeDiagnostic(AotValue.FromList([AotValue.FromList([])]), "AOT6303", span, new AotJsonWriteLimits(1024, 1, 32, 8, 64));
        AssertJsonEncodeDiagnostic(AotValue.FromList([AotValue.Null, AotValue.Null]), "AOT6303", span, new AotJsonWriteLimits(1024, 16, 1, 8, 64));
        AssertJsonEncodeDiagnostic(AotValue.FromList([AotValue.Null, AotValue.Null]), "AOT6303", span, new AotJsonWriteLimits(1024, 16, 32, 1, 64));
        AssertJsonEncodeDiagnostic(AotValue.FromString("long"), "AOT6303", span, new AotJsonWriteLimits(1024, 16, 32, 8, 3));

        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        AssertCancellation(() => AotJsonCodec.Decode("null"u8, AotJsonReadLimits.J0, span, cancelled.Token));
        AssertCancellation(() => AotJsonCodec.Encode(AotValue.Null, AotJsonWriteLimits.J0, span, cancelled.Token));
    }

    private static void AssertJsonDiagnostic(
        ReadOnlySpan<byte> input,
        string expectedId,
        AotSourceSpan expectedSpan,
        AotJsonReadLimits? limits = null)
    {
        try
        {
            _ = AotJsonCodec.Decode(input, limits ?? AotJsonReadLimits.J0, expectedSpan, CancellationToken.None);
            throw new InvalidOperationException($"Expected {expectedId} from the closed JSON decoder.");
        }
        catch (ScriptException error) when (error.Diagnostic.Id == expectedId && error.Diagnostic.Span == expectedSpan)
        {
        }
    }

    private static void AssertJsonEncodeDiagnostic(
        AotValue value,
        string expectedId,
        AotSourceSpan expectedSpan,
        AotJsonWriteLimits limits)
    {
        try
        {
            _ = AotJsonCodec.Encode(value, limits, expectedSpan, CancellationToken.None);
            throw new InvalidOperationException($"Expected {expectedId} from the closed JSON encoder.");
        }
        catch (ScriptException error) when (error.Diagnostic.Id == expectedId && error.Diagnostic.Span == expectedSpan)
        {
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

            IAotHostConfiguration injectedConfiguration = new FixtureHostConfiguration((AotHostConfigurationKey.RepositoriesPath, fixtureRoot));
            IAotHostDiscoveryRoots injectedRoots = new FixtureHostDiscoveryRoots(fixtureRoot, fixtureRoot);
            if (new RepositoryCatalog(injectedConfiguration, injectedRoots).Find("Test.Repository.*").Select(static entry => entry.Version).ToArray() is not ["2.0.0", "1.0.0"])
            {
                throw new InvalidOperationException("Repository catalog did not use injected closed host configuration.");
            }

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
            string injectedDestinationRoot = Path.Combine(fixtureRoot, "injected-extension-root");
            IAotHostConfiguration injectedConfiguration = new FixtureHostConfiguration(
                (AotHostConfigurationKey.PackageRoots, sourceRoot),
                (AotHostConfigurationKey.ExtensionsRoot, injectedDestinationRoot));
            ModuleInstallResult injectedInstall = new LocalPackageModuleInstaller(new StaticRepositoryCatalog(new RepositoryModuleEntry(
                "Fixture.Cleanup", "1.0.0", "injected configuration", "FixtureRepo", "https://example.invalid/fixture/", new Uri(cleanupPackage).AbsoluteUri,
                LocalPackageModuleInstaller.CalculateContentSha256(cleanupPackage), "legacy-pwsh-sidecar", "isolated-sidecar-import")), configuration: injectedConfiguration)
                .Install("Fixture.Cleanup", "FixtureRepo");
            if (injectedInstall.Status != "installed" || !injectedInstall.PackagePath.StartsWith(injectedDestinationRoot, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Install-Module did not use the injected closed host configuration roots.");
            }
            IAotHostDiscoveryRoots injectedDiscoveryRoots = new FixtureHostDiscoveryRoots(fixtureRoot, fixtureRoot);
            if (new CompositeHelpCatalog(injectedConfiguration, injectedDiscoveryRoots).Find("Get-FixtureCleanup").SingleOrDefault() is not { Synopsis: "cleanup fixture" }
                || new CompositeModuleCatalog(injectedConfiguration, injectedDiscoveryRoots).Find("Fixture.Cleanup").SingleOrDefault() is not { PackagePath: var injectedPackagePath }
                || !injectedPackagePath.StartsWith(injectedDestinationRoot, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Extension help/module catalogs did not use injected host configuration and discovery roots.");
            }
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

    private sealed class LifecycleFixtureCmdlet(
        CancellationTokenSource? cancellationSource = null,
        bool cancelDuringProcess = false,
        bool stopThrows = false) : AotCmdletBase
    {
        internal static CmdletDescriptor DescriptorContract { get; } = new("Test-Lifecycle", []);

        public override CmdletDescriptor Descriptor => DescriptorContract;
        public override IReadOnlyList<string> DefaultColumns { get; } = ["Value"];
        internal int BeginCalls { get; private set; }
        internal int ProcessCalls { get; private set; }
        internal int EndCalls { get; private set; }
        internal int StopCalls { get; private set; }

        protected override IEnumerable<IPipelineRecord> BeginProcessing(AotExecutionContext context)
        {
            BeginCalls++;
            return [];
        }

        protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
        {
            ProcessCalls++;
            return Process();
        }

        private IEnumerable<IPipelineRecord> Process()
        {
            yield return new TextRecord("first");
            if (cancelDuringProcess)
            {
                cancellationSource!.Cancel();
                yield return new TextRecord("unreachable-after-cancellation");
            }
        }

        protected override IEnumerable<IPipelineRecord> EndProcessing(AotExecutionContext context)
        {
            EndCalls++;
            return [];
        }

        protected override void StopProcessing(AotExecutionContext context)
        {
            StopCalls++;
            if (stopThrows)
            {
                throw new InvalidOperationException("fixture stop failure");
            }
        }
    }

    private sealed class LifecycleFixtureInputCmdlet(CancellationTokenSource cancellationSource) : AotPipelineInputCmdletBase<TextRecord>
    {
        public override CmdletDescriptor Descriptor { get; } = new("Test-InputLifecycle", []);
        public override IReadOnlyList<string> DefaultColumns { get; } = ["Value"];
        internal int BeginCalls { get; private set; }
        internal int ProcessCalls { get; private set; }
        internal int EndCalls { get; private set; }
        internal int StopCalls { get; private set; }

        protected override IEnumerable<IPipelineRecord> BeginProcessing(AotExecutionContext context)
        {
            BeginCalls++;
            return [];
        }

        protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context) => [];

        protected override IEnumerable<IPipelineRecord> ProcessPipelineInput(
            CommandInvocation invocation,
            IReadOnlyList<TextRecord> input,
            AotExecutionContext context)
        {
            ProcessCalls++;
            cancellationSource.Cancel();
            return input;
        }

        protected override IEnumerable<IPipelineRecord> EndProcessing(AotExecutionContext context)
        {
            EndCalls++;
            return [];
        }

        protected override void StopProcessing(AotExecutionContext context) => StopCalls++;
    }

    private sealed class FixtureProcessCatalog(IReadOnlyList<ProcessRecord> processes, bool emitUserError = false) : IProcessCatalog
    {
        public IEnumerable<ProcessRecord> AllProcesses() => processes;
        public ProcessRecord? GetById(int id) => processes.SingleOrDefault(process => process.Id == id);
        public IEnumerable<ProcessModuleRecord> Modules(int id, bool includeFileVersion, AotExecutionContext context) => [new ProcessModuleRecord(id, "fixture-module", "/fixture", includeFileVersion ? "1.0" : string.Empty)];
        public ProcessFileVersionRecord? MainFileVersion(int id, AotExecutionContext context) => new ProcessFileVersionRecord(id, "/fixture", "1.0", "1.0");
        public string? UserName(int id, AotExecutionContext context)
        {
            if (emitUserError)
            {
                context.WriteNonTerminatingError("FixtureInputUserError", "fixture input-stage user lookup failed");
                return null;
            }

            return "fixture-user";
        }
    }

    // A deliberately observable direct-resolution seam fixture. It gives the
    // Convert-Path test a closed Resolved/Missing/Rejected transcript without
    // depending on host filesystem state, and makes it impossible for a
    // future refactor to hide an early resolver call behind equivalent text.
    private sealed class CountingConvertPathCatalog : IPhysicalChildItemCatalog
    {
        private readonly List<string> _inputs = [];

        internal int ResolveCalls { get; private set; }
        internal IReadOnlyList<string> Inputs => _inputs;

        public IEnumerable<PhysicalChildItem> GetImmediateChildren(string path, AotExecutionContext context, AotSourceSpan? span) =>
            throw new InvalidOperationException("Convert-Path fixture must not enumerate children.");

        public PhysicalChildItem? GetDirectPhysicalItem(string path, AotExecutionContext context, AotSourceSpan? span) =>
            throw new InvalidOperationException("Convert-Path fixture must not describe an item.");

        public PhysicalItemProbeResult ProbeDirectPhysicalItem(string path, AotExecutionContext context, AotSourceSpan? span) =>
            throw new InvalidOperationException("Convert-Path fixture must not probe an item.");

        public DirectPhysicalPathResolution ResolveExistingDirectPhysicalPath(string path, AotExecutionContext context, AotSourceSpan? span)
        {
            ResolveCalls++;
            _inputs.Add(path);
            return path switch
            {
                "resolved-first" => DirectPhysicalPathResolution.Resolved(new DirectPhysicalPathRecord("/fixture/convert-first")),
                "resolved-last" => DirectPhysicalPathResolution.Resolved(new DirectPhysicalPathRecord("/fixture/convert-last")),
                "missing" => Missing(context, span),
                "rejected" => Rejected(context, span),
                "  " => Missing(context, span),
                _ => throw new InvalidOperationException($"Unexpected Convert-Path fixture input '{path}'."),
            };
        }

        private static DirectPhysicalPathResolution Missing(AotExecutionContext context, AotSourceSpan? span)
        {
            context.WriteNonTerminatingError(AotDiagnostics.Runtime(
                "AOT6206", "Cannot find direct physical path in Convert-Path fixture.", span,
                "direct physical path not found", "Use an existing direct physical file or directory path."));
            return DirectPhysicalPathResolution.Missing;
        }

        private static DirectPhysicalPathResolution Rejected(AotExecutionContext context, AotSourceSpan? span)
        {
            context.WriteNonTerminatingError(AotDiagnostics.Runtime(
                "AOT6201", "Convert-Path fixture rejected a provider-qualified path.", span,
                "provider-qualified path rejected", "Use a direct operating-system path without '::'."));
            return DirectPhysicalPathResolution.Rejected;
        }
    }

    private sealed class FixtureDiscoveryRoots(string currentDirectory) : IAotHostDiscoveryRoots
    {
        public string CurrentDirectory { get; } = currentDirectory;
        public string ApplicationBaseDirectory { get; } = currentDirectory;
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

    private sealed class FixtureDelay(CancellationTokenSource? cancellationSource = null) : IAotDelay
    {
        internal int Calls { get; private set; }
        internal int? LastMilliseconds { get; private set; }
        internal bool? LastTokenCanBeCanceled { get; private set; }

        public void DelayMilliseconds(int milliseconds, CancellationToken cancellationToken)
        {
            Calls++;
            LastMilliseconds = milliseconds;
            LastTokenCanBeCanceled = cancellationToken.CanBeCanceled;
            cancellationSource?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
        }
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

    private sealed class FixtureHostConfiguration(params (AotHostConfigurationKey Key, string? Value)[] values) : IAotHostConfiguration
    {
        private readonly IReadOnlyDictionary<AotHostConfigurationKey, string?> _values = values.ToDictionary(static value => value.Key, static value => value.Value);
        public string? Read(AotHostConfigurationKey key) => _values.TryGetValue(key, out string? value) ? value : null;
    }

    private sealed class FixtureHostPlatform(AotHostPlatformSnapshot snapshot) : IAotHostPlatform
    {
        public AotHostPlatformSnapshot Snapshot { get; } = snapshot;
    }

    private sealed class FixtureHostDiscoveryRoots(string currentDirectory, string applicationBaseDirectory) : IAotHostDiscoveryRoots
    {
        public string CurrentDirectory { get; } = currentDirectory;
        public string ApplicationBaseDirectory { get; } = applicationBaseDirectory;
    }

    private sealed class ThrowingHostConfiguration : IAotHostConfiguration
    {
        public string? Read(AotHostConfigurationKey key) => throw new InvalidOperationException("fixture configuration probe failure");
    }

    private sealed class ThrowingHostPlatform : IAotHostPlatform
    {
        public AotHostPlatformSnapshot Snapshot => throw new InvalidOperationException("fixture platform probe failure");
    }
}
