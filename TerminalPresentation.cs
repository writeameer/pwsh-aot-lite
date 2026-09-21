namespace PwshAotLite;

// Terminal policy belongs to the host, not the diagnostic renderer. The
// renderer receives a deterministic presentation choice and remains usable by
// tests, JSON/LSP projections, and non-interactive callers.
internal enum AotColorMode { Auto, Always, Never }

internal sealed record AotTerminalInfo(
    bool IsErrorRedirected,
    string? Term,
    string? NoColor,
    bool IsWindows,
    bool HasWindowsAnsiHost,
    int? ErrorWidth)
{
    internal static AotTerminalInfo Capture()
    {
        try
        {
            bool isErrorRedirected = Console.IsErrorRedirected;
            string? term = Environment.GetEnvironmentVariable("TERM");
            string? noColor = Environment.GetEnvironmentVariable("NO_COLOR");
            bool isWindows = OperatingSystem.IsWindows();
            bool windowsAnsiHost = isWindows && (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WT_SESSION"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANSICON"))
                || string.Equals(Environment.GetEnvironmentVariable("ConEmuANSI"), "ON", StringComparison.OrdinalIgnoreCase));
            int? width = isErrorRedirected || Console.WindowWidth < 20 ? null : Console.WindowWidth;
            return new AotTerminalInfo(isErrorRedirected, term, noColor, isWindows, windowsAnsiHost, width);
        }
        catch (Exception)
        {
            // Console capability checks are advisory. A host failure must not
            // obscure the original diagnostic or affect AOT portability.
            return new AotTerminalInfo(true, null, null, OperatingSystem.IsWindows(), false, null);
        }
    }
}

internal static class AotTerminalColorPolicy
{
    internal static bool Resolve(AotColorMode mode, AotTerminalInfo terminal) => mode switch
    {
        AotColorMode.Always => true,
        AotColorMode.Never => false,
        AotColorMode.Auto => !terminal.IsErrorRedirected
            && string.IsNullOrEmpty(terminal.NoColor)
            && !string.IsNullOrEmpty(terminal.Term)
            && !terminal.Term.Equals("dumb", StringComparison.OrdinalIgnoreCase)
            && (!terminal.IsWindows || terminal.HasWindowsAnsiHost),
        _ => false,
    };

    internal static AotDiagnosticRenderOptions RendererOptions(AotColorMode mode)
    {
        AotTerminalInfo terminal = AotTerminalInfo.Capture();
        return new AotDiagnosticRenderOptions(AotTerminalColorPolicy.Resolve(mode, terminal), terminal.ErrorWidth);
    }

    internal static bool TryParse(string text, out AotColorMode mode) => text.ToLowerInvariant() switch
    {
        "auto" => Assign(AotColorMode.Auto, out mode),
        "always" => Assign(AotColorMode.Always, out mode),
        "never" => Assign(AotColorMode.Never, out mode),
        _ => Assign(default, out mode, false),
    };

    private static bool Assign(AotColorMode value, out AotColorMode target, bool success = true)
    {
        target = value;
        return success;
    }
}
