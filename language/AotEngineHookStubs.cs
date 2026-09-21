// Compatibility-only declarations for upstream parser/AST members that are
// coupled to the PowerShell execution engine.  They preserve the source-level
// AST closure while deliberately providing no execution implementation.
//
// Every member here is an AOT policy boundary: the parser may construct syntax
// nodes that mention engine concepts, but this host never invokes them.  The
// future lowerer must reject such nodes with an explicit diagnostic.

using System.Collections;
using System.Collections.ObjectModel;
using System.Linq.Expressions;

namespace System.Management.Automation
{
    public sealed class PSObject
    {
        public PSPropertyInfoCollection Properties { get; } = new();
        public object? BaseObject => null;
    }

    public sealed class PSPropertyInfoCollection
    {
        public PSPropertyInfo? this[string name] => null;
        public void Add(string name, object? value) => AotExecutionBoundary.Throw();
    }

    public sealed class PSPropertyInfo { public object? Value { get; set; } }

    public sealed class PSArgumentNullException : ArgumentNullException
    {
        public PSArgumentNullException(string? name) : base(name) { }
    }

    public sealed class PSInvalidOperationException : InvalidOperationException { }

    public static class PSTraceSource
    {
        public static PSArgumentNullException NewArgumentNullException(string name) => new(name);
        public static ArgumentException NewArgumentException(string name) => new(name);
        public static ArgumentOutOfRangeException NewArgumentOutOfRangeException(string name, object? value) => new(name, value, null);
        public static InvalidOperationException NewInvalidOperationException() => new();
    }

    public static class LanguagePrimitives
    {
        public static T ConvertTo<T>(object? value) => throw AotExecutionBoundary.Create();
        public static T ConvertTo<T>(object? value, IFormatProvider? provider) => throw AotExecutionBoundary.Create();
        public static object? ConvertTo(object? value, Type type, IFormatProvider? provider) => throw AotExecutionBoundary.Create();
        public static bool IsTrue(object? value) => throw AotExecutionBoundary.Create();
    }

    public sealed class RuntimeDefinedParameterDictionary
    {
        public static object[] EmptyParameterArray { get; } = [];
        public object? Data { get; set; }
    }

    public sealed class ScriptBlock
    {
        public ScriptBlock(Language.ScriptBlockAst ast, bool isFilter) => AotExecutionBoundary.Throw();
    }
    public sealed class PSModuleInfo
    {
        public ReadOnlyDictionary<string, Language.TypeDefinitionAst> GetExportedTypeDefinitions() => throw AotExecutionBoundary.Create();
    }
    public sealed class ExternalScriptInfo
    {
        public ExternalScriptInfo(string path, string name) { }
        public string ScriptContents => throw AotExecutionBoundary.Create();
    }
    public sealed class ScriptCallDepthException : Exception { }
    public static class PSSnapInInfo { public static bool IsPSSnapinIdValid(string name) => false; }
    public sealed class PSSnapInSpecification
    {
        public PSSnapInSpecification(string name) { }
        public Version? Version { get; set; }
    }

    public class ModuleSpecification
    {
        public ModuleSpecification() { }
        public ModuleSpecification(Hashtable table) { }
        public string? Name { get; set; }
    }

    public sealed class PSTypeName
    {
        public PSTypeName(Type type) { }
        public PSTypeName(string name) { }
    }

    public sealed class ExperimentalAttribute : Attribute { }

    public sealed class ParameterAttribute : Attribute
    {
        public string? ParameterSetName { get; set; }
        public int Position { get; set; }
    }

    public sealed class CmdletBindingAttribute : Attribute { }

    public static class SpecialVariables
    {
        public const string Input = "input";
        public const string True = "true";
        public const string False = "false";
        public const string Null = "null";
        public const string Args = "args";
        public static VariablePath FirstTokenVarPath { get; } = new("__aot_first_token");
        public static VariablePath LastTokenVarPath { get; } = new("__aot_last_token");
    }

    public static class AotExecutionBoundary
    {
        public static NotSupportedException Create() => new("This PowerShell execution-engine hook is excluded from the Native AOT language host.");
        public static void Throw() => throw Create();
    }

