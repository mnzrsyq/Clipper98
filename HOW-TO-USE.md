# Cara Pakai Clipper98

## Siapkan aplikasi di Windows

1. Pasang .NET 8 SDK x64.
2. Jalankan `Release-Clipper98.bat` di PC masing-masing. Script membuat folder lokal `Clipper98-v1.0.1` dan mengunduh komponen yang diperlukan ke PC tersebut.
3. Jika hanya ingin membangun executable, jalankan `Build-Clipper98.bat`. Untuk mengisi folder rilis nanti, jalankan `Download-Tools.bat`.
4. Model Whisper Large-v3-Turbo reguler berukuran sekitar 1,5 GiB. Model Q8_0 tidak digunakan.

Tools, model, font, executable, dan arsip rilis tidak disimpan di repositori GitHub.

## Jalankan aplikasi

1. Buka folder lokal `Clipper98-v1.0.1` yang dibuat script, lalu jalankan `Clipper98.exe`.
2. Klik `SETUP`. Komponen yang belum tersedia dapat diunduh dari jendela itu. Model Large-v3-Turbo hanya diunduh bila belum ada.
3. Tempel URL YouTube dan tentukan jumlah kandidat.
4. Klik `CALL SKILL`, lalu jalankan prompt yang tersalin menggunakan Gemini dengan skill `@clipper`.
5. Simpan manifest `.py` dari Gemini dan pilih `IMPORT .PY`. Aplikasi hanya membaca datanya; aplikasi tidak menjalankan kode Python.

## Framing klip

Bagian `Framing Klip Terpilih` berlaku untuk klip yang dipilih:

- **Smart Crop dicentang:** crop vertikal mengikuti wajah utama. Jika wajah tidak terdeteksi selama dua detik atau lebih, bagian itu menampilkan seluruh frame dengan latar blur.
- **Smart Crop tidak dicentang:** seluruh frame video tetap terlihat dengan latar blur sepanjang klip.

## Subtitle dan transkrip

1. Pilih `Otomatis — subtitle YouTube` untuk memakai subtitle yang tersedia. Jika subtitle tidak tersedia atau kosong pada rentang klip, aplikasi memakai Whisper Large-v3-Turbo.
2. Pilih `Whisper — transkrip ulang` untuk mentranskrip ulang bagian video secara lokal.
3. Pilih bahasa Indonesia, English, atau Otomatis.
4. Pilih font yang tersedia di Windows, gaya font, dan salah satu kapitalisasi: `Sentence case.`, `lowercase`, `UPPERCASE`, `Capitalize Each Word`, atau `tOGGLE cASE`.
5. Atur warna, highlight, outline, ukuran, posisi, dan jumlah baris. Simpan style sebagai preset bila diperlukan.

Preview memakai thumbnail statis. Video final dirender dari rentang klip yang dipilih.

## Frame dan hasil

1. Frame PNG opsional harus transparan dan berukuran **1080×1920**.
2. Pilih kandidat, tentukan folder hasil, lalu klik `CREATE CLIP`.
3. Pantau `LOG PROSES`, klik `OPEN FOLDER` untuk membuka hasil, atau `BATAL` untuk menghentikan render.
4. Menu `SETUP` menampilkan pemakaian temp/cache. `HAPUS TEMP/CACHE` hanya menghapus file kerja Clipper, bukan video hasil, tools, model, atau preset.

## Komponen lokal

`Release-Clipper98.bat` atau tombol unduh di Setup menyiapkan yt-dlp, Node.js, FFmpeg, whisper.cpp, model Large-v3-Turbo reguler, dan Haar cascade OpenCV. Semua komponen diunduh di PC masing-masing.

Lihat [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) untuk informasi lisensi. Gunakan video yang kamu punya hak untuk diproses atau izin dari pemiliknya.
