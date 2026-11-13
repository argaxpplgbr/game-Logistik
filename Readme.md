
LOGISTICS APP

Bangun perusahaan logistik. Kuasai jalur distribusi. Jadilah yang terdepan.

Nusantara Logistics adalah game simulasi manajemen perusahaan logistik berbasis konsol. Di sini, pemain tidak hanya mengirim barang dari satu tempat ke tempat lain. Pemain harus membangun perusahaan, mengatur armada, membaca peluang pasar, menjaga keuangan, dan bersaing dengan perusahaan lain—termasuk kompetitor yang dikendalikan AI.

Setiap keputusan punya konsekuensi. Ekspansi terlalu cepat bisa membebani keuangan. Terlalu hemat bisa membuat perusahaan tertinggal. Kuncinya adalah menemukan strategi yang tepat dan mengembangkan bisnis secara berkelanjutan.

# Daftar Isi

- Gambaran Permainan
- Fitur Utama
- Kompetitor AI
- Teknologi
- Memulai Permainan
- Sistem Penyimpanan
- Struktur Proyek
- Database dan Migrasi
- Pengujian
- Catatan Pengembangan

# Gambaran Permainan

Bayangkan kamu memulai sebuah perusahaan logistik dari skala kecil. Kamu perlu mengelola sumber daya yang terbatas, memilih langkah bisnis, dan menghadapi persaingan yang terus bergerak.

Dalam permainan, kamu dapat:

- Mengembangkan perusahaan dan meningkatkan kemampuan operasional.
- Mengelola armada serta kebutuhan distribusi.
- Mengikuti aktivitas pasar dan mengambil peluang yang tersedia.
- Menjaga pemasukan, pengeluaran, dan kestabilan keuangan.
- Menghadapi perusahaan pesaing dengan karakter strategi yang berbeda.

Tujuan utamanya: membangun perusahaan logistik yang kuat, efisien, dan mampu bertahan dalam persaingan.

# Fitur Utama

| Fitur | Deskripsi |
|---|---|
| Manajemen perusahaan | Mengembangkan bisnis dan menentukan arah strategi. |
| Operasional logistik | Mengelola armada dan aktivitas distribusi. |
| Sistem ekonomi | Memperhatikan pendapatan, biaya, dan kondisi keuangan. |
| Pasar bersama | Berinteraksi dengan peluang pasar dan aktivitas perusahaan lain. |
| Kompetitor AI | Bersaing dengan perusahaan yang memiliki profil strategi berbeda. |
| Komunikasi perusahaan | Menyediakan fondasi interaksi sosial ant perusahaan. |
| Save slot | Menyimpan beberapa perjalanan permainan secara terpisah. |

# Kompetitor AI

Kompetitor AI dirancang dengan lima profil perilaku. Masing-masing memiliki kecenderungan strategi yang berbeda sehingga persaingan tidak hanya bergantung pada satu pola.

| Profil | Kecenderungan |
|---|---|
| AGRESIF | Cenderung mengambil peluang dan menekan persaingan. |
| HEMAT | Lebih berhati-hati dalam menggunakan sumber daya. |
| PREMIUM | Mengutamakan pendekatan layanan bernilai tinggi. |
| EKSPANSI | Berorientasi pada pertumbuhan dan perluasan bisnis. |
| SEIMBANG | Berusaha menjaga keseimbangan antara risiko dan perkembangan. |

AI juga memiliki sistem perencanaan, sasaran jangka pendek dan panjang, serta aktivitas dalam pasar. Kedalaman dampak diplomasi dan perjanjian ant perusahaan masih terus dikembangkan; belum semua kesepakatan memiliki konsekuensi ekonomi dan operasional yang lengkap.

# Teknologi

Proyek ini menggunakan:

- C# sebagai bahasa pemrograman.
- .NET 10 sebagai platform aplikasi.
- SQLite sebagai basis data lokal.
- Dapper untuk akses data.
- xUnit untuk pengujian.