    public sealed class ExecutionContext
    {
        public static void CheckStackDepth() => AotExecutionBoundary.Throw();
        public void SetVariable(VariablePath path, object? value) => AotExecutionBoundary.Throw();
        public EngineIntrinsics Engine { get; } = new();
        public EngineIntrinsics EngineIntrinsics => Engine;
        public InternalHost InternalHost { get; } = new();
        public PSLanguageMode LanguageMode => PSLanguageMode.FullLanguage;
    }

    public sealed class EngineIntrinsics { public Language.Parser? EngineParser { get; set; } public SessionState SessionState { get; } = new(); }
    public sealed class SessionState { public PathIntrinsics Path { get; } = new(); }
    public sealed class PathIntrinsics { public PathInfo CurrentLocation { get; } = new(); }
    public sealed class PathInfo { public string Path => string.Empty; }
    public sealed class InternalHost { public object? ExternalHost => null; public HostUi UI { get; } = new(); }
    public sealed class HostUi { public void WriteErrorLine(string message) { } public void WriteWarningLine(string message) { } }
    public enum PSLanguageMode { FullLanguage, ConstrainedLanguage }
    public sealed class PowerShell : IDisposable
    {
        public Runspaces.Runspace? Runspace { get; set; }
        public static PowerShell Create() => throw AotExecutionBoundary.Create();
        public static PowerShell Create(Runspaces.RunspaceMode mode) => throw AotExecutionBoundary.Create();
        public PowerShell AddScript(string script) => throw AotExecutionBoundary.Create();
        public Collection<T> Invoke<T>() => throw AotExecutionBoundary.Create();
        public void Dispose() { }
    }

    // Position.cs exposes remoting serialization helpers as part of the
    // upstream AST API.  The Native AOT language host has no remoting surface;
    // retaining the signatures while throwing prevents accidental transport
    // activation from being mistaken for parser support.
    internal static class RemotingEncoder
    {
        internal static void AddNoteProperty<T>(PSObject destination, string propertyName, Func<T> valueGetter) => AotExecutionBoundary.Throw();
    }

    internal static class RemotingDecoder
    {
        internal static T GetPropertyValue<T>(PSObject source, string propertyName) => throw AotExecutionBoundary.Create();
    }
}

namespace System.Management.Automation
{
    internal static class Diagnostics
    {
        internal static void Assert(bool condition, string message) { }
    }

    // Referenced only by Parser's debug-time resource-key assertion.  This is
    // intentionally a type marker, not an ETS implementation.
    internal static class ExtendedTypeSystem { }
}

namespace System.Management.Automation.Internal
{
    internal static class StringUtil
    {
        internal static string Format(string format, params object?[] args) => string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
        internal static string Padding(int count) => new(' ', count);
    }
}

namespace System.Management.Automation.Language
{
    internal static class AotLanguageDiagnostics
    {
        internal const string ConfigurationExcludedErrorId = "AotConfigurationExcluded";
        internal const string ConfigurationExcludedMessage = "DSC configuration syntax is excluded from the Native AOT language host.";
        internal const string DynamicKeywordRegistryExcludedErrorId = "AotDynamicKeywordRegistryExcluded";
        internal const string DynamicKeywordRegistryExcludedMessage = "Dynamic keyword registration and parser-time delegates are excluded from the Native AOT language host.";

        internal static void ThrowDynamicKeywordRegistryExcluded() =>
            throw new NotSupportedException($"{DynamicKeywordRegistryExcludedErrorId}: {DynamicKeywordRegistryExcludedMessage}");
    }

    // Exact upstream VariableAnalysis.cs extension semantics. VariablePath
    // itself remains verbatim; full variable analysis/binding remains outside
    // this syntax-only closure.
    internal static class VariablePathExtensions
    {
        internal static bool IsAnyLocal(this System.Management.Automation.VariablePath variablePath) =>
            variablePath.IsUnscopedVariable || variablePath.IsLocal || variablePath.IsPrivate;
    }

    public sealed class ParseException : Exception
    {
        public ParseException() { }
        public ParseException(string message) : base(message) { }
        public ParseException(string message, Exception inner) : base(message, inner) { }
        public ParseException(ParseError[] errors) { Errors = errors; }
        public ParseError[]? Errors { get; }
    }

