# Snipper ✂

Clip any video that's playing on your screen into a shareable **GIF / MP4 / MOV /
WebP / APNG** — Windows Snipping Tool style. Drag a box over a playing video,
hit Stop, then trim it and slap meme captions on top.

It works with *anything* on screen (YouTube, a local player, a stream, a game)
because it captures the screen region, not a video file.

## How it works

```
New Snip → drag a box over the video  (region overlay, snipping-tool style)
         → records that region          (ffmpeg gdigrab)
         → trim in/out + add captions    (editor with live preview)
         → Export                        (ffmpeg: GIF palette / x264 / webp / apng)
```

- **WPF (.NET 8, Windows-only)** for the UI.
- **ffmpeg** does all capture + encoding — it's the only dependency.

## Requirements

- **Windows 10/11**
- **.NET SDK 8.0+** (to build) — get it from https://dotnet.microsoft.com/download
- **ffmpeg.exe** — the app finds it in this order:
  1. next to `Snipper.exe`
  2. in a `ffmpeg\` subfolder (drop a static build's `ffmpeg.exe` here and the
     build copies it to the output automatically)
  3. on your `PATH`

  Get a static Windows build from https://www.gyan.dev/ffmpeg/builds/ (the
  "essentials" zip is fine — copy its `bin\ffmpeg.exe`).

## Build & run

> ⚠️ This is a **Windows** app. WPF and ffmpeg `gdigrab` don't exist on
> Linux/WSL, so build and run it from Windows (a normal `cmd`/PowerShell, or
> Visual Studio), **not** from a WSL shell.

```powershell
cd C:\Users\Jeff\Desktop\snipper
dotnet build
dotnet run
```

Or open the folder in Visual Studio 2022 and press F5.

## Tips & meme captions

- Captions are auto-uppercased and rendered in **Impact** with a black outline
  (the classic meme look). Falls back to Arial Bold if Impact isn't installed.
- The on-screen caption preview is approximate — the real outlined text is baked
  in at export time by ffmpeg.
- Smaller **Width** (e.g. 480) + lower **FPS** (12–20) = much smaller GIFs.
- **MP4** is best for Discord/Twitter (tiny, high quality); **GIF** is the most
  universal; **WebP/APNG** are smaller-but-less-universal modern formats.

## Known limitations (v1)

- **Mixed per-monitor DPI** (e.g. a 150% laptop screen next to a 100% external)
  can offset the capture region. Same scale on all monitors works correctly at
  any percentage. Single-monitor is rock solid.
- The floating **record bar** sits at bottom-center; if your capture region
  covers that spot it'll appear in the recording. Pick a region that doesn't
  overlap it.
- No audio (screen-region GIFs/memes are silent by design). MP4/MOV export is
  also silent for now.

## Roadmap ideas

- Drag captions anywhere; per-caption font/color.
- Global hotkey to start a snip without focusing the app.
- Import-a-file mode (scrub an existing video instead of live capture).
- Audio capture for MP4/MOV.

## Copy GIF to clipboard

The editor's **Copy GIF to clipboard** button builds a GIF (using your current
trim/captions/fps/width) and puts it on the clipboard as a file drop, raw GIF
bytes, and a first-frame bitmap. Paste with **Ctrl+V** into Discord/Slack/Teams/
chat and it uploads as an animated GIF.
