using System.Globalization;
using System.Runtime.InteropServices;

namespace PwshAotLite;

// This is deliberately separate from IPhysicalFileResolver. Get-FileHash has
// a reviewed terminal-wildcard contract; changing it while adding directory
// enumeration would silently widen both commands. This catalog owns only a
// captured-root, direct physical child-item read operation.
internal interface IPhysicalChildItemCatalog
{
    IEnumerable<PhysicalChildItem> GetImmediateChildren(string path, AotExecutionContext context, AotSourceSpan? span);

    // Deliberately returns one already-acquired physical item.  Consumers such
    // as Get-Item must never implement "item" lookup by enumerating a
    // directory, since that changes a directory argument into its children.
    PhysicalChildItem? GetDirectPhysicalItem(string path, AotExecutionContext context, AotSourceSpan? span);

    // This is intentionally narrower than GetDirectPhysicalItem. Test-Path
    // only needs an existence/kind fact; acquiring display metadata or making
    // an item record would be needless policy and would risk turning a probe
    // into a second lookup contract.
    PhysicalItemProbeResult ProbeDirectPhysicalItem(string path, AotExecutionContext context, AotSourceSpan? span);

    // Resolve-Path needs only the canonical direct path after the same
    // no-follow acquisition used by the item operations.  It deliberately
    // does not describe an item, enumerate a directory, or expose a provider
    // result/object.
    DirectPhysicalPathResolution ResolveExistingDirectPhysicalPath(string path, AotExecutionContext context, AotSourceSpan? span);
}

internal enum PhysicalChildItemKind { File, Directory }

// The physical probe is a closed substrate result, not a Boolean plus an
// out-of-band errno. Missing is the one non-error outcome. Every boundary
// rejection has already emitted the catalog-owned diagnostic, so callers must
// never translate Rejected into false.
internal enum PhysicalItemProbeStatus { Found, Missing, Rejected }

internal readonly record struct PhysicalItemProbeResult(PhysicalItemProbeStatus Status, PhysicalChildItemKind? Kind = null)
{
    internal static PhysicalItemProbeResult Found(PhysicalChildItemKind kind) => new(PhysicalItemProbeStatus.Found, kind);
    internal static PhysicalItemProbeResult Missing { get; } = new(PhysicalItemProbeStatus.Missing);
    internal static PhysicalItemProbeResult Rejected { get; } = new(PhysicalItemProbeStatus.Rejected);
}

// A Resolve-Path result is intentionally not a bool, errno, PathInfo, or
// FileSystemInfo.  Missing and Rejected already have their distinct catalog
// diagnostic policy; only Resolved owns a closed path record.
internal enum DirectPhysicalPathResolutionStatus { Resolved, Missing, Rejected }

internal readonly record struct DirectPhysicalPathResolution(DirectPhysicalPathResolutionStatus Status, DirectPhysicalPathRecord? Record = null)
{
    internal static DirectPhysicalPathResolution Resolved(DirectPhysicalPathRecord record) => new(DirectPhysicalPathResolutionStatus.Resolved, record);
    internal static DirectPhysicalPathResolution Missing { get; } = new(DirectPhysicalPathResolutionStatus.Missing);
    internal static DirectPhysicalPathResolution Rejected { get; } = new(DirectPhysicalPathResolutionStatus.Rejected);
}

// The only data Resolve-Path admits across the AOT pipeline boundary.  The
// source emits PathInfo/provider objects; this bounded port emits only the
// canonical physical path string that its no-follow catalog acquired.
internal sealed record DirectPhysicalPathRecord(string Path) : IPipelineRecord
{
    public double NumberFor(string property) => throw new ScriptException($"Where-Object does not support property '{property}' for resolved physical paths.");

    public string TextFor(string column) => column.Equals("Path", StringComparison.OrdinalIgnoreCase)
        ? Path
        : throw new ScriptException($"Select-Object does not support column '{column}' for resolved physical paths.");
}

// This is deliberately a small, public-to-the-substrate result rather than
// leaking errno values into cmdlet policy.  In particular, a link in *any*
// component has one stable, source-spanned diagnostic at the command boundary.
internal enum MacOsNoFollowPathFailure { None, Missing, SymbolicLink, Unsupported, Other }

internal sealed record PhysicalChildItem(
    string Name,
    string FullPath,
    string ParentPath,
    PhysicalChildItemKind Kind,
    long? Length,
    DateTime LastWriteTimeUtc,
    string UnixMode,
    string User,
    string Group,
    DateTime LastWriteTime,
    long Size);

internal sealed class SystemPhysicalChildItemCatalog(IAotHostDiscoveryRoots roots, IAotHostPlatform platform) : IPhysicalChildItemCatalog
{
    private readonly string _capturedRoot = CanonicalizeRoot(roots.CurrentDirectory);
    private readonly IAotHostPlatform _platform = platform ?? throw new ArgumentNullException(nameof(platform));

    public IEnumerable<PhysicalChildItem> GetImmediateChildren(string path, AotExecutionContext context, AotSourceSpan? span)
    {
        if (!TryResolveDirectPhysicalPath(path, context, span, out string? canonicalPath))
        {
            return [];
        }

        PhysicalChildItem? exact = TryDescribe(canonicalPath!, context, span, emitMissing: true);
        if (exact is null)
        {
            return [];
        }

        if (exact.Kind == PhysicalChildItemKind.File)
        {
            return [exact];
        }

        return EnumerateDirectoryNoFollow(canonicalPath!, context, span);
    }

    public PhysicalChildItem? GetDirectPhysicalItem(string path, AotExecutionContext context, AotSourceSpan? span)
    {
        if (!TryResolveDirectPhysicalPath(path, context, span, out string? canonicalPath))
        {
            return null;
        }

        return TryDescribe(canonicalPath!, context, span, emitMissing: true);
    }

    public PhysicalItemProbeResult ProbeDirectPhysicalItem(string path, AotExecutionContext context, AotSourceSpan? span)
    {
        if (!TryResolveDirectPhysicalPath(path, context, span, out string? canonicalPath))
        {
            return PhysicalItemProbeResult.Rejected;
        }

        return ProbeCanonicalDirectPhysicalItem(canonicalPath!, context, span);
    }

