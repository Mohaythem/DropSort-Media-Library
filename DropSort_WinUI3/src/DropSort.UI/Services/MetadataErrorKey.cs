using DropSort.Application.External;

namespace DropSort.UI.Services;

/// <summary>Maps safe application categories to presentation resources, never exception text.</summary>
public static class MetadataErrorKey
{
    public static string For(Exception error) => error switch
    {
        OperationCanceledException => "MetadataCancelled",
        MetadataServiceException metadata => For(metadata.Kind),
        _ => "MetadataUnexpectedError",
    };

    public static string For(ConnectionTestResult result) => result.Success
        ? "TmdbConnectionSuccess"
        : result.FailureKind is { } kind ? For(kind) : "TmdbConnectionFailed";

    public static string For(MetadataFailureKind kind) => kind switch
    {
        MetadataFailureKind.NotConfigured => "TmdbNotConfiguredPrompt",
        MetadataFailureKind.Authentication => "MetadataAuthenticationError",
        MetadataFailureKind.RateLimited => "MetadataRateLimitError",
        MetadataFailureKind.Network => "MetadataNetworkError",
        MetadataFailureKind.Timeout => "MetadataTimeoutError",
        MetadataFailureKind.Api => "MetadataApiError",
        MetadataFailureKind.InvalidResponse => "MetadataInvalidResponseError",
        MetadataFailureKind.NotFound => "MetadataNotFoundError",
        MetadataFailureKind.IncompleteMetadata => "MetadataIncompleteError",
        MetadataFailureKind.StaleRequest => "MetadataStaleRequestError",
        _ => "MetadataUnexpectedError",
    };
}
