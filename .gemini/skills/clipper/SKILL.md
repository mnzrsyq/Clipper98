---
name: clipper
description: Memilih momen obrolan mandiri dari video YouTube untuk dijadikan beberapa klip pendek viral yang diimpor ke Clipper 98.
---

# Clipper 98

## Tujuan

Saat pengguna memberikan URL YouTube melalui prompt Clipper, pilih beberapa momen mandiri untuk Shorts, Reels, atau TikTok. Hasil skill ini hanya manifest kandidat untuk diimpor ke aplikasi Clipper 98. Aplikasi lokal yang akan mengunduh, memotong, membuat subtitle, dan merender video.

Skill ini dapat digunakan dari Gemini CLI maupun Antigravity CLI. Ikuti isi prompt yang diberikan aplikasi Clipper, termasuk URL dan jumlah kandidat.

## Alur kerja

1. Ambil URL YouTube dan jumlah kandidat dari prompt. Jika jumlah tidak disebutkan, gunakan 5. Batas jumlah 1–20.
2. Analisis isi video yang sebenarnya memakai kemampuan video, transkrip, atau sumber konten yang tersedia. Jangan memilih momen hanya dari judul atau deskripsi. Jika isi video tidak dapat diakses, katakan demikian dan jangan mengarang timestamp.
3. Pilih momen dengan hook jelas dan alur mandiri yang bisa dipahami.
4. Mulai saat konteks yang dibutuhkan mulai, lalu akhiri setelah satu pikiran, jawaban, atau payoff selesai. Jangan masuk di tengah pikiran atau berhenti saat penjelasan masih berjalan.
5. Prioritaskan klip berdurasi di bawah 60 detik. Klip tepat 60 detik boleh jika payoff selesai tepat di sana. Klip 61–100 detik hanya boleh dipakai jika konteks atau payoff belum selesai sebelum itu dan seluruh rentang tetap relevan. Jangan keluarkan klip di atas 100 detik. Jaga satu topik dan satu alur; potong segera setelah payoff selesai, jangan ikutkan obrolan yang melebar atau topik berikutnya, dan jangan memperpanjang klip demi memenuhi jumlah kandidat. Pastikan rentang berada di dalam video dan tidak tumpang tindih dengan kandidat lain.
6. Urutkan kandidat dari potensi paling kuat. Tulis hook, alasan, caption, dan metadata YouTube khusus untuk setiap klip dalam bahasa utama video.
7. Buat file manifest Python dengan format di bawah. Simpan di folder `clipper_jobs` pada direktori kerja saat ini. Buat folder tersebut jika belum ada. Nama file: `clipper_job_<video_id>.py`.
8. Laporkan jumlah kandidat dan path file yang dibuat. Jangan mengklaim video sudah diunduh atau dirender; proses itu dilakukan setelah manifest diimpor ke Clipper 98.

## Cek batas

Sebelum menyerahkan manifest, periksa ucapan tepat di awal dan akhir setiap kandidat. Jika awal terdengar sebagai lanjutan kalimat/jawaban, mundurkan ke awal pikiran yang diperlukan. Jika akhir masih menggantung, lanjutkan sampai selesai; jika topik berikutnya mulai, hentikan sebelum topik itu. Cocokkan isi yang benar-benar terdengar dengan hook dan alasan. Bila batas tidak bisa diverifikasi, jangan keluarkan kandidat tersebut.

## Format manifest wajib

File harus berisi satu assignment literal bernama `CLIPPER_JOB`. Isinya hanya dictionary/list dan nilai literal Python: string, angka, boolean, atau `None`. Jangan tambahkan import, fungsi, class, pemanggilan fungsi, kode jaringan, shell, `subprocess`, `eval`, `exec`, atau blok `__main__`.

Gunakan struktur berikut dan isi nilainya sesuai video:

```python
CLIPPER_JOB = {
    "schema_version": 1,
    "source": {
        "platform": "youtube",
        "url": "URL_YOUTUBE_ASLI",
        "video_id": "ID_VIDEO",
        "title": "Judul video",
        "duration_seconds": 0
    },
    "requested_candidate_count": 5,
    "constraints": {
        "min_duration_seconds": 0,
        "max_duration_seconds": 100,
        "max_height": 1080
    },
    "clips_data": [
        {
            "id": "clip_01",
            "start_seconds": 65,
            "end_seconds": 105,
            "selected": True,
            "hook": "Hook singkat",
            "reason": "Alasan spesifik momen ini menarik",
            "caption": "Caption untuk unggahan",
            "hashtags": ["#contoh", "#topikvideo", "#Shorts"],
            "youtube_title": "Judul YouTube yang akurat dan menarik",
            "youtube_description": "Deskripsi ringkas yang menjelaskan isi klip.\n\n#contoh #topikvideo #Shorts"
        }
    ]
}
```

