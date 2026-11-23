# Clipper98

Source build kit and release tools for the public Clipper98 Windows application. The development repository is `Clipper-Project`; this repository is for reviewed releases.

## Release package

Run `Release-Clipper98.bat` from this folder to build the self-contained Windows x64 app, download its runtime tools, create a `Clipper98-v1.0.0` folder and a fast, uncompressed ZIP, then publish the ZIP and model as separate GitHub release assets. The model is separate to stay within GitHub's per-file upload limit. It requires the .NET 8 SDK, Git, GitHub CLI authenticated to `mnzrsyq`, and an internet connection.

The release setup downloads yt-dlp, FFmpeg, Node.js for yt-dlp's YouTube JavaScript support, whisper.cpp CPU binaries, portable Python, and only `ggml-large-v3-turbo.bin`. It does not download or include the Q8_0 model. The model is about 1.5 GiB, so setup and release upload can take a while.

To build only the app without downloading tools, run `Build-Clipper98.bat`. To prepare tools in the build folder separately, run `Download-Tools.bat` after the build.

## Folder roles

- `Clipper98.csproj` and the C#/XAML/Python bridge files build the app.
- `fonts/` and `models/` contain the app's bundled fonts and OpenCV face detector.
- `SKILL.md` contains Gemini's Clipper candidate-selection instructions.
- `HOW-TO-USE.md` explains installation and operation.
- `scripts/` contains PowerShell release automation called by the `.bat` launchers.
- `THIRD-PARTY-NOTICES.md` lists bundled components and their licensing sources.

Generated release folders, archives, downloaded tools, and the Whisper model are excluded from Git. See `HOW-TO-USE.md` for setup and app instructions.