    internal sealed class Compiler
    {
        internal static Expression CallSetVariable(Expression variablePath, Expression rhs, Expression? attributes = null) => throw System.Management.Automation.AotExecutionBoundary.Create();
        internal static Expression ConvertValue(Expression rhs, IEnumerable<AttributeBaseAst> attributes) => throw System.Management.Automation.AotExecutionBoundary.Create();
        internal static Attribute GetAttribute(AttributeBaseAst ast) => throw System.Management.Automation.AotExecutionBoundary.Create();
        internal static System.Management.Automation.RuntimeDefinedParameterDictionary GetParameterMetaData(IEnumerable<ParameterAst> parameters, bool automaticPositions, ref bool usesCmdletBinding) => throw System.Management.Automation.AotExecutionBoundary.Create();
        internal ParameterExpression LocalVariablesParameter => throw System.Management.Automation.AotExecutionBoundary.Create();
        internal Type LocalVariablesTupleType => throw System.Management.Automation.AotExecutionBoundary.Create();
        internal bool Optimize => false;
        internal Expression VisitVariableExpression(VariableExpressionAst ast) => throw System.Management.Automation.AotExecutionBoundary.Create();
    }

    internal static class SemanticChecks
    {
        internal static void CheckArrayTypeNameDepth(ITypeName typeName, IScriptExtent extent, Parser parser) { }

        // This is the narrow semantic-check extraction required by the shared
        // parser contract. Foreach parameter diagnostics are parse-time
        // language behavior, not dynamic execution behavior: preserve their
        // stock IDs/extents without importing the upstream compiler/runspace.
        internal static void CheckAst(Parser parser, ScriptBlockAst ast)
        {
            foreach (Ast node in ast.FindAll(static candidate => candidate is ForEachStatementAst, searchNestedScriptBlocks: true))
            {
                var forEach = (ForEachStatementAst)node;
                if ((forEach.Flags & ForEachFlags.Parallel) == ForEachFlags.Parallel)
                {
                    parser.ReportError(
                        forEach.Extent,
                        nameof(ParserStrings.KeywordParameterReservedForFutureUse),
                        ParserStrings.KeywordParameterReservedForFutureUse,
                        "foreach",
                        "parallel");
                }

                if (forEach.ThrottleLimit is not null)
                {
                    parser.ReportError(
                        forEach.Extent,
                        nameof(ParserStrings.KeywordParameterReservedForFutureUse),
                        ParserStrings.KeywordParameterReservedForFutureUse,
                        "foreach",
                        "throttlelimit");
                }

                if (forEach.ThrottleLimit is not null
                    && (forEach.Flags & ForEachFlags.Parallel) != ForEachFlags.Parallel)
                {
                    parser.ReportError(
                        forEach.Extent,
                        nameof(ParserStrings.ThrottleLimitRequiresParallelFlag),
                        ParserStrings.ThrottleLimitRequiresParallelFlag);
                }
            }
        }
    }

    internal static class TypeResolver
    {
        internal static Type? ResolveTypeName(ITypeName typeName, out Exception? exception) { exception = null; return null; }
    }

    internal static class Utils
    {
        internal static int CombineHashCodes(int a, int b) => HashCode.Combine(a, b);
        internal static ReadOnlyCollection<T> EmptyReadOnlyCollection<T>() => new([]);
        internal static bool IsRunningOnProcessArchitectureARM() => false;
        internal static bool IsValidPSEditionValue(string value) => false;
        internal static bool IsWinPEHost() => false;
        internal static object? ParseBinary(string value) => null;
        internal static object? ParseBinary(string value, Type type) => null;
        internal static Version? StringToVersion(string value) => Version.TryParse(value, out var parsed) ? parsed : null;

        // Copied from upstream engine/Utils.cs because tokenizer.cs uses these
        // overloads while constructing NumberToken values.  They are lexical
        // support, not PowerShell runtime conversion/binding.
        internal static bool TryCast(System.Numerics.BigInteger value, out byte result)
        {
            if (value < byte.MinValue || value > byte.MaxValue) { result = 0; return false; }
            result = (byte)value;
            return true;
        }

        internal static bool TryCast(System.Numerics.BigInteger value, out sbyte result)
        {
            if (value < sbyte.MinValue || value > sbyte.MaxValue) { result = 0; return false; }
            result = (sbyte)value;
            return true;
        }

