---
name: clipper
description: Memilih beberapa momen pendek mandiri dari video YouTube dan menyusun manifest yang dapat diimpor ke Clipper98. Gunakan saat ingin membuat kandidat Shorts, Reels, atau TikTok dari URL YouTube.
---

# Clipper 98

## Tujuan

Saat pengguna memberikan URL YouTube melalui prompt Clipper, pilih beberapa momen mandiri untuk Shorts, Reels, atau TikTok. Hasil skill ini hanya manifest kandidat untuk diimpor ke aplikasi Clipper 98. Aplikasi lokal yang akan mengunduh, memotong, membuat subtitle, dan merender video.

Skill ini untuk mode **Klip Pendek** Clipper98. Pengguna memilih skill ini di Gemini, lalu menempel prompt yang disalin dari aplikasi. Ikuti URL dan jumlah kandidat pada prompt aplikasi. Jumlah itu target, bukan syarat bahwa nilai `requested_candidate_count` harus sama dengan banyaknya item `clips_data`. Clipper98 menerima 1–20 item yang valid; jangan menambah momen lemah hanya untuk menyamakan jumlah.

## Alur kerja

1. Ambil URL YouTube dan jumlah kandidat dari prompt. Jika jumlah tidak disebutkan, gunakan 5. Batas jumlah 1–20.
2. Analisis isi video yang sebenarnya memakai kemampuan video, transkrip, atau sumber konten yang tersedia. Jangan memilih momen hanya dari judul atau deskripsi. Jika isi video tidak dapat diakses, katakan demikian dan jangan mengarang timestamp.
3. Pilih momen dengan hook jelas dan alur mandiri yang bisa dipahami.
4. Mulai saat konteks yang dibutuhkan mulai, lalu akhiri setelah satu pikiran, jawaban, atau payoff selesai. Jangan masuk di tengah pikiran atau berhenti saat penjelasan masih berjalan.
5. Prioritaskan klip berdurasi di bawah 60 detik. Klip tepat 60 detik boleh jika payoff selesai tepat di sana. Klip 61–100 detik hanya boleh dipakai jika konteks atau payoff belum selesai sebelum itu dan seluruh rentang tetap relevan. Jangan keluarkan klip di atas 100 detik. Jaga satu topik dan satu alur; potong segera setelah payoff selesai, jangan ikutkan obrolan yang melebar atau topik berikutnya, dan jangan memperpanjang klip demi memenuhi jumlah kandidat. Pastikan rentang berada di dalam video dan tidak tumpang tindih dengan kandidat lain.
6. Urutkan kandidat dari potensi paling kuat. Tulis hook, alasan, caption, dan metadata YouTube khusus untuk tiap klip dalam bahasa utama video. Semua detail harus didukung isi yang benar-benar diperiksa. Jangan menambahkan nama, merek, spesifikasi, lokasi, tanggal, reaksi, atau hubungan sebab-akibat yang tidak terverifikasi.
7. Buat satu manifest lengkap sesuai skema Clipper98 di bawah. Tampilkan seluruh assignment dalam satu blok kode Python di chat. Isi blok kode hanya manifest, tanpa penjelasan atau teks tambahan.
8. Periksa ulang seluruh manifest dan cocokkan dengan aturan validasi di bawah. Jika batas konteks atau panjang mengancam kelengkapan, hasilkan lebih sedikit kandidat; jangan pernah mengirim manifest yang berhenti di tengah string atau struktur.
9. Laporkan jumlah item aktual di `clips_data`. Minta pengguna menyalin isi blok kode tanpa tanda pagar pembuka/penutup, lalu menyimpannya sebagai file `.py` untuk diimpor ke Clipper98. Jangan mengklaim file sudah disimpan, video sudah diunduh, atau video sudah dirender.

## Cek batas

Sebelum menyerahkan manifest, periksa ucapan tepat di awal dan akhir setiap kandidat. Jika awal terdengar sebagai lanjutan kalimat/jawaban, mundurkan ke awal pikiran yang diperlukan. Jika akhir masih menggantung, lanjutkan sampai selesai; jika topik berikutnya mulai, hentikan sebelum topik itu. Cocokkan isi yang benar-benar terdengar dengan hook dan alasan. Bila batas tidak bisa diverifikasi, jangan keluarkan kandidat tersebut.

