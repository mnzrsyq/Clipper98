# Menyiapkan Skill Clipper98 di Gemini

> Buat dua skill ini satu kali di Gemini web. Panduan ini memakai **Create manually**, jadi kamu tidak perlu mengunggah file skill.

## Buka halaman Skills

1. Buka [gemini.google.com](https://gemini.google.com) dan masuk ke akun Google.
2. Di sidebar, klik **More** jika **Customize** belum terlihat.
3. Klik **Customize**, lalu **Skills**.
4. Klik **Create**, lalu **Create manually**.
5. Isi **Name/Judul**, **Description/Deskripsi**, dan **Instructions/Petunjuk** sesuai bagian di bawah.
6. Klik **Create**. Jika kamu sedang mengubah skill yang sudah ada, klik **Save**.

## Skill untuk Klip Pendek

**Name / Judul**

```text
clipper
```

**Description / Deskripsi**

```text
Memilih beberapa momen pendek mandiri dari video YouTube dan menyusun manifest yang dapat diimpor ke Clipper98. Gunakan saat ingin membuat kandidat Shorts, Reels, atau TikTok dari URL YouTube.
```

**Instructions / Petunjuk**

Buka [instruksi siap salin untuk Klip Pendek](gemini/clipper-instructions.md), klik **Raw**, lalu tekan **Ctrl+A** dan **Ctrl+C**. Kembali ke Gemini dan tempel semuanya ke kolom **Instructions**. File itu hanya berisi instruksi, tanpa Name atau Description.

## Skill untuk Ringkas Video

Buat skill kedua dengan cara yang sama.

**Name / Judul**

```text
clipper-ringkas
```

**Description / Deskripsi**

```text
Memilih bagian penting dari video YouTube panjang untuk membuat satu versi lebih pendek, dengan opsi narasi VO, lalu menyusun manifest Clipper98. Gunakan saat ingin meringkas satu video tanpa mempercepatnya.
```

**Instructions / Petunjuk**

Buka [instruksi siap salin untuk Ringkas Video](gemini/clipper-ringkas-instructions.md), klik **Raw**, lalu tekan **Ctrl+A** dan **Ctrl+C**. Kembali ke Gemini dan tempel semuanya ke kolom **Instructions**.

## Gunakan skill saat membuat video

1. Buka chat baru di Gemini.
2. Di kolom pesan, ketik `/`, lalu pilih **clipper** untuk mode Klip Pendek atau **clipper-ringkas** untuk mode Ringkas Video. Pilih skill dari daftar yang muncul sebelum menempel prompt Clipper98.
3. Di Clipper98, tempel URL YouTube, pilih mode, lalu klik **PROMPT GEMINI**.
4. Tempel prompt yang sudah disalin ke chat Gemini tadi dan kirim.
5. Skill meminta Gemini menyusun manifest. Salin mulai dari `CLIPPER_JOB =` sampai kurung penutup terakhir. Jangan ikut menyalin teks penjelasan di luar blok kode.
6. Tempel ke Notepad, lalu simpan sebagai file berakhiran `.py`. Pada **Save as type**, pilih **All files** agar file tidak berakhiran `.py.txt`.
7. Kembali ke Clipper98, klik **IMPORT .PY**, lalu pilih file tersebut.

> Gemini mungkin tidak dapat membaca isi semua video dari URL saja. Skill meminta Gemini menjelaskan keterbatasan itu dan tidak menebak timestamp. Jangan gunakan timestamp yang tidak bisa diverifikasi.

Jika skill tidak muncul setelah kamu mengetik `/`, buka **Customize** > **Skills** dan pastikan skill tersebut sudah dibuat serta aktif.

Nama menu dapat berubah. Lihat [panduan resmi Google untuk membuat dan menggunakan Skills](https://support.google.com/gemini/answer/17094296) jika tampilan Gemini berbeda.