Catatan: penyimpanan database proyek ini menggunakan SQLite, bukan MySQL.

# Memulai Permainan

## Persyaratan

- .NET SDK 10
- Terminal atau command prompt
- Sistem operasi yang mendukung .NET 10

## Menjalankan aplikasi

Buka terminal pada folder utama proyek, lalu jalankan:

```bash
dotnet restore
dotnet build
dotnet run --project GameLogistics.csproj
```

Perintah tersebut memulihkan dependensi, membangun aplikasi, lalu menjalankannya.

# Sistem Penyimpanan

Nusantara Logistics mendukung 10 slot penyimpanan. Setiap slot menggunakan file database SQLite tersendiri sehingga beberapa perjalanan permainan dapat dikelola secara terpisah.

Informasi penyimpanan:

- Data save disimpan di folder saves/.
- Sistem menyediakan file cadangan dengan ekstensi .bak.
- Lokasi folder penyimpanan dapat diatur melalui environment variable NUSANTARA_SAVE_DIR.

Sebaiknya jangan menghapus atau mengubah file save secara manual ketika permainan sedang berjalan.

# Struktur Proyek

Berikut gambaran direktori utama:

```text
.
├── Database/
│   ├── Db.cs
│   ├── SchemaSqlite.cs
│   ├── SchemaV2.cs
│   ├── SchemaV3.cs
│   ├── SchemaV4.cs
│   └── SchemaV5.cs
├── Menus/
├── Models/
├── Services/
├── Tests/
├── GameLogistics.csproj
└── Readme.md
```

- Database/ menangani koneksi, skema, dan perubahan struktur database.
- Menus/ berisi tampilan menu dan alur interaksi pemain.
- Models/ menyimpan model data yang digunakan aplikasi.
- Services/ berisi logika bisnis dan layanan permainan.
- Tests/ berisi proyek pengujian.

# Database dan Migrasi

Struktur database dikelola melalui sistem migrasi. Versi skema yang tersedia dalam proyek mencakup migrasi 1 sampai 5.

Pendekatan migrasi membantu perubahan struktur database dilakukan secara bertahap tanpa harus membangun ulang seluruh penyimpanan dari awal.

# Pengujian

Untuk menjalankan pengujian:

```bash
dotnet test Tests/Nusantara.Tests.csproj
```

Pastikan dependensi sudah dipulihkan sebelum menjalankan pengujian. Status lulus atau gagal perlu dikonfirmasi dengan menjalankan perintah tersebut pada lingkungan pengembangan.

# Catatan Pengembangan

Beberapa bagian masih memiliki ruang penyempurnaan, terutama:

- Memperdalam perilaku AI agar keputusan kompetitor terasa lebih alami dan konsisten.
- Mengembangkan komunikasi dan diplomasi ant perusahaan.
- Memberi dampak operasional serta ekonomi yang lebih lengkap pada perjanjian.
- Menyeimbangkan ekonomi agar strategi yang berbeda tetap layak dimainkan.
- Membuat aktivitas kompetitor lebih mudah dipantau oleh pemain.

Catatan navigasi: kode GameLoop memiliki handler untuk pilihan menu nomor 15 (CompanyChatMenu), tetapi MainMenu saat ini hanya menampilkan 14 pilihan. Karena itu, menu komunikasi tersebut belum muncul sebagai pilihan pada dashboard utama.

# Tentang Proyek

Nusantara Logistics dikembangkan sebagai simulasi bisnis logistik yang menempatkan strategi, pengelolaan sumber daya, dan persaingan sebagai inti permainan.

Rencanakan dengan cermat. Kelola dengan efisien. Kembangkan perusahaanmu dan buktikan bahwa kamu mampu menjadi pemimpin industri logistik.

## Pembaruan operasional v5

Pembaruan skema v5 menambahkan jejak operasional tanpa menghapus tabel atau kolom save lama:

