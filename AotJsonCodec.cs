using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PwshAotLite;

// J0 deliberately admits only JSON's native shapes and the existing closed
// value union. This is a data-plane utility: it has no registry, binder,
// provider, host, reflection, or generic CLR-object dependency.
internal sealed record AotJsonReadLimits(
    int MaximumInputBytes,
    int MaximumDepth,
    int MaximumValues,
    int MaximumContainerMembers,
    int MaximumStringBytes)
{
    internal static AotJsonReadLimits J0 { get; } = new(
        MaximumInputBytes: 1024 * 1024,
        MaximumDepth: 16,
        MaximumValues: 32_768,
        MaximumContainerMembers: 4_096,
        MaximumStringBytes: 65_536);

    internal void Validate()
    {
        if (MaximumInputBytes <= 0
            || MaximumDepth <= 0
            || MaximumValues <= 0
            || MaximumContainerMembers <= 0
            || MaximumStringBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumInputBytes), "JSON codec limits must all be positive.");
        }
    }
}

internal sealed record AotJsonWriteLimits(
    int MaximumOutputBytes,
    int MaximumDepth,
    int MaximumValues,
    int MaximumContainerMembers,
    int MaximumStringBytes)
{
    internal static AotJsonWriteLimits J0 { get; } = new(
        MaximumOutputBytes: 1024 * 1024,
        MaximumDepth: 16,
        MaximumValues: 32_768,
        MaximumContainerMembers: 4_096,
        MaximumStringBytes: 65_536);

    internal void Validate()
    {
        if (MaximumOutputBytes <= 0
            || MaximumDepth <= 0
            || MaximumValues <= 0
            || MaximumContainerMembers <= 0
            || MaximumStringBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumOutputBytes), "JSON codec limits must all be positive.");
        }
    }
}

