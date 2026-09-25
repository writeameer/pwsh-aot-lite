namespace PwshAotLite;

// The only executable J2 names. Registration is compile-time data derived from
// generated upstream metadata; this is not a general command discovery path.
internal static class AotStaticRecordStageRegistry
{
    private static readonly IReadOnlyDictionary<string, AotStaticRecordStageDescriptor> Descriptors =
        new Dictionary<string, AotStaticRecordStageDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            [GeneratedCmdletPorts.WhereObject.Name] = new AotWhereStaticNumericDescriptor(),
            [GeneratedCmdletPorts.SelectObject.Name] = new AotSelectStaticFieldsDescriptor(),
        };

    internal static bool IsRegistered(string name) => Descriptors.ContainsKey(name);

    internal static AotPipelineTailStagePlan Bind(AotCommandPlan command)
    {
        if (!Descriptors.TryGetValue(command.Name, out AotStaticRecordStageDescriptor? descriptor))
        {
            throw new InvalidOperationException($"No static record-stage descriptor is registered for '{command.Name}'.");
        }

        return descriptor.Bind(command);
    }
}

internal abstract class AotStaticRecordStageDescriptor
{
    internal abstract CmdletDescriptor Descriptor { get; }
    internal abstract AotPipelineTailStagePlan Bind(AotCommandPlan command);

    protected static ScriptException Binding(string id, string message, AotSourceSpan? span, string label, string help) =>
        new(AotDiagnostics.Binding(id, message, span) with { Label = label, Help = help });

    protected static AotValueArgumentPlan RequireValue(
        IReadOnlyList<AotCommandArgumentPlan> arguments,
        ref int index,
        AotParameterArgumentPlan parameter,
        string diagnostic)
    {
        if (parameter.AttachedValue is not null)
        {
            return new AotValueArgumentPlan(parameter.AttachedValue);
        }

        if (++index >= arguments.Count || arguments[index] is not AotValueArgumentPlan value)
        {
            throw Binding("AOT6403", diagnostic, parameter.Span, "invalid static stage binding", "Supply one direct value for the admitted parameter.");
        }

        return value;
    }

    protected static string RequireLiteralField(AotValueArgumentPlan value, string commandName)
    {
        if (value.Expression is not AotLiteralExpressionPlan literal
            || !literal.Value.TryGetString(out string? field)
            || string.IsNullOrWhiteSpace(field)
            || field.IndexOfAny(['*', '?', '[', ']']) >= 0)
        {
            throw Binding("AOT6406", $"{commandName} accepts only one direct non-wildcard field name in this Native AOT subset.", value.Span, "unsupported static stage form", "Use a literal field exposed by the preceding AOT record batch.");
        }

        return field;
    }

    protected static AotExpressionPlan RequireNumericValue(AotValueArgumentPlan value)
    {
        if (value.Expression is AotLiteralExpressionPlan literal)
        {
            if (literal.Value.Kind is AotValueKind.Integer or AotValueKind.Decimal or AotValueKind.FloatingPoint)
            {
                return literal;
            }

            // In a command argument, the upstream PowerShell AST represents
            // untyped numeric tokens such as -1000 as a string constant. This
            // normalization is over that one already-parsed literal atom; it
            // does not parse a script or invoke PowerShell conversion.
            if (literal.IsBareWord && literal.Value.TryGetString(out string? text) && TryParseFiniteNumericLiteral(text, out AotValue numeric))
            {
                return new AotLiteralExpressionPlan(numeric, value.Span);
            }

            throw Binding("AOT6403", "Where-Object requires a finite numeric Value in this Native AOT subset.", value.Span, "invalid numeric predicate", "Supply an integer, decimal, finite floating-point literal, or a reviewed closed-scope numeric value.");
        }

        if (value.Expression is not (AotLiteralExpressionPlan or AotVariableExpressionPlan))
        {
            throw Binding("AOT6406", "Where-Object does not admit expression, collection, or script value binding in this Native AOT subset.", value.Span, "unsupported static stage form", "Supply one finite numeric literal or closed-scope variable.");
        }

        return value.Expression;
    }

    private static bool TryParseFiniteNumericLiteral(string? text, out AotValue value)
    {
        if (long.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long integer))
        {
            value = AotValue.FromInteger(integer);
            return true;
        }

        if (decimal.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out decimal decimalValue))
        {
            value = AotValue.FromDecimal(decimalValue);
            return true;
        }

        if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double floatingPoint)
            && double.IsFinite(floatingPoint))
        {
            value = AotValue.FromFloatingPoint(floatingPoint);
            return true;
        }

        value = default;
        return false;
    }
}

internal sealed class AotWhereStaticNumericDescriptor : AotStaticRecordStageDescriptor
{
    private static readonly CmdletDescriptor WhereDescriptor = new(
        GeneratedCmdletPorts.WhereObject.Name,
        GeneratedCmdletPorts.WhereObject.CreateAotDescriptor(
            "Property", "Value", "EQ", "NE", "GT", "GE", "LT", "LE").Parameters,
        "Property",
        StaticBindingMode.J2WhereStaticNumeric);

