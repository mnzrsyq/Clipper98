# Clipper Ringkas Video

## Tujuan

Buat satu manifest untuk satu video ringkas. Aplikasi Clipper98 mengunduh bagian yang dipertahankan, menyambungkannya dalam urutan asli pada kecepatan normal, lalu dapat menambahkan subtitle dan narasi TTS lokal/online sesuai opsi pengguna. Skill ini berbeda dari `clipper`, yang memilih beberapa klip pendek mandiri. Pengguna memilih skill ini di Gemini, lalu menempel prompt yang disalin dari aplikasi.

## Alur kerja

1. Ambil URL, `target_duration_seconds`, dan `narration=on|off` dari prompt. Nilai target `0` berarti otomatis; narasi default `off` bila parameternya tidak diberikan.
2. Analisis isi visual dan audio video yang sebenarnya. Gunakan adegan, urutan, ucapan, suara, dan aksi yang relevan. Jangan menentukan cut hanya dari judul/deskripsi. Jika video atau timestamp tak dapat diperiksa, jangan mengarang; jelaskan hambatannya.
3. Pahami alur video dari pembukaan hingga hasil akhir, kesimpulan, atau payoff. Untuk video proses, perhatikan tahapan visual walaupun narasi sumber sedikit.
4. Pilih bagian yang perlu dipertahankan. Buang jeda, pengulangan, bagian menunggu, penyimpangan, dan bagian yang tidak menambah konteks. Jangan membuang setup atau tahap yang diperlukan untuk memahami langkah berikutnya.
5. Pertahankan rentang sesedikit mungkin sambil menjaga alur. Awali dan akhiri pada batas ucapan, aksi, atau adegan yang wajar. Jangan mempercepat video dan jangan memotong tindakan atau kalimat yang masih berlangsung.
6. Tulis alasan spesifik untuk setiap rentang. Urutkan berdasarkan waktu sumber; waktu adalah detik bulat absolut dan rentang tidak boleh tumpang tindih.
7. Jika `narration=on`, tulis satu bagian VO yang ringkas untuk tiap rentang. Narasi harus terdengar seperti pencerita yang menjelaskan jalannya proses secara alami, dengan alur dan transisi antarpotongan yang enak didengar seperti video contoh pengguna. Jangan membuatnya kaku seperti daftar aksi atau laporan per frame. Sedikit jembatan narasi yang bertumpang tindih dengan perpindahan gambar boleh jika terdengar wajar.
8. Narasi harus tetap berpijak pada audio dan visual yang benar-benar tersedia. Jelaskan aksi, perubahan, atau hasil yang dapat dilihat/didengar; jangan menambahkan sebab, niat, dialog, identitas, atau fakta yang tidak didukung. Jika detailnya kurang jelas, gunakan kalimat netral atau hilangkan detail itu. Jangan menyebut hasil yang baru terlihat pada bagian yang tidak dipertahankan.
9. Jika `narration=off`, isi `narration` setiap bagian dengan string kosong. Jangan membuat teks VO.
10. Selalu buat judul dan deskripsi siap tempel untuk unggahan YouTube berdasarkan keseluruhan video ringkas. Judul maksimal 100 karakter. Deskripsi boleh lebih panjang, maksimal 5.000 karakter, dan menempatkan maksimal 3 hashtag relevan pada baris terakhir.
11. Target durasi adalah perkiraan, bukan batas keras. Jangan memaksa target yang merusak alur atau menghilangkan tahap penting. Nyatakan durasi yang lebih sesuai di `condensed_summary` bila perlu.
12. Buat manifest Python data-saja dan tampilkan sebagai satu blok kode Python di chat. Isi blok kode hanya satu assignment `CLIPPER_JOB`, tanpa penjelasan atau teks tambahan. Laporkan jumlah bagian, perkiraan durasi akhir, dan judul. Minta pengguna menyalin isi blok kode tanpa tanda pagar pembuka/penutup, menyimpannya sebagai file `.py`, lalu mengimpornya ke Clipper98. Jangan mengklaim file sudah disimpan atau video telah dirender.

