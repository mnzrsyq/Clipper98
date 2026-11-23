# Clipper98

Source build kit for the public Clipper98 Windows application. The development repository is `Clipper-Project`; this repository contains the source and scripts needed to build Clipper98 locally.

## Local release

Run `Release-Clipper98.bat` on your PC to build the self-contained Windows x64 app, download the required tools and models locally, and create `Clipper98-v1.0.1`. It requires the .NET 8 SDK and an internet connection.

This repository does not store or publish executables, tools, fonts, models, or release archives. Each user downloads tools and the Large-v3-Turbo model to their own PC. The release script prepares only the regular `ggml-large-v3-turbo.bin` model.

To build only the app without downloading tools, run `Build-Clipper98.bat`. Run `Download-Tools.bat` later to fetch the missing components into the local release folder.

## App workflow

- `CALL SKILL` copies a prompt for the `@clipper` skill. The app does not store a Gemini API key or call Gemini API.
- Import the Gemini `.py` manifest to load clip candidates. Clipper reads it as data and does not execute Python code; Python is not an app dependency.
- The Setup window checks required components and can download missing tools to the local `tools` folder.
- Subtitle settings include Windows fonts and five capitalization modes. Whisper Large-v3-Turbo runs when requested or when YouTube subtitles are missing.
- In `Framing Klip Terpilih`, checked `Smart Crop` follows the main face. Unchecked keeps the entire video visible with a blurred background.

See [HOW-TO-USE.md](HOW-TO-USE.md) for setup and use instructions. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the component license information.
