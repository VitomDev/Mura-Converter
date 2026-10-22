# 🎧 Mura Converter — Simple Media Converter
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A simple desktop media converter built with C# and WPF.  
Paste a link from YouTube, TikTok, Instagram, SoundCloud and many more sites, and download the video or just the audio in the format you need.

> ⚠️ **Legal Disclaimer**

This project was created for **educational and personal use only**.  
I do **not endorse** or take **any responsibility** for how others use this tool, especially if used to violate YouTube’s Terms of Service or download copyrighted content without permission.

**Use at your own risk.**

---

## 🚀 Features

- Video (MP4, up to 4K) or audio (MP3, M4A, Opus or lossless WAV)
- Preview before downloading: thumbnail, title, channel and duration
- Download queue: paste several links in a row and they download one after another
- Whole playlists in one go, saved into their own numbered folder
- Trim: download only a section (e.g. 1:20 → 2:45) without fetching the full video
- Works with YouTube, TikTok, Instagram, X, Twitch, SoundCloud, Vimeo and [many more](https://github.com/yt-dlp/yt-dlp/blob/master/supportedsites.md)
- Auto-pastes copied links (on launch and when the window gets focus); drag & drop; Enter to download
- Windows notification when a download finishes in the background
- Explorer right-click menu: **"Descargar enlace copiado con Mura"** downloads the copied link straight into that folder, without opening the window
- Powered by bundled [yt-dlp](https://github.com/yt-dlp/yt-dlp) + FFmpeg, auto-updated daily so it keeps working when YouTube changes
- MP4 prefers H.264 + AAC so files play everywhere; merged without re-encoding (fast)
- Remembers your format, quality and output folder (default `Downloads\Mura`)

---

## 📦 Install

Run `MuraConverter-Setup-x.y.z.exe`. Per-user install, no admin rights and no .NET needed.

Optional: installing [Deno](https://deno.com) (`winget install DenoLand.Deno`) lets yt-dlp see every YouTube format; it is picked up automatically.

---

## 🔧 Build

Requirements: .NET 10 SDK and Inno Setup 6 (`winget install JRSoftware.InnoSetup`).

```powershell
.\build.ps1
```

It downloads yt-dlp and FFmpeg into `Mura Converter/tools/` if missing, publishes the app and writes the installer to `dist/`.

---

## 🧪 Project Status

🛠️ Currently paused.  
This project is fully functional as-is and ready to use.
However, future updates to improve features or the interface are not ruled out.
For now, development is paused while I focus on other projects.

---

## 📲 Contact

Feel free to reach out or follow me:

- 🐙 GitHub: [@VitomDev](https://github.com/VitomDev)
- 🐦 Twitter/X: [@VitomDev](https://twitter.com/VitomDev)
- 🎮 Twitch: [@VitomDev](https://www.twitch.tv/VitomDev)
- 💼 LinkedIn: [VitomDev](https://www.linkedin.com/in/víctor-escós-batalla/)

---

## 🧑‍💻 Author

**Víctor Escós Batalla**  


