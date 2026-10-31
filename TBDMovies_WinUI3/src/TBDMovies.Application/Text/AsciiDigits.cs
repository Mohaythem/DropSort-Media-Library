using System.Text;

namespace TBDMovies.Application.Text;

/// <summary>
/// Numeric-input digit normalization for controls that must always present ASCII digits regardless of the
/// app's display language. Arabic-Indic (٠-٩) and extended/Persian (۰-۹) digits are folded to ASCII 0-9;
/// any non-digit character is dropped. This keeps a numeric field (e.g. Season / Episode) showing "29"
/// even in Arabic, and turns a pasted "٢٩" into "29", without changing the app's language anywhere else.
/// </summary>
public static class AsciiDigits
{
    /// <summary>Returns only the ASCII digits of <paramref name="value" />, folding Arabic/Persian digits.</summary>
    public static string OnlyAsciiDigits(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            var folded = character switch
            {
                >= '٠' and <= '٩' => (char)('0' + (character - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (character - '۰')),
                _ => character,
            };

            if (folded is >= '0' and <= '9')
            {
                builder.Append(folded);
            }
        }

        return builder.ToString();
    }
}