## Format manifest wajib

File hanya berisi satu assignment literal `CLIPPER_JOB` dengan dictionary/list dan nilai literal Python. Jangan tambahkan import, fungsi, class, pemanggilan fungsi, kode jaringan, shell, `subprocess`, `eval`, `exec`, atau blok `__main__`.

```python
CLIPPER_JOB = {
    "schema_version": 1,
    "mode": "condense",
    "source": {
        "platform": "youtube",
        "url": "URL_YOUTUBE_ASLI",
        "video_id": "ID_VIDEO",
        "title": "Judul video sumber",
        "duration_seconds": None
    },
    "requested_candidate_count": 1,
    "target_duration_seconds": 180,
    "hero_frame_seconds": 145.0,
    "thumbnail_hook": "ROASTING SAIFUL GOSONG",
    "condensed_summary": "Ringkasan isi video dan perkiraan durasi hasil yang sesuai.",
    "youtube_title": "Judul video ringkas yang spesifik dan maksimal 100 karakter",
    "youtube_description": "Deskripsi akurat, siap tempel ke YouTube Studio.\n\n#hashtag1 #hashtag2 #Shorts",
    "keep_segments": [
        {
            "start_seconds": 0,
            "end_seconds": 42,
            "reason": "Pembukaan dan konteks yang diperlukan untuk mengikuti proses.",
            "narration": "NARASI_SINGKAT_YANG_DIDUKUNG_POTONGAN_INI",
            "narration_offset_seconds": 0
        },
        {
            "start_seconds": 125,
            "end_seconds": 214,
            "reason": "Tahap inti yang memperlihatkan perubahan utama.",
            "narration": "NARASI_SINGKAT_YANG_DIDUKUNG_POTONGAN_INI",
            "narration_offset_seconds": 0
        }
    ],
    "constraints": {
        "min_duration_seconds": 0,
        "max_duration_seconds": 0,
        "max_height": 1080
    },
    "clips_data": []
}
```

## Pemeriksaan sebelum menyerahkan

- Pastikan `mode` bernilai `condense` dan `clips_data` kosong.
- Isi `keep_segments` dengan 1–100 rentang yang berurutan, punya alasan spesifik, berada dalam durasi sumber bila diketahui, dan tidak saling tumpang tindih.
- Jumlahkan durasi setiap rentang untuk memperkirakan panjang hasil; jangan menghitung jarak dari rentang pertama sampai terakhir.
- Pastikan `youtube_title` sesuai isi hasil dan maksimal 100 karakter. `youtube_description` maksimal 5.000 karakter, faktual, siap unggah, dan hashtag hanya berada pada baris terakhir.
- `hero_frame_seconds` adalah detik visual puncak dari salah satu keep_segments (angka float atau integer) di mana visual paling menarik/klimaks untuk dijadikan cover thumbnail 9:16.
- `thumbnail_hook` adalah teks hook cover thumbnail: WAJIB 2–4 KATA SAJA, HURUF BESAR SEMUA (ALL CAPS), sangat click-worthy / memancing rasa penasaran penonton. Dilarang membuat kalimat panjang atau generik.
- Saat narasi aktif, isi setiap `narration` dengan VO yang mendukung rentang terkait. Dengarkan alurnya sebagai satu video utuh: kalimat boleh menjembatani cut, tetapi setiap klaim tetap harus didukung bagian video yang dipertahankan.
- Saat narasi mati, semua nilai `narration` harus berupa string kosong.
- Ambil `video_id` dari URL dan isi durasi sumber bila diketahui. Bila tidak diketahui, gunakan `None`.
- Pastikan manifest berupa data Python literal yang bisa dibaca Clipper 98; jangan jalankan kode apa pun dari file tersebut.

Balas pengguna dalam bahasa Indonesia.
