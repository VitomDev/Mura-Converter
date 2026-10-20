# 🎧 Mura Converter
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A desktop application to convert YouTube videos to MP3 or MP4, built with C# and WPF.  
It allows pasting a YouTube URL and easily downloading the video or just the audio.

> ⚠️ **Legal Disclaimer**

This project was created for **educational and personal use only**.  
I do **not endorse** or take **any responsibility** for how others use this tool, especially if used to violate YouTube’s Terms of Service or download copyrighted content without permission.

**Use at your own risk.**

---

## 🚀 Features

- MP4 (up to 4K) or MP3 (128–320 kbps) in one click
- Auto-pastes YouTube links from the clipboard (on launch and when the window gets focus)
- Real progress bar with size, speed and ETA; cancel at any time
- Powered by bundled [yt-dlp](https://github.com/yt-dlp/yt-dlp) + FFmpeg, auto-updated daily so it keeps working when YouTube changes
- MP4 prefers H.264 + AAC so files play everywhere; merged without re-encoding (fast)
- MP3 with embedded title/artist and cover art
- Choose and remember the output folder (default `Downloads\Mura`)
- Drag & drop links, Enter to download

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
- 💼 LinkedIn: [Víctor Escós](https://www.linkedin.com/in/víctor-escós-batalla/)

---

## 🧑‍💻 Author

**Víctor Escós Batalla**  