        internal static bool TryCast(System.Numerics.BigInteger value, out short result)
        {
            if (value < short.MinValue || value > short.MaxValue) { result = 0; return false; }
            result = (short)value;
            return true;
        }

        internal static bool TryCast(System.Numerics.BigInteger value, out ushort result)
        {
            if (value < ushort.MinValue || value > ushort.MaxValue) { result = 0; return false; }
            result = (ushort)value;
            return true;
        }

        internal static bool TryCast(System.Numerics.BigInteger value, out int result)
        {
            if (value < int.MinValue || value > int.MaxValue) { result = 0; return false; }
            result = (int)value;
            return true;
        }

        internal static bool TryCast(System.Numerics.BigInteger value, out uint result)
        {
            if (value < uint.MinValue || value > uint.MaxValue) { result = 0; return false; }
            result = (uint)value;
            return true;
        }

        internal static bool TryCast(System.Numerics.BigInteger value, out long result)
        {
            if (value < long.MinValue || value > long.MaxValue) { result = 0; return false; }
            result = (long)value;
            return true;
        }

        internal static bool TryCast(System.Numerics.BigInteger value, out ulong result)
        {
            if (value < ulong.MinValue || value > ulong.MaxValue) { result = 0; return false; }
            result = (ulong)value;
            return true;
        }

        internal static bool TryCast(System.Numerics.BigInteger value, out decimal result)
        {
            if (value < (System.Numerics.BigInteger)decimal.MinValue || (System.Numerics.BigInteger)decimal.MaxValue < value) { result = 0; return false; }
            result = (decimal)value;
            return true;
        }

        internal static bool TryCast(System.Numerics.BigInteger value, out double result)
        {
            if (value < (System.Numerics.BigInteger)double.MinValue || (System.Numerics.BigInteger)double.MaxValue < value) { result = 0; return false; }
            result = (double)value;
            return true;
        }

        internal static System.Numerics.BigInteger ParseBinary(ReadOnlySpan<char> digits, bool unsigned)
        {
            if (!unsigned)
            {
                if (digits[0] == '0')
                {
                    unsigned = true;
                }
                else
                {
                    switch (digits.Length)
                    {
                        case 8:
                        case 16:
                        case 32:
                        case 64:
                        case 96:
                        case int length when length >= 128:
                            break;
                        default:
                            unsigned = true;
                            break;
                    }
                }
            }

            const int MaxStackAllocation = 512;
            int outputByteCount = (digits.Length + 7) / 8;
            Span<byte> outputBytes = outputByteCount <= MaxStackAllocation ? stackalloc byte[outputByteCount] : new byte[outputByteCount];
            int outputByteIndex = outputBytes.Length - 1;

            int byteWalker;
            for (byteWalker = digits.Length - 1; byteWalker >= 7; byteWalker -= 8)
            {
                outputBytes[outputByteIndex--] =
                    (byte)((((digits[byteWalker - 7] << 7)
                            | (digits[byteWalker - 6] << 6)
                            | (digits[byteWalker - 5] << 5)
                            | (digits[byteWalker - 4] << 4))
                           | (((digits[byteWalker - 3] << 3)
                               | (digits[byteWalker - 2] << 2)
                               | (digits[byteWalker - 1] << 1)
                               | digits[byteWalker]) & 0b1111)));
            }

            if (byteWalker >= 0)
            {
                int currentByteValue = 0;
                for (int index = 0; index <= byteWalker; index++)
                {
                    currentByteValue = (currentByteValue << 1) | (digits[index] - '0');
                }

                outputBytes[outputByteIndex] = (byte)currentByteValue;
            }

            return new System.Numerics.BigInteger(outputBytes, isUnsigned: unsigned, isBigEndian: true);
        }
    }

    internal static class HelpCommentsParser
    {
        internal static CommentHelpInfo GetHelpContents(List<Token> comments, List<string> descriptions) => throw System.Management.Automation.AotExecutionBoundary.Create();
        internal static Tuple<List<Token>, List<string>>? GetHelpCommentTokens(IParameterMetadataProvider provider, Dictionary<Ast, Token[]> tokens) => null;
    }

