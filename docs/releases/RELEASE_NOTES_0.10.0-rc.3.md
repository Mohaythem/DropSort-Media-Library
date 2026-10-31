# TBD Movies 0.10.0-rc.3

Unsigned Windows x64 release candidate. This is not a stable or signed release.

- Refined the native Home dashboard, landscape library cards, Slate colors, and empty states.
- Continue Watching records successful playback launches. Mark Watched completes an active viewing and preserves the watch history. Playback position and remaining time are not measured.
- Simplified My Lists to Watch Later, Favorites, and custom lists. Both movies and TV shows are supported. Existing movie memberships remain stored.
- Check Library uses the available page width and puts metadata issues before missing files.
- TV Details has a wider bounded layout, a larger poster, and a confirmed Remove from library action. Removal leaves physical video files and historical watch snapshots intact.
- Forward-only database migrations add active viewing state (v16) and TV personal memberships (v17). Existing databases use the existing pre-migration backup protection.
- Folder Watcher and Smart Relink remain removed. Add Media, Smart TV Batch Import, native folder picking, and TMDB movie reuse remain available.

The per-user installer creates a Start menu shortcut and offers a desktop shortcut, selected by default. Install and uninstall preserve the library data under `%LOCALAPPDATA%\TBD Movies` and never remove media files. Close TBD Movies before installing.

The user accepted the current TV Details layout on 2026-10-15. Automated tests and runtime verification are left to the user by explicit instruction. Current build evidence and exact source provenance accompany the installer. Earlier test totals are historical and do not certify this candidate. Clean-machine installation and code signing remain unverified.