    public DirectPhysicalPathResolution ResolveExistingDirectPhysicalPath(string path, AotExecutionContext context, AotSourceSpan? span)
    {
        // Empty has a source-specific diagnostic.  Whitespace intentionally
        // reaches canonicalization as a literal filename; trimming it would
        // silently turn an existence miss into the captured root.
        if (path.Length == 0)
        {
            WriteError(context, "AOT6213", "Resolve-Path does not accept an empty direct physical -Path value in the current Native AOT slice.", span,
                "empty direct physical path", "Supply one non-empty direct operating-system file or directory path.");
            return DirectPhysicalPathResolution.Rejected;
        }

        if (!TryResolveDirectPhysicalPath(path, context, span, out string? canonicalPath))
        {
            return DirectPhysicalPathResolution.Rejected;
        }

        DirectPhysicalAcquisition acquisition = AcquireCanonicalDirectPhysicalPath(canonicalPath!, context, span, emitMissingDiagnostic: true);
        return acquisition.Status switch
        {
            DirectPhysicalAcquisitionStatus.Found => DirectPhysicalPathResolution.Resolved(new DirectPhysicalPathRecord(canonicalPath!)),
            DirectPhysicalAcquisitionStatus.Missing => DirectPhysicalPathResolution.Missing,
            DirectPhysicalAcquisitionStatus.Rejected => DirectPhysicalPathResolution.Rejected,
            _ => throw new InvalidOperationException("Unexpected direct physical acquisition status."),
        };
    }

    private bool TryResolveDirectPhysicalPath(string path, AotExecutionContext context, AotSourceSpan? span, out string? canonicalPath)
    {
        canonicalPath = null;
        // The source Unix display contract depends on the UnixStat native
        // bridge. This first static extraction implements its macOS ABI only;
        // accepting a non-macOS result with invented/missing User and Group
        // values would be false compatibility.
        if (_platform.Snapshot is not { OperatingSystem: AotHostOperatingSystem.MacOS, Architecture: Architecture.Arm64 }
            || !OperatingSystem.IsMacOS()
            || RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
        {
            WriteError(context, "AOT6209", "Direct physical Unix display metadata is currently supported only on reviewed macOS arm64 hosts.", span,
                "unsupported filesystem display platform", "Use the macOS arm64 direct-physical slice or wait for a reviewed Linux/Windows metadata adapter.");
            return false;
        }

        context.ThrowIfCancellationRequested();

        if (IsProviderQualified(path))
        {
            WriteError(context, "AOT6201", $"Direct physical item access does not support provider-qualified path '{path}'.", span,
                "provider-qualified path rejected", "Use a direct operating-system path without '::'.");
            return false;
        }

        if (ContainsWildcard(path))
        {
            WriteError(context, "AOT6202", $"Direct physical item access does not support wildcard path '{path}' in the current Native AOT slice.", span,
                "wildcard path rejected", "Use one direct file or directory path.");
            return false;
        }

        canonicalPath = TryCanonicalize(path, context, span);
        return canonicalPath is not null;
    }

    private string? TryCanonicalize(string path, AotExecutionContext context, AotSourceSpan? span)
    {
        if (path.Length == 0)
        {
            path = ".";
        }

        try
        {
            // Never read Directory.GetCurrentDirectory() at invocation time:
            // the host composed this catalog with one immutable discovery root.
            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(_capturedRoot, path));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            WriteError(context, "AOT6203", $"Direct physical path '{path}' is invalid: {error.Message}", span,
                "invalid direct physical path", "Use a valid direct operating-system file or directory path.");
            return null;
        }
    }

    private PhysicalChildItem? TryDescribe(string canonicalPath, AotExecutionContext context, AotSourceSpan? span, bool emitMissing)
    {
        DirectPhysicalAcquisition acquisition = AcquireCanonicalDirectPhysicalPath(canonicalPath, context, span, emitMissing);
        if (acquisition.Status != DirectPhysicalAcquisitionStatus.Found)
        {
            return null;
        }

        string name = Path.GetFileName(canonicalPath.TrimEnd(Path.DirectorySeparatorChar));
        return TryCreateItem(name, canonicalPath, acquisition.Stat!.Value, emitMissing, context, span);
    }

    // Test-Path deliberately consumes the descriptor acquisition fact rather
    // than TryDescribe/GetDirectPhysicalItem. It must not acquire Unix display
    // metadata, create a physical item record, or enumerate a directory just
    // to answer an existence question.
    private PhysicalItemProbeResult ProbeCanonicalDirectPhysicalItem(string canonicalPath, AotExecutionContext context, AotSourceSpan? span)
    {
        DirectPhysicalAcquisition acquisition = AcquireCanonicalDirectPhysicalPath(canonicalPath, context, span, emitMissingDiagnostic: false);
        return acquisition.Status switch
        {
            DirectPhysicalAcquisitionStatus.Found => PhysicalItemProbeResult.Found(acquisition.Stat!.Value.IsDirectory ? PhysicalChildItemKind.Directory : PhysicalChildItemKind.File),
            DirectPhysicalAcquisitionStatus.Missing => PhysicalItemProbeResult.Missing,
            DirectPhysicalAcquisitionStatus.Rejected => PhysicalItemProbeResult.Rejected,
            _ => throw new InvalidOperationException("Unexpected direct physical acquisition status."),
        };
    }

