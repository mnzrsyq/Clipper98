# Clipper98

Source build kit for the public Clipper98 Windows application. The development repository is `Clipper-Project`; this repository contains only the files needed to build and prepare Clipper98 locally.

## Release package

Run `Release-Clipper98.bat` on your PC to build the self-contained Windows x64 app, download the runtime tools, and create a ready-to-run `Clipper98-v1.0.0` folder locally. It requires the .NET 8 SDK and an internet connection. This repository does not contain or publish executables, tools, models, or release archives.

The local setup downloads yt-dlp, FFmpeg, Node.js for yt-dlp's YouTube JavaScript support, whisper.cpp CPU binaries, portable Python, and only `ggml-large-v3-turbo.bin`. It does not download or include the Q8_0 model. The model is about 1.5 GiB, so setup can take a while.

To build only the app without downloading tools, run `Build-Clipper98.bat`. To fetch tools and the model separately after that, run `Download-Tools.bat`.

## Folder roles

- `Clipper98.csproj` and the C#/XAML/Python bridge files build the app.
- `fonts/` and `models/` contain the app's bundled fonts and OpenCV face detector.
- `SKILL.md` contains Gemini's Clipper candidate-selection instructions.
- `HOW-TO-USE.md` explains installation and operation.
- `scripts/` contains PowerShell release automation called by the `.bat` launchers.
- `THIRD-PARTY-NOTICES.md` lists bundled components and their licensing sources.

Generated release folders, archives, downloaded tools, and the Whisper model are excluded from Git. Each user creates their own local release folder. See `HOW-TO-USE.md` for setup and app instructions.
