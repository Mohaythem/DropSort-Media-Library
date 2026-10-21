# ADR-0016: Read-only Trakt account cache

Status: accepted and implemented for the authorized Trakt Phase 1 scope. Non-UI verification completed on 2026-10-21. Manual UI and live-account acceptance remain pending.

## Context

The local catalog represents registered local media. Its custom lists require local catalog foreign keys. Trakt lists and history can contain remote-only items and items without TMDB identities. Reusing local catalog rows would create false local media and could change local viewing state.

The existing migration runner verifies backups, serializes writers, validates schema during transactions, and checks foreign keys. OAuth tokens need protected storage. SQLite settings contain plain strings.

## Decision

- Use Device Code authentication. The official Trakt OAuth contract supports device token polling and refresh without a client secret. All OAuth requests use `https://auth.trakt.tv`. No hosted callback or Windows URI registration is needed for the device flow. Refresh still supplies the redirect URI configured for the registered API app; configuration must provide it explicitly.
- Store the token pair as one protected credential value under a separate Trakt Credential Manager target. Bind it to the public client ID. Isolated QA profiles use separate hashed targets. Never access the normal profile in automated tests.
- Serialize token rotation and account transitions. A failed or uncertain refresh must not replay a possibly consumed refresh token. Require reconnect when secure persistence cannot establish a usable token pair.
- Bind definitive access-token rejection to the exact rejected token under the authentication gate. A second 401 after refresh marks credentials as requiring reconnect and blocks further automatic refresh. A stale rejection cannot invalidate a replacement token. Sync-state readers return immutable views without publishing captured snapshots back to shared state.
- Add schema version 18 through the existing migration runner. Use one `trakt_resource_cache` table keyed by immutable account ID and resource key. Store each complete resource snapshot as JSON, with separate success time, attempt time, and safe failure code. JSON retains provider identities, unknown fields, order, memberships, and repeated history event IDs without changing local catalog identity contracts.
- Replace a resource only after every required page succeeds. A failed resource retains its last complete payload. Network work occurs outside the database write gate. Do not save credentials or raw OAuth responses in this table.
- Keep matching as a pure application function. Match exact compatible external IDs and media kind. Hierarchy fallback requires an exact parent-show identity and season/episode coordinates. Reject ambiguity and conflicting identifiers. Availability and remote watched state remain independent from identity.
- Add separate Trakt account controls and a remote list viewer through the existing application composition. Do not require Trakt for normal local library use. The user performs all UI acceptance; agents do not launch or inspect the UI.
- Import custom lists, watchlist, watched state, and history only after endpoint-specific pagination and access are verified. Collection, player tracking, discovery, ratings, favorites, scrobbling, and remote mutations are outside this implementation.
- Local catalog clearing does not implicitly delete account cache or credentials. Disconnect behavior must be explicit and account scoped.

## Alternatives

Local custom-list membership cannot retain remote-only items without fabricating local catalog rows. A normalized remote-media schema is possible, but a complete resource snapshot is sufficient for this read-only phase and avoids premature cross-resource identity merging. A future migration can normalize snapshots if query or scale requirements justify it.

## Consequences

Cache replacement is atomic and retryable. Parsing and presentation remain typed at the application boundary. Large snapshots require bounded memory and responsive reads; stress tests must measure this before scale acceptance. Account transition tests must prove that late requests cannot publish data into a replacement session.

## Verified official references

- [OAuth endpoint contract](https://github.com/trakt/trakt-api/blob/master/projects/api/src/contracts/oauth/index.ts)
- [Optional deprecated client secret schema](https://github.com/trakt/trakt-api/blob/master/projects/api/src/contracts/oauth/schema/request/clientSecretSchema.ts)
- [Refresh request base schema](https://github.com/trakt/trakt-api/blob/master/projects/api/src/contracts/oauth/schema/request/tokenBaseSchema.ts)
- [Pagination](https://developer.trakt.tv/docs/pagination)
- [Rate limits](https://developer.trakt.tv/docs/rate-limiting)
- [Watched endpoint pagination and explicit progress mode](https://github.com/trakt/trakt-api/discussions/775): watched-show imports request `extended=progress` on every page. The client follows the applied page limit reported by the server.

These references establish protocol design. They do not establish successful authentication with a real account or completed application behavior.