    // The sole metadata-free descriptor acquisition primitive for direct path
    // consumers.  It is deliberately below cmdlet policy: callers choose only
    // whether a missing target receives the catalog diagnostic.  No caller may
    // substitute File.Exists, TryDescribe, provider dispatch, or enumeration.
    private DirectPhysicalAcquisition AcquireCanonicalDirectPhysicalPath(
        string canonicalPath,
        AotExecutionContext context,
        AotSourceSpan? span,
        bool emitMissingDiagnostic)
    {
        context.ThrowIfCancellationRequested();
        try
        {
            if (!MacOsPhysicalMetadata.TryAcquireNoFollow(canonicalPath, out MacOsPhysicalStat acquired, out MacOsNoFollowPathFailure failure))
            {
                if (failure == MacOsNoFollowPathFailure.Missing)
                {
                    if (emitMissingDiagnostic)
                    {
                        WriteNoFollowFailure(context, canonicalPath, failure, span);
                    }

                    return DirectPhysicalAcquisition.Missing;
                }

                WriteNoFollowFailure(context, canonicalPath, failure, span);
                return DirectPhysicalAcquisition.Rejected;
            }

            string name = Path.GetFileName(canonicalPath.TrimEnd(Path.DirectorySeparatorChar));
            if (IsHidden(name, acquired.Flags))
            {
                WriteError(context, "AOT6204", $"Hidden direct physical path '{canonicalPath}' requires unsupported -Force behavior.", span,
                    "hidden path rejected", "Use a non-hidden path in the current Native AOT slice.");
                return DirectPhysicalAcquisition.Rejected;
            }

            if (!acquired.IsRegularFileOrDirectory)
            {
                WriteError(context, "AOT6210", $"Direct physical path '{canonicalPath}' is not a regular file or directory.", span,
                    "unsupported physical item type", "Use a non-link direct physical file or directory path.");
                return DirectPhysicalAcquisition.Rejected;
            }

            context.ThrowIfCancellationRequested();
            return DirectPhysicalAcquisition.Found(acquired);
        }
        catch (FileNotFoundException)
        {
            if (emitMissingDiagnostic)
            {
                WriteError(context, "AOT6206", $"Cannot find direct physical path '{canonicalPath}'.", span,
                    "direct physical path not found", "Use an existing direct physical file or directory path.");
            }

            return DirectPhysicalAcquisition.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            if (emitMissingDiagnostic)
            {
                WriteError(context, "AOT6206", $"Cannot find direct physical path '{canonicalPath}'.", span,
                    "direct physical path not found", "Use an existing direct physical file or directory path.");
            }

            return DirectPhysicalAcquisition.Missing;
        }
        catch (UnauthorizedAccessException error)
        {
            WriteError(context, "AOT6207", $"Cannot read direct physical path '{canonicalPath}': {error.Message}", span,
                "physical path access denied", "Choose a readable direct physical file or directory.");
            return DirectPhysicalAcquisition.Rejected;
        }
        catch (IOException error)
        {
            WriteError(context, "AOT6208", $"Cannot read direct physical path '{canonicalPath}': {error.Message}", span,
                "physical path read failed", "Choose an accessible direct physical file or directory.");
            return DirectPhysicalAcquisition.Rejected;
        }
    }

    private enum DirectPhysicalAcquisitionStatus { Found, Missing, Rejected }

    private readonly record struct DirectPhysicalAcquisition(DirectPhysicalAcquisitionStatus Status, MacOsPhysicalStat? Stat = null)
    {
        internal static DirectPhysicalAcquisition Found(MacOsPhysicalStat stat) => new(DirectPhysicalAcquisitionStatus.Found, stat);
        internal static DirectPhysicalAcquisition Missing { get; } = new(DirectPhysicalAcquisitionStatus.Missing);
        internal static DirectPhysicalAcquisition Rejected { get; } = new(DirectPhysicalAcquisitionStatus.Rejected);
    }

    private static string CanonicalizeRoot(string root) => Path.GetFullPath(root);

    private static bool ContainsWildcard(string path) => path.IndexOfAny(['*', '?', '[', ']']) >= 0;

    private static bool IsProviderQualified(string path)
    {
        int separator = path.IndexOf("::", StringComparison.Ordinal);
        return separator > 0 && path[..separator].All(static character => char.IsLetterOrDigit(character) || character is '-' or '_');
    }

    private static bool IsHidden(string name, uint darwinFlags) =>
        name.StartsWith(".", StringComparison.Ordinal)
        // UF_HIDDEN is the source of FileAttributes.Hidden on Darwin. Keep it
        // in the stat fact set so classification and metadata have one
        // no-follow acquisition path.
        || (darwinFlags & 0x00008000u) != 0;

    private PhysicalChildItem? TryCreateItem(
        string name,
        string fullPath,
        MacOsPhysicalStat stat,
        bool emitMissing,
        AotExecutionContext context,
        AotSourceSpan? span)
    {
        context.ThrowIfCancellationRequested();
        if (!MacOsPhysicalMetadata.TryCreate(stat, out MacOsPhysicalMetadata metadata, out bool isDirectory))
        {
            if (emitMissing)
            {
                WriteError(context, "AOT6210", $"Cannot read Unix display metadata for direct physical path '{fullPath}'.", span,
                    "physical metadata read failed", "Choose an accessible non-link macOS physical path.");
            }

            return null;
        }

        return new PhysicalChildItem(
            name,
            fullPath,
            Path.GetDirectoryName(fullPath) ?? fullPath,
            isDirectory ? PhysicalChildItemKind.Directory : PhysicalChildItemKind.File,
            isDirectory ? null : metadata.Size,
            metadata.LastWriteTimeUtc,
            metadata.UnixMode,
            metadata.User,
            metadata.Group,
            metadata.LastWriteTime,
            metadata.Size);
    }

    private IEnumerable<PhysicalChildItem> EnumerateDirectoryNoFollow(string canonicalPath, AotExecutionContext context, AotSourceSpan? span)
    {
        context.ThrowIfCancellationRequested();
        if (!MacOsPhysicalMetadata.TryOpenDirectoryNoFollow(canonicalPath, out IntPtr directory, out MacOsNoFollowPathFailure failure))
        {
            WriteNoFollowFailure(context, canonicalPath, failure, span);
            return [];
        }

        try
        {
            List<PhysicalChildItem> directories = [];
            List<PhysicalChildItem> files = [];
            while (true)
            {
                context.ThrowIfCancellationRequested();
                string? name = MacOsPhysicalMetadata.ReadNextDirectoryEntry(directory);
                if (name is null)
                {
                    break;
                }

                if (name is "." or ".." || name.StartsWith(".", StringComparison.Ordinal))
                {
                    continue;
                }

                context.ThrowIfCancellationRequested();
                if (!MacOsPhysicalMetadata.TryReadRelativeNoFollow(directory, name, out MacOsPhysicalStat stat)
                    || stat.IsSymbolicLink)
                {
                    // A child can disappear or be replaced while enumerating.
                    // fstatat(..., AT_SYMLINK_NOFOLLOW) means that condition
                    // never follows it; suppress it like the existing direct
                    // enumeration race policy.
                    continue;
                }

                if (IsHidden(name, stat.Flags))
                {
                    continue;
                }

                string fullPath = Path.Combine(canonicalPath, name);
                PhysicalChildItem? item = TryCreateItem(name, fullPath, stat, emitMissing: false, context, span);
                if (item is null)
                {
                    continue;
                }

                (item.Kind == PhysicalChildItemKind.Directory ? directories : files).Add(item);
            }

            SortChildren(directories, context);
            SortChildren(files, context);
            context.ThrowIfCancellationRequested();
            return directories.Concat(files).ToArray();
        }
        finally
        {
            MacOsPhysicalMetadata.CloseDirectory(directory);
        }
    }

