namespace DropSort.Application.Trakt;

/// <summary>
/// Secure credential storage contract for Trakt OAuth tokens bound to client ID.
/// Implementations must store the credentials in protected OS storage (e.g. Windows Credential Manager).
/// </summary>
public interface ITraktCredentialStore
{
    TraktCredentialPayload? Read();
    void Write(TraktCredentialPayload payload);
    void Delete();
}
