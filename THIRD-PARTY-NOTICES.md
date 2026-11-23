# Third-party notices

The Clipper98 release package contains or downloads the following third-party components. License texts for downloaded runtime binaries are placed in `tools/licenses/` by `Download-Tools.bat`.

- **FFmpeg**: GPL build from Gyan.dev. The release setup includes the GNU GPL v3 text. Build source and licensing information are available from [Gyan.dev FFmpeg builds](https://www.gyan.dev/ffmpeg/builds/) and the [FFmpeg download page](https://ffmpeg.org/download.html).
- **yt-dlp**: Unlicense. The release setup fetches the upstream license text from [yt-dlp](https://github.com/yt-dlp/yt-dlp).
- **Node.js**: MIT license. The release setup fetches the license for the downloaded Node 24 build from [nodejs/node](https://github.com/nodejs/node).
- **whisper.cpp**: MIT license. The release setup fetches the upstream license from [ggml-org/whisper.cpp](https://github.com/ggml-org/whisper.cpp).
- **Whisper Large-v3-Turbo model**: model card and license information are published by [ggerganov/whisper.cpp on Hugging Face](https://huggingface.co/ggerganov/whisper.cpp).
- **Python**: PSF license, included in the portable Python package under `tools/python/LICENSE.txt` and copied to `tools/licenses/`.
- **Montserrat** and **Poppins** fonts: SIL Open Font License texts are included beside the font files.
- **OpenCV Haar cascade**: license notice is included in `tools/models/LICENSE-OpenCV.txt`.
- **OpenCvSharp**: bundled package license and upstream source are available from [OpenCvSharp](https://github.com/shimat/opencvsharp).

The bundled FFmpeg executable reports that it is built with `--enable-gpl --enable-version3`; it is distributed under GPL v3 or later. Refer to the included license and upstream build source for the corresponding source and build details.