    private static void SortChildren(List<PhysicalChildItem> children, AotExecutionContext context)
    {
        context.ThrowIfCancellationRequested();
        children.Sort((left, right) =>
        {
            context.ThrowIfCancellationRequested();
            return StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name);
        });
        context.ThrowIfCancellationRequested();
    }

    private static void WriteError(AotExecutionContext context, string id, string message, AotSourceSpan? span, string label, string help) =>
        context.WriteNonTerminatingError(AotDiagnostics.Runtime(id, message, span, label, help));

    private static void WriteNoFollowFailure(AotExecutionContext context, string path, MacOsNoFollowPathFailure failure, AotSourceSpan? span)
    {
        switch (failure)
        {
            case MacOsNoFollowPathFailure.Missing:
                WriteError(context, "AOT6206", $"Cannot find direct physical path '{path}'.", span,
                    "direct physical path not found", "Use an existing direct physical file or directory path.");
                break;
            case MacOsNoFollowPathFailure.SymbolicLink:
                WriteError(context, "AOT6205", $"Symbolic-link component in direct physical path '{path}' is outside the current Native AOT slice.", span,
                    "symbolic-link traversal rejected", "Use a path whose every component is a non-link file or directory.");
                break;
            default:
                WriteError(context, "AOT6210", $"Cannot safely acquire direct physical path '{path}' without following a link.", span,
                    "no-follow physical acquisition failed", "Use an accessible non-link direct physical file or directory path.");
                break;
        }
    }
}

