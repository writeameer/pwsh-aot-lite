using System.Collections.ObjectModel;

namespace PwshAotLite;

// The immutable batch is the one-way handoff from concrete cmdlet records to
// the generic data plane. It deliberately stores only AotRecord: neither the
// terminal renderer nor arbitrary CLR objects can leak back into transforms.
internal sealed class AotRecordBatch
{
    private readonly IReadOnlyList<AotRecord> _records;

    internal AotRecordBatch(IEnumerable<AotRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        _records = new ReadOnlyCollection<AotRecord>(records.ToArray());
    }

    internal IReadOnlyList<AotRecord> Records => _records;

    internal static AotRecordBatch FromTypedRows(
        AotExecutionContext context,
        IEnumerable<IPipelineRecord> rows,
        AotSourceSpan? boundarySpan = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rows);

        List<AotRecord> records = [];
        foreach (IPipelineRecord row in rows)
        {
            context.ThrowIfCancellationRequested();
            records.Add(PipelineValueAdapter.ToRecord(row, boundarySpan));
        }

        context.ThrowIfCancellationRequested();
        return new AotRecordBatch(records);
    }

    internal AotRecordBatch Apply(AotExecutionContext context, IReadOnlyList<AotRecordTransform> transforms)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(transforms);

        AotRecordBatch current = this;
        foreach (AotRecordTransform transform in transforms)
        {
            List<AotRecord> next = [];
            foreach (AotRecord record in current.Records)
            {
                context.ThrowIfCancellationRequested();
                if (transform.TryApply(record, out AotRecord? transformed))
                {
                    next.Add(transformed!);
                }
            }

            current = new AotRecordBatch(next);
        }

        context.ThrowIfCancellationRequested();
        return current;
    }

    internal void ValidateForProjection(AotExecutionContext context, AotRecordShape shape, AotSourceSpan? span)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(shape);
        foreach (AotRecord record in Records)
        {
            context.ThrowIfCancellationRequested();
            shape.ValidateForProjection(record, span);
        }

        context.ThrowIfCancellationRequested();
    }

    // Compatibility records exist only for a terminal projection. They never
    // re-enter function composition or generic transform execution.
    internal IReadOnlyList<IPipelineRecord> ToTerminalRows(AotExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        List<IPipelineRecord> rows = [];
        foreach (AotRecord record in Records)
        {
            context.ThrowIfCancellationRequested();
            rows.Add(new AotPipelineRecord(record));
        }

        return rows;
    }
}

// These are data-plane operations, not commands. Their arguments are already
// lowered from the upstream AST and they never inspect raw PowerShell source.
internal abstract class AotRecordTransform(AotSourceSpan? span)
{
    internal AotSourceSpan? Span { get; } = span;

    internal abstract bool TryApply(AotRecord record, out AotRecord? transformed);
}

internal sealed class AotRecordFilterTransform(Filter filter, AotSourceSpan? span) : AotRecordTransform(span)
{
    internal override bool TryApply(AotRecord record, out AotRecord? transformed)
    {
        transformed = record;
        return filter.Matches(AotValue.FromRecord(record));
    }
}

internal sealed class AotRecordProjectionTransform(
    IReadOnlyList<string> columns,
    AotSourceSpan? span) : AotRecordTransform(span)
{
    internal IReadOnlyList<string> Columns { get; } = columns;

    internal override bool TryApply(AotRecord record, out AotRecord? transformed)
    {
        HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);
        foreach (string column in Columns)
        {
            if (!selected.Add(column))
            {
                throw new ScriptException(AotDiagnostics.Runtime(
                    "AOT4007",
                    "Select-Object does not permit duplicate fields that differ only by case in the AOT subset.",
                    Span,
                    "duplicate projection field",
                    "Select each field only once."));
            }
        }

        try
        {
            AotRecordShape shape = new(Columns);
            transformed = record.Project(shape.Fields);
            shape.ValidateExact(transformed, Span);
            return true;
        }
        catch (KeyNotFoundException)
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT4008",
                "Select-Object requested a column not present on this pipeline value.",
                Span,
                "unknown projection field",
                "Use a field exposed by the preceding AOT pipeline record."));
        }
    }
}

// Shapes are metadata for explicit record projection and terminal contracts,
// not dynamic type descriptions. They carry a copied, ordered field sequence
// and never query CLR members.
internal sealed class AotRecordShape
{
    private readonly IReadOnlyList<string> _fields;

    internal AotRecordShape(IEnumerable<string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        List<string> ordered = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string field in fields)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(field);
            if (!seen.Add(field))
            {
                throw new ArgumentException($"A record shape already contains field '{field}'.", nameof(fields));
            }

            ordered.Add(field);
        }

        _fields = new ReadOnlyCollection<string>(ordered);
    }

    internal IReadOnlyList<string> Fields => _fields;

    internal void ValidateForProjection(AotRecord record, AotSourceSpan? span)
    {
        foreach (string field in Fields)
        {
            if (!record.TryGetValue(field, out _))
            {
                throw ShapeViolation(span, field);
            }
        }
    }

    internal void ValidateExact(AotRecord record, AotSourceSpan? span)
    {
        if (record.Fields.Count != Fields.Count)
        {
            throw ShapeViolation(span, null);
        }

        for (int index = 0; index < Fields.Count; index++)
        {
            if (!record.Fields[index].Name.Equals(Fields[index], StringComparison.OrdinalIgnoreCase))
            {
                throw ShapeViolation(span, Fields[index]);
            }
        }
    }

    private static ScriptException ShapeViolation(AotSourceSpan? span, string? field) => new(AotDiagnostics.Runtime(
        "AOT4011",
        field is null
            ? "AOT record output does not match its explicit shape contract."
            : $"AOT record output does not expose required field '{field}'.",
        span,
        "record shape contract mismatch",
        "Use a last direct Select-Object that names fields emitted by every record."));
}