    internal override CmdletDescriptor Descriptor => WhereDescriptor;

    internal override AotPipelineTailStagePlan Bind(AotCommandPlan command)
    {
        AotValueArgumentPlan? property = null;
        AotValueArgumentPlan? value = null;
        ParameterSpec? comparisonParameter = null;
        for (int index = 0; index < command.Arguments.Count; index++)
        {
            switch (command.Arguments[index])
            {
                case AotValueArgumentPlan positional:
                    if (property is null)
                    {
                        property = positional;
                    }
                    else if (comparisonParameter is not null && value is null)
                    {
                        value = positional;
                    }
                    else
                    {
                        throw Binding("AOT6403", "Where-Object requires exactly Property, one comparison operator, and Value.", positional.Span, "invalid static predicate", "Use '<field> -GT <number>' or the equivalent named Property/Value form.");
                    }

                    break;
                case AotParameterArgumentPlan parameter when MatchesGeneratedParameter("Property", parameter.Name):
                    if (property is not null)
                    {
                        throw Binding("AOT6403", "Where-Object Property was specified more than once.", parameter.Span, "duplicate static predicate parameter", "Supply Property once.");
                    }

                    property = RequireValue(command.Arguments, ref index, parameter, "Where-Object -Property requires one direct field value.");
                    break;
                case AotParameterArgumentPlan parameter when MatchesGeneratedParameter("Value", parameter.Name):
                    if (value is not null)
                    {
                        throw Binding("AOT6403", "Where-Object Value was specified more than once.", parameter.Span, "duplicate static predicate parameter", "Supply Value once.");
                    }

                    value = RequireValue(command.Arguments, ref index, parameter, "Where-Object -Value requires one finite numeric value.");
                    break;
                case AotParameterArgumentPlan parameter when TryGetNumericComparison(parameter.Name, out ParameterSpec? comparison):
                    if (comparisonParameter is not null || parameter.AttachedValue is not null)
                    {
                        throw Binding("AOT6403", "Where-Object requires exactly one unadorned numeric comparison operator.", parameter.Span, "invalid static predicate", "Use exactly one of -EQ, -NE, -GT, -GE, -LT, or -LE.");
                    }

                    comparisonParameter = comparison;
                    break;
                case AotParameterArgumentPlan parameter:
                    throw Binding("AOT6406", $"Where-Object parameter '-{parameter.Name}' is outside the Native AOT static numeric subset.", parameter.Span, "unsupported static stage parameter", "Use Property, Value, and one admitted case-insensitive numeric operator.");
                default:
                    throw new InvalidOperationException("Unknown static record-stage argument type.");
            }
        }

        if (property is null || comparisonParameter is null || value is null)
        {
            throw Binding("AOT6403", "Where-Object requires Property, exactly one numeric comparison operator, and Value.", command.CommandSpan, "incomplete static predicate", "Use '<field> -GT <number>' or the equivalent named Property/Value form.");
        }

        string field = RequireLiteralField(property, Descriptor.Name);
        AotExpressionPlan numericValue = RequireNumericValue(value);
        return new AotWhereStaticNumericStagePlan(Descriptor, field, ToComparison(comparisonParameter.Name), numericValue, property.Span, value.Span, command.CommandSpan);
    }

    private bool MatchesGeneratedParameter(string canonicalName, string suppliedName) =>
        Descriptor.Parameters.Single(parameter => parameter.Name.Equals(canonicalName, StringComparison.OrdinalIgnoreCase)).Matches(suppliedName);

    private bool TryGetNumericComparison(string suppliedName, out ParameterSpec? parameter)
    {
        parameter = Descriptor.Parameters.FirstOrDefault(candidate => candidate.Matches(suppliedName)
            && candidate.Name is "EQ" or "NE" or "GT" or "GE" or "LT" or "LE");
        return parameter is not null;
    }

    private static Comparison ToComparison(string canonicalName) => canonicalName switch
    {
        "EQ" => Comparison.Equal,
        "NE" => Comparison.NotEqual,
        "GT" => Comparison.GreaterThan,
        "GE" => Comparison.GreaterThanOrEqual,
        "LT" => Comparison.LessThan,
        "LE" => Comparison.LessThanOrEqual,
        _ => throw new InvalidOperationException($"Unknown generated static Where-Object comparison '{canonicalName}'."),
    };
}

internal sealed class AotSelectStaticFieldsDescriptor : AotStaticRecordStageDescriptor
{
    private static readonly CmdletDescriptor SelectDescriptor = new(
        GeneratedCmdletPorts.SelectObject.Name,
        GeneratedCmdletPorts.SelectObject.CreateAotDescriptor("Property").Parameters,
        "Property",
        StaticBindingMode.J2SelectStaticFields);

    internal override CmdletDescriptor Descriptor => SelectDescriptor;

