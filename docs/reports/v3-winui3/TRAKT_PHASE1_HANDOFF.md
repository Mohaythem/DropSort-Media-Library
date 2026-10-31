# Trakt Phase 1 setup and manual acceptance

Status: non-UI implementation and verification complete on 2026-10-21. Ready for user-owned manual UI and live-account acceptance. This document is a checklist, not proof of UI or account acceptance.

## Setup

1. Use a registered Trakt API application's public Client ID and its exact registered HTTPS redirect URI. Enter these in Settings > Trakt and save. Do not enter a client secret. Configuration is stored as one nonsecret settings value.
2. Saving replacement configuration disconnects the existing Trakt session. Reconnect after changing registration parameters.
3. Select Connect. Open the verification URL from the application, enter the displayed user code, and authorize in your browser. Device-code polling completes in TBD Movies. No callback listener or custom Windows protocol is installed.
4. Select Sync Now to import account data. Open Trakt in the navigation pane to browse Watchlist, custom lists, watched movies, watched shows, and history.

The agent has not registered an API app, signed in, accessed normal-profile credentials, or tested a live account. Do not send tokens or a client secret in chat.

## Data and matching behavior

- Trakt uses a separate account-scoped cache. Import does not add movies/files to Library, populate local custom lists, or change local watch history or playback positions.
- Protected Windows credential storage holds tokens separately from SQLite. QA profiles use credential targets derived from `TBD Movies_TEST_DATA_ROOT`.
- Disconnect removes the connection's protected credential. The last account's cached resources remain available offline. It does not revoke authorization on Trakt's website.
- Exact compatible provider IDs and media type establish a match. Episode fallback requires exact parent-show IDs plus season/episode coordinates. Conflicting or ambiguous identities remain unmatched. Titles do not establish automatic matches.
- Remote-only and unsupported rows remain visible. They have no fabricated local file path. A matched row can open existing local details.
- Remote watched badges are separate from local availability. A watched subset does not establish that a whole show or season is complete or available. Unknown totals remain unknown.
- Each resource replaces its previous cache only after a complete validated response. A failed resource retains its previous successful payload. A partial sync can update successful resources while retaining failed ones.

## Limits and failure behavior

- Data requests use fixed production hosts and read-only resource endpoints. OAuth device/refresh requests are the authentication exception. No scrobbling or remote list/history mutations are implemented.
- Requests are bounded to 10 MiB per page, 100 MiB per resource, and 500 pages. Paginated requests use a page limit of 250. These are safety ceilings, not proof of performance at that size.
- Changing pagination totals, incomplete responses, invalid payloads, or exceeded limits fail that resource without replacing its last complete cache.
- Rate limits allow at most three retries, with a maximum accepted retry delay of 60 seconds. Watched-show pages explicitly request extended=progress. Authorization retries allow one refresh; a second 401 marks the session as requiring reconnect and stops further automatic token rotation. An uncertain refresh outcome requires reconnect rather than replaying a possibly consumed refresh token.
- Collection, ratings, favorites, discovery, playback tracking, and two-way sync are outside this phase.

## Manual checks owned by the user

Agents must not launch, inspect, automate, screenshot, or visually verify the application. The user checks:

- Settings and Trakt navigation in English and Arabic, including RTL, narrow windows, and theme changes.
- Save valid configuration, validation of empty/invalid values, and optional startup with no Trakt configuration.
- Connect, cancel during device-code creation/polling, reconnect immediately, deny or expire a code, and disconnect during sync.
- Custom-list choices after entering Trakt, switching resources, leaving the page, and returning from local details.
- Remote-only items, unsupported types, repeated history entries, matching conflicts, season zero, watched episode counts, and partial show availability.
- Cached browsing after restart/disconnect, resource timestamps, retained data after a failed sync, and clear partial/error feedback.
- Local Library, local lists, local watch history, and media files retain their independent state.

Before using a real profile, assess startup migration/backup behavior. Automated tests use disposable fixtures. No agent has launched the application against the real database.

## Verification record

Final actual repository results after independent-review fixes: the broader run passed 973 tests with the native WindowsTmdbCredentialStoreTests class excluded. This includes all Trakt tests and the new null-JSON, progress-pagination, second-401 reconnect, and concurrent-state-read regressions. Debug x64 Visual Studio MSBuild succeeded. No app launch or real-profile database migration occurred. Existing NU1900 vulnerability-feed and xUnit analyzer warnings remain. The native Trakt credential store and live OAuth flow require manual acceptance. Large-resource ceilings have failure-path tests; performance at the maximum supported resource size has not been established. Theme switching and visual behavior require the user's checks.

Exact commands, job status, passing test counts, exclusions, build results, and open findings are recorded in `TRAKT_PHASE1_WORKLOG.md`. A successful build is not manual UI acceptance. A mocked authentication test is not live-account verification.