internal static class AotJsonCodec
{
    internal static AotValue Decode(
        ReadOnlySpan<byte> utf8,
        AotJsonReadLimits limits,
        AotSourceSpan? inputSpan,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        token.ThrowIfCancellationRequested();

        if (utf8.Length > limits.MaximumInputBytes)
        {
            throw DecodeLimit(inputSpan, "JSON input exceeds the configured byte limit.");
        }

        try
        {
            Utf8JsonReader reader = new(
                utf8,
                isFinalBlock: true,
                new JsonReaderState(new JsonReaderOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    // The J0 depth limit is enforced by the recursive walk so
                    // its diagnostic remains stable rather than depending on
                    // a JsonReaderException message.
                    MaxDepth = 0,
                }));

            if (!reader.Read())
            {
                throw Malformed(inputSpan, "JSON input must contain exactly one root value.");
            }

            DecodeBudget budget = new(limits, inputSpan);
            AotValue value = ReadValue(ref reader, budget, depth: 1, token);
            token.ThrowIfCancellationRequested();
            if (reader.Read())
            {
                throw Malformed(inputSpan, "JSON input must contain exactly one root value with no trailing content.");
            }

            return value;
        }
        catch (JsonException)
        {
            throw Malformed(inputSpan, "JSON input is malformed or contains a disallowed JSON construct.");
        }
    }

    internal static byte[] Encode(
        AotValue value,
        AotJsonWriteLimits limits,
        AotSourceSpan? inputSpan,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        token.ThrowIfCancellationRequested();

        BoundedUtf8Buffer buffer = new(limits.MaximumOutputBytes, inputSpan);
        using Utf8JsonWriter writer = new(buffer, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = false,
        });

        WriteBudget budget = new(limits, inputSpan);
        WriteValue(writer, value, budget, depth: 1, token);
        token.ThrowIfCancellationRequested();
        writer.Flush();
        return buffer.ToArray();
    }

    private static AotValue ReadValue(ref Utf8JsonReader reader, DecodeBudget budget, int depth, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        budget.EnterValue(depth);

        return reader.TokenType switch
        {
            JsonTokenType.Null => AotValue.Null,
            JsonTokenType.True => AotValue.FromBoolean(true),
            JsonTokenType.False => AotValue.FromBoolean(false),
            JsonTokenType.String => AotValue.FromString(ReadString(ref reader, budget)),
            JsonTokenType.Number => ReadNumber(ref reader, budget),
            JsonTokenType.StartArray => ReadArray(ref reader, budget, depth, token),
            JsonTokenType.StartObject => ReadObject(ref reader, budget, depth, token),
            _ => throw Malformed(budget.Span, "JSON input contains an unexpected token."),
        };
    }

    private static AotValue ReadArray(ref Utf8JsonReader reader, DecodeBudget budget, int depth, CancellationToken token)
    {
        List<AotValue> values = [];
        int memberCount = 0;
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                return AotValue.FromList(values);
            }

            budget.CheckContainerMembers(++memberCount);
            values.Add(ReadValue(ref reader, budget, depth + 1, token));
        }

        throw Malformed(budget.Span, "JSON array is not terminated.");
    }

    private static AotValue ReadObject(ref Utf8JsonReader reader, DecodeBudget budget, int depth, CancellationToken token)
    {
        List<AotField> fields = [];
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        int memberCount = 0;
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return AotValue.FromRecord(new AotRecord(fields));
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw Malformed(budget.Span, "JSON object must contain property names followed by values.");
            }

            budget.CheckContainerMembers(++memberCount);
            string name = ReadString(ref reader, budget);
            if (string.IsNullOrWhiteSpace(name))
            {
                throw InvalidPropertyName(budget.Span, "JSON object property names cannot be blank or whitespace-only in the closed AOT record contract.");
            }

            if (!names.Add(name))
            {
                throw InvalidPropertyName(budget.Span, "JSON object property names cannot be duplicate or differ only by case in the closed AOT record contract.");
            }

            if (!reader.Read())
            {
                throw Malformed(budget.Span, "JSON object property is missing its value.");
            }

            fields.Add(new AotField(name, ReadValue(ref reader, budget, depth + 1, token)));
        }

        throw Malformed(budget.Span, "JSON object is not terminated.");
    }

    private static string ReadString(ref Utf8JsonReader reader, DecodeBudget budget)
    {
        budget.CheckStringBytes(ValueByteLength(ref reader));
        string value = reader.GetString() ?? throw Malformed(budget.Span, "JSON string token has no value.");
        budget.CheckStringBytes(Encoding.UTF8.GetByteCount(value));
        return value;
    }

    private static AotValue ReadNumber(ref Utf8JsonReader reader, DecodeBudget budget)
    {
        budget.CheckStringBytes(ValueByteLength(ref reader));
        if (reader.TryGetInt64(out long integer))
        {
            return AotValue.FromInteger(integer);
        }

        string raw = reader.HasValueSequence
            ? Encoding.UTF8.GetString(reader.ValueSequence.ToArray())
            : Encoding.UTF8.GetString(reader.ValueSpan);
        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal decimalValue))
        {
            return AotValue.FromDecimal(decimalValue);
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double floatingPoint)
            && double.IsFinite(floatingPoint))
        {
            return AotValue.FromFloatingPoint(floatingPoint);
        }

        throw NumberNotRepresentable(budget.Span);
    }

    private static long ValueByteLength(ref Utf8JsonReader reader) =>
        reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;

    private static void WriteValue(Utf8JsonWriter writer, AotValue value, WriteBudget budget, int depth, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        budget.EnterValue(depth);

        switch (value.Kind)
        {
            case AotValueKind.Null:
                writer.WriteNullValue();
                return;
            case AotValueKind.Boolean:
                value.TryGetBoolean(out bool boolean);
                writer.WriteBooleanValue(boolean);
                return;
            case AotValueKind.Integer:
                value.TryGetInteger(out long integer);
                writer.WriteNumberValue(integer);
                return;
            case AotValueKind.Decimal:
                value.TryGetDecimal(out decimal decimalValue);
                writer.WriteNumberValue(decimalValue);
                return;
            case AotValueKind.FloatingPoint:
                value.TryGetFloatingPoint(out double floatingPoint);
                writer.WriteNumberValue(floatingPoint);
                return;
            case AotValueKind.String:
                value.TryGetString(out string? text);
                budget.CheckStringBytes(Encoding.UTF8.GetByteCount(text!));
                writer.WriteStringValue(text);
                return;
            case AotValueKind.List:
                value.TryGetItems(out IReadOnlyList<AotValue>? items);
                writer.WriteStartArray();
                int itemCount = 0;
                foreach (AotValue item in items!)
                {
                    budget.CheckContainerMembers(++itemCount);
                    WriteValue(writer, item, budget, depth + 1, token);
                }

                writer.WriteEndArray();
                return;
            case AotValueKind.Record:
                value.TryGetRecord(out AotRecord? record);
                writer.WriteStartObject();
                int fieldCount = 0;
                foreach (AotField field in record!.Fields)
                {
                    budget.CheckContainerMembers(++fieldCount);
                    budget.CheckStringBytes(Encoding.UTF8.GetByteCount(field.Name));
                    writer.WritePropertyName(field.Name);
                    WriteValue(writer, field.Value, budget, depth + 1, token);
                }

                writer.WriteEndObject();
                return;
            case AotValueKind.DateTime:
            case AotValueKind.Bytes:
                throw UnsupportedValueKind(budget.Span, value.Kind);
            default:
                throw UnsupportedValueKind(budget.Span, value.Kind);
        }
    }

    private static ScriptException Malformed(AotSourceSpan? span, string message) =>
        new(AotDiagnostics.Runtime("AOT6301", message, span, "invalid JSON"));

    private static ScriptException DecodeLimit(AotSourceSpan? span, string message) =>
        new(AotDiagnostics.Runtime("AOT6302", message, span, "JSON decode limit"));

    private static ScriptException EncodeLimit(AotSourceSpan? span, string message) =>
        new(AotDiagnostics.Runtime("AOT6303", message, span, "JSON encode limit"));

    private static ScriptException InvalidPropertyName(AotSourceSpan? span, string message) =>
        new(AotDiagnostics.Runtime("AOT6304", message, span, "unsupported JSON property name"));

    private static ScriptException NumberNotRepresentable(AotSourceSpan? span) =>
        new(AotDiagnostics.Runtime("AOT6305", "JSON number is outside the closed AOT numeric domain.", span, "unsupported JSON number"));

    private static ScriptException UnsupportedValueKind(AotSourceSpan? span, AotValueKind kind) =>
        new(AotDiagnostics.Runtime("AOT6306", $"AOT value kind '{kind}' has no admitted J0 JSON representation.", span, "unsupported AOT value"));

    private sealed class DecodeBudget(AotJsonReadLimits limits, AotSourceSpan? span)
    {
        private int _values;

        internal AotSourceSpan? Span { get; } = span;

        internal void EnterValue(int depth)
        {
            if (depth > limits.MaximumDepth)
            {
                throw DecodeLimit(Span, "JSON nesting exceeds the configured depth limit.");
            }

            if (++_values > limits.MaximumValues)
            {
                throw DecodeLimit(Span, "JSON value count exceeds the configured limit.");
            }
        }

        internal void CheckContainerMembers(int count)
        {
            if (count > limits.MaximumContainerMembers)
            {
                throw DecodeLimit(Span, "JSON container member count exceeds the configured limit.");
            }
        }

        internal void CheckStringBytes(long byteCount)
        {
            if (byteCount > limits.MaximumStringBytes)
            {
                throw DecodeLimit(Span, "JSON string or property name exceeds the configured byte limit.");
            }
        }
    }

    private sealed class WriteBudget(AotJsonWriteLimits limits, AotSourceSpan? span)
    {
        private int _values;

        internal AotSourceSpan? Span { get; } = span;

        internal void EnterValue(int depth)
        {
            if (depth > limits.MaximumDepth)
            {
                throw EncodeLimit(Span, "JSON nesting exceeds the configured depth limit.");
            }

            if (++_values > limits.MaximumValues)
            {
                throw EncodeLimit(Span, "JSON value count exceeds the configured limit.");
            }
        }

        internal void CheckContainerMembers(int count)
        {
            if (count > limits.MaximumContainerMembers)
            {
                throw EncodeLimit(Span, "JSON container member count exceeds the configured limit.");
            }
        }

        internal void CheckStringBytes(int byteCount)
        {
            if (byteCount > limits.MaximumStringBytes)
            {
                throw EncodeLimit(Span, "JSON string or property name exceeds the configured byte limit.");
            }
        }
    }

    private sealed class BoundedUtf8Buffer(int maximumBytes, AotSourceSpan? span) : IBufferWriter<byte>
    {
        // Utf8JsonWriter is entitled to request a scratch segment larger than
        // the final JSON value (for example, 256 bytes for `null`). Keep the
        // logical output budget in Advance, where the actual committed byte
        // count is known, while bounding only that unavoidable scratch floor.
        // This preserves an exact four-byte `null` output limit without
        // treating writer implementation chunking as serialized output.
        private const int MinimumWriterScratchBytes = 256;
        private readonly int _allocationMaximum = Math.Max(maximumBytes, MinimumWriterScratchBytes);
        private byte[] _buffer = Array.Empty<byte>();
        private int _written;

        public void Advance(int count)
        {
            if (count < 0 || count > _buffer.Length - _written)
            {
                throw EncodeLimit(span, "JSON writer advanced outside the bounded output buffer.");
            }

            if (count > maximumBytes - _written)
            {
                throw EncodeLimit(span, "JSON output exceeds the configured byte limit.");
            }

            _written += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsMemory(_written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsSpan(_written);
        }

        internal byte[] ToArray() => _buffer.AsSpan(0, _written).ToArray();

        private void EnsureCapacity(int sizeHint)
        {
            if (sizeHint < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sizeHint));
            }

            int required = Math.Max(sizeHint, 1);
            if (_buffer.Length - _written >= required)
            {
                return;
            }

            if (required > _allocationMaximum - _written)
            {
                throw EncodeLimit(span, "JSON output exceeds the configured byte limit.");
            }

            int growth = Math.Max(required, Math.Max(256, _buffer.Length));
            int targetLength = Math.Min(_allocationMaximum, checked(_written + growth));
            Array.Resize(ref _buffer, targetLength);
        }
    }
}