// Static macOS translation of the upstream UnixStat display inputs. Upstream
// obtains the same uid/gid/mode/size facts through libpsl-native before its
// TypeTable and FileSystem format view project them. This narrow target cannot
// reference that engine-native library, so it calls the documented macOS ABI
// directly and preserves only the fields required by the extracted view.
internal sealed partial record MacOsPhysicalMetadata(
    string UnixMode,
    string User,
    string Group,
    long Size,
    DateTime LastWriteTimeUtc,
    DateTime LastWriteTime)
{
    private static readonly object NameLookupLock = new();
    private static readonly Dictionary<uint, string> Users = [];
    private static readonly Dictionary<uint, string> Groups = [];

    private const int OReadOnly = 0;
    private const int ONoFollow = 0x00000100;
    private const int OEventOnly = 0x00008000;
    private const int ODirectory = 0x00100000;
    private const int OCloseOnExec = 0x01000000;
    private const int AtSymlinkNoFollow = 0x0020;
    private const ushort FileTypeMask = 0xf000;
    private const ushort DirectoryType = 0x4000;
    private const ushort RegularFileType = 0x8000;
    private const ushort SymbolicLinkType = 0xa000;

    // The Darwin dirent layout below is deliberately ABI-gated by the caller
    // to macOS arm64. Do not reuse it for a new Darwin architecture without a
    // separate ABI review and native fixture evidence.
    private const int DarwinDirentNameOffset = 21;

    internal static bool TryAcquireNoFollow(string path, out MacOsPhysicalStat stat, out MacOsNoFollowPathFailure failure)
    {
        stat = default;
        failure = MacOsNoFollowPathFailure.Other;
        if (!TryOpenPathNoFollow(path, requireDirectory: false, out int descriptor, out stat, out failure))
        {
            return false;
        }

        try
        {
            return stat.IsRegularFileOrDirectory;
        }
        finally
        {
            Close(descriptor);
        }
    }

    internal static bool TryOpenDirectoryNoFollow(string path, out IntPtr directory, out MacOsNoFollowPathFailure failure)
    {
        directory = IntPtr.Zero;
        if (!TryOpenPathNoFollow(path, requireDirectory: true, out int descriptor, out MacOsPhysicalStat stat, out failure)
            || !stat.IsDirectory)
        {
            if (descriptor >= 0)
            {
                Close(descriptor);
            }

            if (failure == MacOsNoFollowPathFailure.None)
            {
                failure = MacOsNoFollowPathFailure.Unsupported;
            }

            return false;
        }

        directory = FdOpenDir(descriptor);
        if (directory == IntPtr.Zero)
        {
            Close(descriptor);
            return false;
        }

        // fdopendir owns descriptor from here; CloseDirectory performs the
        // one matching close through closedir.
        return true;
    }

    // This is the all-components no-follow acquisition primitive for the
    // reviewed Darwin arm64 path capability.  `Path.GetFullPath` supplies only
    // a lexical absolute spelling; no path-based BCL access happens after it.
    // The walk starts at a descriptor for `/`, validates every component with
    // fstatat(..., AT_SYMLINK_NOFOLLOW), and opens every component relative to
    // the descriptor that named its already validated parent.  A later rename
    // to a link is still rejected by that component's O_NOFOLLOW openat.
    private static bool TryOpenPathNoFollow(
        string absolutePath,
        bool requireDirectory,
        out int descriptor,
        out MacOsPhysicalStat stat,
        out MacOsNoFollowPathFailure failure)
    {
        descriptor = -1;
        stat = default;
        failure = MacOsNoFollowPathFailure.Other;
        if (!OperatingSystem.IsMacOS() || !Path.IsPathRooted(absolutePath))
        {
            return false;
        }

        int current = Open(Path.DirectorySeparatorChar.ToString(), OReadOnly | ODirectory | OCloseOnExec);
        if (current < 0)
        {
            failure = ClassifyPathError(Marshal.GetLastPInvokeError());
            return false;
        }

        try
        {
            string[] components = absolutePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (components.Length == 0)
            {
                if (FStat(current, out DarwinStat rootStat) != 0)
                {
                    failure = ClassifyPathError(Marshal.GetLastPInvokeError());
                    return false;
                }

                stat = new MacOsPhysicalStat(rootStat);
                if (requireDirectory && !stat.IsDirectory)
                {
                    failure = MacOsNoFollowPathFailure.Unsupported;
                    return false;
                }

                descriptor = current;
                current = -1;
                failure = MacOsNoFollowPathFailure.None;
                return true;
            }

            for (int index = 0; index < components.Length; index++)
            {
                string component = components[index];
                if (component is "." or "..")
                {
                    failure = MacOsNoFollowPathFailure.Unsupported;
                    return false;
                }

                if (FStatAt(current, component, out DarwinStat observed, AtSymlinkNoFollow) != 0)
                {
                    failure = ClassifyPathError(Marshal.GetLastPInvokeError());
                    return false;
                }

                MacOsPhysicalStat observedStat = new(observed);
                if (observedStat.IsSymbolicLink)
                {
                    failure = MacOsNoFollowPathFailure.SymbolicLink;
                    return false;
                }

                bool final = index == components.Length - 1;
                if (!final && !observedStat.IsDirectory)
                {
                    failure = MacOsNoFollowPathFailure.Unsupported;
                    return false;
                }

                int flags = (!final || requireDirectory || observedStat.IsDirectory)
                    ? OReadOnly | ODirectory | ONoFollow | OCloseOnExec
                    : OEventOnly | ONoFollow | OCloseOnExec;
                int next = OpenAt(current, component, flags);
                if (next < 0)
                {
                    failure = ClassifyPathError(Marshal.GetLastPInvokeError());
                    return false;
                }

                Close(current);
                current = next;
            }

            if (FStat(current, out DarwinStat acquired) != 0)
            {
                failure = ClassifyPathError(Marshal.GetLastPInvokeError());
                return false;
            }

            stat = new MacOsPhysicalStat(acquired);
            if ((requireDirectory && !stat.IsDirectory) || stat.IsSymbolicLink)
            {
                failure = stat.IsSymbolicLink ? MacOsNoFollowPathFailure.SymbolicLink : MacOsNoFollowPathFailure.Unsupported;
                return false;
            }

            descriptor = current;
            current = -1;
            failure = MacOsNoFollowPathFailure.None;
            return true;
        }
        finally
        {
            if (current >= 0)
            {
                Close(current);
            }
        }
    }

    private static MacOsNoFollowPathFailure ClassifyPathError(int error) => error switch
    {
        2 or 20 => MacOsNoFollowPathFailure.Missing, // ENOENT / ENOTDIR
        62 => MacOsNoFollowPathFailure.SymbolicLink, // ELOOP on Darwin
        _ => MacOsNoFollowPathFailure.Other,
    };

    internal static string? ReadNextDirectoryEntry(IntPtr directory)
    {
        IntPtr entry = ReadDir(directory);
        if (entry == IntPtr.Zero)
        {
            return null;
        }

        ushort nameLength = (ushort)Marshal.ReadInt16(entry, 18);
        if (nameLength == 0)
        {
            return string.Empty;
        }

        return Marshal.PtrToStringUTF8(IntPtr.Add(entry, DarwinDirentNameOffset), nameLength);
    }

    internal static bool TryReadRelativeNoFollow(IntPtr directory, string name, out MacOsPhysicalStat stat)
    {
        stat = default;
        int descriptor = DirFd(directory);
        return descriptor >= 0
            && FStatAt(descriptor, name, out DarwinStat nativeStat, AtSymlinkNoFollow) == 0
            && (stat = new MacOsPhysicalStat(nativeStat)).IsRegularFileOrDirectory;
    }

    internal static void CloseDirectory(IntPtr directory)
    {
        if (directory != IntPtr.Zero)
        {
            CloseDir(directory);
        }
    }

    internal static bool TryCreate(MacOsPhysicalStat stat, out MacOsPhysicalMetadata metadata, out bool isDirectory)
    {
        metadata = default!;
        isDirectory = stat.IsDirectory;
        if (!stat.IsRegularFileOrDirectory)
        {
            return false;
        }

        string? user = LookupName(stat.UserId, isUser: true);
        string? group = LookupName(stat.GroupId, isUser: false);
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(group))
        {
            return false;
        }

        DateTime utc;
        try
        {
            utc = DateTimeOffset.FromUnixTimeSeconds(stat.ModifiedSeconds)
                .AddTicks(stat.ModifiedNanoseconds / 100)
                .UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        metadata = new MacOsPhysicalMetadata(FormatMode(stat.Mode, isDirectory), user, group, stat.Size, utc, utc.ToLocalTime());
        return true;
    }

    private static string? LookupName(uint id, bool isUser)
    {
        lock (NameLookupLock)
        {
            Dictionary<uint, string> cache = isUser ? Users : Groups;
            if (cache.TryGetValue(id, out string? value))
            {
                return value;
            }

            IntPtr entry = isUser ? GetPwUid(id) : GetGrGid(id);
            if (entry == IntPtr.Zero)
            {
                return null;
            }

            IntPtr name = Marshal.ReadIntPtr(entry);
            string? resolved = name == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(name);
            if (string.IsNullOrEmpty(resolved))
            {
                return null;
            }

            cache.Add(id, resolved);
            return resolved;
        }
    }

    // This is a mechanical adaptation of CorePsPlatform.Unix.CommonStat's
    // GetModeString behavior for ordinary direct files/directories. Links are
    // rejected before reaching this reader, and the source's setuid/setgid/
    // sticky-bit rendering is retained for the admitted POSIX mode bits.
    private static string FormatMode(ushort rawMode, bool isDirectory)
    {
        UnixFileMode mode = (UnixFileMode)rawMode;
        Span<char> value = stackalloc char[10]
        {
            isDirectory ? 'd' : '-',
            mode.HasFlag(UnixFileMode.UserRead) ? 'r' : '-',
            mode.HasFlag(UnixFileMode.UserWrite) ? 'w' : '-',
            mode.HasFlag(UnixFileMode.SetUser) ? (mode.HasFlag(UnixFileMode.UserExecute) ? 's' : 'S') : (mode.HasFlag(UnixFileMode.UserExecute) ? 'x' : '-'),
            mode.HasFlag(UnixFileMode.GroupRead) ? 'r' : '-',
            mode.HasFlag(UnixFileMode.GroupWrite) ? 'w' : '-',
            mode.HasFlag(UnixFileMode.SetGroup) ? (mode.HasFlag(UnixFileMode.GroupExecute) ? 's' : 'S') : (mode.HasFlag(UnixFileMode.GroupExecute) ? 'x' : '-'),
            mode.HasFlag(UnixFileMode.OtherRead) ? 'r' : '-',
            mode.HasFlag(UnixFileMode.OtherWrite) ? 'w' : '-',
            mode.HasFlag(UnixFileMode.StickyBit) ? (mode.HasFlag(UnixFileMode.OtherExecute) ? 't' : 'T') : (mode.HasFlag(UnixFileMode.OtherExecute) ? 'x' : '-'),
        };
        return new string(value);
    }

    // Darwin's stat is 144 bytes on the supported macOS arm64 ABI. The full
    // layout is required even though this static view reads only mode, uid,
    // gid, and size; declaring a prefix would let native stat overwrite the
    // managed destination buffer.
    [StructLayout(LayoutKind.Sequential)]
    internal struct DarwinStat
    {
        internal int Device;
        internal ushort Mode;
        internal ushort LinkCount;
        internal ulong Inode;
        internal uint UserId;
        internal uint GroupId;
        internal int RDevice;
        internal int Padding;
        internal DarwinTimeSpec AccessTime;
        internal DarwinTimeSpec ModifiedTime;
        internal DarwinTimeSpec StatusChangeTime;
        internal DarwinTimeSpec BirthTime;
        internal long Size;
        internal long Blocks;
        internal int BlockSize;
        internal uint Flags;
        internal uint Generation;
        internal int Spare;
        internal long QSpareOne;
        internal long QSpareTwo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DarwinTimeSpec
    {
        internal long Seconds;
        internal long Nanoseconds;
    }

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "open", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern int Open(string path, int flags);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "openat", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern int OpenAt(int directoryDescriptor, string path, int flags);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int descriptor);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "fstat", SetLastError = true)]
    private static extern int FStat(int descriptor, out DarwinStat stat);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "fstatat", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern int FStatAt(int descriptor, string path, out DarwinStat stat, int flags);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "fdopendir", SetLastError = true)]
    private static extern IntPtr FdOpenDir(int descriptor);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "readdir", SetLastError = true)]
    private static extern IntPtr ReadDir(IntPtr directory);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dirfd", SetLastError = true)]
    private static extern int DirFd(IntPtr directory);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "closedir", SetLastError = true)]
    private static extern int CloseDir(IntPtr directory);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "getpwuid")]
    private static extern IntPtr GetPwUid(uint id);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "getgrgid")]
    private static extern IntPtr GetGrGid(uint id);
}

