# Cara Menggunakan Clipper98

Clipper98 mengolah video di PC lokal. Gemini digunakan terpisah melalui skill `@clipper`.

## 1. Siapkan aplikasi

1. Pastikan PC memakai Windows dan sudah memiliki .NET 8 SDK x64 serta koneksi internet.
2. Jalankan `Release-Clipper98.bat`. Skrip membangun aplikasi dan membuat atau memperbarui folder lokal `Clipper98-v1.0.0`.
3. Tunggu hingga tools terbaru dan model Whisper Large-v3-Turbo selesai disiapkan di folder tersebut. Saat diperbarui, model lokal yang masih valid dipakai kembali; unduhan baru berukuran sekitar 1,5 GiB.
4. Buka `Clipper98.exe` dari folder `Clipper98-v1.0.0`.

Tools, model, executable, dan arsip rilis disimpan lokal dan tidak diunggah ke GitHub. Jika hanya menjalankan `Build-Clipper98.bat`, gunakan `Download-Tools.bat` untuk mengisi tools ke folder hasil build.

## 2. Masukkan video dan kandidat

1. Tempel URL YouTube publik pada kolom `SOURCE URL`.
2. Tentukan jumlah kandidat, lalu klik `CALL SKILL` untuk menyalin prompt.
3. Jalankan prompt dengan Gemini dan skill `@clipper`. Simpan manifest hasil Gemini (`.py`) ke PC.
4. Klik `IMPORT .PY`, pilih manifest, lalu pilih kandidat yang ingin dibuat. Aplikasi membaca manifest sebagai data dan tidak menjalankan file Python.
5. Timestamp kandidat dapat disunting di panel kiri.

## 3. Atur framing

Pilih kandidat untuk melihat dan mengatur framing:

- **Smart Crop dicentang:** crop vertikal mengikuti wajah utama. Jika wajah tidak terdeteksi selama dua detik atau lebih, tampilan beralih sementara ke video utuh dengan latar blur.
- **Smart Crop tidak dicentang:** seluruh frame video terlihat dengan latar blur sepanjang klip.

## 4. Atur subtitle

1. Pilih `Otomatis — subtitle YouTube` untuk memakai subtitle yang tersedia. Jika subtitle tidak tersedia atau kosong pada rentang klip, aplikasi memakai Whisper Large-v3-Turbo.
2. Pilih `Whisper — transkrip ulang` untuk mentranskrip ulang bagian video secara lokal.
3. Pilih bahasa Indonesia, English, atau Otomatis.
4. Pilih font yang tersedia di Windows, gaya font, kapitalisasi, warna, highlight, outline, ukuran, posisi, dan jumlah baris.
5. Simpan style sebagai preset jika ingin menggunakannya kembali.

Preview utama memakai thumbnail statis. Video final dirender dari rentang klip yang dipilih.

## 5. Tambahkan frame PNG opsional

1. Siapkan PNG transparan berukuran tepat **1080×1920**.
2. Klik `PILIH` di bagian `FRAME PNG`.
3. Preview menampilkan frame di atas thumbnail. Subtitle dirender setelah frame agar tetap terlihat.

## 6. Buat klip

1. Centang kandidat yang ingin dibuat.
2. Pilih folder hasil lewat `BROWSE` bila diperlukan.
3. Klik `CREATE CLIP`. Klip dibuat satu per satu.
4. Pantau `LOG PROSES`, lalu klik `OPEN FOLDER` untuk membuka hasil.
5. Klik `BATAL` untuk menghentikan render.

## Komponen lokal

`Release-Clipper98.bat` menyiapkan yt-dlp, Node.js, FFmpeg, whisper.cpp, model Large-v3-Turbo, dan Haar cascade OpenCV di PC masing-masing. Jendela `SETUP` juga dapat mengunduh komponen yang terdeteksi belum tersedia. File tools dan model tidak disimpan di repo.

Lihat [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) untuk lisensi komponen. Gunakan video yang kamu punya hak untuk diproses atau izin dari pemiliknya.
