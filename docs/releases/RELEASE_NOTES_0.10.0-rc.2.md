# DropSort 0.10.0-rc.2

Windows x64 pre-release. The installer and application are unsigned.

- Removed Folder Watcher, its background monitoring, configuration, and navigation.
- Removed Smart Relink, its candidate search, batch review, and verification cache integration.
- Retained Check Library's original availability checks and manual file selection.
- Retained Add Media, Smart TV Batch Import, compact grouped review, Movie/TV Details, and the native clickable folder picker.
- Multiple distinct files and editions can reuse one TMDB movie identity. The exact same local file cannot be registered twice.
- Existing library records, metadata, watch history, lists, and relationships are preserved. No database migration is introduced.
- Existing watcher configuration and verification JSON files in user profiles are left untouched. The removed features no longer load or update them.

Close other DropSort instances before launching. Library data remains outside the installation directory. This release does not move, rename, or delete media automatically.

Verification uses isolated databases and synthetic media. Clean-machine testing, broader Windows hardware, real SMB/removable-drive checks, video decoding, and code signing remain limitations. This is a release candidate, not a stable release.
