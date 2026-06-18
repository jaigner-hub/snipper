# CLAUDE.md

Guidance for working in this repo.

## What this is

**Snipper** — a Windows "snipping tool for video." You drag a box over any video
playing on screen (YouTube, a local player, a stream, a game), it records that
*screen region*, then you trim it and add meme captions and export to **GIF /
MP4 / MOV / WebP / APNG** — or copy a GIF straight to the clipboard for
Discord/Slack/Teams.

It captures the screen region, not a video file, so it works with anything
on-screen. There's no real video decoding by us — **ffmpeg does all the capture
and encoding**; we just build its command lines and drive the WPF UI.

Stack: **WPF (.NET 8, `net8.0-windows`, C#, nullable enabled)**, Windows-only.
Single dependency: **ffmpeg.exe**.

## ⚠️ Windows-only — do not try to build/run from WSL

WPF and ffmpeg `gdigrab` (the screen-region grabber) only exist on Windows. This
repo lives under a WSL-visible path (`/mnt/c/...`) but **`dotnet build`/`dotnet
run` must be run from Windows** (cmd, PowerShell, or Visual Studio), not a WSL
shell. You can read/edit files from here, but can't actually compile or run.

```powershell
cd C:\Users\Jeff\Desktop\snipper
dotnet build      # or: dotnet run, or open in VS 2022 and press F5
```

`ffmpeg.exe` is resolved at runtime in this order (`Ffmpeg.Path`):
1. next to `Snipper.exe`
2. in a `./ffmpeg/` subfolder (the `.csproj` copies this into the build output if present)
3. on `PATH`

The `ffmpeg/` folder and `ffmpeg.exe` are **gitignored** (large binary) — download
a static build per `README.md` and drop `ffmpeg.exe` into `./ffmpeg/`.

## Architecture — logic vs. ffmpeg vs. WPF

There's no folder split; everything is flat at the repo root. The mental model:

**Plain logic / data (no WPF):**
- `Models.cs` — all the data types: `CaptureRect` (region in **physical pixels**),
  `SnipSettings`, `OutputFormat` enum, `Caption`, `ExportJob`.
- `Ffmpeg.cs` — locates `ffmpeg.exe` and runs it. `Start(args)` for the live
  recording process (keeps stdin open so we can send `q` to stop); `RunAsync(args)`
  for one-shot encodes (returns exit code + captured stderr log).
- `Recorder.cs` — builds the `gdigrab` capture command, writes a near-lossless
  intermediate `capture_<guid>.mp4` to `%TEMP%\Snipper`. `StopAsync` sends `q`
  for a clean finalize (so the moov atom is written), force-kills after 5s.
- `Exporter.cs` — turns an `ExportJob` into **one** ffmpeg invocation. Builds the
  `-vf` filtergraph (`fps`, `scale`, `drawtext` captions, and the 2-pass GIF
  palette graph), trims via `-ss`/`-t`, picks encoder args per format.
- `ClipboardHelper.cs` — puts a GIF on the clipboard in 3 formats (FileDrop +
  raw bytes + first-frame bitmap) so it pastes well across apps. STA-thread only.

**WPF UI (XAML + code-behind, code-built logic in `.xaml.cs`):**
- `MainWindow` — the control panel. `NewSnip_Click` orchestrates the whole flow:
  hide → region overlay → optional countdown → record → record bar → editor.
- `RegionOverlayWindow` — snipping-tool overlay: dims the whole virtual desktop,
  drag punches a "hole," returns the selection. **Does the DIP→physical-pixel
  conversion** for gdigrab (`ToPhysical`).
- `RecordBarWindow` — the floating "REC 3.4s" bar with Stop/Cancel
  (`DialogResult` true = keep, false = discard).
- `CountdownWindow` — full-screen **click-through** 3·2·1 overlay (uses
  `WS_EX_TRANSPARENT`) so you can press Play on the video behind it before capture.
- `EditorWindow` — preview (`MediaElement`), drag-handle trim bar (in/out + playhead),
  live caption preview, then Export / Copy-GIF.
- `App.xaml` / `app.manifest` — app entry + **PerMonitorV2 DPI awareness** (see below).

The data flow object is `ExportJob`: the editor fills it from the trim handles +
caption boxes + fps/width, and `Exporter` consumes it. Both Export and Copy-GIF
build one.

## Conventions & gotchas

- **The region is in physical pixels, end to end.** `CaptureRect` is physical px
  because that's what `gdigrab` wants. The only DIP→physical conversion is in
  `RegionOverlayWindow.ToPhysical`, which scales by the virtual-screen metrics
  from Win32 `GetSystemMetrics`. **Mixed per-monitor DPI is a known v1 limitation**
  (offsets the capture); uniform DPI on all monitors is correct at any scale.
- **DPI awareness is load-bearing.** `app.manifest` declares `PerMonitorV2`.
  Without it, captures on scaled displays come out offset and wrong-sized. Don't
  remove it.
- **Even dimensions required.** libx264 / yuv420p need even width/height. Always
  go through `CaptureRect.ToEven()` (the overlay and recorder already do).
- **drawtext path escaping is fragile on Windows.** ffmpeg filtergraphs treat `:`
  as a separator and `\` specially, so paths use forward slashes with the drive
  colon escaped (`C\:/...`) and are written **without** surrounding single quotes
  (single quotes mangle the escaped colon on Windows). Caption text is written to
  a temp UTF-8 file and read via `textfile=` so we never escape user text into the
  filtergraph. See `Exporter.DrawText` / `EscapeFilterPath` before touching this.
- **Captions are meme-style:** auto-uppercased, Impact font with a black outline,
  falling back to Arial Bold if Impact isn't installed (`ResolveFontEscaped`).
- **GIF quality** comes from the 2-pass palette graph
  (`palettegen`/`paletteuse`) appended in `BuildFilter` — don't drop it or GIFs
  look like 1996.
- **Temp files** live in `%TEMP%\Snipper` (`capture_*.mp4`, `cap_*.txt`,
  `clip_*.gif`). Caption temp files are cleaned up after each export; the
  clipboard GIF is intentionally left in place (the file-drop references its path).
- **Adding an output format:** add to the `OutputFormat` enum (`Models.cs`),
  handle it in `Exporter.EncoderArgs` + `DefaultExtension`, and add it to the
  `FormatBox` mapping + save-dialog filter in `EditorWindow`.
- **No audio.** Recording and all exports are silent by design (`-an`).

## Versioning & release

- **Version lives in two places** — `<Version>` in `Snipper.csproj` and
  `MyAppVersion` in `packaging\Snipper.iss`. Bump both together.
- **Installer:** `packaging\build-installer.ps1` does a self-contained `win-x64`
  publish to `publish\`, then compiles `packaging\Snipper.iss` (Inno Setup 6) into
  `Snipper-<ver>-setup.exe` at the repo root. Needs .NET SDK 8 + Inno Setup 6;
  run it from Windows PowerShell. Installs per-user (no UAC); bundles the runtime
  + ffmpeg so the target PC needs nothing. Keep the Inno `AppId` GUID **stable**
  across releases or upgrades/uninstall break.
- **Unsigned** → Windows SmartScreen warns on first run. Expected.

## Git

`bin/`, `obj/`, `.vs/`, `publish/`, `*-setup.exe`, and `ffmpeg/`+`ffmpeg.exe` are
gitignored. A built `Snipper-0.1.0-setup.exe` may be sitting in the working tree
(output of `build-installer.ps1`) but it's **not** tracked — don't commit it.
