# Clipper98

Clipper98 adalah aplikasi Windows untuk mengolah kandidat klip YouTube menjadi video vertikal dengan subtitle. Repo ini berisi sumber aplikasi dan skrip build untuk rilis lokal. Repo pengembangan utama: `Clipper-Project`.

## Build rilis lokal

1. Siapkan Windows, .NET 8 SDK x64, dan koneksi internet.
2. Jalankan `Release-Clipper98.bat`.
3. Skrip membuat atau memperbarui `Clipper98-v1.0.0` di PC tersebut, membangun ulang aplikasi self-contained, lalu memperbarui tools lokal. Model Whisper Large-v3-Turbo yang sudah valid akan dipakai kembali.
4. Jalankan `Clipper98.exe` dari folder itu.

Untuk membangun aplikasi tanpa mengunduh tools, jalankan `Build-Clipper98.bat`; setelahnya jalankan `Download-Tools.bat` untuk mengisi folder rilis. Skrip tidak mengunggah executable, tools, model, atau ZIP ke GitHub. Model Whisper yang disiapkan hanya `ggml-large-v3-turbo.bin`.

## Alur aplikasi

1. Tempel URL YouTube dan pilih jumlah klip.
2. Klik `CALL SKILL`, lalu jalankan prompt dengan Gemini dan skill `@clipper`.
3. Impor manifest `.py` dari Gemini. Aplikasi membaca manifest sebagai data dan tidak menjalankan kode Python.
4. Pilih kandidat, atur framing dan subtitle, lalu buat klip.

## Framing dan subtitle

- `Smart Crop` mengikuti wajah utama. Jika wajah tidak terdeteksi selama dua detik atau lebih, aplikasi menampilkan seluruh frame dengan latar blur.
- Jika `Smart Crop` tidak dicentang, seluruh frame video tetap terlihat dengan latar blur.
- Subtitle dapat memakai subtitle YouTube atau transkrip lokal dengan Whisper Large-v3-Turbo.
- Pilihan font memakai font yang tersedia di Windows. Pengaturan subtitle mencakup kapitalisasi, warna, highlight kata, outline, posisi, dan jumlah baris.

Tools dan model disimpan di PC masing-masing. Lihat [HOW-TO-USE.md](HOW-TO-USE.md) untuk panduan lengkap dan [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) untuk lisensi komponen.
