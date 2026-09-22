using System.Globalization;

namespace PwshAotLite;

// The adapter is intentionally the only crossing from the strongly typed
// cmdlet plane to the generic pipeline plane.  Ports keep returning their
// normal, reviewable records; adding a port requires an explicit case here
// instead of a CLR-property/reflection fallback.
internal static class PipelineValueAdapter
{
    // This is intentionally a closed registry of declared IPipelineRecord
    // shapes. AotPipelineRecord is accepted because it was already produced
    // by this same explicit boundary, not because arbitrary wrappers may
    // surface their members.
    internal static AotRecord ToRecord(IPipelineRecord row, AotSourceSpan? boundarySpan = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row is AotPipelineRecord projected)
        {
            return projected.Record;
        }

        if (row is not (ProcessRecord
            or ProcessModuleRecord
            or ProcessFileVersionRecord
            or UptimeRecord
            or CultureRecord
            or TimeZoneRecord
            or VerbRecord
            or DateRecord
            or TextRecord
            or FileHashRecord
            or CommandInfoRecord
            or ModuleInfoRecord
            or RepositoryModuleRecord
            or HelpRecord
            or InstallModuleRecord))
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT4009",
                "This pipeline record type is not registered for the Native AOT record boundary.",
                boundarySpan,
                "unregistered pipeline record",
                "Add an explicit reviewed record adapter before using this cmdlet in a structural pipeline."));
        }

        AotValue value = ToValue(row);
        if (!value.TryGetRecord(out AotRecord? record))
        {
            throw new InvalidOperationException("A declared pipeline record adapter did not produce an AOT record.");
        }

        return record!;
    }

    internal static AotValue ToValue(IPipelineRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return AotValue.FromRecord(row switch
        {
            ProcessRecord value => Record(
                Field("Name", Text(value.Name)),
                Field("Id", Integer(value.Id)),
                Field("CPU", Floating(value.CpuSeconds)),
                Field("WorkingSet", Integer(value.WorkingSetBytes)),
                Field("UserName", value.UserName is null ? AotValue.Null : Text(value.UserName))),
            ProcessModuleRecord value => Record(
                Field("Name", Text(value.ModuleName)),
                Field("ModuleName", Text(value.ModuleName)),
                Field("Id", Integer(value.ProcessId)),
                Field("ProcessId", Integer(value.ProcessId)),
                Field("FileName", Text(value.FileName)),
                Field("FileVersion", Text(value.FileVersion)),
                Field("CPU", Text(string.Empty))),
            ProcessFileVersionRecord value => Record(
                Field("Name", Text(value.FileName)),
                Field("FileName", Text(value.FileName)),
                Field("Id", Integer(value.ProcessId)),
                Field("ProcessId", Integer(value.ProcessId)),
                Field("FileVersion", Text(value.FileVersion)),
                Field("ProductVersion", Text(value.ProductVersion)),
                Field("CPU", Text(string.Empty))),
            UptimeRecord value => Record(
                Field("Value", value.Since is null ? Text(value.Value.ToString()) : DateTime(value.Since.Value)),
                Field("Uptime", Text(value.Value.ToString())),
                Field("Since", value.Since is null ? AotValue.Null : DateTime(value.Since.Value)),
                Field("Name", Text("Uptime"))),
            CultureRecord value => Record(
                Field("Name", Text(value.Name)),
                Field("DisplayName", Text(value.DisplayName)),
                Field("EnglishName", Text(value.EnglishName)),
                Field("LCID", Integer(value.Lcid))),
            TimeZoneRecord value => Record(
                Field("Id", Text(value.Id)),
                Field("Name", Text(value.Id)),
                Field("DisplayName", Text(value.DisplayName)),
                Field("StandardName", Text(value.StandardName)),
                Field("DaylightName", Text(value.DaylightName)),
                Field("BaseUtcOffset", Text(value.BaseUtcOffset.ToString())),
                Field("BaseUtcOffsetMinutes", Floating(value.BaseUtcOffset.TotalMinutes)),
                Field("SupportsDaylightSavingTime", Boolean(value.SupportsDaylightSavingTime))),
            VerbRecord value => Record(
                Field("Verb", Text(value.Verb)),
                Field("AliasPrefix", Text(value.AliasPrefix)),
                Field("Group", Text(value.Group)),
                Field("Description", Text(value.Description))),
            DateRecord value => Record(
                Field("Value", DateTime(value.Value)),
                Field("DateTime", DateTime(value.Value)),
                Field("DisplayHint", Text(value.DisplayHint))),
            TextRecord value => Record(Field("Value", Text(value.Value))),
            FileHashRecord value => Record(
                Field("Algorithm", Text(value.Algorithm)),
                Field("Hash", Text(value.Hash)),
                Field("Path", Text(value.Path))),
            CommandInfoRecord value => Record(
                Field("Name", Text(value.Name)),
                Field("CommandType", Text(value.CommandType)),
                Field("ModuleName", Text(value.ModuleName)),
                Field("Version", Text(value.Version)),
                Field("Source", Text(value.Source)),
                Field("Availability", Text(value.Availability)),
                Field("Status", Text(value.Status)),
                Field("Syntax", Text(value.Syntax))),
            ModuleInfoRecord value => Record(
                Field("Name", Text(value.Name)),
                Field("Version", Text(value.Version)),
                Field("Origin", Text(value.Origin)),
                Field("Availability", Text(value.Availability)),
                Field("Status", Text(value.Status)),
                Field("Path", Text(value.Path)),
                Field("Trust", Text(value.Trust)),
                Field("Provenance", Text(value.Provenance))),
            RepositoryModuleRecord value => Record(
                Field("Name", Text(value.Name)),
                Field("Version", Text(value.Version)),
                Field("Description", Text(value.Description)),
                Field("Repository", Text(value.Repository)),
                Field("RepositoryUri", Text(value.RepositoryUri)),
                Field("PackageUri", Text(value.PackageUri)),
                Field("Compatibility", Text(value.Compatibility)),
                Field("RegistrationMode", Text(value.RegistrationMode))),
            HelpRecord value => Record(Field("Value", Text(value.Content))),
            InstallModuleRecord value => Record(
                Field("Name", Text(value.Name)),
                Field("Version", Text(value.Version)),
                Field("Repository", Text(value.Repository)),
                Field("Status", Text(value.Status)),
                Field("Path", Text(value.Path)),
                Field("ContentSha256", Text(value.ContentSha256))),
            _ => throw new InvalidOperationException("No explicit AOT value adapter is registered for this pipeline record."),
        });
    }

    private static AotRecord Record(params AotField[] fields) => new(fields);

    private static AotField Field(string name, AotValue value) => new(name, value);

    private static AotValue Boolean(bool value) => AotValue.FromBoolean(value);

    private static AotValue Integer(int value) => AotValue.FromInteger(value);

    private static AotValue Integer(long value) => AotValue.FromInteger(value);

    private static AotValue Floating(double value) => AotValue.FromFloatingPoint(value);

    private static AotValue Text(string value) => AotValue.FromString(value);

    private static AotValue DateTime(DateTime value) => AotValue.FromDateTime(value);
}