- `trip_status_history`: catatan status, alasan, waktu simulasi, dan hari game untuk setiap perjalanan.
- `maintenance_history`: riwayat komponen yang diservis, biaya, dan kondisi kendaraan sebelum servis.
- `driver_training_history`: riwayat pelatihan sopir, peningkatan atribut, biaya, dan hari game.
- `contract_payment_history`: struktur pencatatan periode pembayaran kontrak untuk pengembangan termin/piutang berikutnya.

Perjalanan baru mencatat tahapan siap berangkat, dalam perjalanan, tiba, lalu status akhir `DONE` atau `FAILED`. Riwayat perjalanan menampilkan 20 hasil terakhir, termasuk kegagalan. Kegagalan pengiriman spot tidak lagi ditandai sebagai kontrak selesai. Perawatan armada dan pelatihan sopir dicatat dalam tabel riwayat dan dapat dilihat dari menu masing-masing.

### Catatan kompatibilitas

- Migrasi bersifat additive (v5); jangan mengubah migrasi v1–v4 karena sudah dipakai oleh save yang ada.
- Trip lama yang dahulu ditandai `DONE` meskipun gagal tidak dapat diklasifikasikan ulang secara aman karena data penyebab historisnya tidak tersedia.
- `contract_payment_history` disiapkan sebagai fondasi idempotensi termin. Sistem kontrak dedicated yang ada masih memakai proses tutup buku bulanan dan belum diubah menjadi piutang jatuh tempo terpisah.
- Gudang saat ini adalah fasilitas sparepart yang memengaruhi biaya servis, bukan sistem inventaris barang kiriman. Sistem stok kargo penuh belum diaktifkan agar tidak mengubah ekonomi save lama secara diam-diam.


### YANG BELUM ADA

Fitur-fitur berikut memang belum diimplementasikan penuh dalam perubahan ini dan perlu pekerjaan lanjutan khusus, bukan sekadar klaim selesai:

- 6: proses muat/bongkar yang memakai waktu simulasi terpisah dan status `TERTUNDA` berbasis event dinamis.
- 7: jadwal servis otomatis, lokasi kendaraan, dan nilai jual kendaraan bekas yang mengikuti depresiasi aktual.
- 8: jadwal shift/kelelahan fisik terpisah dari moral, promosi, dan pemutusan kerja dengan pesangon.
- 9: inventaris kargo, kapasitas gudang, tenaga bongkar muat, kehilangan stok, dan gudang transit. Fasilitas gudang saat ini adalah gudang sparepart yang memberi diskon servis.
- 10: estimasi kontrak lengkap sebelum menerima, uang muka, termin, dan pembatalan berpenalti.
- 11: piutang/utang berbasis jatuh tempo, neraca penuh, dan rekonsiliasi laporan keuangan yang lebih rinci. Sistem yang ada tetap menggunakan ledger kas dan tutup buku bulanan.
- 12: kepribadian AI yang dijelaskan dalam dokumen belum identik penamaannya dengan profil lama (`AGRESIF`, `HEMAT`, `PREMIUM`, `EKSPANSI`, `SEIMBANG`). Lima profil dan rencana AI lama tetap dipertahankan agar save kompatibel.
- 13: aturan seluruh jenis perjanjian, bagi hasil, dan penalti pelanggaran belum lengkap.
- 14: intelijen dengan umur informasi, tingkat keyakinan, rumor, dan risiko terbongkar belum lengkap.
- 15–16: pasar dan event sudah ada, tetapi belum seluruh dampak berantai, indikator penyebab, dan histori per wilayah tercakup.
- 17: peta sudah memiliki edge ferry dan biaya penyeberangan, tetapi belum ada jadwal kapal, antrean pelabuhan persisten, atau transfer truk-kapal-truk yang terpisah.
- 18: pohon riset dan pengembangan belum ada.
- 19: reputasi delivery dan brand sudah ada, tetapi pemisahan metrik kepuasan, ketepatan waktu, kondisi barang, dan komplain belum tuntas.
