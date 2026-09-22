using System.Security.Cryptography;
using System.Text;

namespace PwshAotLite;

// Installation is deliberately a separate write boundary from repository
// discovery.  The v1 proof consumes only a pre-existing, local directory
// package named by a file URI.  It does not download, unzip, import, load, or
// execute anything.  A future transport/trust implementation can implement
// IModuleInstaller without teaching repository search or extension discovery
// how to mutate the filesystem.
internal interface IModuleInstaller
{
    ModuleInstallResult Install(string name, string? repository);
}

internal sealed record ModuleInstallResult(
    string Name,
    string Version,
    string Repository,
    string Status,
    string PackagePath,
    string ContentSha256);

// Narrow seams let the installer prove its transaction properties without
// making catalog/discovery code aware of mutable filesystem operations.
internal interface IPackageStager
{
    void Copy(string source, string stagedPackage);
}

internal interface IStagingAreaCleaner
{
    void Cleanup(string stagingRoot);
}

internal sealed class PhysicalPackageStager : IPackageStager
{
    public void Copy(string source, string stagedPackage)
    {
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)
                .Prepend(source)
                .OrderBy(path => path, StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath(source, directory);
                Directory.CreateDirectory(relative == "." ? stagedPackage : Path.Combine(stagedPackage, relative));
            }

            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                NativeFileObjectPolicy.EnsureRegularFile(file);
                string target = Path.Combine(stagedPackage, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: false);
            }
        }
        catch (IOException)
        {
            throw new ScriptException("InstallStageCopyFailed: Package files could not be staged safely.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new ScriptException("InstallStageCopyFailed: Package files could not be staged safely.");
        }
    }
}

internal sealed class PhysicalStagingAreaCleaner : IStagingAreaCleaner
{
    public void Cleanup(string stagingRoot)
    {
        if (Directory.Exists(stagingRoot))
        {
            Directory.Delete(stagingRoot, recursive: true);
        }
    }
}

internal sealed class LocalPackageModuleInstaller : IModuleInstaller
{
    private const int MaximumFiles = 32;
    private const long MaximumFileBytes = 1024 * 1024;
    private const long MaximumPackageBytes = 4 * 1024 * 1024;
    private readonly IRepositoryCatalog _repositories;
    private readonly IPackageStager _stager;
    private readonly IStagingAreaCleaner _cleaner;
    private readonly IActivationVolumeVerifier _volumeVerifier;
    private readonly IAotHostConfiguration _configuration;

    internal LocalPackageModuleInstaller(
        IRepositoryCatalog repositories,
        IPackageStager? stager = null,
        IStagingAreaCleaner? cleaner = null,
        IActivationVolumeVerifier? volumeVerifier = null,
        IAotHostConfiguration? configuration = null)
    {
        _repositories = repositories;
        _stager = stager ?? new PhysicalPackageStager();
        _cleaner = cleaner ?? new PhysicalStagingAreaCleaner();
        _volumeVerifier = volumeVerifier ?? new SystemActivationVolumeVerifier();
        _configuration = configuration ?? new ProcessAotHostConfiguration();
    }

    // Used by fixture authors to produce the same declared content digest the
    // installer verifies. It deliberately applies the installer bounds and
    // symlink policy rather than offering a broader general-purpose hash API.
    internal static string CalculateContentSha256(string packageRoot) => Convert.ToHexString(ScanPackage(packageRoot).Sha256);

