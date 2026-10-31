using TBDMovies.Application.Text;
using Xunit;

namespace TBDMovies.Tests.Application;

/// <summary>
/// The Season/Episode digit-input normalization. These fields must always present ASCII digits (even when
/// the app language is Arabic) and fold any pasted Arabic-Indic / Persian digits to ASCII.
/// </summary>
public sealed class AsciiDigitsTests
{
    [Theory]
    [InlineData("29", "29")]        // ASCII stays ASCII
    [InlineData("٢٩", "29")]        // Arabic-Indic -> ASCII
    [InlineData("۰۵", "05")]        // extended/Persian -> ASCII (leading zero preserved as text)
    [InlineData("٣٠", "30")]
    [InlineData("S02", "02")]       // non-digits dropped
    [InlineData("1,000", "1000")]   // separators dropped
    [InlineData("٣٠ abc", "30")]    // mixed Arabic digits + letters
    [InlineData("", "")]
    [InlineData(null, "")]
    public void OnlyAsciiDigits_folds_and_filters_to_ascii(string? input, string expected)
    {
        Assert.Equal(expected, AsciiDigits.OnlyAsciiDigits(input));
    }
}
