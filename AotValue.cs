using System.Collections.ObjectModel;

namespace PwshAotLite;

// The closed, transportable data model for the future AOT pipeline.  This is
// deliberately not an object wrapper: every supported value must enter through
// one of the named factories below.
internal enum AotValueKind
{
    Null,
    Boolean,
    Integer,
    Decimal,
    FloatingPoint,
    String,
    DateTime,
    Bytes,
    List,
    Record,
}

/// <summary>
/// An immutable value in the AOT data plane.  It has a fixed set of cases and
/// never reflects over, adapts, or retains an arbitrary CLR object.
/// </summary>
internal readonly struct AotValue : IEquatable<AotValue>
{
    private readonly bool _boolean;
    private readonly long _integer;
    private readonly decimal _decimal;
    private readonly double _floatingPoint;
    private readonly string? _text;
    private readonly DateTime _dateTime;
    private readonly ReadOnlyMemory<byte> _bytes;
    private readonly IReadOnlyList<AotValue>? _items;
    private readonly AotRecord? _record;

    private AotValue(AotValueKind kind)
    {
        Kind = kind;
    }

    private AotValue(bool value)
        : this(AotValueKind.Boolean)
    {
        _boolean = value;
    }

    private AotValue(long value)
        : this(AotValueKind.Integer)
    {
        _integer = value;
    }

    private AotValue(decimal value)
        : this(AotValueKind.Decimal)
    {
        _decimal = value;
    }

    private AotValue(double value)
        : this(AotValueKind.FloatingPoint)
    {
        _floatingPoint = value;
    }

    private AotValue(string value)
        : this(AotValueKind.String)
    {
        _text = value;
    }

    private AotValue(DateTime value)
        : this(AotValueKind.DateTime)
    {
        _dateTime = value;
    }

    private AotValue(ReadOnlyMemory<byte> value)
        : this(AotValueKind.Bytes)
    {
        // Copy on ingress.  No caller-owned mutable byte buffer crosses this
        // boundary, and the public accessor remains read-only.
        _bytes = value.ToArray();
    }

    private AotValue(IReadOnlyList<AotValue> value)
        : this(AotValueKind.List)
    {
        _items = value;
    }

    private AotValue(AotRecord value)
        : this(AotValueKind.Record)
    {
        _record = value;
    }

    internal AotValueKind Kind { get; }

    internal static AotValue Null { get; } = new(AotValueKind.Null);

    internal static AotValue FromBoolean(bool value) => new(value);

    internal static AotValue FromInteger(long value) => new(value);

    internal static AotValue FromDecimal(decimal value) => new(value);

    internal static AotValue FromFloatingPoint(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "AOT values do not represent NaN or infinity.");
        }

        return new AotValue(value);
    }

    internal static AotValue FromString(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    internal static AotValue FromDateTime(DateTime value) => new(value);

    internal static AotValue FromBytes(ReadOnlyMemory<byte> value) => new(value);

    internal static AotValue FromList(IEnumerable<AotValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        AotValue[] copy = values.ToArray();
        return new AotValue(Array.AsReadOnly(copy));
    }

    internal static AotValue FromRecord(AotRecord value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    internal bool TryGetProperty(string name, out AotValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (Kind == AotValueKind.Record)
        {
            return _record!.TryGetValue(name, out value);
        }

        value = default;
        return false;
    }

    internal bool TryGetBoolean(out bool value)
    {
        value = _boolean;
        return Kind == AotValueKind.Boolean;
    }

    internal bool TryGetInteger(out long value)
    {
        value = _integer;
        return Kind == AotValueKind.Integer;
    }

    internal bool TryGetDecimal(out decimal value)
    {
        value = _decimal;
        return Kind == AotValueKind.Decimal;
    }

    internal bool TryGetFloatingPoint(out double value)
    {
        value = _floatingPoint;
        return Kind == AotValueKind.FloatingPoint;
    }

    internal bool TryGetString(out string? value)
    {
        value = _text;
        return Kind == AotValueKind.String;
    }

    internal bool TryGetDateTime(out DateTime value)
    {
        value = _dateTime;
        return Kind == AotValueKind.DateTime;
    }

    internal bool TryGetBytes(out ReadOnlyMemory<byte> value)
    {
        value = _bytes;
        return Kind == AotValueKind.Bytes;
    }

    internal bool TryGetItems(out IReadOnlyList<AotValue>? value)
    {
        value = _items;
        return Kind == AotValueKind.List;
    }

    internal bool TryGetRecord(out AotRecord? value)
    {
        value = _record;
        return Kind == AotValueKind.Record;
    }

    public bool Equals(AotValue other)
    {
        if (Kind != other.Kind)
        {
            return false;
        }

        return Kind switch
        {
            AotValueKind.Null => true,
            AotValueKind.Boolean => _boolean == other._boolean,
            AotValueKind.Integer => _integer == other._integer,
            AotValueKind.Decimal => _decimal == other._decimal,
            AotValueKind.FloatingPoint => _floatingPoint.Equals(other._floatingPoint),
            AotValueKind.String => StringComparer.Ordinal.Equals(_text, other._text),
            AotValueKind.DateTime => _dateTime.Equals(other._dateTime),
            AotValueKind.Bytes => _bytes.Span.SequenceEqual(other._bytes.Span),
            AotValueKind.List => SequenceEqual(_items!, other._items!),
            AotValueKind.Record => _record!.Equals(other._record),
            _ => throw new InvalidOperationException($"Unknown AOT value kind '{Kind}'."),
        };
    }

    public override bool Equals(object? obj) => obj is AotValue value && Equals(value);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Kind);
        switch (Kind)
        {
            case AotValueKind.Null:
                break;
            case AotValueKind.Boolean:
                hash.Add(_boolean);
                break;
            case AotValueKind.Integer:
                hash.Add(_integer);
                break;
            case AotValueKind.Decimal:
                hash.Add(_decimal);
                break;
            case AotValueKind.FloatingPoint:
                hash.Add(_floatingPoint);
                break;
            case AotValueKind.String:
                hash.Add(_text, StringComparer.Ordinal);
                break;
            case AotValueKind.DateTime:
                hash.Add(_dateTime);
                break;
            case AotValueKind.Bytes:
                foreach (byte item in _bytes.Span)
                {
                    hash.Add(item);
                }

                break;
            case AotValueKind.List:
                foreach (AotValue item in _items!)
                {
                    hash.Add(item);
                }

                break;
            case AotValueKind.Record:
                hash.Add(_record);
                break;
            default:
                throw new InvalidOperationException($"Unknown AOT value kind '{Kind}'.");
        }

        return hash.ToHashCode();
    }

    private static bool SequenceEqual(IReadOnlyList<AotValue> left, IReadOnlyList<AotValue> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (!left[index].Equals(right[index]))
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed record AotField(string Name, AotValue Value);

/// <summary>
/// Immutable, ordered, case-insensitive record fields.  A record is the sole
/// generic property bag in the value plane; it cannot surface CLR properties.
/// </summary>
internal sealed class AotRecord : IEquatable<AotRecord>
{
    private readonly IReadOnlyList<AotField> _fields;
    private readonly IReadOnlyDictionary<string, AotValue> _lookup;

    internal AotRecord(IEnumerable<AotField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        List<AotField> ordered = [];
        Dictionary<string, AotValue> lookup = new(StringComparer.OrdinalIgnoreCase);
        foreach (AotField field in fields)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(field.Name);
            if (!lookup.TryAdd(field.Name, field.Value))
            {
                throw new ArgumentException($"A record already contains field '{field.Name}'.", nameof(fields));
            }

            ordered.Add(field);
        }

        _fields = new ReadOnlyCollection<AotField>(ordered);
        _lookup = new ReadOnlyDictionary<string, AotValue>(lookup);
    }

    internal IReadOnlyList<AotField> Fields => _fields;

    internal bool TryGetValue(string name, out AotValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _lookup.TryGetValue(name, out value);
    }

    internal AotValue GetRequiredValue(string name)
    {
        if (!TryGetValue(name, out AotValue value))
        {
            throw new KeyNotFoundException($"Record does not contain field '{name}'.");
        }

        return value;
    }

    internal string TextFor(string name) => AotValueText.Render(GetRequiredValue(name));

    internal AotRecord WithField(string name, AotValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        List<AotField> replacement = [];
        bool replaced = false;
        foreach (AotField field in _fields)
        {
            if (field.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                replacement.Add(new AotField(field.Name, value));
                replaced = true;
            }
            else
            {
                replacement.Add(field);
            }
        }

        if (!replaced)
        {
            replacement.Add(new AotField(name, value));
        }

        return new AotRecord(replacement);
    }

    internal AotRecord Project(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        List<AotField> projected = [];
        foreach (string name in names)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            projected.Add(new AotField(name, GetRequiredValue(name)));
        }

        return new AotRecord(projected);
    }

    public bool Equals(AotRecord? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || _fields.Count != other._fields.Count)
        {
            return false;
        }

        foreach (AotField field in _fields)
        {
            if (!other.TryGetValue(field.Name, out AotValue otherValue) || !field.Value.Equals(otherValue))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is AotRecord record && Equals(record);

    public override int GetHashCode()
    {
        // Field order is presentation data, not record identity.  XOR is
        // deliberately order-independent and remains compatible with Equals.
        int hash = 0;
        foreach (AotField field in _fields)
        {
            hash ^= HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(field.Name), field.Value.GetHashCode());
        }

        return hash;
    }
}