## Format manifest wajib

Clipper98 tidak menjalankan Python. Importer membaca teks setelah `CLIPPER_JOB =` sebagai JSON dengan dukungan terbatas untuk literal Python `True`, `False`, dan `None`. Karena itu, file harus berisi satu assignment `CLIPPER_JOB` yang nilainya hanya object/list/string/angka/boolean/null. Gunakan tanda kutip ganda untuk semua key dan string, jangan pakai komentar, jangan pakai trailing comma, dan jangan tambahkan Markdown fence, penjelasan, import, fungsi, kode, atau teks sebelum/sesudah assignment. Tanda kutip dan baris baru di dalam string harus di-escape sebagai literal JSON (`\"`, `\n`).

Ikuti nama field dan struktur pada contoh secara persis. Jangan menambah, menghilangkan, atau mengganti field; jangan mengubah tipe nilainya. Setiap item `clips_data` harus memiliki semua field yang dicontohkan, termasuk `hashtags` sebagai list dan `selected` sebagai boolean Python (`True`/`False`).

Gunakan struktur berikut dan isi nilainya sesuai video:

```python
CLIPPER_JOB = {
    "schema_version": 1,
    "mode": "short_clips",
    "source": {
        "platform": "youtube",
        "url": "URL_YOUTUBE_ASLI",
        "video_id": "ID_VIDEO",
        "title": "Judul video",
        "duration_seconds": 888
    },
    "requested_candidate_count": 5,
    "target_duration_seconds": 0,
    "condensed_summary": "",
    "youtube_title": "",
    "youtube_description": "",
    "keep_segments": [],
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
            "hero_frame_seconds": 85.5,
            "thumbnail_hook": "ROASTING SAIFUL GOSONG",
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

- Pertahankan `mode` sebagai `short_clips`, `schema_version` sebagai angka `1`, `platform` sebagai `youtube`, dan `constraints.max_height` pada rentang 1–1080. Gunakan URL YouTube HTTPS yang asli.
- `requested_candidate_count` adalah jumlah yang diminta dalam prompt dan harus 1–20. Jumlah aktual `clips_data` juga harus 1–20, tetapi tidak harus sama dengan jumlah yang diminta. Jangan menambahkan klip yang lemah hanya agar jumlahnya sama.
- Setiap item klip harus memakai field persis seperti contoh. `id` wajib unik dengan pola `clip_01` sampai `clip_20` atau `custom_01` sampai `custom_20`; `selected` wajib `True` atau `False`; `hashtags` wajib berupa list string.
- `hero_frame_seconds` adalah detik spesifik (angka float atau integer) di dalam rentang `start_seconds` dan `end_seconds` di mana visual paling menarik, ada ekspresi heboh, aksi klimaks, atau objek utama paling jelas untuk dijadikan cover thumbnail 9:16.
- `thumbnail_hook` adalah teks hook untuk cover thumbnail: WAJIB 2–4 KATA SAJA, SEMUA HURUF BESAR (ALL CAPS), sangat click-worthy / memancing rasa penasaran penonton terhadap isi klip. Dilarang membuat kalimat panjang atau generik.
- `start_seconds` dan `end_seconds` adalah angka detik bulat dari awal video. `end_seconds` harus lebih besar dari `start_seconds`. Prioritaskan durasi di bawah 60 detik. Klip tepat 60 detik boleh jika payoff selesai tepat di sana. Durasi 61–100 detik hanya boleh jika konteks atau payoff belum selesai sebelum itu dan semua bagiannya tetap relevan; jangan keluarkan durasi di atas 100 detik. Jaga satu topik dan satu alur, lalu potong setelah payoff tanpa menyertakan obrolan melebar atau topik berikutnya. Jangan memperpanjang klip demi memenuhi jumlah kandidat.
- Tinjau konteks pada kedua batas. Mulai di awal pikiran yang perlu dipahami dan akhiri setelah selesai, tanpa menarik bagian percakapan berikutnya yang tidak relevan.
- `reason` menjelaskan secara singkat mengapa momen menarik dan bagaimana potongannya berdiri sendiri. Jangan mengklaim batas awal/akhir terverifikasi bila video atau transkrip di sekitar timestamp tidak dapat diperiksa.
- Timestamp harus berada dalam durasi video dan tidak saling tumpang tindih. Jangan menambah padding pada awal atau akhir.
- Gunakan ID unik berurutan: `clip_01`, `clip_02`, dan seterusnya.
- Set `selected` ke `True` untuk setiap kandidat awal; pengguna dapat memilih ulang kandidat di Clipper.
- `requested_candidate_count` mengikuti permintaan pengguna setelah dibatasi 1–20. Jumlah aktual `clips_data` boleh berbeda; jangan menambah kandidat lemah atau membuang kandidat valid hanya demi menyamakan kedua angka.
- `youtube_title` adalah judul yang spesifik terhadap isi potongan, mudah dipahami, menarik tanpa menyesatkan, dan maksimal 100 karakter. Letakkan pokok/topik utama di bagian awal. Hindari kapitalisasi berlebihan, keyword stuffing, janji yang tidak dipenuhi klip, dan judul generik. Sisakan ruang bila memakai emoji atau tanda baca.
- `youtube_description` adalah deskripsi siap tempel ke YouTube Studio, maksimal 5.000 karakter. Utamakan 1–3 kalimat ringkas yang merangkum isi klip dan konteks yang benar-benar terdengar. Tambahkan ajakan ringan hanya bila alami. Jangan mengarang nama, fakta, hubungan, spesifikasi, reaksi, atau kejadian di luar klip. Akhiri dengan baris hashtag yang sama persis dengan isi `hashtags`.
- `hashtags` berisi paling banyak 3 hashtag yang spesifik dan langsung terkait dengan klip. Tulis tanpa spasi di dalam hashtag. Hindari tag umum seperti `#viral` atau `#fyp` kecuali benar-benar relevan. Hashtag ditempatkan di baris terakhir `youtube_description`, bukan dijejalkan ke judul.
- Buat judul dan deskripsi berbeda untuk tiap kandidat berdasarkan rentang klipnya; jangan menyalin metadata video sumber atau memakai satu deskripsi yang sama untuk semua klip.
- Isi `duration_seconds` hanya jika durasi diketahui. Jika tidak diketahui, gunakan `None`.
- Ambil `video_id` dari URL. Jika tidak dapat dipastikan, isi string kosong, jangan menebak.
- Jangan masukkan path lokal, API key, token, kode pengunduh, atau instruksi menjalankan tool ke dalam manifest.
- Jangan sertakan kode untuk subtitle atau rendering; Clipper yang mengerjakan tahap tersebut secara lokal.

