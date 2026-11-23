# Cara Pakai Clipper98

## Siapkan rilis di Windows

1. Pasang .NET 8 SDK x64, Git, dan GitHub CLI (`gh`).
2. Jalankan `gh auth login` dan pastikan akun GitHub yang aktif adalah `mnzrsyq`.
3. Jalankan `Release-Clipper98.bat` untuk membangun aplikasi, mengunduh tools, membuat folder `Clipper98-v1.0.0` dan ZIP, lalu menerbitkan ZIP ke GitHub Releases.
4. Jika hanya ingin membuat executable tanpa mengunduh tools, jalankan `Build-Clipper98.bat`. Untuk mengisi folder hasil build sesudahnya, jalankan `Download-Tools.bat`.

Model `ggml-large-v3-turbo.bin` berukuran sekitar 1,5 GiB. Proses unduh dan unggah ZIP rilis memerlukan koneksi internet yang stabil dan dapat memakan waktu.

## Jalankan aplikasi

1. Unduh `Clipper98-v1.0.0.zip` dan aset `ggml-large-v3-turbo.bin` dari halaman GitHub Releases.
2. Ekstrak ZIP ke folder yang dapat ditulis. Salin aset model ke `tools\models\ggml-large-v3-turbo.bin` di folder hasil ekstrak.
3. Jalankan `Clipper98.exe` dari folder itu. Jangan pisahkan exe dari file lain.
4. Klik `SETUP` untuk memastikan tools, Python, dan model menunjukkan status tersedia.
5. Tempel URL YouTube publik. Pilih analisis Gemini API atau impor manifest `.py` dari Gemini CLI/Antigravity.
6. Pilih kandidat klip, framing, sumber transkrip, bahasa, dan gaya subtitle, lalu tekan `CREATE CLIP`.

## Analisis kandidat Gemini

Gunakan instruksi pada `SKILL.md` ketika meminta Gemini menganalisis video. Manifest `.py` yang dibuat Gemini hanya dibaca sebagai data; Clipper tidak mengeksekusi kode dari manifest.

## Tools pada rilis

- `yt-dlp.exe` mengambil metadata, rentang video, dan subtitle YouTube.
- `ffmpeg.exe` memproses video, audio, crop, dan subtitle.
- `whisper-cli.exe` dan DLL CPU whisper.cpp menjalankan transkripsi lokal.
- `ggml-large-v3-turbo.bin` adalah satu-satunya model Whisper yang otomatis disiapkan.
- `node.exe` memenuhi kebutuhan runtime JavaScript yt-dlp untuk YouTube.
- `tools/python/python.exe` adalah Python portable untuk jembatan render lokal.
- OpenCV dan Haar cascade mendukung smart crop berbasis wajah.

Baca `THIRD-PARTY-NOTICES.md` untuk lisensi komponen paket.

Gunakan video yang berhak kamu proses atau sudah mendapat izin pemiliknya. Jangan membagikan API key Gemini.