    internal static class GetSafeValueVisitor
    {
        internal enum SafeValueContext { Default, GetPowerShell, ModuleAnalysis, AttributeArgument, SkipHashtableSizeCheck }

        // Ast.SafeGetValue is an execution/constant-evaluation API.  This
        // extraction keeps the public AST surface source-compatible but never
        // interprets an expression while parsing.
        internal static object GetSafeValue(
            Ast ast,
            System.Management.Automation.ExecutionContext? context,
            SafeValueContext safeValueContext) => throw System.Management.Automation.AotExecutionBoundary.Create();
    }

    internal static class IsConstantValueVisitor
    {
        internal static bool IsConstant(Ast ast, out object? value, bool forRequires = false) { value = null; return false; }
    }

    internal static class SymbolResolver { internal static void ResolveSymbols(Parser parser, ScriptBlockAst ast) { } }
    internal static class VariableAnalysis
    {
        // Upstream's sentinel is a structural AST invariant, not analysis
        // behavior.  Actual variable analysis remains outside this slice.
        internal const int Unanalyzed = -1;
        internal static void Analyze(ScriptBlockAst ast, bool? isScriptCmdlet = null) { }
    }
    internal static class TypeAccelerators { internal static Dictionary<string, Type> Get { get; } = new(StringComparer.OrdinalIgnoreCase); }
    internal static class StaticParameterBinder { internal static StaticBindingResult BindCommand(CommandAst command) => throw System.Management.Automation.AotExecutionBoundary.Create(); }
    internal sealed class StaticBindingResult { internal IReadOnlyDictionary<string, ParameterBindingResult?> BoundParameters { get; } = new Dictionary<string, ParameterBindingResult?>(); }
    internal sealed class ParameterBindingResult { internal object? Value { get; init; } }

    internal static class NumberExtensions
    {
        internal static System.Numerics.BigInteger AsBigInt(this double value) => new(Math.Round(value));
    }

    internal static class ScriptBlockToPowerShellConverter
    {
        internal static System.Management.Automation.PowerShell Convert(
            ScriptBlockAst body,
            ReadOnlyCollection<ParameterAst>? functionParameters,
            bool isTrustedInput,
            System.Management.Automation.ExecutionContext? context,
            Dictionary<string, object>? variables,
            bool filterNonUsingVariables,
            bool? createLocalScope,
            object[]? args) => throw System.Management.Automation.AotExecutionBoundary.Create();
    }

    internal static class PSVariableAssignmentBinder { internal static Expression Get(Expression target, Expression rhs) => throw System.Management.Automation.AotExecutionBoundary.Create(); }
    internal static class MutableTuple { internal static Expression GetAccessPath(Expression value, Type type, IEnumerable<System.Reflection.PropertyInfo> path) => throw System.Management.Automation.AotExecutionBoundary.Create(); }
    internal enum AutomaticVariable { }

    internal sealed class MemberAssignableValue : IAssignableValue
    {
        internal MemberExpressionAst? MemberExpression { get; set; }
        public Expression? GetValue(Compiler compiler, List<Expression> exprs, List<ParameterExpression> temps) => throw System.Management.Automation.AotExecutionBoundary.Create();
        public Expression SetValue(Compiler compiler, Expression rhs) => throw System.Management.Automation.AotExecutionBoundary.Create();
    }
    internal sealed class InvokeMemberAssignableValue : IAssignableValue
    {
        internal InvokeMemberExpressionAst? InvokeMemberExpressionAst { get; set; }
        public Expression? GetValue(Compiler compiler, List<Expression> exprs, List<ParameterExpression> temps) => throw System.Management.Automation.AotExecutionBoundary.Create();
        public Expression SetValue(Compiler compiler, Expression rhs) => throw System.Management.Automation.AotExecutionBoundary.Create();
    }
    internal sealed class ArrayAssignableValue : IAssignableValue
    {
        internal ArrayLiteralAst? ArrayLiteral { get; set; }
        public Expression? GetValue(Compiler compiler, List<Expression> exprs, List<ParameterExpression> temps) => throw System.Management.Automation.AotExecutionBoundary.Create();
        public Expression SetValue(Compiler compiler, Expression rhs) => throw System.Management.Automation.AotExecutionBoundary.Create();
    }
    internal sealed class IndexAssignableValue : IAssignableValue
    {
        internal IndexExpressionAst? IndexExpressionAst { get; set; }
        public Expression? GetValue(Compiler compiler, List<Expression> exprs, List<ParameterExpression> temps) => throw System.Management.Automation.AotExecutionBoundary.Create();
        public Expression SetValue(Compiler compiler, Expression rhs) => throw System.Management.Automation.AotExecutionBoundary.Create();
    }
}

