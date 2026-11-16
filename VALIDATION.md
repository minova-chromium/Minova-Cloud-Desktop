# Windows preview validation — 0.1.0

Validated on this Windows x64 PC with an isolated Minova server and synthetic accounts. No production user credentials were entered or extracted, and integration checks never targeted production storage.

Seventeen checks passed:

1. The branded WPF window loads and renders as an offscreen artifact.
2. Minova sign-in and encrypted Windows DPAPI credentials round-trip.
3. Native Windows registration creates online-only Cloud Files placeholders.
4. Opening a placeholder downloads the exact cloud contents.
5. A local file uploads with SHA-256 integrity verification.
6. Local edits replace only the expected cloud revision; the previous version remains recoverable.
7. Website edits invalidate cached data and supply the updated contents.
8. Simultaneous local and remote edits keep both versions locally and in Cloud.
9. Keep offline and free-up-space calls pin, hydrate, and dehydrate files.
10. Explorer pin-state changes are applied by the sync engine.
11. Online-only files survive renaming and moving between folders.
12. Folder moves retain working identities for their child placeholders.
13. Local deletion moves the matching remote version to Cloud trash.
14. Cloud deletion removes the Explorer entry and keeps a downloaded recovery copy.
15. Client restart reconnects using the existing account ledger.
16. Missing sync-root identity blocks synchronization and cloud deletions.
17. Traversal paths cannot escape the sync folder.

All eight Node server suites passed, including revision race detection, account isolation, conditional moves, stale downloads, safe directory deletion, and previous-version restoration. The Android download/release suite still passes.

The Release build completed without compiler warnings. The self-contained installer was installed, its DLL hash matched the packaged DLL, the uninstaller removed the application and its registration, and reinstall succeeded. Native integration checks unregister only their temporary test provider. Screen capture was not used; the UI preview was rendered directly by WPF.

GitHub Actions builds the Windows application and installer, verifies release metadata, retains artifacts, and prepares a draft. This is an unsigned preview; Windows compatibility beyond this PC is not yet independently verified. Remote server availability, open-file locks, and Windows hydration behavior can affect timing. Some rename operations may cache contents before sync completes. Shared-with-me items are accessed through the website in this preview.
