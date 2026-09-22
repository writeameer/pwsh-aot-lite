using System.Runtime.InteropServices;
using System.Diagnostics;

namespace PwshAotLite;

// This is the composition boundary for capabilities that a reviewed native
// adapter may consume. It is intentionally a fixed record, not a provider,
// service locator, session state, or host-object compatibility facade.
internal sealed record AotHostSubstrate(
    IPhysicalFileResolver PhysicalFiles,
    IProcessCatalog Processes,
    IClock Clock,
    IHostCulture Culture,
    ICultureCatalog Cultures,
    ITimeZoneCatalog TimeZones,
    IAotHostConfiguration Configuration,
    IAotHostDiscoveryRoots DiscoveryRoots,
    IAotHostPlatform Platform,
    IAotTerminalInfoSource Terminal,
    ICredentialCapability Credentials,
    INetworkCapability Network)
{
    internal static AotHostSubstrate CreateLocal()
    {
        IAotHostConfiguration configuration = new ProcessAotHostConfiguration();
        IAotHostPlatform platform = new SystemAotHostPlatform();
        return new AotHostSubstrate(
            new SystemPhysicalFileResolver(),
            new SystemProcessCatalog(platform),
            new SystemClock(),
            new SystemHostCulture(),
            new SystemCultureCatalog(),
            new SystemTimeZoneCatalog(),
            configuration,
            new ProcessAotHostDiscoveryRoots(),
            platform,
            new SystemTerminalInfoSource(configuration, platform),
            UnavailableCredentialCapability.Instance,
            OfflineNetworkCapability.Instance);
    }
}

// Only these deployment keys are authority-bearing. This deliberately does
// not expose Env:, arbitrary process variables, mutation, PATH, HOME, or a
// current-location provider to adapters.
internal enum AotHostConfigurationKey
{
    ExtensionsRoot,
    ExtensionsPath,
    PackageRoots,
    RepositoriesPath,
    Term,
    NoColor,
    WindowsTerminalSession,
    AnsiCon,
    ConEmuAnsi,
}

internal interface IAotHostConfiguration
{
    string? Read(AotHostConfigurationKey key);
}

internal sealed class ProcessAotHostConfiguration : IAotHostConfiguration
{
    public string? Read(AotHostConfigurationKey key) => Environment.GetEnvironmentVariable(key switch
    {
        AotHostConfigurationKey.ExtensionsRoot => "PWSH_AOT_EXTENSIONS_ROOT",
        AotHostConfigurationKey.ExtensionsPath => "PWSH_AOT_EXTENSIONS_PATH",
        AotHostConfigurationKey.PackageRoots => "PWSH_AOT_PACKAGE_ROOTS",
        AotHostConfigurationKey.RepositoriesPath => "PWSH_AOT_REPOSITORIES_PATH",
        AotHostConfigurationKey.Term => "TERM",
        AotHostConfigurationKey.NoColor => "NO_COLOR",
        AotHostConfigurationKey.WindowsTerminalSession => "WT_SESSION",
        AotHostConfigurationKey.AnsiCon => "ANSICON",
        AotHostConfigurationKey.ConEmuAnsi => "ConEmuANSI",
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    });
}

// Catalog discovery may use only these two captured physical starting points;
// it never reads a session location or an arbitrary environment path.
internal interface IAotHostDiscoveryRoots
{
    string CurrentDirectory { get; }
    string ApplicationBaseDirectory { get; }
}

internal sealed class ProcessAotHostDiscoveryRoots : IAotHostDiscoveryRoots
{
    public string CurrentDirectory { get; } = Directory.GetCurrentDirectory();
    public string ApplicationBaseDirectory { get; } = AppContext.BaseDirectory;
}

internal static class AotHostComposition
{
    // The one immutable production composition root. It is constructed before
    // registry/catalog use and intentionally has no mutation or lookup API.
    internal static AotHostSubstrate Substrate { get; } = AotHostSubstrate.CreateLocal();
    internal static RepositoryCatalog Repositories { get; } = new(Substrate.Configuration, Substrate.DiscoveryRoots);
    internal static CompositeHelpCatalog Help { get; } = new(Substrate.Configuration, Substrate.DiscoveryRoots);
    internal static CompositeModuleCatalog Modules { get; } = new(Substrate.Configuration, Substrate.DiscoveryRoots);
    internal static CompletionService Completion { get; } = new(Help, Modules);
}

internal enum AotHostOperatingSystem { Unknown, Windows, MacOS, Linux }

