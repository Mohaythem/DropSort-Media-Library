# DropSort 0.10.0-rc.1

Local Windows x64 release candidate for manual acceptance.

- Updated Movie Details layout and file actions.
- Smart TV Batch Import recognizes common episode filenames and folder hierarchies. Review series, seasons and episodes; correct or exclude items before confirming library registration.
- Compact grouped review supports tri-state selection and expand/collapse. The chevron sits between selection and the title. Correction actions stay at the opposite edge. English, Arabic/RTL, Slate, Light and Dark are retained.
- Missing/changed files, duplicates, cancellation and partial failures are handled without automatic media movement or deletion.
- Settings shows the application version. Existing library schema v15 is reused; no new migration is introduced by this release work.

This candidate is unsigned. Clean-machine, broad Windows, real SMB/removable-drive and large-library verification remain acceptance limitations. Existing deferred roadmap items remain open. Synthetic-file QA checks registration and UI behavior; it does not validate video decoding/playback of real media.

The installer and portable ZIP contain the same self-contained WinUI application. Library data remains in the user data directory. Close other DropSort instances before launching.