internal readonly struct MacOsPhysicalStat(MacOsPhysicalMetadata.DarwinStat value)
{
    private const ushort FileTypeMask = 0xf000;
    private const ushort DirectoryType = 0x4000;
    private const ushort RegularFileType = 0x8000;
    private const ushort SymbolicLinkType = 0xa000;

    internal MacOsPhysicalMetadata.DarwinStat Value { get; } = value;
    internal ushort Mode => Value.Mode;
    internal uint UserId => Value.UserId;
    internal uint GroupId => Value.GroupId;
    internal uint Flags => Value.Flags;
    internal long Size => Value.Size;
    internal long ModifiedSeconds => Value.ModifiedTime.Seconds;
    internal long ModifiedNanoseconds => Value.ModifiedTime.Nanoseconds;
    internal bool IsDirectory => (Mode & FileTypeMask) == DirectoryType;
    internal bool IsRegularFile => (Mode & FileTypeMask) == RegularFileType;
    internal bool IsSymbolicLink => (Mode & FileTypeMask) == SymbolicLinkType;
    internal bool IsRegularFileOrDirectory => IsDirectory || IsRegularFile;
}

// Closed projection of direct FileInfo/DirectoryInfo-like data. It exposes
// only the fields the current structural pipeline and renderer can honestly
// carry, rather than exposing a CLR object or PowerShell ETS wrapper.
internal sealed record PhysicalChildItemRecord(
    string Name,
    string Path,
    string ParentPath,
    PhysicalChildItemKind Kind,
    long? Length,
    DateTime LastWriteTimeUtc,
    string UnixMode,
    string User,
    string Group,
    DateTime LastWriteTime,
    long Size) : IPipelineRecord
{
    public double NumberFor(string property) => property switch
    {
        "Length" when Length is long length => length,
        "IsDirectory" => Kind == PhysicalChildItemKind.Directory ? 1 : 0,
        _ => throw new ScriptException($"Where-Object does not support property '{property}' for direct physical child items.")
    };

    public string TextFor(string column) => column switch
    {
        "Name" => Name,
        "Path" or "FullName" => Path,
        "Type" or "Kind" => Kind.ToString(),
        "Length" => Length?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        "IsDirectory" => (Kind == PhysicalChildItemKind.Directory).ToString(),
        "LastWriteTimeUtc" => LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture),
        "ParentPath" => ParentPath,
        "UnixMode" => UnixMode,
        "User" => User,
        "Group" => Group,
        "LastWriteTime" => LastWriteTime.ToString("d HH:mm", CultureInfo.CurrentCulture),
        "Size" => Size.ToString(CultureInfo.InvariantCulture),
        _ => throw new ScriptException($"Select-Object does not support column '{column}' for direct physical child items.")
    };
}

// Static transcription of the source FileSystemInfo Unix default view.  This
// is presentation owned by the shared physical-item record, not by one
// particular cmdlet: both Get-ChildItem and Get-Item emit precisely that
// record shape and therefore share the same source-attributed display
// contract.
internal static class PhysicalItemPresentation
{
    internal static IReadOnlyList<string> UnixDefaultColumns { get; } = ["UnixMode", "User", "Group", "LastWriteTime", "Size", "Name"];

    internal static AotTableLayout UnixDefaultTable { get; } = new(
        [
            new("UnixMode", "UnixMode", AotTableAlignment.Left, 10),
            new("User", "User", AotTableAlignment.Right, 10),
            new("Group", "Group", AotTableAlignment.Left, 10),
            new("LastWriteTime", "LastWriteTime", AotTableAlignment.Right, UnixLastWriteTimeColumnWidth, AotTableValueFormat.FileSystemLastWriteTime),
            new("Size", "Size", AotTableAlignment.Right, 12),
            new("Name", "Name"),
        ],
        new AotTableGroup("ParentPath", "Directory"),
        // The upstream TableControl has one literal-column gap. This is not
        // the generic renderer default.
        columnSeparator: " ");