internal sealed record AotHostPlatformSnapshot(AotHostOperatingSystem OperatingSystem, Architecture Architecture)
{
    internal bool IsUnix => OperatingSystem is AotHostOperatingSystem.MacOS or AotHostOperatingSystem.Linux;
}

internal interface IAotHostPlatform
{
    AotHostPlatformSnapshot Snapshot { get; }
}

internal sealed class SystemAotHostPlatform : IAotHostPlatform
{
    public AotHostPlatformSnapshot Snapshot { get; } = new(
        OperatingSystem.IsWindows() ? AotHostOperatingSystem.Windows
            : OperatingSystem.IsMacOS() ? AotHostOperatingSystem.MacOS
            : OperatingSystem.IsLinux() ? AotHostOperatingSystem.Linux
            : AotHostOperatingSystem.Unknown,
        RuntimeInformation.ProcessArchitecture);
}

internal interface IAotTerminalInfoSource
{
    AotTerminalInfo Capture();
}

internal sealed class SystemTerminalInfoSource(IAotHostConfiguration configuration, IAotHostPlatform platform) : IAotTerminalInfoSource
{
    public AotTerminalInfo Capture()
    {
        try
        {
            bool redirected = Console.IsErrorRedirected;
            AotHostPlatformSnapshot snapshot = platform.Snapshot;
            bool isWindows = snapshot.OperatingSystem == AotHostOperatingSystem.Windows;
            bool windowsAnsiHost = isWindows && (!string.IsNullOrEmpty(configuration.Read(AotHostConfigurationKey.WindowsTerminalSession))
                || !string.IsNullOrEmpty(configuration.Read(AotHostConfigurationKey.AnsiCon))
                || string.Equals(configuration.Read(AotHostConfigurationKey.ConEmuAnsi), "ON", StringComparison.OrdinalIgnoreCase));
            int? width = redirected || Console.WindowWidth < 20 ? null : Console.WindowWidth;
            return new AotTerminalInfo(
                redirected,
                configuration.Read(AotHostConfigurationKey.Term),
                configuration.Read(AotHostConfigurationKey.NoColor),
                isWindows,
                windowsAnsiHost,
                width);
        }
        catch (Exception)
        {
            // Terminal probing is advisory and cannot hide the primary error.
            return new AotTerminalInfo(true, null, null, false, false, null);
        }
    }
}

internal enum AotUnavailableCapability { Credentials, Network }

internal sealed record AotCapabilityUnavailable(AotUnavailableCapability Capability, string DiagnosticId, string Message);

internal interface ICredentialCapability
{
    AotCapabilityUnavailable Unavailable { get; }
}

internal sealed class UnavailableCredentialCapability : ICredentialCapability
{
    internal static UnavailableCredentialCapability Instance { get; } = new();
    public AotCapabilityUnavailable Unavailable { get; } = new(AotUnavailableCapability.Credentials, "AOT6101", "Credentials are unavailable in this Native AOT host.");
}

internal interface INetworkCapability
{
    AotCapabilityUnavailable Unavailable { get; }
}

internal sealed class OfflineNetworkCapability : INetworkCapability
{
    internal static OfflineNetworkCapability Instance { get; } = new();
    public AotCapabilityUnavailable Unavailable { get; } = new(AotUnavailableCapability.Network, "AOT6102", "Network transport is unavailable in this Native AOT host.");
}

// This narrow ownership reader preserves the existing Get-Process enrichment
// seam without turning process launch into a generally available host API.
internal interface IProcessOwnerReader
{
    string? TryGetOwner(int processId, AotExecutionContext context);
}

internal sealed class UnixPsProcessOwnerReader(IAotHostPlatform platform) : IProcessOwnerReader
{
    public string? TryGetOwner(int processId, AotExecutionContext context)
    {
        if (!platform.Snapshot.IsUnix)
        {
            context.WriteNonTerminatingError("CouldNotRetrieveUserName", "Process ownership lookup is unavailable on this platform.");
            return null;
        }

        try
        {
            ProcessStartInfo startInfo = new("/bin/ps")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add("user=");
            startInfo.ArgumentList.Add("-p");
            startInfo.ArgumentList.Add(processId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using Process? process = Process.Start(startInfo);
            string? user = process?.StandardOutput.ReadToEnd().Trim();
            process?.WaitForExit();
            return string.IsNullOrWhiteSpace(user) ? null : user;
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            context.WriteNonTerminatingError("CouldNotRetrieveUserName", error.Message);
            return null;
        }
    }
}
