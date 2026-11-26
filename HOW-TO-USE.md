# Cara Menggunakan Clipper98

## Instal dan update

1. Buka halaman [Releases Clipper98](https://github.com/mnzrsyq/Clipper98/releases).
2. Unduh `Clipper98-Setup-v1.0.0.exe`, lalu jalankan. Installer memasang aplikasi beserta .NET yang diperlukan; pengguna tidak perlu memasang .NET sendiri.
3. Jalankan `Clipper98` dari Start Menu atau shortcut Desktop.
4. Untuk update, unduh installer versi terbaru dan jalankan. Pengaturan, cache, serta tools yang sudah diunduh disimpan di PC dan tetap tersedia.

Pada penggunaan pertama, buka `SETUP` lalu pilih `UNDUH KOMPONEN KURANG`. Tools diunduh ke PC masing-masing. Whisper Large-v3-Turbo sekitar 1,5 GiB dan hanya diunduh jika belum tersedia. Installer dan repository GitHub tidak menyertakan tools atau model besar.

## Masukkan video dan kandidat

1. Tempel URL YouTube publik pada kolom `SOURCE URL`.
2. Pilih mode `Klip Pendek` untuk beberapa potongan mandiri atau `Ringkas Video` untuk satu video panjang yang dipendekkan dengan membuang bagian tertentu.
3. Pada `Ringkas Video`, pilih target durasi dan rasio. `Vertikal 9:16 · isi 4:5` memakai konten 4:5 dengan ruang atas-bawah berisi background blur. Subtitle dan VO dapat diatur masing-masing.
4. Klik `PROMPT GEMINI`, lalu jalankan skill `/clipper` atau `/clipper-ringkas` sesuai mode. Simpan manifest `.py` dari Gemini.
5. Klik `IMPORT .PY`. Clipper membaca manifest sebagai data dan tidak menjalankan kode Python di dalamnya.
6. Pada `Klip Pendek`, pilih kandidat yang ingin dibuat. Di kedua mode, periksa timestamp dan narasi sebelum render.

## Voice-over

1. Centang `Tambahkan narasi VO`.
2. Pada `PENGATURAN SUARA`, pilih `Fish Audio S2.1 Pro Free` atau `Edge-TTS`.
3. Untuk Fish Audio, tempel API key di kolom yang tersedia. Key disimpan terenkripsi di pengaturan Windows lokal, tidak di manifest.
4. Pilih suara, lalu atur laju, volume narasi, volume audio asli, dan pitch.

Narasi yang cocok akan dipakai dari cache. Fish Audio dan Edge-TTS memerlukan internet.

## Framing dan subtitle

- Centang `Smart Crop` agar crop vertikal mengikuti wajah utama. Jika wajah tak terdeteksi selama dua detik, bagian itu memakai full frame dengan latar blur.
- Tanpa Smart Crop, seluruh frame terlihat dengan latar blur.
- Pilih sumber transkrip `Otomatis — subtitle YouTube` atau `Whisper — transkrip ulang`. Jika subtitle YouTube tidak tersedia, aplikasi memakai Whisper Large-v3-Turbo.
- Pilih font yang terpasang di Windows, kapitalisasi, warna, highlight, outline, jumlah baris, dan posisi subtitle. Simpan pengaturan sebagai preset untuk digunakan kembali.
- Preview utama memakai thumbnail statis; hasil final dirender dari video sumber.

## Metadata YouTube

Pada panel `UPLOAD YOUTUBE`, periksa judul dan deskripsi. Judul dibatasi 100 karakter. Hashtag disimpan di baris terakhir deskripsi. Gunakan tombol salin untuk menempelkan keduanya ke YouTube Studio.

## Buat video

1. Pilih kandidat yang akan dibuat atau periksa daftar bagian yang dipertahankan untuk mode `Ringkas Video`.
2. Pilih folder hasil jika diperlukan.
3. Klik `CREATE CLIP`, pantau `LOG PROSES`, lalu klik `OPEN FOLDER` saat selesai.

Segmen, transkrip, analisis framing, dan audio narasi disimpan di cache lokal agar perubahan subtitle atau framing tidak selalu memproses ulang semuanya. `HAPUS TEMP/CACHE` membersihkan cache; hasil video dan pengaturan tetap tersimpan.

Skill Gemini ada di `.gemini/skills/clipper/SKILL.md` dan `.gemini/skills/clipper-ringkas/SKILL.md`. Jika Gemini CLI terbuka saat skill berubah, jalankan `/skills reload`.
