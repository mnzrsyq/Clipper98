# Clipper98

Clipper98 adalah aplikasi Windows untuk membuat klip pendek atau meringkas video panjang, dengan subtitle, framing, voice-over online, dan metadata YouTube.

## Instalasi

Unduh `Clipper98-Setup-v1.0.0.exe` dari halaman [Releases](https://github.com/mnzrsyq/Clipper98/releases) lalu jalankan. Installer membawa .NET yang dibutuhkan, jadi PC pengguna tidak perlu memasang .NET Runtime terpisah. Aplikasi dipasang untuk akun Windows yang sedang dipakai.

Saat fitur membutuhkannya, Clipper menyediakan unduhan tools dan model ke PC pengguna. Repo dan installer tidak menyertakan yt-dlp, FFmpeg, whisper.cpp, atau model Whisper. Model yang dipakai hanya Whisper Large-v3-Turbo.

## Update

Unduh installer versi terbaru dari halaman Releases lalu jalankan di PC yang sama. Installer memakai lokasi yang sama dan memperbarui file aplikasi. Pengaturan, cache, serta tools dan model yang sudah diunduh tetap berada di PC.

## Fitur

- Mode `Klip Pendek` dan `Ringkas Video` dengan prompt Gemini dan manifest yang dibaca sebagai data, tidak dieksekusi sebagai Python.
- Subtitle dari YouTube atau Whisper Large-v3-Turbo.
- Pilihan framing, Smart Crop, font Windows, subtitle perkata, warna dasar dan custom, highlight, serta preset.
- Voice-over melalui Fish Audio S2.1 Pro Free atau Edge-TTS; Fish API key disimpan terenkripsi di pengaturan lokal.
- Judul dan deskripsi siap salin untuk upload YouTube.
- Cache untuk menghindari pengolahan ulang bagian yang tidak berubah.

Panduan lengkap: [HOW-TO-USE.md](HOW-TO-USE.md). Skill Gemini: `.gemini/skills/clipper/SKILL.md` dan `.gemini/skills/clipper-ringkas/SKILL.md`.

## Build dari source

Untuk membangun sendiri di Windows, pasang .NET 8 SDK dan Inno Setup, lalu jalankan `Release-Clipper98.bat`. Ini hanya diperlukan untuk developer; pengguna installer tidak perlu memasang .NET.