    private static int UnixLastWriteTimeColumnWidth => string.Format(
        CultureInfo.CurrentCulture,
        "{0:d} {0:HH}:{0:mm}",
        CultureInfo.CurrentCulture.Calendar.MaxSupportedDateTime).Length;

    internal static PhysicalChildItemRecord ToRecord(PhysicalChildItem item) => new(
        item.Name,
        item.FullPath,
        item.ParentPath,
        item.Kind,
        item.Length,
        item.LastWriteTimeUtc,
        item.UnixMode,
        item.User,
        item.Group,
        item.LastWriteTime,
        item.Size);
}

// Resolve-Path has a distinct, source-observed view: one Path column with one
// literal leading blank line.  The blank is a property of this immutable
// layout, never a formatter-wide special case.
internal static class ResolvePathPresentation
{
    internal static IReadOnlyList<string> PathColumns { get; } = ["Path"];

    internal static AotTableLayout PathTable { get; } = new(
        [new("Path", "Path")],
        leadingBlankLines: 1,
        // The observed single-object Path table has a matching literal final
        // gap. Keep both gaps on this view rather than changing all tables.
        trailingBlankLines: 1);
}

// Port boundary for Microsoft.PowerShell.Commands.GetChildItemCommand. The
// source delegates all behavior to SessionState providers; this adapter admits
// only a captured-root, direct OS file/directory subset through the dedicated
// child-item catalog above. It intentionally does not reuse Get-FileHash's
// terminal-wildcard resolver contract.
internal sealed class GetChildItemCmdlet(IPhysicalChildItemCatalog childItems) : AotCmdletBase
{
    private static readonly CmdletDescriptor GetChildItemDescriptor = CreateDescriptor();

    public override CmdletDescriptor Descriptor => GetChildItemDescriptor;
    public override IReadOnlyList<string> DefaultColumns => PhysicalItemPresentation.UnixDefaultColumns;
    public override AotTableLayout DefaultTableLayout => PhysicalItemPresentation.UnixDefaultTable;

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        string[] paths = invocation.TryGetValues("Path", out string[] values) ? values : ["."];
        List<IPipelineRecord> output = [];
        for (int index = 0; index < paths.Length; index++)
        {
            AotSourceSpan? span = invocation.TryGetValues("Path", out _)
                ? invocation.GetValueSpan("Path", index)
                : invocation.SourceSpan;
            output.AddRange(childItems.GetImmediateChildren(paths[index], context, span)
                .Select(static item => (IPipelineRecord)PhysicalItemPresentation.ToRecord(item)));
        }

        return output;
    }

    private static CmdletDescriptor CreateDescriptor()
    {
        CmdletDescriptor generated = GeneratedCmdletPorts.GetChildItem.CreateAotDescriptor("Path");
        return new CmdletDescriptor(generated.Name, generated.Parameters, "Path");
    }
}

// Port boundary for Microsoft.PowerShell.Commands.GetItemCommand. The source
// routes through the dynamic provider engine; this admitted Path-only subset
// uses the shared direct lookup and therefore returns exactly one existing
// physical file or directory rather than enumerating directory children.
internal sealed class GetItemCmdlet(IPhysicalChildItemCatalog childItems) : AotCmdletBase
{
    private static readonly CmdletDescriptor GetItemDescriptor = CreateDescriptor();

    public override CmdletDescriptor Descriptor => GetItemDescriptor;
    public override IReadOnlyList<string> DefaultColumns => PhysicalItemPresentation.UnixDefaultColumns;
    public override AotTableLayout DefaultTableLayout => PhysicalItemPresentation.UnixDefaultTable;

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        if (!invocation.TryGetValues("Path", out string[] paths))
        {
            // The extracted descriptor preserves source-mandatory metadata,
            // but the intentionally small static binder does not synthesize
            // PowerShell's interactive mandatory-parameter prompt. Keep the
            // noninteractive boundary explicit rather than treating a missing
            // source-required Path as an empty item result.
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT6211",
                "Get-Item requires a direct physical -Path value in the current Native AOT slice.",
                invocation.SourceSpan,
                "required direct path missing",
                "Supply one existing direct physical file or directory path."));
        }

        List<IPipelineRecord> output = [];
        for (int index = 0; index < paths.Length; index++)
        {
            PhysicalChildItem? item = childItems.GetDirectPhysicalItem(paths[index], context, invocation.GetValueSpan("Path", index));
            if (item is not null)
            {
                output.Add(PhysicalItemPresentation.ToRecord(item));
            }
        }

        return output;
    }

    private static CmdletDescriptor CreateDescriptor() =>
        GeneratedCmdletPorts.GetItem.CreateAotDescriptor("Path");
}

// Port boundary for Microsoft.PowerShell.Commands.ResolvePathCommand.  The
// upstream command returns provider/glob PathInfo results.  This generated
// Path-only adapter instead emits the catalog-acquired canonical path record
// for an existing direct file/directory and carries no provider/session state.
internal sealed class ResolvePathCmdlet(IPhysicalChildItemCatalog childItems) : AotCmdletBase
{
    private static readonly CmdletDescriptor ResolvePathDescriptor = CreateDescriptor();

    public override CmdletDescriptor Descriptor => ResolvePathDescriptor;
    public override IReadOnlyList<string> DefaultColumns => ResolvePathPresentation.PathColumns;
    public override AotTableLayout DefaultTableLayout => ResolvePathPresentation.PathTable;

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        if (!invocation.TryGetValues("Path", out string[] paths))
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT6211",
                "Resolve-Path requires a direct physical -Path value in the current Native AOT slice.",
                invocation.SourceSpan,
                "required direct path missing",
                "Supply one existing direct physical file or directory path."));
        }

        List<IPipelineRecord> output = [];
        for (int index = 0; index < paths.Length; index++)
        {
            DirectPhysicalPathResolution resolution = childItems.ResolveExistingDirectPhysicalPath(
                paths[index], context, invocation.GetValueSpan("Path", index));
            if (resolution is { Status: DirectPhysicalPathResolutionStatus.Resolved, Record: { } record })
            {
                output.Add(record);
            }
        }

        return output;
    }

    private static CmdletDescriptor CreateDescriptor() =>
        GeneratedCmdletPorts.ResolvePath.CreateAotDescriptor("Path");
}