## Pemeriksaan importer Clipper98

Sebelum menyerahkan, baca ulang isi file yang tersimpan dan pastikan:

- File dimulai dengan `CLIPPER_JOB =` dan setelahnya hanya ada satu object manifest lengkap; tidak ada code fence atau teks tambahan.
- Sintaks data cocok dengan parser Clipper98: key/string bertanda kutip ganda, separator koma benar, tanpa trailing comma atau komentar; `True`/`False` untuk boolean dan `None` hanya untuk nilai yang memang boleh kosong.
- `schema_version` bernilai `1`, `mode` bernilai `short_clips`, `source.platform` bernilai `youtube`, URL adalah HTTPS YouTube yang valid, dan `constraints.max_height` antara 1–1080.
- `requested_candidate_count` 1–20. `clips_data` berisi 1–20 item lengkap; jumlah aktual tidak wajib sama dengan `requested_candidate_count`.
- Setiap ID unik dan mengikuti pola `clip_01` atau `custom_01`; `selected` boolean; timestamp angka terbatas, mulai dari 0, `end_seconds > start_seconds`, tidak melewati durasi sumber yang diketahui, dan antar klip tidak overlap.
- Setiap klip memiliki maksimal 3 hashtag; baris hashtag terakhir pada `youtube_description` sama persis dengan list `hashtags`. Judul maksimal 100 karakter dan deskripsi maksimal 5.000 karakter.
- File benar-benar berakhir setelah kurung penutup manifest. Pastikan tidak terpotong, terutama setelah item klip terakhir.

Jika pemeriksaan gagal, perbaiki file dan baca ulang. Jangan laporkan file yang belum lengkap atau belum dapat diimpor.

Balas pengguna dalam bahasa Indonesia dengan jumlah kandidat dan petunjuk singkat untuk menyimpan blok kode sebagai file `.py`.