namespace System.Management.Automation
{
    public static class TypeExtensions
    {
        public static bool HasDefaultCtor(this Type type) => type.GetConstructor(Type.EmptyTypes) is not null;

        // Copied from utils/ExtensionMethods.cs: parser validation of enum
        // underlying types depends on this syntax-only extension.
        internal static TypeCode GetTypeCode(this Type type) => Type.GetTypeCode(type);
    }
}

namespace Microsoft.PowerShell.Commands
{
    public sealed class ModuleSpecification : System.Management.Automation.ModuleSpecification
    {
        public ModuleSpecification() { }
        public ModuleSpecification(Hashtable table) : base(table) { }
    }
}

namespace System.Management.Automation.Runspaces
{
    public enum PSThreadOptions { UseCurrentThread }
    public enum RunspaceMode { CurrentRunspace }
    public sealed class Runspace : IDisposable
    {
        public static Runspace? DefaultRunspace { get; set; }
        public System.Management.Automation.ExecutionContext ExecutionContext { get; } = new();
        public PSThreadOptions ThreadOptions { get; set; }
        public void Open() => System.Management.Automation.AotExecutionBoundary.Throw();
        public void Close() { }
        public void Dispose() { }
    }

    public static class RunspaceFactory { public static Runspace CreateRunspace(InitialSessionState state) => throw System.Management.Automation.AotExecutionBoundary.Create(); }
    public sealed class InitialSessionState { public static InitialSessionState CreateDefault2() => throw System.Management.Automation.AotExecutionBoundary.Create(); }
}

namespace Microsoft.PowerShell
{
    public static class ToStringCodeMethods
    {
        // ReflectionTypeName.FullName is an execution-engine display API.  It
        // remains unavailable until type-name presentation has a deliberate
        // AOT design; it must not pull in type accelerators or ETS.
        internal static string Type(Type type, bool dropNamespaces = false, string? key = null) => throw System.Management.Automation.AotExecutionBoundary.Create();
    }

    public sealed class PowerShell : IDisposable
    {
        public System.Management.Automation.Runspaces.Runspace? Runspace { get; set; }
        public static PowerShell Create() => throw System.Management.Automation.AotExecutionBoundary.Create();
        public static PowerShell Create(System.Management.Automation.Runspaces.RunspaceMode mode) => throw System.Management.Automation.AotExecutionBoundary.Create();
        public PowerShell AddScript(string script) => throw System.Management.Automation.AotExecutionBoundary.Create();
        public Collection<T> Invoke<T>() => throw System.Management.Automation.AotExecutionBoundary.Create();
        public void Dispose() { }
    }
}

namespace System.Management.Automation.Security
{
    internal static class SecuritySupport { }

    // The only remaining active consumer is Parser's constrained-language
    // class check.  Returning Enforce gives that upstream path its normal
    // rejection diagnostic; it never permits an audit-mode bypass or logs.
    public enum SystemEnforcementMode { None, Audit, Enforce }

    public sealed class SystemPolicy
    {
        private SystemPolicy() { }
        public static SystemEnforcementMode GetSystemLockdownPolicy() => SystemEnforcementMode.Enforce;
        internal static void LogWDACAuditMessage(
            System.Management.Automation.ExecutionContext? context,
            string title,
            string message,
            string fqid,
            bool dropIntoDebugger = false) => System.Management.Automation.AotExecutionBoundary.Throw();
    }
}

namespace System.Management.Automation.Subsystem { internal static class AotSubsystemMarker { } }
namespace System.Management.Automation.Subsystem.DSC { internal static class AotDscSubsystemMarker { } }
namespace Microsoft.PowerShell.DesiredStateConfiguration.Internal { internal static class AotDscMarker { } }
