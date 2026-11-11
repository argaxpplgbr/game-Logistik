LOGISTICS APP
Bangun perusahaan logistik. Kuasai jalur distribusi. Jadilah yang terdepan.
Nusantara Logistics adalah game simulasi manajemen perusahaan logistik berbasis konsol. Di sini, pemain tidak hanya mengirim barang dari satu tempat ke tempat lain. Pemain harus membangun perusahaan, mengatur armada, membaca peluang pasar, menjaga keuangan, dan bersaing dengan perusahaan lain—termasuk kompetitor yang dikendalikan AI.

Setiap keputusan punya konsekuensi. Ekspansi terlalu cepat bisa membebani keuangan. Terlalu hemat bisa membuat perusahaan tertinggal. Kuncinya adalah menemukan strategi yang tepat dan mengembangkan bisnis secara berkelanjutan.
Daftar Isi
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
Gambaran Permainan
Bayangkan kamu memulai sebuah perusahaan logistik dari skala kecil. Kamu perlu mengelola sumber daya yang terbatas, memilih langkah bisnis, dan menghadapi persaingan yang terus bergerak.

Dalam permainan, kamu dapat:
- Mengembangkan perusahaan dan meningkatkan kemampuan operasional.
- Mengelola armada serta kebutuhan distribusi.
- Mengikuti aktivitas pasar dan mengambil peluang yang tersedia.
- Menjaga pemasukan, pengeluaran, dan kestabilan keuangan.
- Menghadapi perusahaan pesaing dengan karakter strategi yang berbeda.
Tujuan utamanya: membangun perusahaan logistik yang kuat, efisien, dan mampu bertahan dalam persaingan.
Fitur Utama
Fitur	Deskripsi
Manajemen perusahaan	Mengembangkan bisnis dan menentukan arah strategi.
Operasional logistik	Mengelola armada dan aktivitas distribusi.
Sistem ekonomi	Memperhatikan pendapatan, biaya, dan kondisi keuangan.
Pasar bersama	Berinteraksi dengan peluang pasar dan aktivitas perusahaan lain.
Kompetitor AI	Bersaing dengan perusahaan yang memiliki profil strategi berbeda.
Komunikasi perusahaan	Menyediakan fondasi interaksi sosial ant perusahaan.
Save slot	Menyimpan beberapa perjalanan permainan secara terpisah.


Kompetitor AI
Kompetitor AI dirancang dengan lima profil perilaku. Masing-masing memiliki kecenderungan strategi yang berbeda sehingga persaingan tidak hanya bergantung pada satu pola.
Profil	Kecenderungan
AGRESIF	Cenderung mengambil peluang dan menekan persaingan.
HEMAT	Lebih berhati-hati dalam menggunakan sumber daya.
PREMIUM	Mengutamakan pendekatan layanan bernilai tinggi.
EKSPANSI	Berorientasi pada pertumbuhan dan perluasan bisnis.
SEIMBANG	Berusaha menjaga keseimbangan antara risiko dan perkembangan.


AI juga memiliki sistem perencanaan, sasaran jangka pendek dan panjang, serta aktivitas dalam pasar. Kedalaman dampak diplomasi dan perjanjian ant perusahaan masih terus dikembangkan; belum semua kesepakatan memiliki konsekuensi ekonomi dan operasional yang lengkap.
Teknologi
Proyek ini menggunakan:
- C# sebagai bahasa pemrograman.
- .NET 10 sebagai platform aplikasi.
- SQLite sebagai basis data lokal.
- Dapper untuk akses data.
- xUnit untuk pengujian.
Catatan: penyimpanan database proyek ini menggunakan SQLite, bukan MySQL.

Memulai Permainan
Persyaratan
- .NET SDK 10
- Terminal atau command prompt
- Sistem operasi yang mendukung .NET 10
Menjalankan aplikasi
Buka terminal pada folder utama proyek, lalu jalankan:
dotnet restore
dotnet build
dotnet run --project GameLogistics.csproj
Perintah tersebut memulihkan dependensi, membangun aplikasi, lalu menjalankannya.
Sistem Penyimpanan
Nusantara Logistics mendukung 10 slot penyimpanan. Setiap slot menggunakan file database SQLite tersendiri sehingga beberapa perjalanan permainan dapat dikelola secara terpisah.

Informasi penyimpanan:
- Data save disimpan di folder saves/.
- Sistem menyediakan file cadangan dengan ekstensi .bak.
- Lokasi folder penyimpanan dapat diatur melalui environment variable NUSANTARA_SAVE_DIR.
Sebaiknya jangan menghapus atau mengubah file save secara manual ketika permainan sedang berjalan.
Struktur Proyek
Berikut gambaran direktori utama:
.
├── Database/
│   ├── Db.cs
│   ├── SchemaSqlite.cs
│   ├── SchemaV2.cs
│   ├── SchemaV3.cs
│   └── SchemaV4.cs
├── Menus/
├── Models/
├── Services/
├── Tests/
├── GameLogistics.csproj
└── Readme.md
- Database/ menangani koneksi, skema, dan perubahan struktur database.
- Menus/ berisi tampilan menu dan alur interaksi pemain.
- Models/ menyimpan model data yang digunakan aplikasi.
- Services/ berisi logika bisnis dan layanan permainan.
- Tests/ berisi proyek pengujian.
Database dan Migrasi
Struktur database dikelola melalui sistem migrasi. Versi skema yang tersedia dalam proyek mencakup migrasi 1 sampai 4.

Pendekatan migrasi membantu perubahan struktur database dilakukan secara bertahap tanpa harus membangun ulang seluruh penyimpanan dari awal.
Pengujian
Untuk menjalankan pengujian:
dotnet test Tests/Nusantara.Tests.csproj
Pastikan dependensi sudah dipulihkan sebelum menjalankan pengujian. Status lulus atau gagal perlu dikonfirmasi dengan menjalankan perintah tersebut pada lingkungan pengembangan.
Catatan Pengembangan
Beberapa bagian masih memiliki ruang penyempurnaan, terutama:
- Memperdalam perilaku AI agar keputusan kompetitor terasa lebih alami dan konsisten.
- Mengembangkan komunikasi dan diplomasi ant perusahaan.
- Memberi dampak operasional serta ekonomi yang lebih lengkap pada perjanjian.
- Menyeimbangkan ekonomi agar strategi yang berbeda tetap layak dimainkan.
- Membuat aktivitas kompetitor lebih mudah dipantau oleh pemain.
Catatan navigasi: kode GameLoop memiliki handler untuk pilihan menu nomor 15 (CompanyChatMenu), tetapi MainMenu saat ini hanya menampilkan 14 pilihan. Karena itu, menu komunikasi tersebut belum muncul sebagai pilihan pada dashboard utama.
Tentang Proyek
Nusantara Logistics dikembangkan sebagai simulasi bisnis logistik yang menempatkan strategi, pengelolaan sumber daya, dan persaingan sebagai inti permainan.

Rencanakan dengan cermat. Kelola dengan efisien. Kembangkan perusahaanmu dan buktikan bahwa kamu mampu menjadi pemimpin industri logistik.