    public ModuleInstallResult Install(string name, string? repository)
    {
        if (string.IsNullOrWhiteSpace(name) || !IsSafeSegment(name))
        {
            throw new ScriptException("InstallInvalidName: Install-Module requires one safe, exact module name.");
        }

        RepositoryModuleEntry entry = ResolveEntry(name, repository);
        if (!IsSafeSegment(entry.Name) || !IsSafeSegment(entry.Version))
        {
            throw new ScriptException("InstallRepositoryIdentityInvalid: The advertised module name or version is unsafe for an extension package path.");
        }
        if (!Uri.TryCreate(entry.PackageUri, UriKind.Absolute, out Uri? packageUri)
            || !packageUri.IsFile
            || !string.IsNullOrEmpty(packageUri.Host)
            || packageUri.UserInfo.Length != 0
            || !string.IsNullOrEmpty(packageUri.Query)
            || !string.IsNullOrEmpty(packageUri.Fragment))
        {
            throw new ScriptException("InstallSourceUriNotAllowed: Only credential-free local file package URIs are supported by this installer proof.");
        }

        if (!IsSha256(entry.PackageSha256))
        {
            throw new ScriptException("InstallHashRequired: The selected repository entry does not declare a valid SHA-256 content hash.");
        }

        string source = CanonicalDirectory(packageUri.LocalPath, "InstallSourceNotFound");
        source = EnsureContainedByConfiguredRoots(source, _configuration);
        PackageScan scan = ScanPackage(source);
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(entry.PackageSha256!), scan.Sha256))
        {
            throw new ScriptException("InstallHashMismatch: Package content did not match the repository-declared SHA-256; nothing was activated.");
        }

        string extensionRoot = ExplicitExtensionRoot(_configuration);
        // Repository identity, rather than caller casing, determines the
        // activation path so `fixture.safe` cannot create a second package
        // beside an advertised `Fixture.Safe` on a case-sensitive filesystem.
        string destination = Path.Combine(extensionRoot, entry.Name, entry.Version);
        EnsureNoSymlinksBelowRoot(extensionRoot, extensionRoot, destination, "InstallDestinationSymlink");
        if (Directory.Exists(destination))
        {
            EnsureNotLink(destination, "InstallDestinationSymlink");
            PackageScan existing = ScanPackage(destination);
            if (CryptographicOperations.FixedTimeEquals(existing.Sha256, scan.Sha256))
            {
                return new ModuleInstallResult(entry.Name, entry.Version, entry.Repository, "already-installed", destination, Convert.ToHexString(scan.Sha256));
            }

            throw new ScriptException("InstallConflict: The requested module version already exists with different content; it was not overwritten.");
        }

        // Staging lives under the resolved extension root so it is necessarily
        // on the activation volume. ExtensionPackageCatalog explicitly skips
        // this fixed directory, keeping incomplete packages unobservable to
        // Get-Help/Get-Command/Get-Module/completion before activation.
        string stagingParent = Path.Combine(extensionRoot, ".pwsh-aot-lite-staging");
        EnsureNoSymlinksBelowRoot(extensionRoot, extensionRoot, stagingParent, "InstallStagingSymlink");
        string stagingRoot = Path.Combine(stagingParent, Guid.NewGuid().ToString("N"));
        string stagedPackage = Path.Combine(stagingRoot, entry.Name, entry.Version);
        try
        {
            // Repeat the bounded source walk immediately before copying. The
            // post-copy staged digest below is the final authority, but this
            // gives a stable source-change error for a detected source swap.
            PackageScan preCopyScan = ScanPackage(source);
            if (!CryptographicOperations.FixedTimeEquals(preCopyScan.Sha256, scan.Sha256))
            {
                throw new ScriptException("InstallSourceChanged: Package bytes changed after initial verification; nothing was activated.");
            }

            _stager.Copy(source, stagedPackage);
            PackageScan postCopySourceScan = ScanPackage(source);
            if (!CryptographicOperations.FixedTimeEquals(postCopySourceScan.Sha256, scan.Sha256))
            {
                throw new ScriptException("InstallSourceChanged: Package bytes changed while staging; nothing was activated.");
            }

            PackageScan stagedScan = ScanPackage(stagedPackage);
            if (!CryptographicOperations.FixedTimeEquals(stagedScan.Sha256, scan.Sha256))
            {
                throw new ScriptException("InstallStagedHashMismatch: Staged package bytes changed during copy; nothing was activated.");
            }

            _volumeVerifier.EnsureSameVolume(stagingRoot, extensionRoot);
            ValidateStagedPackage(stagedPackage, entry.Name, entry.Version);

            string destinationParent = Path.GetDirectoryName(destination)!;
            try
            {
                Directory.CreateDirectory(destinationParent);
            }
            catch (IOException)
            {
                throw new ScriptException("InstallDestinationCreateFailed: The extension destination directory could not be created safely.");
            }
            catch (UnauthorizedAccessException)
            {
                throw new ScriptException("InstallDestinationCreateFailed: The extension destination directory could not be created safely.");
            }
            EnsureNoSymlinksBelowRoot(extensionRoot, extensionRoot, destinationParent, "InstallDestinationSymlink");
            if (Directory.Exists(destination))
            {
                // A concurrent installer won the race. Do not overwrite it.
                PackageScan existing = ScanPackage(destination);
                if (CryptographicOperations.FixedTimeEquals(existing.Sha256, scan.Sha256))
                {
                    return new ModuleInstallResult(entry.Name, entry.Version, entry.Repository, "already-installed", destination, Convert.ToHexString(scan.Sha256));
                }

                throw new ScriptException("InstallConflict: Another installer activated different content for this module version.");
            }

            // Same-volume directory rename is the activation point: a failed
            // validation/copy leaves no visible package under extensions/.
            try
            {
                Directory.Move(stagedPackage, destination);
            }
            catch (IOException)
            {
                throw new ScriptException("InstallActivationFailed: The validated staged package could not be atomically activated.");
            }
            catch (UnauthorizedAccessException)
            {
                throw new ScriptException("InstallActivationFailed: The validated staged package could not be atomically activated.");
            }

            return new ModuleInstallResult(entry.Name, entry.Version, entry.Repository, "installed", destination, Convert.ToHexString(scan.Sha256));
        }
        finally
        {
            try
            {
                // Cleanup after a successful activation is maintenance, not a
                // transaction result. It must not convert an installed module
                // into a reported failure or replace the primary exception.
                _cleaner.Cleanup(stagingRoot);
            }
            catch (IOException)
            {
                // Best effort only; a later hygiene task can remove leftovers.
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort only; a later hygiene task can remove leftovers.
            }
            catch (Exception)
            {
                // Injectable/test cleanup failures must follow the same policy.
            }
        }
    }

    private RepositoryModuleEntry ResolveEntry(string name, string? repository)
    {
        RepositoryModuleEntry[] candidates = _repositories.Find(name, repository)
            .Where(entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (candidates.Length == 0)
        {
            throw new ScriptException("InstallModuleNotFound: No matching module was advertised by the configured repository catalog.");
        }

        RepositoryModuleEntry[] newest = candidates
            .GroupBy(entry => VersionKey(entry.Version), StringComparer.Ordinal)
            .OrderByDescending(group => group.Key, StringComparer.Ordinal)
            .First()
            .ToArray();
        if (newest.Length != 1)
        {
            throw new ScriptException("InstallAmbiguousPackage: Select one repository; the newest advertised version is not unique.");
        }

        return newest[0];
    }

    private static string VersionKey(string version) => Version.TryParse(version, out Version? parsed)
        ? $"1-{parsed.Major:D10}.{parsed.Minor:D10}.{Math.Max(0, parsed.Build):D10}.{Math.Max(0, parsed.Revision):D10}"
        : "0-" + version;

    private static string ExplicitExtensionRoot(IAotHostConfiguration configuration)
    {
        string? configured = configuration.Read(AotHostConfigurationKey.ExtensionsRoot);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new ScriptException("InstallDestinationRootRequired: Set PWSH_AOT_EXTENSIONS_ROOT before installing; discovery-only ancestor paths are never write targets.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(configured);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            throw new ScriptException("InstallDestinationRootInvalid: PWSH_AOT_EXTENSIONS_ROOT is not a usable filesystem path.");
        }

        if (Directory.Exists(fullPath))
        {
            return ResolveTrustedRoot(fullPath, "InstallDestinationRootInvalid");
        }
        else
        {
            try
            {
                Directory.CreateDirectory(fullPath);
                return ResolveTrustedRoot(fullPath, "InstallDestinationRootInvalid");
            }
            catch (IOException)
            {
                throw new ScriptException("InstallDestinationRootInvalid: PWSH_AOT_EXTENSIONS_ROOT could not be created safely.");
            }
            catch (UnauthorizedAccessException)
            {
                throw new ScriptException("InstallDestinationRootInvalid: PWSH_AOT_EXTENSIONS_ROOT could not be created safely.");
            }
        }

        throw new ScriptException("InstallDestinationRootInvalid: PWSH_AOT_EXTENSIONS_ROOT could not be resolved safely.");
    }

    private static string CanonicalDirectory(string path, string errorId)
    {
        try
        {
            string full = Path.GetFullPath(path);
            if (!Directory.Exists(full))
            {
                throw new ScriptException(errorId + ": The local package directory does not exist.");
            }

            return full;
        }
        catch (ScriptException)
        {
            throw;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            throw new ScriptException(errorId + ": The local package URI cannot be resolved to a directory.");
        }
    }

    private static string EnsureContainedByConfiguredRoots(string source, IAotHostConfiguration configuration)
    {
        string? configured = configuration.Read(AotHostConfigurationKey.PackageRoots);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new ScriptException("InstallSourceRootRequired: Set PWSH_AOT_PACKAGE_ROOTS; a repository file URI alone is not authority to read arbitrary local files.");
        }

        foreach (string root in configured.Split(Path.PathSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string fullRoot = Path.GetFullPath(root);
                if (!Directory.Exists(fullRoot))
                {
                    continue;
                }

                string physicalRoot = ResolveTrustedRoot(fullRoot, "InstallSourceRootInvalid");
                string physicalSource = ResolveTrustedRoot(source, "InstallSourceNotFound");
                if (IsContained(physicalRoot, physicalSource))
                {
                    // An explicit root may itself be a legitimate platform
                    // alias (/tmp -> /private/tmp, /var -> /private/var).
                    // Only links *below* the resolved trusted root are
                    // untrusted traversal and therefore rejected.
                    EnsureNoSymlinksBelowRoot(fullRoot, physicalRoot, source, "InstallSourceSymlink");
                    return physicalSource;
                }
            }
            catch (ArgumentException)
            {
                // Ignore a malformed configured root and continue checking a
                // separately configured valid root.
            }
        }

        throw new ScriptException("InstallSourceOutsideRoot: The package file URI is outside PWSH_AOT_PACKAGE_ROOTS.");
    }

    private static PackageScan ScanPackage(string root)
    {
        try
        {
            List<(string RelativePath, string FullPath, long Length)> files = [];
            long total = 0;
            Stack<string> directories = new();
            directories.Push(root);
            while (directories.Count > 0)
            {
                string directory = directories.Pop();
                EnsureNotLink(directory, "InstallPackageSymlink");
                IEnumerable<string> entries = Directory.EnumerateFileSystemEntries(directory).ToArray();

                foreach (string entry in entries)
                {
                    FileSystemInfo info = Directory.Exists(entry) ? new DirectoryInfo(entry) : new FileInfo(entry);
                    EnsureNotLink(info.FullName, "InstallPackageSymlink");
                    if (info is DirectoryInfo)
                    {
                        directories.Push(info.FullName);
                        continue;
                    }

                    FileInfo file = (FileInfo)info;
                    NativeFileObjectPolicy.EnsureRegularFile(file.FullName);
                    string relative = Path.GetRelativePath(root, file.FullName);
                    if (!IsSafeRelativePath(relative) || file.Length > MaximumFileBytes)
                    {
                        throw new ScriptException(file.Length > MaximumFileBytes
                            ? "InstallPackageFileTooLarge: A package file exceeds the 1 MiB local-proof limit."
                            : "InstallPackagePathInvalid: Package content contains an unsafe relative path.");
                    }

                    total += file.Length;
                    files.Add((relative.Replace(Path.DirectorySeparatorChar, '/'), file.FullName, file.Length));
                    if (files.Count > MaximumFiles || total > MaximumPackageBytes)
                    {
                        throw new ScriptException("InstallPackageTooLarge: Package exceeds the local-proof file-count or content-size limit.");
                    }
                }
            }

            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach ((string relative, string fullPath, _) in files.OrderBy(file => file.RelativePath, StringComparer.Ordinal))
            {
                hash.AppendData(Encoding.UTF8.GetBytes(relative));
                hash.AppendData([0]);
                using FileStream stream = File.OpenRead(fullPath);
                byte[] buffer = new byte[81920];
                int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    hash.AppendData(buffer, 0, count);
                }

                hash.AppendData([0]);
            }

            return new PackageScan(hash.GetHashAndReset());
        }
        catch (ScriptException)
        {
            throw;
        }
        catch (IOException)
        {
            throw new ScriptException("InstallPackageUnreadable: Package files could not be read safely.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new ScriptException("InstallPackageUnreadable: Package files could not be read safely.");
        }
    }

    private static void ValidateStagedPackage(string stagedPackage, string name, string version)
    {
        string manifestPath = Path.Combine(stagedPackage, "extension.json");
        if (!ExtensionPackageCatalog.TryLoadPackage(manifestPath, out ExtensionPackage? package)
            || !package!.Manifest.Extension!.DisplayName!.Equals(name, StringComparison.OrdinalIgnoreCase)
            || !package.Manifest.Extension.Version!.Equals(version, StringComparison.Ordinal))
        {
            throw new ScriptException("InstallPackageInvalid: Staged extension manifest/help/provenance failed centralized catalog validation or repository identity matching.");
        }
    }

    private static void EnsureNotLink(string path, string errorId)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        if (info.LinkTarget is not null)
        {
            throw new ScriptException(errorId + ": Symbolic links are not permitted in local-proof installer paths or package content.");
        }
    }

    private static string ResolveTrustedRoot(string path, string errorId)
    {
        try
        {
            string full = Path.GetFullPath(path);
            if (!Directory.Exists(full))
            {
                throw new ScriptException(errorId + ": The trusted directory does not exist.");
            }

            // Resolve aliases one component at a time. This deliberately
            // accepts explicit roots such as /tmp or /var on macOS while
            // producing physical roots for containment and destinations.
            Stack<string> components = new();
            for (DirectoryInfo? current = new DirectoryInfo(full); current is not null && current.Parent is not null; current = current.Parent)
            {
                components.Push(current.Name);
            }

            string resolved = Path.GetPathRoot(full)!;
            while (components.Count > 0)
            {
                string next = Path.Combine(resolved, components.Pop());
                DirectoryInfo info = new(next);
                if (info.LinkTarget is not null)
                {
                    FileSystemInfo? target = info.ResolveLinkTarget(returnFinalTarget: true);
                    if (target is not DirectoryInfo)
                    {
                        throw new ScriptException(errorId + ": A trusted directory alias did not resolve to a directory.");
                    }

                    resolved = target.FullName;
                }
                else
                {
                    resolved = next;
                }
            }

            return Path.GetFullPath(resolved);
        }
        catch (ScriptException)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new ScriptException(errorId + ": The trusted directory could not be physically resolved.");
        }
    }

    private static bool IsContained(string root, string candidate)
    {
        string relative = Path.GetRelativePath(root, candidate);
        return !Path.IsPathRooted(relative)
            && !relative.Equals("..", StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    // Explicit trusted roots may use a platform alias. Links below that root
    // remain forbidden; only that untrusted lexical suffix is inspected.
    private static void EnsureNoSymlinksBelowRoot(string configuredRoot, string physicalRoot, string candidate, string errorId)
    {
        string fullCandidate = Path.GetFullPath(candidate);
        // A source may spell an explicit root through either its configured
        // alias (/tmp) or physical spelling (/private/tmp). Inspect both
        // suffixes; never return early merely because one spelling differs.
        foreach (string root in new[] { Path.GetFullPath(configuredRoot), Path.GetFullPath(physicalRoot) }
            .Distinct(StringComparer.Ordinal))
        {
            if (!IsContained(root, fullCandidate))
            {
                continue;
            }

            string relative = Path.GetRelativePath(root, fullCandidate);
            string current = root;
            foreach (string segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                if (segment is "." or "")
                {
                    continue;
                }

                current = Path.Combine(current, segment);
                if (Directory.Exists(current) || File.Exists(current))
                {
                    EnsureNotLink(current, errorId);
                }
            }
        }
    }

    private static bool IsSafeSegment(string name) => name.Length is > 0 and <= 128
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');

    private static bool IsSafeRelativePath(string path) => !Path.IsPathRooted(path)
        && !path.Equals("..", StringComparison.Ordinal)
        && !path.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        && !path.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
        && path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).All(segment => IsSafeSegment(segment));

    private static bool IsSha256(string? value) => value is { Length: 64 }
        && value.All(static character => char.IsAsciiHexDigit(character));

    private sealed record PackageScan(byte[] Sha256);
}