/// <summary>
/// Explicit, reflection-free comparison rules for lowerers and predicate
/// cmdlets.  This is intentionally narrower than PowerShell's ETS coercion.
/// </summary>
internal static class AotValueComparison
{
    internal static bool TryCompare(AotValue left, AotValue right, out int result) =>
        TryCompare(left, right, StringComparison.OrdinalIgnoreCase, out result);

    internal static bool TryCompare(AotValue left, AotValue right, StringComparison stringComparison, out int result)
    {
        if (left.Kind == AotValueKind.Null || right.Kind == AotValueKind.Null)
        {
            result = left.Kind == right.Kind ? 0 : left.Kind == AotValueKind.Null ? -1 : 1;
            return true;
        }

        if (IsNumber(left.Kind) && IsNumber(right.Kind))
        {
            return TryCompareNumbers(left, right, out result);
        }

        if (left.Kind != right.Kind)
        {
            result = default;
            return false;
        }

        switch (left.Kind)
        {
            case AotValueKind.Boolean:
                left.TryGetBoolean(out bool leftBoolean);
                right.TryGetBoolean(out bool rightBoolean);
                result = leftBoolean.CompareTo(rightBoolean);
                return true;
            case AotValueKind.String:
                left.TryGetString(out string? leftText);
                right.TryGetString(out string? rightText);
                result = string.Compare(leftText, rightText, stringComparison);
                return true;
            case AotValueKind.DateTime:
                left.TryGetDateTime(out DateTime leftDateTime);
                right.TryGetDateTime(out DateTime rightDateTime);
                result = leftDateTime.CompareTo(rightDateTime);
                return true;
            default:
                result = default;
                return false;
        }
    }

