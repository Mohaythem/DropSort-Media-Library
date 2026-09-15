using System.Text.RegularExpressions;
using DropSort.Application.External;
using DropSort.UI.Services;
using Xunit;

namespace DropSort.Tests.UI;

public sealed class PostHandoffPresentationTests
{
    private static string UiRoot
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            {
                var path = Path.Combine(directory.FullName, "src", "DropSort.UI");
                if (Directory.Exists(path)) return path;
            }
            throw new DirectoryNotFoundException("UI source not found.");
        }
    }

    [Fact]
    public void Every_metadata_category_has_distinct_English_and_Arabic_copy()
    {
        var source = File.ReadAllText(Path.Combine(UiRoot, "Services", "LocalizationService.cs"));
        var keys = Enum.GetValues<MetadataFailureKind>().Select(MetadataErrorKey.For).ToArray();
        Assert.Equal(keys.Length, keys.Distinct().Count());
        foreach (var key in keys.Concat(["MetadataCancelled", "MetadataUnexpectedError", "RelinkFailedHelp", "CrashTempTitle", "CrashTempHelp"]))
        {
            var entries = Regex.Matches(source, "\\[\"" + key + "\"\\] = \"([^\"]+)\"");
            Assert.Equal(2, entries.Count);
            Assert.NotEqual(entries[0].Groups[1].Value, entries[1].Groups[1].Value);
            Assert.Matches("[\\u0600-\\u06ff]", entries[1].Groups[1].Value);
        }
    }

    [Fact]
    public void Error_mapping_never_returns_sensitive_exception_or_connection_text()
    {
        const string sensitive = "secret-token https://example.invalid/?api_key=secret-token";
        Assert.Equal("MetadataUnexpectedError", MetadataErrorKey.For(new Exception(sensitive)));
        Assert.Equal("MetadataCancelled", MetadataErrorKey.For(new OperationCanceledException(sensitive)));
        Assert.Equal("TmdbConnectionFailed", MetadataErrorKey.For(new ConnectionTestResult(false, sensitive)));
        Assert.Equal("MetadataAuthenticationError", MetadataErrorKey.For(
            new ConnectionTestResult(false, sensitive, 401, MetadataFailureKind.Authentication)));
    }

    [Fact]
    public void Settings_and_relink_use_localized_safe_errors_and_temp_report_is_read_only()
    {
        var settings = File.ReadAllText(Path.Combine(UiRoot, "Views", "SettingsPage.xaml.cs"));
        var connection = settings[settings.IndexOf("private async void TestConnectionButton_Click", StringComparison.Ordinal)..];
        connection = connection[..connection.IndexOf("private void SetupGuideButton_Click", StringComparison.Ordinal)];
        Assert.Contains("MetadataErrorText.For(result)", connection);
        Assert.DoesNotContain("ex.Message", connection);
        Assert.DoesNotContain("AppServices.TmdbClient", connection);
        var check = File.ReadAllText(Path.Combine(UiRoot, "Views", "CheckLibraryPage.xaml.cs"));
        var relink = check[check.IndexOf("private async void RelinkAction_Click", StringComparison.Ordinal)..];
        relink = relink[..relink.IndexOf("private async void IssueAction_Click", StringComparison.Ordinal)];
        Assert.Contains("LocalizationService.Text(\"RelinkFailedHelp\")", relink);
        Assert.DoesNotContain("error.Message", relink);
        Assert.Contains("AppServices.DetectStaleTempFiles()", check);
        Assert.Contains("_staleTemps.Select(file => file.FilePath)", check);
        Assert.DoesNotContain("File.Delete", check);
        Assert.DoesNotContain("Directory.Delete", check);
    }
}