internal sealed class InstallModuleCmdlet(IModuleInstaller installer) : AotCmdletBase
{
    public override CmdletDescriptor Descriptor => HostControlPlaneCatalog.InstallModuleDescriptor;
    public override IReadOnlyList<string> DefaultColumns => ["Name", "Version", "Status", "Repository", "Path"];

    protected override IEnumerable<IPipelineRecord> ProcessRecord(CommandInvocation invocation, AotExecutionContext context)
    {
        if (!invocation.TryGetValues("Name", out string[]? names) || names.Length != 1)
        {
            throw new ScriptException("InstallInvalidName: Install-Module requires exactly one -Name.");
        }

        string? repository = invocation.TryGetValues("Repository", out string[]? suppliedRepository)
            ? suppliedRepository.SingleOrDefault()
            : null;
        ModuleInstallResult result = installer.Install(names[0], repository);
        return [new InstallModuleRecord(result.Name, result.Version, result.Repository, result.Status, result.PackagePath, result.ContentSha256)];
    }
}

internal sealed record InstallModuleRecord(string Name, string Version, string Repository, string Status, string Path, string ContentSha256) : IPipelineRecord
{
    public double NumberFor(string property) => throw new ScriptException("Where-Object does not support installation records.");
    public string TextFor(string column) => column switch
    {
        "Name" => Name,
        "Version" => Version,
        "Repository" => Repository,
        "Status" => Status,
        "Path" => Path,
        "ContentSha256" => ContentSha256,
        _ => throw new ScriptException($"Select-Object does not support column '{column}' for installation records."),
    };
}
