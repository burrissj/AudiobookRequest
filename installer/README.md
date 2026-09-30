# Building the installer

Prerequisites (one-time):
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`)
- `tools/ffmpeg/ffmpeg.exe` and `tools/ffmpeg/ffprobe.exe` — a slim Windows build, e.g. the
  "essentials" build from https://www.gyan.dev/ffmpeg/builds/ (`ffmpeg-release-essentials.zip`).
  Not committed to the repo; download once and drop the two .exe files there.

Build:

```powershell
powershell -File scripts\publish.ps1
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\AudiobookRequest.iss
```

This produces `installer\Output\AudiobookRequestSetup.exe`.

## What the installer does

- Installs everything to `C:\ProgramData\AudiobookRequest` (writable by the LocalSystem account
  the service runs as, unlike Program Files).
- Registers and starts a Windows Service named `AudiobookRequest` (display name "Audiobook Request").
- Opens an inbound firewall rule for TCP 5230 (the app listens on `http://0.0.0.0:5230`, i.e. it's
  reachable from other devices on the LAN — phones, etc. — not just localhost).
- Adds Start Menu / Desktop shortcuts that open the web UI.
- Does **not** prompt for any app configuration (Chaptarr/Jellyfin URLs, library paths, ...) — that's
  all handled by the app's own first-run setup wizard at http://localhost:5230 after install.

## Uninstalling

Standard "Apps & Features" uninstall. Stops and deletes the service and firewall rule. The `data\`
folder (SQLite DB, logs, data-protection keys) is deliberately left behind so a reinstall doesn't
lose request history or the library index — delete `C:\ProgramData\AudiobookRequest` manually if you
want a full wipe.
