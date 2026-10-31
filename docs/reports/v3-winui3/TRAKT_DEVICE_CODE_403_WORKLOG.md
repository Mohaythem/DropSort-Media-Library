# Trakt Device Code HTTP 403 investigation

Date: 2026-10-25, Africa/Cairo. Workspace: TBDMovies_WinUI3. Branch: codex.

## Result

The user's live HTTP 403 is not yet attributed to an exact server-side cause. No real Client ID, secret, token, credential vault, database, or account was accessed. The known failure was reproduced by the user, not by an authenticated request from this task. Do not claim that a first real connection has succeeded or that the 403 is fixed.

The suspected wrong-host defect is disproven by the current official operation definitions. The public OpenAPI document at https://developer.trakt.tv/openapi.json sets the root server to api.trakt.tv/apiz.trakt.tv, but explicitly overrides servers to https://auth.trakt.tv on POST /oauth/device/code, POST /oauth/device/token and POST /oauth/token. The inspected application already uses that authentication host. Switching it to the general API host is not justified. A public, credential-free extract is saved in artifacts/trakt-403-20261025/official-auth-endpoints.json.

Official guides consulted: https://developer.trakt.tv/docs/api-url, https://developer.trakt.tv/docs/required-headers, https://developer.trakt.tv/docs/authentication-oauth, https://developer.trakt.tv/docs/status-codes, https://developer.trakt.tv/docs/premium and https://developer.trakt.tv/docs/create-an-app. The general guide's Standard/Premium distinction applies to the root data API servers; the OAuth operation override is more specific. The developer account Premium tier is distinct from user VIP. No source established that Development mode or the supplied callback causes this initial 403.

## Verified request/configuration trace

SettingsPage.TraktSaveConfigButton_Click trims Client ID and redirect URI, validates nonempty ID and absolute HTTPS callback, and awaits AppServices.ConfigureTraktAsync. That method constructs TraktAuthConfig, stores the serialized ClientId/RedirectUri DTO in settings key trakt_api_config, and publishes a service graph built with that same config. TraktConfig reads the same key. TryRestoreTraktOnStartup reads it again, trims both fields and constructs the auth service. There is no placeholder/fallback ID in this path. This is source evidence, not evidence of what the user's saved value contains.

Connect uses AppServices.Trakt.BeginAuthAsync, TraktSyncService calls TraktAuthService.BeginDeviceAuthAsync, and the latter supplies _config.ClientId to TraktAuthClient.GenerateDeviceCodeAsync.

The actual source request is POST https://auth.trakt.tv/oauth/device/code, Content-Type application/json (UTF-8), Accept application/json, trakt-api-key equal to the configured Client ID, trakt-api-version 2, and a JSON object containing only client_id. The first device-code payload contains neither redirect_uri nor client_secret nor a user access token. The redirect callback is not the device verification URL. User-Agent was absent; the current guide suggests it but does not establish its absence as this 403's cause. No speculative header, host, app-mode or authentication-policy change was made.

## Scoped changes

Only production file changed for this task: src/TBD Movies.Infrastructure/Trakt/TraktAuthClient.cs. Tests: tests/TBD Movies.Tests/Infrastructure/TraktAuthClientTests.cs.

- Previously, a failure discarded all context and showed only the numeric status. Now it gives fixed method/host/path, numeric status, safe body classification (JSON, unreadable JSON, HTML/gateway, non-JSON, unavailable), known allowlisted OAuth error codes, and strictly validated optional UUID request ID/Cloudflare Ray ID.
- Raw bodies, arbitrary server error codes/descriptions and arbitrary headers are never included in the exception. Potential credential echoes are not displayed. Malformed payloads do not become inner exceptions containing response text.
- A 403 advises verifying the API Client ID and app approval/status, explicitly without asserting a single cause. No retry loop was added.
- Failure body reads are capped at 4096 bytes with a 5-second diagnostic read deadline. Transport read failures preserve the status with a safe unavailable-body classification. Caller cancellation propagates. The existing HTTP timeout covers response content as well as headers.
- Pre-cancelled device-code requests stop before sending, and direct device-code callers trim Client ID. UI/config trim behavior was already present.

Offline cache, identity matching, read-only sync, token refresh/polling policy, credential storage, schema, scanner and title behavior remain unchanged by this task. Existing uncommitted source/test/packaging work and unrelated deletions were preserved. No Git branch switch, commit, push, installer generation/installation, UI launch or UI inspection occurred.

## Executed verification

