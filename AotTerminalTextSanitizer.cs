using System.Globalization;
using System.Text;

namespace PwshAotLite;

// Side-stream text may come from a future port. Escape controls at the shared
// host boundary so a verbose/debug message cannot inject ANSI control flow,
// erase a diagnostic, or split terminal lines.
internal static class AotTerminalTextSanitizer
{
    internal static string SanitizeInline(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        StringBuilder safe = new(value.Length);
        foreach (char character in value)
        {
            safe.Append(character switch
            {
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(character) => "\\u" + ((int)character).ToString("X4", CultureInfo.InvariantCulture),
                _ => character,
            });
        }

        return safe.ToString();
    }
}
