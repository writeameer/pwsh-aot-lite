using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PwshAotPortGenerator;

[Generator]
public sealed class PortManifestGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Most port contracts come from C# source.  Some static command data
        // (Get-Verb's descriptions) deliberately lives in a .resx file, so
        // retain the complete source inventory rather than asking the AOT
        // runtime to load resources or use reflection at execution time.
        IncrementalValueProvider<ImmutableArray<AdditionalText>> files = context.AdditionalTextsProvider.Collect();

        context.RegisterSourceOutput(files, static (productionContext, input) =>
            Emit(productionContext, ParseInventory(input, productionContext.CancellationToken)));
    }

    private static InventoryModel ParseInventory(ImmutableArray<AdditionalText> files, System.Threading.CancellationToken cancellationToken)
    {
        // AdditionalFiles are source inventory, not code the target project
        // compiles. Remove conditional directives so the manifest includes
        // declarations from every supported PowerShell platform branch.
        SourceDeclaration[] declarations = files
            .Where(static file => file.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .SelectMany(file =>
            {
                string text = RemoveConditionalDirectives(file.GetText(cancellationToken)?.ToString() ?? string.Empty);
                CompilationUnitSyntax root = CSharpSyntaxTree.ParseText(text, cancellationToken: cancellationToken).GetCompilationUnitRoot(cancellationToken);
                return root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                    .Select(declaration => new SourceDeclaration(Path.GetFileName(file.Path), declaration));
            })
            .ToArray();
        Dictionary<string, ClassDeclarationSyntax> classes = declarations
            .Select(static entry => entry.Declaration)
            .GroupBy(static declaration => declaration.Identifier.ValueText, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        List<CmdletModel> cmdlets = [];
        List<string> skipped = [];
        foreach (SourceDeclaration entry in declarations)
        {
            ClassDeclarationSyntax declaration = entry.Declaration;
            AttributeSyntax? cmdlet = FindAttribute(declaration.AttributeLists, "Cmdlet");
            if (cmdlet is null)
            {
                continue;
            }

            SeparatedSyntaxList<AttributeArgumentSyntax> arguments = cmdlet.ArgumentList?.Arguments ?? default;
            if (arguments.Count < 2)
            {
                skipped.Add(declaration.Identifier.ValueText + ": attribute has fewer than two positional arguments");
                continue;
            }

            string verb = LastIdentifier(arguments[0].Expression);
            string noun = ResolveString(arguments[1].Expression, declaration, classes);
            if (verb.Length == 0 || noun.Length == 0)
            {
                skipped.Add(declaration.Identifier.ValueText + ": verb or noun could not be resolved");
                continue;
            }

            cmdlets.Add(new CmdletModel(
                declaration.Identifier.ValueText,
                verb + "-" + noun,
                CollectBaseTypeChain(declaration, classes),
                CollectCapabilities(cmdlet),
                CollectLifecycle(declaration, classes),
                CollectOutputTypes(declaration),
                CollectParameters(declaration, classes),
                CollectMigrationBlockers(declaration, classes),
                entry.SourceFile));
        }

        return new InventoryModel(
            cmdlets.ToImmutableArray(),
            ExtractVerbCatalog(classes, ExtractVerbDescriptions(files, cancellationToken)),
            skipped.ToImmutableArray());
    }

    // Get-Verb is a useful first proof that a source generator can transfer
    // static PowerShell data, rather than copy/pasting a second hand-maintained
    // catalogue into the AOT target.  The original runtime finds these values
    // with reflection and ResourceManager; both steps happen here at build time.
    private static ImmutableArray<VerbModel> ExtractVerbCatalog(
        IReadOnlyDictionary<string, ClassDeclarationSyntax> classes,
        IReadOnlyDictionary<string, string> descriptions)
    {
        (string ClassName, string Group)[] groups =
        [
            ("VerbsCommon", "Common"),
            ("VerbsCommunications", "Communications"),
            ("VerbsData", "Data"),
            ("VerbsDiagnostic", "Diagnostic"),
            ("VerbsLifecycle", "Lifecycle"),
            ("VerbsOther", "Other"),
            ("VerbsSecurity", "Security"),
        ];

        Dictionary<string, string> aliases = ExtractStringConstants(classes, "VerbAliasPrefixes");
        List<VerbModel> catalog = [];
        foreach ((string className, string group) in groups)
        {
            // Preserve declaration order.  The original uses Type.GetFields(),
            // whose metadata order follows these source declarations today.
            foreach (KeyValuePair<string, string> constant in ExtractStringConstants(classes, className))
            {
                // The source uses the constant field name to make VerbInfo.
                // Keep the same distinction even if a future source changes a
                // constant's literal value.
                catalog.Add(new VerbModel(constant.Key, aliases.TryGetValue(constant.Key, out string? alias) ? alias : string.Empty, group,
                    descriptions.TryGetValue(constant.Key, out string? description) ? description : string.Empty));
            }
        }

        return catalog.ToImmutableArray();
    }

    private static Dictionary<string, string> ExtractStringConstants(IReadOnlyDictionary<string, ClassDeclarationSyntax> classes, string className)
    {
        if (!classes.TryGetValue(className, out ClassDeclarationSyntax? declaration))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return declaration.Members.OfType<FieldDeclarationSyntax>()
            .Where(static field => field.Modifiers.Any(SyntaxKind.ConstKeyword) && field.Declaration.Type.ToString() == "string")
            .SelectMany(static field => field.Declaration.Variables)
            .Where(static variable => variable.Initializer?.Value is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
            .ToDictionary(
                static variable => variable.Identifier.ValueText,
                static variable => ((LiteralExpressionSyntax)variable.Initializer!.Value).Token.ValueText,
                StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> ExtractVerbDescriptions(ImmutableArray<AdditionalText> files, System.Threading.CancellationToken cancellationToken)
    {
        AdditionalText? resource = files.FirstOrDefault(static file => Path.GetFileName(file.Path).Equals("VerbDescriptionStrings.resx", StringComparison.OrdinalIgnoreCase));
        if (resource is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            XDocument document = XDocument.Parse(resource.GetText(cancellationToken)?.ToString() ?? string.Empty);
            return document.Root?.Elements("data")
                .Select(element => (Name: (string?)element.Attribute("name"), Value: (string?)element.Element("value")))
                .Where(static item => !string.IsNullOrEmpty(item.Name) && item.Value is not null)
                .ToDictionary(static item => item.Name!, static item => item.Value!, StringComparer.Ordinal)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (System.Xml.XmlException)
        {
            // The normal source manifest remains useful even if an optional
            // data file is malformed; emit empty descriptions rather than
            // crashing all ports.
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static ImmutableArray<ParameterModel> CollectParameters(
        ClassDeclarationSyntax declaration,
        IReadOnlyDictionary<string, ClassDeclarationSyntax> classes)
    {
        List<ClassDeclarationSyntax> hierarchy = [];
        for (ClassDeclarationSyntax? current = declaration; current is not null; current = FindBaseClass(current, classes))
        {
            hierarchy.Add(current);
        }

        Dictionary<string, ParameterModel> parameters = new(StringComparer.Ordinal);
        foreach (ClassDeclarationSyntax current in hierarchy)
        {
            foreach (PropertyDeclarationSyntax property in current.Members.OfType<PropertyDeclarationSyntax>())
            {
                AttributeSyntax[] parameterAttributes = property.AttributeLists.SelectMany(static list => list.Attributes)
                    .Where(static attribute => AttributeName(attribute) == "Parameter")
                    .ToArray();
                if (parameterAttributes.Length == 0)
                {
                    continue;
                }

                List<string> aliases = property.AttributeLists.SelectMany(static list => list.Attributes)
                    .Where(static attribute => AttributeName(attribute) == "Alias")
                    .SelectMany(attribute => attribute.ArgumentList?.Arguments ?? default)
                    .Select(argument => ResolveString(argument.Expression, current, classes))
                    .Where(static alias => alias.Length != 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                ParameterSetModel[] parameterSets = parameterAttributes
                    .Select(attribute => CollectParameterSet(attribute, current, classes))
                    .ToArray();

                ValidationModel[] validations = property.AttributeLists.SelectMany(static list => list.Attributes)
                    .Where(static attribute => AttributeName(attribute).StartsWith("Validate", StringComparison.Ordinal))
                    .Select(static attribute => new ValidationModel(
                        AttributeName(attribute),
                        (attribute.ArgumentList?.Arguments ?? default).Select(static argument => argument.Expression.ToString()).ToImmutableArray()))
                    .ToArray();

                string typeName = property.Type.ToString();
                string shape = property.Type is ArrayTypeSyntax ? "Array"
                    : LastIdentifier(property.Type) == "SwitchParameter" ? "Switch"
                    : "Scalar";
                bool supportsWildcards = property.AttributeLists.SelectMany(static list => list.Attributes)
                    .Any(static attribute => AttributeName(attribute) == "SupportsWildcards");

                if (!parameters.ContainsKey(property.Identifier.ValueText))
                {
                    parameters[property.Identifier.ValueText] = new ParameterModel(
                        property.Identifier.ValueText,
                        typeName,
                        shape,
                        aliases.ToImmutableArray(),
                        parameterSets.ToImmutableArray(),
                        validations.ToImmutableArray(),
                        supportsWildcards);
                }
            }
        }

        return parameters.Values.OrderBy(static parameter => parameter.Name, StringComparer.Ordinal).ToImmutableArray();
    }

    private static ParameterSetModel CollectParameterSet(AttributeSyntax attribute, ClassDeclarationSyntax scope, IReadOnlyDictionary<string, ClassDeclarationSyntax> classes)
    {
        string parameterSet = ResolveNamedString(attribute, "ParameterSetName", scope, classes, "__AllParameterSets");
        int? position = ResolveNamedInt(attribute, "Position");
        bool mandatory = ResolveNamedBool(attribute, "Mandatory");
        int pipelineBinding = (ResolveNamedBool(attribute, "ValueFromPipeline") ? 1 : 0)
            | (ResolveNamedBool(attribute, "ValueFromPipelineByPropertyName") ? 2 : 0)
            | (ResolveNamedBool(attribute, "ValueFromRemainingArguments") ? 4 : 0);
        return new ParameterSetModel(parameterSet, position, mandatory, pipelineBinding);
    }

    private static ClassDeclarationSyntax? FindBaseClass(
        ClassDeclarationSyntax declaration,
        IReadOnlyDictionary<string, ClassDeclarationSyntax> classes)
    {
        TypeSyntax? baseType = declaration.BaseList?.Types.FirstOrDefault()?.Type;
        return baseType is null ? null : classes.TryGetValue(LastIdentifier(baseType), out ClassDeclarationSyntax? result) ? result : null;
    }

    private static AttributeSyntax? FindAttribute(SyntaxList<AttributeListSyntax> lists, string name) => lists
        .SelectMany(static list => list.Attributes)
        .FirstOrDefault(attribute => AttributeName(attribute) == name);

    private static string AttributeName(AttributeSyntax attribute)
    {
        string name = LastIdentifier(attribute.Name);
        return name.EndsWith("Attribute", StringComparison.Ordinal)
            ? name.Substring(0, name.Length - "Attribute".Length)
            : name;
    }

    private static string ResolveString(ExpressionSyntax expression, ClassDeclarationSyntax scope, IReadOnlyDictionary<string, ClassDeclarationSyntax> classes)
    {
        if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return literal.Token.ValueText;
        }

        string identifier = LastIdentifier(expression);
        foreach (ClassDeclarationSyntax current in EnumerateHierarchy(scope, classes))
        {
            VariableDeclaratorSyntax? constant = current.Members.OfType<FieldDeclarationSyntax>()
                .Where(static field => field.Modifiers.Any(SyntaxKind.ConstKeyword))
                .SelectMany(static field => field.Declaration.Variables)
                .FirstOrDefault(variable => variable.Identifier.ValueText == identifier);
            if (constant?.Initializer?.Value is LiteralExpressionSyntax value && value.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return value.Token.ValueText;
            }
        }

        return identifier;
    }

    private static IEnumerable<ClassDeclarationSyntax> EnumerateHierarchy(ClassDeclarationSyntax declaration, IReadOnlyDictionary<string, ClassDeclarationSyntax> classes)
    {
        for (ClassDeclarationSyntax? current = declaration; current is not null; current = FindBaseClass(current, classes))
        {
            yield return current;
        }
    }

    private static ImmutableArray<string> CollectBaseTypeChain(ClassDeclarationSyntax declaration, IReadOnlyDictionary<string, ClassDeclarationSyntax> classes)
    {
        List<string> names = [];
        for (ClassDeclarationSyntax? current = declaration; current is not null; current = FindBaseClass(current, classes))
        {
            TypeSyntax? baseType = current.BaseList?.Types.FirstOrDefault()?.Type;
            if (baseType is null)
            {
                break;
            }

            names.Add(LastIdentifier(baseType));
            if (!classes.ContainsKey(LastIdentifier(baseType)))
            {
                break;
            }
        }

        return names.ToImmutableArray();
    }

    private static CapabilityModel CollectCapabilities(AttributeSyntax cmdlet)
    {
        return new CapabilityModel(
            ResolveNamedBool(cmdlet, "SupportsShouldProcess"),
            ResolveNamedBool(cmdlet, "SupportsTransactions"),
            ResolveNamedText(cmdlet, "ConfirmImpact"),
            ResolveNamedText(cmdlet, "RemotingCapability"));
    }

    private static LifecycleModel CollectLifecycle(ClassDeclarationSyntax declaration, IReadOnlyDictionary<string, ClassDeclarationSyntax> classes)
    {
        bool Has(string name) => EnumerateHierarchy(declaration, classes)
            .SelectMany(static current => current.Members.OfType<MethodDeclarationSyntax>())
            .Any(method => method.Identifier.ValueText == name);
        return new LifecycleModel(Has("BeginProcessing"), Has("ProcessRecord"), Has("EndProcessing"), Has("StopProcessing"));
    }

    private static ImmutableArray<string> CollectOutputTypes(ClassDeclarationSyntax declaration) => declaration.AttributeLists
        .SelectMany(static list => list.Attributes)
        .Where(static attribute => AttributeName(attribute) == "OutputType")
        .SelectMany(attribute => attribute.ArgumentList?.Arguments ?? default)
        .Where(static argument => argument.NameEquals is null)
        .Select(static argument => argument.Expression.ToString())
        .Distinct(StringComparer.Ordinal)
        .ToImmutableArray();

    private static readonly ImmutableDictionary<string, string> EngineApiReasons = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["WriteObject"] = "output stream bridge",
        ["WriteError"] = "error stream bridge",
        ["ThrowTerminatingError"] = "terminating error bridge",
        ["ShouldProcess"] = "confirmation policy",
        ["SessionState"] = "session-state dependency",
        ["InvokeCommand"] = "command-discovery dependency",
        ["PSObject"] = "extended type-system dependency",
        ["LanguagePrimitives"] = "PowerShell conversion dependency",
        ["IDynamicParameters"] = "dynamic-parameter dependency",
        ["GetDynamicParameters"] = "dynamic-parameter dependency",
    }.ToImmutableDictionary(StringComparer.Ordinal);

    private static ImmutableArray<BlockerModel> CollectMigrationBlockers(ClassDeclarationSyntax declaration, IReadOnlyDictionary<string, ClassDeclarationSyntax> classes) => EnumerateHierarchy(declaration, classes)
        .SelectMany(static current => current.DescendantNodes().OfType<IdentifierNameSyntax>())
        .Select(static identifier => identifier.Identifier.ValueText)
        .Where(EngineApiReasons.ContainsKey)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(static api => api, StringComparer.Ordinal)
        .Select(api => new BlockerModel(api, EngineApiReasons[api]))
        .ToImmutableArray();

    private static bool ResolveNamedBool(AttributeSyntax attribute, string name)
    {
        ExpressionSyntax? expression = FindNamedArgument(attribute, name);
        return expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.TrueLiteralExpression);
    }

    private static int? ResolveNamedInt(AttributeSyntax attribute, string name)
    {
        ExpressionSyntax? expression = FindNamedArgument(attribute, name);
        return expression is LiteralExpressionSyntax literal && literal.Token.Value is int value ? value : null;
    }

    private static string ResolveNamedText(AttributeSyntax attribute, string name) => FindNamedArgument(attribute, name)?.ToString() ?? string.Empty;

    private static string ResolveNamedString(AttributeSyntax attribute, string name, ClassDeclarationSyntax scope, IReadOnlyDictionary<string, ClassDeclarationSyntax> classes, string fallback)
    {
        ExpressionSyntax? expression = FindNamedArgument(attribute, name);
        return expression is null ? fallback : ResolveString(expression, scope, classes);
    }

    private static ExpressionSyntax? FindNamedArgument(AttributeSyntax attribute, string name) => (attribute.ArgumentList?.Arguments ?? default)
        .FirstOrDefault(argument => argument.NameEquals?.Name.Identifier.ValueText == name)?.Expression;

    private static string LastIdentifier(SyntaxNode node) => node switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        GenericNameSyntax generic => generic.Identifier.ValueText,
        QualifiedNameSyntax qualified => LastIdentifier(qualified.Right),
        AliasQualifiedNameSyntax alias => LastIdentifier(alias.Name),
        MemberAccessExpressionSyntax member => LastIdentifier(member.Name),
        PredefinedTypeSyntax predefined => predefined.Keyword.ValueText,
        _ => node.ToString().Split('.').LastOrDefault() ?? string.Empty,
    };

    private static void Emit(SourceProductionContext context, InventoryModel inventory)
    {
        CmdletModel[] cmdlets = inventory.Cmdlets
            .OrderBy(static cmdlet => cmdlet.ClassName, StringComparer.Ordinal)
            .ToArray();
        StringBuilder source = new();
        source.AppendLine("// <auto-generated />");
        foreach (string skipped in inventory.Skipped.OrderBy(static item => item, StringComparer.Ordinal))
        {
            source.Append("// skipped: ").Append(skipped).AppendLine();
        }
        source.AppendLine("namespace PwshAotLite;");
        source.AppendLine();
        source.AppendLine("internal static partial class GeneratedCmdletPorts");
        source.AppendLine("{");
        source.Append("    internal static int Count => ").Append(cmdlets.Length).AppendLine(";");
        source.Append("    internal static IReadOnlyList<GeneratedVerbInfo> GetVerbCatalog { get; } = [");
        foreach (VerbModel verb in inventory.Verbs)
        {
            source.Append("new GeneratedVerbInfo(\"").Append(Escape(verb.Verb)).Append("\", \"")
                .Append(Escape(verb.AliasPrefix)).Append("\", \"").Append(Escape(verb.Group)).Append("\", \"")
                .Append(Escape(verb.Description)).Append("\"), ");
        }
        source.AppendLine("]; ");
        foreach (IGrouping<string, CmdletModel> group in cmdlets.GroupBy(static cmdlet => ToIdentifier(cmdlet.CommandName), StringComparer.Ordinal).OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            CmdletModel[] sameName = group.OrderBy(static cmdlet => cmdlet.ClassName, StringComparer.Ordinal).ThenBy(static cmdlet => cmdlet.SourceFile, StringComparer.Ordinal).ToArray();
            for (int index = 0; index < sameName.Length; index++)
            {
                CmdletModel cmdlet = sameName[index];
                string propertyName = sameName.Length == 1 ? group.Key : group.Key + "_" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                source.Append("    internal static SourceCmdletMetadata ").Append(propertyName).AppendLine(" { get; } = new(");
                source.Append("        \"").Append(Escape(cmdlet.CommandName)).AppendLine("\",");
                source.Append("        \"").Append(Escape(cmdlet.SourceFile)).AppendLine("\",");
                source.Append("        \"").Append(Escape(cmdlet.ClassName)).AppendLine("\",");
                source.Append("        [").Append(string.Join(", ", cmdlet.BaseTypeChain.Select(baseType => "\"" + Escape(baseType) + "\""))).AppendLine("],");
                source.Append("        new CmdletCapabilities(")
                    .Append(cmdlet.Capabilities.SupportsShouldProcess ? "true" : "false").Append(", ")
                    .Append(cmdlet.Capabilities.SupportsTransactions ? "true" : "false").Append(", \"")
                    .Append(Escape(cmdlet.Capabilities.ConfirmImpact)).Append("\", \"")
                    .Append(Escape(cmdlet.Capabilities.RemotingCapability)).AppendLine("\"),");
                source.Append("        new CmdletLifecycle(")
                    .Append(cmdlet.Lifecycle.HasBegin ? "true" : "false").Append(", ")
                    .Append(cmdlet.Lifecycle.HasProcess ? "true" : "false").Append(", ")
                    .Append(cmdlet.Lifecycle.HasEnd ? "true" : "false").Append(", ")
                    .Append(cmdlet.Lifecycle.HasStop ? "true" : "false").AppendLine("),");
                source.Append("        [").Append(string.Join(", ", cmdlet.OutputTypes.Select(output => "\"" + Escape(output) + "\""))).AppendLine("],");
                source.AppendLine("        [");
                foreach (ParameterModel parameter in cmdlet.Parameters)
                {
                    source.Append("            new SourceParameterMetadata(\"").Append(Escape(parameter.Name)).Append("\", \"")
                        .Append(Escape(parameter.TypeName)).Append("\", AotParameterShape.").Append(parameter.Shape).Append(", ");
                    source.Append("[").Append(string.Join(", ", parameter.Aliases.Select(alias => "\"" + Escape(alias) + "\""))).Append("], [");
                    source.Append(string.Join(", ", parameter.ParameterSets.Select(set => "new ParameterSetContract(\"" + Escape(set.Name) + "\", " + (set.Position.HasValue ? set.Position.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null") + ", " + (set.Mandatory ? "true" : "false") + ", (PipelineBindingSource)" + set.PipelineBinding.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")")));
                    source.Append("], [");
                    source.Append(string.Join(", ", parameter.Validations.Select(validation => "new ValidationRuleMetadata(\"" + Escape(validation.Name) + "\", [" + string.Join(", ", validation.Arguments.Select(argument => "\"" + Escape(argument) + "\"")) + "])")));
                    source.Append("], ").Append(parameter.SupportsWildcards ? "true" : "false").AppendLine("),");
                }

                source.AppendLine("        ],");
                source.Append("        [").Append(string.Join(", ", cmdlet.MigrationBlockers.Select(blocker => "new MigrationBlockerMetadata(\"" + Escape(blocker.Api) + "\", \"" + Escape(blocker.Reason) + "\")"))).AppendLine("]);");
            }
        }

        // The per-command properties are useful to executable adapters. Help
        // additionally needs a complete generated inventory, independent of
        // which adapters happen to be in the native binary.
        source.AppendLine("    internal static IReadOnlyList<SourceCmdletMetadata> All { get; } = [");
        foreach (IGrouping<string, CmdletModel> group in cmdlets.GroupBy(static cmdlet => ToIdentifier(cmdlet.CommandName), StringComparer.Ordinal).OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            CmdletModel[] sameName = group.OrderBy(static cmdlet => cmdlet.ClassName, StringComparer.Ordinal).ThenBy(static cmdlet => cmdlet.SourceFile, StringComparer.Ordinal).ToArray();
            for (int index = 0; index < sameName.Length; index++)
            {
                string propertyName = sameName.Length == 1 ? group.Key : group.Key + "_" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                source.Append("        ").Append(propertyName).AppendLine(",");
            }
        }
        source.AppendLine("    ]; ");

        source.AppendLine("}");
        context.AddSource("GeneratedCmdletPorts.g.cs", source.ToString());
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string RemoveConditionalDirectives(string text)
    {
        StringBuilder normalized = new();
        foreach (string line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("#if", StringComparison.Ordinal)
                || trimmed.StartsWith("#elif", StringComparison.Ordinal)
                || trimmed.StartsWith("#else", StringComparison.Ordinal)
                || trimmed.StartsWith("#endif", StringComparison.Ordinal))
            {
                normalized.AppendLine();
            }
            else
            {
                normalized.AppendLine(line);
            }
        }

        return normalized.ToString();
    }

    private static string ToIdentifier(string commandName)
    {
        StringBuilder identifier = new();
        foreach (char character in commandName)
        {
            if (char.IsLetterOrDigit(character) || character == '_')
            {
                identifier.Append(character);
            }
        }

        if (identifier.Length == 0 || char.IsDigit(identifier[0]))
        {
            identifier.Insert(0, '_');
        }

        return identifier.ToString();
    }

    private sealed record InventoryModel(ImmutableArray<CmdletModel> Cmdlets, ImmutableArray<VerbModel> Verbs, ImmutableArray<string> Skipped);
    private sealed record SourceDeclaration(string SourceFile, ClassDeclarationSyntax Declaration);
    private sealed record CmdletModel(
        string ClassName,
        string CommandName,
        ImmutableArray<string> BaseTypeChain,
        CapabilityModel Capabilities,
        LifecycleModel Lifecycle,
        ImmutableArray<string> OutputTypes,
        ImmutableArray<ParameterModel> Parameters,
        ImmutableArray<BlockerModel> MigrationBlockers,
        string SourceFile);
    private sealed record ParameterModel(
        string Name,
        string TypeName,
        string Shape,
        ImmutableArray<string> Aliases,
        ImmutableArray<ParameterSetModel> ParameterSets,
        ImmutableArray<ValidationModel> Validations,
        bool SupportsWildcards);
    private sealed record ParameterSetModel(string Name, int? Position, bool Mandatory, int PipelineBinding);
    private sealed record ValidationModel(string Name, ImmutableArray<string> Arguments);
    private sealed record CapabilityModel(bool SupportsShouldProcess, bool SupportsTransactions, string ConfirmImpact, string RemotingCapability);
    private sealed record LifecycleModel(bool HasBegin, bool HasProcess, bool HasEnd, bool HasStop);
    private sealed record BlockerModel(string Api, string Reason);
    private sealed record VerbModel(string Verb, string AliasPrefix, string Group, string Description);
}