- New diagnostic regression run before the production change: 20 tests, 5 failed, 15 passed. Four failures demonstrated missing safe/actionable diagnostics; one demonstrated a pre-cancelled request reaching the fake handler. This did not reproduce the user's server-side 403 cause.
- Final targeted command: dotnet test TBDMovies_WinUI3/tests/TBD Movies.Tests/TBD Movies.Tests.csproj --no-restore --filter FullyQualifiedName~Trakt --logger "trx;LogFileName=trakt-verified.trx" --results-directory TBDMovies_WinUI3/artifacts/trakt-403-20261025. Exit 0: 195 passed, 0 failed, 0 skipped.
- Final broad command: dotnet test TBDMovies_WinUI3/tests/TBD Movies.Tests/TBD Movies.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName!~WindowsTmdbCredentialStoreTests" --logger "trx;LogFileName=broad-verified.trx" --results-directory TBDMovies_WinUI3/artifacts/trakt-403-20261025. This used the final binaries compiled by the preceding targeted command. Exit 0: 1122 passed, 0 failed, 0 skipped. Native WindowsTmdbCredentialStoreTests were excluded, not passed. Existing analyzer warnings remain in test compilation.
- Test coverage includes request URL/headers/JSON and no secret, 200 response, 403 JSON/HTML/malformed/text bodies, credential echo rejection, bounded body reads, valid/invalid correlation IDs, IO read failure, cancellation, content timeouts for success/failure, no 403 retry, and save/replace/read through a new real SettingsRepository plus TraktAuthConfig/TraktAuthService/TraktAuthClient. That integration fixture uses a disposable settings-only SQLite file; it does not execute the SettingsPage click handler or AppServices. UI persistence wiring was inspected from source.
- Release x64 self-contained rebuild uses Visual Studio 18 MSBuild /t:Rebuild /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /p:SelfContained=true /p:WindowsAppSDKSelfContained=true /m:1 /v:minimal /nologo, with a new output directory artifacts/trakt-403-20261025/release-verified. Exit 0. Required EXE, DLL, PRI, Trakt XBF, .NET, WinUI and SQLite files exist. Nothing was copied over the installed app. Installed TBD Movies.UI.exe and TBD Movies.Infrastructure.dll SHA256 values still match the previous local installation manifest. Final git diff --check exited 0; branch remains codex.

Logs are in repository artifacts/trakt-403-*.log. TRX evidence and public source extract are under TBDMovies_WinUI3/artifacts/trakt-403-20261025. Focused and broad counts overlap and must not be added. All execution used mocks/disposable fixtures; no live authorization request occurred.

## Ranked remaining hypotheses and manual check

1. An incorrect/mismatched saved API Client ID or app approval/status: consistent with the official 403 description, but the actual configured value and dashboard have not been inspected.
2. HTML/gateway/Cloudflare rejection: possible until the response classification/correlation from the real failed request is observed. The numeric 403 alone cannot distinguish this.
3. Other Trakt-side restrictions or service conditions: unresolved if the dashboard/key check is correct. No claim that switching Development to Production or changing redirect URI will solve it.

The installed 0.11.1 copy has NOT been updated with this diagnostic change. For an authorized manual test, close other TBD Movies instances, run the complete diagnostic build from artifacts/trakt-403-20261025/release-verified/TBD Movies.UI.exe, open Settings/Trakt and verify privately that the saved Client ID exactly matches the same developer app (not its Client Secret). Keep the registered callback unchanged unless it actually differs from the app registration. Save if editing is required, then press Connect once. If codes appear, visit the returned verification URL, enter the displayed code and authorize as the user, then verify Connected. If 403 persists, supply only the sanitized diagnostic message, with settings fields excluded from any screenshot. Check app status/approval and account eligibility in the developer dashboard or contact Trakt support with the safe correlation ID. Never paste keys/tokens/secrets. Stop before further automated account actions.

## Follow-up: identifying User-Agent

The user supplied two real sanitized diagnostics: HTML/gateway 403 with Cloudflare correlation IDs before and after disabling V2Ray. Disabling V2Ray alone did not resolve it. The user then confirmed the browser activation page opens and waits for a code. This establishes browser access to the activation page, not successful API authorization and not the root cause of the POST rejection.

Further primary-source research found Trakt team announcement https://github.com/trakt/trakt-api/discussions/655. It explicitly states that some requests without an identifying User-Agent may be blocked by security measures, and asks for application name/version. This is stronger evidence than the general headers guide's suggestion. Source inspection confirmed no explicit identifying User-Agent in any of the three OAuth requests or the common GET request construction.

Added internal TraktRequestHeaders.IdentifyApplication, using a ProductInfoHeaderValue with product TBD Movies and the entry application's numeric assembly version (0.11.1.0 in the current executable; safe 0.0.0 fallback). It contains no account/client/token values and does not impersonate a browser. Called per request from device-code generation, device polling, token refresh, and common data GET construction, including subsequent pages/retries. Shared HttpClient default headers are not modified. Hosts, request payloads, retry limits, credential storage and local persistence remain unchanged.

Tests: the new OAuth cases and the strengthened two-page watched-show case first failed (4 failed, 0 passed), proving the missing identifying request header. After the fix, focused Trakt suite passed 198 tests, 0 failed, 0 skipped. This proves request construction, not that Cloudflare accepted the user's real credentials. Broader test/build results follow after completion. The new manual-test build directory is artifacts/trakt-403-20261025/release-user-agent; use this instead of the earlier diagnostic build. No installer, app launch, real database/credential access or live authorization was performed.

Final follow-up verification: applicable broad suite 1125 passed, 0 failed, 0 skipped, exit 0 (same exclusion of WindowsTmdbCredentialStoreTests). Fresh self-contained Release x64 /t:Rebuild to release-user-agent exited 0. Required executable, PRI, Trakt XBF, runtime and SQLite files exist; entry assembly numeric version is 0.11.1.0. git diff --check exited 0 and branch remains codex. Logs: repository artifacts/trakt-ua-before.log, artifacts/trakt-ua-focused.log, artifacts/trakt-ua-broad.log, artifacts/trakt-ua-release.log. TRX files: ua-before.trx, ua-focused.trx, ua-broad.trx in the investigation artifact directory. Focused/broad counts overlap. Successful real device-code generation remains pending user verification.
