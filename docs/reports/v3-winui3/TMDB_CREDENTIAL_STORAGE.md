# TMDB credential storage

Status: V2 implementation decision.

TBD Movies is an unpackaged Windows desktop app. Its former session-only TMDB
credential required re-entry after restart. The SQLite settings repository stores
plain strings and is unsuitable for a secret.

Use Windows Credential Manager through a TMDB-specific application contract.
Infrastructure implements native generic credential read, write, and deletion.
Persistence is scoped to the current Windows user on this computer. No new
package dependency, database column, credential file, or generic secret framework
is required. Catalog backups do not include the Credential Manager entry.

SettingsService restores the credential locally during startup. It performs no
startup network validation. The existing shared TmdbClient reads the current
credential through its existing provider delegate. Settings validates a candidate
through that same client before writing, reading back, and publishing the new
value. Failed candidate authentication or service connectivity does not replace
the saved credential. Normal request authentication failures identify the
credential used by that request, so a late response cannot reject a replacement.

The Settings input contains only a new value. It never displays or restores a
saved secret. Removal requires confirmation and removes only the TMDB credential.
Library records, cached metadata, cached posters, watch history, and media files
are retained. Local library access does not require TMDB.

If the obsolete `tmdb_read_access_token` settings row exists, migration writes it
to protected storage and verifies the read-back before deleting the row. An
existing protected value takes precedence. Migration failure retains the legacy
row for recovery. This deletes the logical setting; it does not rewrite old
database backups or promise forensic erasure of historical SQLite pages.

An isolated `TBD Movies_TEST_DATA_ROOT` selects a separate Credential Manager target
derived from that profile path. QA never reads, replaces, or deletes the normal
profile's credential. Native round-trip tests use unique isolated profiles and
synthetic values. Reports, screenshots, and logs must never contain real secrets.
