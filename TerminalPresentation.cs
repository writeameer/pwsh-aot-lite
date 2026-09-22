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
    internal static AotTerminalInfo Capture() => AotHostComposition.Substrate.Terminal.Capture();
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

    internal static AotDiagnosticRenderOptions RendererOptions(AotColorMode mode, IAotTerminalInfoSource? terminalSource = null)
    {
        AotTerminalInfo terminal = (terminalSource ?? AotHostComposition.Substrate.Terminal).Capture();
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