    internal override AotPipelineTailStagePlan Bind(AotCommandPlan command)
    {
        List<AotValueArgumentPlan> fields = [];
        bool namedProperty = false;
        for (int index = 0; index < command.Arguments.Count; index++)
        {
            switch (command.Arguments[index])
            {
                case AotValueArgumentPlan positional when !namedProperty:
                    fields.Add(positional);
                    break;
                case AotValueArgumentPlan positional:
                    fields.Add(positional);
                    break;
                case AotParameterArgumentPlan parameter when MatchesGeneratedParameter(parameter.Name) && !namedProperty:
                    namedProperty = true;
                    if (parameter.AttachedValue is not null)
                    {
                        fields.Add(new AotValueArgumentPlan(parameter.AttachedValue));
                    }

                    break;
                case AotParameterArgumentPlan parameter when MatchesGeneratedParameter(parameter.Name):
                    throw Binding("AOT6404", "Select-Object Property was specified more than once.", parameter.Span, "duplicate projection parameter", "Supply one Property group with one or more direct fields.");
                case AotParameterArgumentPlan parameter:
                    throw Binding("AOT6406", $"Select-Object parameter '-{parameter.Name}' is outside the Native AOT static field subset.", parameter.Span, "unsupported static stage parameter", "Use only the generated Property parameter with direct field names.");
                default:
                    throw new InvalidOperationException("Unknown static record-stage argument type.");
            }
        }

        if (fields.Count == 0)
        {
            throw Binding("AOT6404", "Select-Object requires one or more direct Property fields.", command.CommandSpan, "missing projection field", "Supply direct literal field names, for example 'Select-Object Name, Id'.");
        }

        List<string> names = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (AotValueArgumentPlan field in fields)
        {
            string name = RequireLiteralField(field, Descriptor.Name);
            if (!seen.Add(name))
            {
                throw Binding("AOT6405", "Select-Object does not permit duplicate fields that differ only by case in the AOT subset.", field.Span, "duplicate projection field", "Select each field only once.");
            }

            names.Add(name);
        }

        return new AotSelectStaticFieldsStagePlan(Descriptor, names, fields[0].Span);
    }

    private bool MatchesGeneratedParameter(string suppliedName) =>
        Descriptor.Parameters.Single(parameter => parameter.Name.Equals("Property", StringComparison.OrdinalIgnoreCase)).Matches(suppliedName);
}

internal sealed class AotWhereStaticNumericStagePlan(
    CmdletDescriptor descriptor,
    string property,
    Comparison comparison,
    AotExpressionPlan value,
    AotSourceSpan propertySpan,
    AotSourceSpan valueSpan,
    AotSourceSpan commandSpan) : AotPipelineTailStagePlan
{
    internal override CmdletDescriptor Descriptor => descriptor;
    internal override AotRecordShape? OutputShape => null;

    internal override IAotRecordBatchStage Resolve(AotScope scope)
    {
        AotValue numeric = value.Evaluate(scope);
        if (numeric.Kind is not (AotValueKind.Integer or AotValueKind.Decimal or AotValueKind.FloatingPoint))
        {
            throw new ScriptException(AotDiagnostics.Binding(
                "AOT6403",
                "Where-Object requires a finite numeric Value in this Native AOT subset.",
                valueSpan) with { Label = "invalid numeric predicate", Help = "Supply an integer, decimal, finite floating-point literal, or a reviewed closed-scope numeric value." });
        }

        return new AotWhereStaticNumericStage(descriptor, property, comparison, numeric, propertySpan, commandSpan);
    }
}

internal sealed class AotSelectStaticFieldsStagePlan(
    CmdletDescriptor descriptor,
    IReadOnlyList<string> fields,
    AotSourceSpan propertySpan) : AotPipelineTailStagePlan
{
    internal override CmdletDescriptor Descriptor => descriptor;
    internal override AotRecordShape? OutputShape => new(fields);
    internal override IAotRecordBatchStage Resolve(AotScope scope) => new AotSelectStaticFieldsStage(descriptor, fields, propertySpan);
}

internal sealed class AotWhereStaticNumericStage(
    CmdletDescriptor descriptor,
    string property,
    Comparison comparison,
    AotValue value,
    AotSourceSpan propertySpan,
    AotSourceSpan commandSpan) : IAotRecordBatchStage
{
    public CmdletDescriptor Descriptor { get; } = descriptor;
    public AotRecordShape? OutputShape => null;

    public AotRecordBatch Apply(AotExecutionContext context, AotRecordBatch input) =>
        input.ApplyTransforms(context, [new AotRecordFilterTransform(new Filter(property, comparison, value, propertySpan), commandSpan)]);
}

internal sealed class AotSelectStaticFieldsStage(
    CmdletDescriptor descriptor,
    IReadOnlyList<string> fields,
    AotSourceSpan propertySpan) : IAotRecordBatchStage
{
    public CmdletDescriptor Descriptor { get; } = descriptor;
    public AotRecordShape? OutputShape => new(fields);

    public AotRecordBatch Apply(AotExecutionContext context, AotRecordBatch input) =>
        input.ApplyTransforms(context, [new AotRecordProjectionTransform(fields, propertySpan)]);
}