// Compatibility shell for the existing table writer.  The contained record,
// rather than the source CLR type, is the pipeline result.  Rendering keeps
// the established textual shape where possible while every property lookup and
// projection comes from AotRecord.
internal sealed class AotPipelineRecord(AotRecord record) : IPipelineRecord
{
    internal AotRecord Record { get; } = record ?? throw new ArgumentNullException(nameof(record));

    public double NumberFor(string property)
    {
        if (!Record.TryGetValue(property, out AotValue value)
            || !AotValueComparison.TryCompare(value, AotValue.FromInteger(0), out _)
            || !TryNumber(value, out double number))
        {
            throw new ScriptException($"Where-Object does not support property '{property}' for this pipeline value.");
        }

        return number;
    }

    public string TextFor(string column)
    {
        if (!Record.TryGetValue(column, out AotValue value))
        {
            throw new ScriptException($"Select-Object does not support column '{column}' for this pipeline value.");
        }

        return AotValueText.Render(value);
    }

    private static bool TryNumber(AotValue value, out double number)
    {
        if (value.TryGetInteger(out long integer))
        {
            number = integer;
            return true;
        }

        if (value.TryGetDecimal(out decimal decimalValue))
        {
            number = (double)decimalValue;
            return true;
        }

        if (value.TryGetFloatingPoint(out double floatingPoint))
        {
            number = floatingPoint;
            return true;
        }

        number = default;
        return false;
    }
}

internal static class AotValueText
{
    internal static string Render(AotValue value)
    {
        if (value.Kind == AotValueKind.Null)
        {
            return string.Empty;
        }

        if (value.TryGetBoolean(out bool boolean))
        {
            return boolean.ToString();
        }

        if (value.TryGetInteger(out long integer))
        {
            return integer.ToString(CultureInfo.InvariantCulture);
        }

        if (value.TryGetDecimal(out decimal decimalValue))
        {
            return decimalValue.ToString(CultureInfo.InvariantCulture);
        }

        if (value.TryGetFloatingPoint(out double floatingPoint))
        {
            return floatingPoint.ToString("0.00", CultureInfo.InvariantCulture);
        }

        if (value.TryGetString(out string? text))
        {
            return text!;
        }

        if (value.TryGetDateTime(out DateTime dateTime))
        {
            return dateTime.ToString("O", CultureInfo.InvariantCulture);
        }

        if (value.TryGetBytes(out ReadOnlyMemory<byte> bytes))
        {
            return Convert.ToHexString(bytes.Span);
        }

        throw new ScriptException($"The AOT table renderer does not support value kind '{value.Kind}'.");
    }
}
