# Minova Cloud Desktop

A native Windows client for [Minova Cloud](https://cloud.project-minova.com). Sign in with your existing Minova account to see **Minova Cloud** in File Explorer's navigation pane.

## Desktop release - 0.3.0

- Approved Minova Cloud branding, retaining the original Minova M inside a dark cloud.
- Built-in updates: checks the latest published desktop release at startup and every six hours, downloads on unmetered connections, and installs when synchronization and the main window are idle. Open **Updates** in the app or tray to check, install, or disable automatic installation. Files and sign-in are retained.
- Every update requires a Minova RSA-signed release manifest and matching installer SHA-256. The embedded public key verifies updates independently of the download service.

- Default **Files**, **Music**, **Videos**, and **Pictures** folders, synced to the website and Android. Existing folders are reused; deleting or renaming them is respected.
- Native Windows Cloud Files API registration and online-only placeholders.
- Opening a file downloads its contents. Explorer's **Always keep on this device** and **Free up space** controls are supported.
- File creation, edits, renames, moves, folders, and deletions sync with the same private library used by Android and the website.
- Local changes wake the sync engine after a short delay; remote changes are polled every 15 seconds.
- Concurrent edits retain a separate conflict copy. Replaced cloud versions remain in Cloud trash; downloaded remote-deleted files are kept under the account's local Recovered files folder.
- Tray status, pause/resume, sync now, per-user installation, and automatic startup at Windows sign-in.
- Device credentials protected with Windows DPAPI. The password is never saved. Disconnect the PC from the app or the website's Connected devices screen.

Windows 10 version 2004 or later / Windows 11, x64, with an NTFS sync drive. ARM builds are not included. The current preview installer is unsigned. Shared-with-me files and account invitations open through the website; the Explorer folder syncs your own private library. Large libraries exceeding the server's traversal limit stop safely rather than applying a partial listing. An online-only file needs this app running and the Minova server online. Windows may cache file contents during some file operations.

The default folder is `%USERPROFILE%\Minova Cloud - <username>`. This is a client folder, separate from the server's storage. Existing nonempty folders are never adopted on first connection. Missing or replaced sync roots stop synchronization without deleting cloud data. Keep the hidden `.minova-sync-root` identity file in place.

## Install and uninstall

Open `Minova-Cloud-Setup.exe`. Installation is for the current Windows user and includes the .NET runtime. Sign in with your existing Cloud username and password; invited people first create an account using their invitation email.

Closing the window keeps the tray app running. Choose **Quit** in the tray menu to stop it. Uninstall using Windows Installed apps. Cloud storage and downloaded files are retained; online-only entries are removed by Windows provider unregistration and remain available on the website. Account sync metadata is reset so reinstalling repopulates the folder safely.

## Build

Install the .NET SDK version in `global.json`, then run:

```powershell
.\Build-Minova-Cloud.ps1
```

The script produces a self-contained x64 application and a checksum-verified installer using the Windows .NET Framework compiler. No administrator access or extra installer compiler is needed. Output goes to ignored `releases/`; build intermediates go to ignored `.work/`.

Release packaging also signs the exact `release.json` bytes as `release.json.sig`. GitHub Actions uses `MINOVA_UPDATE_SIGNING_KEY`; local packaging uses the DPAPI-protected key under `%LOCALAPPDATA%\Minova Cloud\Update Signing`. Back up the private signing identity securely; never commit it. `update-public.pem` is public and embedded in the app. Pull requests use `-UnsignedPreview` and receive no private key. Manifest signing is separate from Windows Authenticode; the installer does not yet have an Authenticode certificate.

Install 0.3.0 once over the previous desktop release to enable the updater. Only published stable GitHub releases are offered; drafts are never installed. Automatic installation relaunches the client in the background. The updater pauses on download, trust or integrity errors and keeps the current installation.

Changes pushed to `main` compile automatically on GitHub Actions and prepare a **draft** release. Publishing a draft makes it the latest stable desktop build. Download the latest published installer at https://github.com/minova-chromium/Minova-Cloud-Desktop/releases/latest/download/Minova-Cloud-Setup.exe. Windows releases are separate from Android so neither platform's latest download hides the other platform's assets. Increase the project version before publishing another version; published releases are never overwritten.

## Validation

The local integration suite uses the actual Windows Cloud Files API and an isolated Minova server with synthetic accounts. It covers DPAPI, online-only hydration, verified upload, local and remote edits, simultaneous-edit preservation, pin-state changes, file/folder moves, cloud trash, local recovery, restart, missing-root safeguards, and path traversal. The test root is registered temporarily and unregistered on completion. Its fixture expects the sibling `minova-cloud` server checkout and uses port 4197:

```powershell
node test/fixture.mjs
# In another terminal, build Debug, then:
dotnet src/Minova.Cloud.Windows/bin/Debug/net10.0-windows10.0.19041.0/Minova.Cloud.dll --integration-test .work/integration-report.txt http://127.0.0.1:4197
```

Release builds ignore test origins and exclude the integration harness. They connect only to `https://cloud.project-minova.com` with normal HTTPS validation. User files, device credentials, server configuration, signing secrets, and build artifacts are never committed.

## Cloud wordmark

The two-row white/cyan Minova Cloud wordmark is the supplied artwork, not an AI redraw. `brand/minova-cloud-wordmark-source.png` retains the original PNG byte for byte. `brand/PrepareWordmark.cs` finds its transparent bounds, exports a clipped SVG embedding the unchanged source, and makes a 1024-pixel UI PNG. The approved cloud/M application icon is retained.