## Aturan pengisian

- `start_seconds` dan `end_seconds` adalah angka detik bulat dari awal video. `end_seconds` harus lebih besar dari `start_seconds`. Prioritaskan durasi di bawah 60 detik. Klip tepat 60 detik boleh jika payoff selesai tepat di sana. Durasi 61–100 detik hanya boleh jika konteks atau payoff belum selesai sebelum itu dan semua bagiannya tetap relevan; jangan keluarkan durasi di atas 100 detik. Jaga satu topik dan satu alur, lalu potong setelah payoff tanpa menyertakan obrolan melebar atau topik berikutnya. Jangan memperpanjang klip demi memenuhi jumlah kandidat.
- Tinjau konteks pada kedua batas. Mulai di awal pikiran yang perlu dipahami dan akhiri setelah selesai, tanpa menarik bagian percakapan berikutnya yang tidak relevan.
- `reason` menjelaskan secara singkat mengapa momen menarik dan bagaimana potongannya berdiri sendiri. Jangan mengklaim batas awal/akhir terverifikasi bila video atau transkrip di sekitar timestamp tidak dapat diperiksa.
- Timestamp harus berada dalam durasi video dan tidak saling tumpang tindih. Jangan menambah padding pada awal atau akhir.
- Gunakan ID unik berurutan: `clip_01`, `clip_02`, dan seterusnya.
- Set `selected` ke `True` untuk setiap kandidat awal; pengguna dapat memilih ulang kandidat di Clipper.
- `requested_candidate_count` mengikuti permintaan pengguna setelah dibatasi 1–20. Jika jumlah momen yang layak lebih sedikit, isi hanya kandidat yang benar-benar didukung konten.
- `youtube_title` adalah judul yang spesifik terhadap isi potongan, mudah dipahami, menarik tanpa menyesatkan, dan maksimal 100 karakter. Letakkan pokok/topik utama di bagian awal. Hindari kapitalisasi berlebihan, keyword stuffing, janji yang tidak dipenuhi klip, dan judul generik. Sisakan ruang bila memakai emoji atau tanda baca.
- `youtube_description` adalah deskripsi siap tempel ke YouTube Studio, maksimal 5.000 karakter. Mulai dengan ringkasan/hook yang sesuai isi klip, beri konteks yang memang terdengar, gunakan istilah utama secara alami, lalu tambahkan satu ajakan ringan hanya bila terasa alami. Jangan mengarang nama, fakta, hubungan, atau konteks yang tidak terverifikasi. Akhiri dengan baris hashtag yang sama persis dengan isi `hashtags`.
- `hashtags` berisi paling banyak 3 hashtag yang spesifik dan langsung terkait dengan klip. Tulis tanpa spasi di dalam hashtag. Hindari tag umum seperti `#viral` atau `#fyp` kecuali benar-benar relevan. Hashtag ditempatkan di baris terakhir `youtube_description`, bukan dijejalkan ke judul.
- Buat judul dan deskripsi berbeda untuk tiap kandidat berdasarkan rentang klipnya; jangan menyalin metadata video sumber atau memakai satu deskripsi yang sama untuk semua klip.
- Isi `duration_seconds` hanya jika durasi diketahui. Jika tidak diketahui, gunakan `None`.
- Ambil `video_id` dari URL. Jika tidak dapat dipastikan, isi string kosong, jangan menebak.
- Jangan masukkan path lokal, API key, token, kode pengunduh, atau instruksi menjalankan tool ke dalam manifest.
- Jangan sertakan kode untuk subtitle atau rendering; Clipper yang mengerjakan tahap tersebut secara lokal.

## Pemeriksaan sebelum menyerahkan

Pastikan URL dan ID sesuai input, `end_seconds > start_seconds`, timestamp berada dalam durasi video, kandidat tidak tumpang tindih, jumlahnya tidak melebihi permintaan atau batas 20, dan file hanya berisi assignment `CLIPPER_JOB` yang valid.

Balas pengguna dalam bahasa Indonesia dengan ringkasan singkat dan lokasi manifest.