// Port boundary for Microsoft.PowerShell.Commands.ConvertPathCommand. The
// source converts provider paths. This thin Path-only port consumes the
// already-reviewed direct-resolution outcome and emits its canonical path as
// the existing prose TextRecord; it introduces no second resolver or view.
internal sealed class ConvertPathCmdlet(IPhysicalChildItemCatalog childItems) : AotCmdletBase
{
    private static readonly CmdletDescriptor ConvertPathDescriptor = CreateDescriptor();

    public override CmdletDescriptor Descriptor => ConvertPathDescriptor;
    public override IReadOnlyList<string> DefaultColumns { get; } = ["Value"];
    public override AotTerminalPresentation TerminalPresentation => AotTerminalPresentation.Prose;

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        if (!invocation.TryGetValues("Path", out string[] paths))
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT6211", "Convert-Path requires a direct physical -Path value in the current Native AOT slice.",
                invocation.SourceSpan, "required direct path missing", "Supply one existing direct physical file or directory path."));
        }

        // This is deliberately a collection gate, not a per-value error.
        // Nothing may resolve before every value has been checked: the first
        // exact empty path is the sole AOT6213 diagnostic and returns no rows.
        for (int index = 0; index < paths.Length; index++)
        {
            if (paths[index].Length == 0)
            {
                throw new ScriptException(AotDiagnostics.Runtime(
                    "AOT6213", "Convert-Path does not accept an empty direct physical -Path value in the current Native AOT slice.",
                    invocation.GetValueSpan("Path", index), "empty direct physical path", "Supply one non-empty direct operating-system file or directory path."));
            }
        }

        List<IPipelineRecord> output = [];
        for (int index = 0; index < paths.Length; index++)
        {
            DirectPhysicalPathResolution resolution = childItems.ResolveExistingDirectPhysicalPath(paths[index], context, invocation.GetValueSpan("Path", index));
            if (resolution is { Status: DirectPhysicalPathResolutionStatus.Resolved, Record: { } record })
            {
                output.Add(new TextRecord(record.Path));
            }
        }

        return output;
    }

    private static CmdletDescriptor CreateDescriptor() => GeneratedCmdletPorts.ConvertPath.CreateAotDescriptor("Path");
}

// Port boundary for Microsoft.PowerShell.Commands.TestPathCommand. The source
// calls provider Exists/IsContainer; this bounded adapter asks the existing
// captured-root catalog for one closed no-follow fact and projects it as a
// typed Boolean. Missing is the sole false/no-error result. A rejected
// capability boundary has already written its source-spanned diagnostic and
// deliberately yields no Boolean value.
internal sealed class TestPathCmdlet(IPhysicalChildItemCatalog childItems) : AotCmdletBase
{
    private static readonly CmdletDescriptor TestPathDescriptor = CreateDescriptor();

    public override CmdletDescriptor Descriptor => TestPathDescriptor;
    public override IReadOnlyList<string> DefaultColumns { get; } = ["Value"];
    public override AotTerminalPresentation TerminalPresentation => AotTerminalPresentation.Prose;

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        if (!invocation.TryGetValues("Path", out string[] paths))
        {
            // Preserve mandatory source metadata but do not fabricate the
            // interactive parameter prompt that belongs to a future host
            // contract. This is the same noninteractive boundary as Get-Item.
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT6211",
                "Test-Path requires a direct physical -Path value in the current Native AOT slice.",
                invocation.SourceSpan,
                "required direct path missing",
                "Supply one direct physical file or directory path."));
        }

        DirectPhysicalPathType pathType = ParsePathType(invocation);
        List<IPipelineRecord> output = [];
        for (int index = 0; index < paths.Length; index++)
        {
            string path = paths[index];
            if (string.IsNullOrWhiteSpace(path))
            {
                // Upstream's non-null empty/whitespace input is an existence
                // miss. Do not let the catalog's dot canonicalization turn it
                // into a query for the captured root.
                output.Add(new BooleanRecord(false));
                continue;
            }

            PhysicalItemProbeResult probe = childItems.ProbeDirectPhysicalItem(path, context, invocation.GetValueSpan("Path", index));
            if (probe.Status == PhysicalItemProbeStatus.Rejected)
            {
                continue;
            }

            bool result = probe.Status == PhysicalItemProbeStatus.Found
                && pathType switch
                {
                    DirectPhysicalPathType.Any => true,
                    DirectPhysicalPathType.Container => probe.Kind == PhysicalChildItemKind.Directory,
                    DirectPhysicalPathType.Leaf => probe.Kind == PhysicalChildItemKind.File,
                    _ => throw new InvalidOperationException("Unexpected validated Test-Path type."),
                };
            output.Add(new BooleanRecord(result));
        }

        return output;
    }

    private static DirectPhysicalPathType ParsePathType(CommandInvocation invocation)
    {
        if (!invocation.TryGetValues("PathType", out string[] values))
        {
            return DirectPhysicalPathType.Any;
        }

        if (values.Length != 1)
        {
            throw new ScriptException(AotDiagnostics.Runtime(
                "AOT6212",
                "Test-Path -PathType accepts exactly one static value: Any, Container, or Leaf.",
                invocation.GetValueSpan("PathType", 0),
                "invalid direct physical path type",
                "Use Any, Container, or Leaf."));
        }

        if (values[0].Equals("Any", StringComparison.OrdinalIgnoreCase)) return DirectPhysicalPathType.Any;
        if (values[0].Equals("Container", StringComparison.OrdinalIgnoreCase)) return DirectPhysicalPathType.Container;
        if (values[0].Equals("Leaf", StringComparison.OrdinalIgnoreCase)) return DirectPhysicalPathType.Leaf;

        throw new ScriptException(AotDiagnostics.Runtime(
            "AOT6212",
            $"Test-Path -PathType value '{values[0]}' is outside the Native AOT subset.",
            invocation.GetValueSpan("PathType", 0),
            "unsupported direct physical path type",
            "Use Any, Container, or Leaf."));
    }

    private static CmdletDescriptor CreateDescriptor()
    {
        CmdletDescriptor generated = GeneratedCmdletPorts.TestPath.CreateAotDescriptor("Path", "PathType");
        return new CmdletDescriptor(generated.Name, generated.Parameters, "Path");
    }

    private enum DirectPhysicalPathType { Any, Container, Leaf }
}
