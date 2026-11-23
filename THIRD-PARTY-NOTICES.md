# Third-party notices

The local Clipper98 release downloads the following components to the user's PC. License texts for downloaded runtime binaries are placed in `tools/licenses/` by `Download-Tools.bat`.

- **FFmpeg**: GPL build from Gyan.dev. The release setup includes the GNU GPL v3 text. Build source and licensing information are available from [Gyan.dev FFmpeg builds](https://www.gyan.dev/ffmpeg/builds/) and the [FFmpeg download page](https://ffmpeg.org/download.html).
- **yt-dlp**: Unlicense. The release setup fetches the upstream license text from [yt-dlp](https://github.com/yt-dlp/yt-dlp).
- **Node.js**: MIT license. The release setup fetches the license for the downloaded Node 24 build from [nodejs/node](https://github.com/nodejs/node).
- **whisper.cpp**: MIT license. The release setup fetches the upstream license from [ggml-org/whisper.cpp](https://github.com/ggml-org/whisper.cpp).
- **Whisper Large-v3-Turbo model**: model card and license information are published by [ggerganov/whisper.cpp on Hugging Face](https://huggingface.co/ggerganov/whisper.cpp).
- **OpenCV Haar cascade**: downloaded from [opencv/opencv](https://github.com/opencv/opencv/tree/4.x/data/haarcascades). The detector's license notice is included in `models/LICENSE-OpenCV.txt` and copied into the local release folder.
- **OpenCvSharp**: package license and source are available from [OpenCvSharp](https://github.com/shimat/opencvsharp).

The downloaded FFmpeg executable reports that it is built with `--enable-gpl --enable-version3`; it is distributed under GPL v3 or later. Refer to the included license and upstream build source for the corresponding source and build details.