    private static bool IsNumber(AotValueKind kind) => kind is AotValueKind.Integer or AotValueKind.Decimal or AotValueKind.FloatingPoint;

    private static bool TryCompareNumbers(AotValue left, AotValue right, out int result)
    {
        if (left.Kind == AotValueKind.FloatingPoint || right.Kind == AotValueKind.FloatingPoint)
        {
            double leftNumber = ToDouble(left);
            double rightNumber = ToDouble(right);
            result = leftNumber.CompareTo(rightNumber);
            return true;
        }

        decimal leftExactNumber = ToDecimal(left);
        decimal rightExactNumber = ToDecimal(right);
        result = leftExactNumber.CompareTo(rightExactNumber);
        return true;
    }

    private static double ToDouble(AotValue value)
    {
        return value.Kind switch
        {
            AotValueKind.Integer when value.TryGetInteger(out long integer) => integer,
            AotValueKind.Decimal when value.TryGetDecimal(out decimal decimalValue) => (double)decimalValue,
            AotValueKind.FloatingPoint when value.TryGetFloatingPoint(out double floatingPoint) => floatingPoint,
            _ => throw new InvalidOperationException("Value is not numeric."),
        };
    }

    private static decimal ToDecimal(AotValue value)
    {
        return value.Kind switch
        {
            AotValueKind.Integer when value.TryGetInteger(out long integer) => integer,
            AotValueKind.Decimal when value.TryGetDecimal(out decimal decimalValue) => decimalValue,
            _ => throw new InvalidOperationException("Value is not an exact numeric value."),
        };
    }
}
