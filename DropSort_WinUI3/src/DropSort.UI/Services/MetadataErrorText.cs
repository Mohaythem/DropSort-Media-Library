using DropSort.Application.External;

namespace DropSort.UI.Services;

internal static class MetadataErrorText
{
    public static string For(Exception error) => LocalizationService.Text(MetadataErrorKey.For(error));
    public static string For(ConnectionTestResult result) => LocalizationService.Text(MetadataErrorKey.For(result));
}
