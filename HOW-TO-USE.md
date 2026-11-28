# Cara Menggunakan Clipper98

Clipper98 membuat video dari URL YouTube. Gemini membantu memilih bagian video dan menyiapkan manifest; Clipper98 yang mengunduh dan merender video di PC.

## Persiapan Gemini

Buat skill Gemini **clipper** dan **clipper-ringkas** satu kali. Ikuti [Panduan Skill Gemini](https://github.com/mnzrsyq/Clipper98/blob/main/docs/GEMINI-SKILLS.md). Panduan itu memakai menu **Create manually**, jadi tidak perlu mengunggah file.

## Buat video

1. Buka Clipper98. Tempel URL video YouTube publik ke kolom **SOURCE URL**.
2. Pilih **Klip Pendek** untuk membuat beberapa potongan mandiri, atau **Ringkas Video** untuk membuat satu video pendek dari bagian-bagian yang dipertahankan.
3. Atur jumlah kandidat pada **Klip Pendek**. Pada **Ringkas Video**, atur target durasi dan pilihan narasi jika diperlukan.
4. Klik **PROMPT GEMINI**. Clipper98 menyalin prompt ke clipboard.
5. Buka chat baru di [Gemini](https://gemini.google.com). Ketik `/`, pilih skill **clipper** atau **clipper-ringkas** sesuai mode, lalu tempel prompt yang disalin.
6. Salin isi manifest yang ditampilkan Gemini, mulai dari `CLIPPER_JOB =` sampai kurung penutupnya. Jangan salin teks di luar blok kode. Buka Notepad dan tempel isinya.
7. Di Notepad, pilih **Save as**. Beri nama file dengan akhiran `.py`, contohnya `clipper_job.py`. Pilih **All files** pada tipe file dan simpan.
8. Kembali ke Clipper98, klik **IMPORT .PY**, lalu pilih file tadi. Jika berhasil, daftar kandidat atau bagian video akan muncul.
9. Periksa timestamp, pilihan klip, subtitle, framing, dan narasi. Klik **CREATE CLIP** untuk mulai merender.
10. Pantau **LOG PROSES**. Klik **OPEN FOLDER** setelah proses selesai untuk melihat hasilnya.

Simpan hanya isi manifest, mulai dari `CLIPPER_JOB =` sampai kurung penutup terakhir. Jangan ikut menyalin teks penjelasan Gemini. Pastikan file berakhiran `.py`, bukan `.py.txt`.

## Atur hasil

- Pada **Klip Pendek**, pilih kandidat yang ingin dirender dan atur subtitle atau framing.
- Pada **Ringkas Video**, periksa urutan bagian yang dipertahankan. Voice-over tersedia sebagai pilihan.
- **Smart Crop** mengikuti wajah bila terdeteksi. Tanpa Smart Crop, Clipper98 menampilkan seluruh frame dengan latar blur pada output vertikal.
- Folder hasil bisa diubah dari aplikasi. Default-nya `Videos\Clipper98`.
- Untuk subtitle, pilih **Otomatis** atau **Whisper**. Jika subtitle YouTube tidak tersedia dan subtitle aktif, Clipper98 dapat memakai Whisper Large-v3-Turbo.
- **HAPUS TEMP/CACHE** membersihkan cache lokal tanpa menghapus hasil video atau pengaturan.

## Jika manifest tidak bisa diimpor

Pastikan file hanya berisi manifest, tidak ada pagar blok kode atau penjelasan Gemini, lalu simpan ulang sebagai file `.py` dengan tipe **All files**. Jika Gemini memotong jawabannya, minta Gemini mengirim ulang seluruh manifest dalam satu blok kode Python sebelum menyimpannya.

## Voice-over

Voice-over tersedia pada **Ringkas Video**. Di **PENGATURAN SUARA**, pilih Fish Audio atau Edge-TTS. Fish Audio memerlukan API key; Clipper98 menyimpan key terenkripsi pada akun Windows lokal. Kedua layanan memerlukan koneksi internet.

## Instal dan update

1. Buka [GitHub Releases](https://github.com/mnzrsyq/Clipper98/releases).
2. Unduh installer terbaru bernama `Clipper98-Setup-vX.Y.Z.exe`, lalu jalankan.
3. Untuk update, jalankan installer terbaru di PC yang sama. Pengaturan, cache, tools, dan model yang sudah diunduh tetap tersimpan.

Pada penggunaan pertama, buka **SETUP** lalu pilih **UNDUH KOMPONEN KURANG**. Clipper98 mengunduh komponen yang diperlukan ke PC. Model Whisper Large-v3-Turbo sekitar 1,5 GiB dan hanya diunduh jika belum tersedia serta diperlukan.

## Upload dan jadwalkan di YouTube

1. Siapkan OAuth client JSON bertipe **Desktop app** dari Google Cloud dengan YouTube Data API v3 aktif. Tambahkan akun channel sebagai test user jika project masih berstatus **Testing**.
2. Klik ikon YouTube di Clipper98, pilih **CONNECT YOUTUBE**, lalu pilih file JSON. Masuk ke Google dengan akun yang mengelola channel.
3. Hasil render baru otomatis masuk ke publication queue dengan metadata dari manifest. Untuk render lama, klik **ADD VIDEOS**, lalu **IMPORT METADATA (.PY)** untuk mengisi judul dan deskripsi dari manifest yang sesuai.
4. Centang video yang akan diproses. Klik **AUTO-SCHEDULE** untuk mengisi slot WIB yang tersedia, atau pilih satu baris dan atur waktu publish manual.
5. Klik **UPLOAD SCHEDULED VIDEOS**. Video dikirim sebagai Private dengan waktu tayang terjadwal; audience default-nya **Not Made for Kids**.

Status jadwal hanya disimpan dalam antrean lokal di PC ini. Auto-Schedule tidak membaca jadwal yang dibuat dari PC lain atau YouTube Studio.

Manifest dengan field `day` dan `slot` per klip mengikuti kategori hari-slot saat Auto-Schedule. Klip kategori sama berikutnya dijadwalkan di minggu berikutnya. Tambah/kurangi jam slot secara manual lewat **CUSTOMIZE SLOTS**; jumlah slot pada manifest yang belum tersedia harus ditambahkan sebelum Auto-Schedule. Kedua field kategori opsional, sehingga manifest Clipper biasa tetap bisa dipakai untuk membuat klip tanpa mengunggah.
