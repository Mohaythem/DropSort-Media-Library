namespace DropSort.Application.External;

public interface ITmdbCredentialStore
{
    string? Read();
    void Write(string credential);
    void Delete();
}

public interface ITmdbCredentialValidator
{
    Task<ConnectionTestResult> ValidateCredentialAsync(string credential, CancellationToken cancellationToken = default);
}

public enum TmdbCredentialState { NotConfigured, Configured, AuthenticationFailed, StorageUnavailable }
public enum TmdbCredentialFailure { None, Empty, Authentication, Network, Timeout, Service, Storage }
public record TmdbCredentialResult(bool Success, TmdbCredentialFailure Failure);
