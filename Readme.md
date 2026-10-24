# Nusantara Logistics Manager

Ini game simulasi bisnis truk logistik yang jalan di console. Idenya dari game tycoon, tapi isinya aku bikin serba Indonesia: uang jalan, Pantura, jembatan timbangan, pungli, sampai rem blong di Nagreg. Kamu mulai dari satu pick-up, dan kalau jago bisa sampai punya armada tronton yang nyeberang pakai Roro.

Dibuat pakai C# (.NET 10) dan MySQL, ngoding di VS Code. Karena berbasis console, tidak ada grafis. Semua lewat menu angka.

## Yang dibutuhkan

- .NET SDK 10
- MySQL, dan aku pakai MySQL Command Line Client buat ngurus databasenya
- VS Code + extension C# Dev Kit

Cek .NET kamu sudah benar belum:

```
dotnet --version
```

## Cara pasang

Kalau mulai dari nol, buka terminal dan ketik ini satu-satu:

```
mkdir NusantaraLogistics
cd NusantaraLogistics
dotnet new console --framework net10.0
dotnet add package MySqlConnector
dotnet add package Dapper
mkdir Database
mkdir Models
mkdir Services
mkdir Menu
code .
```

Habis itu hapus `Program.cs` bawaan, lalu buat semua file sesuai daftar di bagian "Isi folder" di bawah.

### Database

Impor `Database/schema.sql` sekali di database kosong lewat MySQL Command Line Client:

```sql
source Database/schema.sql;
```

Skrip ini tidak menghapus apa pun. Kalau tabelnya sudah ada, MySQL berhenti dengan error dan datamu aman.

Untuk mengulang dari nol, jalankan `Database/reset.sql` (ini MENGHAPUS database), lalu impor `schema.sql` lagi.

Cek hasilnya:

```sql
USE nusantara_logistics;
SHOW TABLES;
SELECT COUNT(*) FROM truck_catalog;
SELECT COUNT(*) FROM nodes;
```

Harusnya ada 16 tabel, `truck_catalog` isinya 13 baris, dan `nodes` 28 baris. Tabel `ledger` (buku kas) dan kolom tutorial dibuat otomatis saat game pertama kali dijalankan, jadi setelah itu totalnya 17 tabel.

### Samakan password MySQL

Buka `Database/Db.cs` dan cari baris ini:

```csharp
const string Placeholder =
    "Server=localhost;Port=3306;Database=nusantara_logistics;User=root;Password=PASSWORD_MU;";
```

Ganti `PASSWORD_MU` dengan password yang kamu pakai waktu login di MySQL Command Line Client. Kalau MySQL kamu memang tidak pakai password (misalnya XAMPP), kosongkan saja jadi `Password=;`.

Alternatif tanpa edit kode: isi variabel lingkungan `NUSANTARA_DB_CONN` dengan connection string lengkap.

## Menjalankan

```
dotnet restore
dotnet run
```

Game akan nanya nama perusahaan. Pakai nama baru untuk mulai baru, pakai nama lama untuk lanjut progres sebelumnya.

Untuk menjalankan tes aturan game:

```
dotnet test Tests/Nusantara.Tests.csproj
```

## Cara main (buat yang baru mulai)

Di menu utama ada **Panduan Pemula** (menu 13) dan kotak PANDUAN yang menunjukkan langkah berikutnya. Selesaikan 6 langkahnya untuk hadiah Rp 5 juta (satu kali saja). Tutorial tidak mengunci menu lain dan boleh dilewati.

Urutan yang aku sarankan untuk menit-menit pertama:

1. Beli truk dulu di menu Dealer, pilih Truk baru. Ambil Grand Maxx Pick-Up atau Blind Van, karena baknya sudah terpasang dan langsung bisa jalan.
2. Cari supir di menu Supir. Lihat CV, lalu interview orang yang kamu taksir (Rp 100.000). Ini penting, soalnya kejujuran di CV sering dilebihkan dan interview menunjukkan angka aslinya. Tier 1 cukup SIM A.
3. Pasang supirnya ke truk lewat menu Armada.
4. Buka menu Kontrak & Berangkat. Pilih filter "layak", baca preview biaya dan laba, pilih kontrak, pilih truk, atur uang jalan, lalu berangkat.
5. Tunggu sampai tiba. Hasilnya muncul sendiri waktu kamu balik ke menu utama. Kalau penasaran sisa waktunya, lihat menu Perjalanan.
6. Buka menu Perjalanan, ketik ID trip yang selesai untuk melihat laporan laba/rugi. Setiap rupiah yang masuk dan keluar tercatat di sana.
7. Jangan lupa servis. Jangan tunggu sampai kanvas rem di bawah 15%, itu awal dari masalah.
8. Kalau reputasi sudah 50, naik jadi CV lewat menu Pool / Tier / Manajer.

Beberapa tips: kasih uang jalan standar atau lebih supaya supir tidak ngambek, jangan beli truk bekas di awal karena komponennya sudah hampir habis, dan kalau ada kontrak yang minta reputasi tinggi, lewati dulu. Cek menu Keuangan & Tutup Buku sebelum menambah staf atau mengambil utang.

## Ekonomi v4 (ringkas)

- Modal awal akun BARU Rp 1,5 miliar. Akun lama tidak diubah. Batas armada Tier 1 jadi 5 truk.
- Tarif spot dan borongan dihitung dari kelas kendaraan x jarak tertagih x tarif/km x faktor pasar, dengan minimum per dispatch. Tarif/km diundi sekali saat kontrak dibuat lalu dikunci.
- Semua rumus ada di `Services/EconomySvc.cs`. Kelas bertanda "asumsi" adalah angka balancing, bukan tarif resmi pasar.
- Preview kontrak memisahkan kas bersih trip, cadangan keausan (BUKAN buku kas), kontribusi trip, dan overhead.
- Ada ringkasan saldo dan cadangan operasi sebelum beli truk baru atau bekas.
- Kolom database ditambah otomatis saat game jalan. Tidak perlu reset.
- Tes: `dotnet test Tests/Nusantara.Tests.csproj`

## Isi menu

Menu utama ada 13 pilihan. Setiap submenu bakal terus muncul sampai kamu pilih 0 untuk kembali.

1. **Armada**: lihat kondisi truk, pasang supir, ganti jenis BBM, atur asuransi, pasang kosmetik.
2. **Dealer**: beli truk baru, lihat pasar truk bekas, dan karoseri untuk pasang bak.
3. **Servis**: ganti oli, kanvas rem, ban, filter, dan mesin.
4. **Supir**: lihat CV, interview, rekrut, naikkan gaji, pecat.
5. **Kontrak & Berangkat**: papan kontrak dengan filter dan tanda LAYAK / BELUM LAYAK, preview laba, lalu atur semua detail perjalanan.
6. **Perjalanan**: truk yang lagi jalan, lima hasil terakhir, dan laporan laba/rugi per trip.
7. **Berita & Sosmed**: running news dan kampanye marketing buat naikin reputasi brand.
8. **Bank & Asuransi**: pinjam dan lunasi uang bank.
9. **Pool / Tier / Manajer**: naik jadi CV atau PT, pool, depo solar, gudang sparepart, mekanik, sertifikat B3, manajer.
10. **Toko**: top-up kas, kosmetik, dan hadiah dari nonton iklan.
11. **Buku Kas & Laporan**: riwayat semua transaksi, filter kategori, ringkasan masuk/keluar, dan cek rekonsiliasi.
12. **Keuangan & Tutup Buku**: kas tersedia, tagihan bulan depan, saldo minimum yang perlu dijaga, dan proyeksi 7/30 hari.
13. **Panduan Pemula**: tutorial 6 langkah.

Waktu mau berangkat, kamu bakal ditanya beberapa hal (dan sebelum memilih, dampak tiap pilihan UJ dan overload ditampilkan dulu):

- **Uang jalan**: pas-pasan (0.9x), standar (1.0x), longgar (1.15x), atau sangat longgar (1.3x). Kalau kurang dari yang diminta supir, moralnya turun.
- **Overload**: 0, 20, atau 40 persen. Untungnya lebih gede tapi bisa kena timbangan.
- **Kalau kena timbangan**: bayar denda, pakai jasa pengawalan, atau bongkar muatan.
- **Keamanan**: tanpa pengamanan, bayar uang keamanan Ormas, atau wajib istirahat di rest area.
- **Kalau solar langka**: antre di SPBU (lebih lama) atau beli eceran (lebih mahal).

Sebelum benar-benar berangkat, game menghitung ulang semua syarat. Kalau biaya berubah sejak preview (harga, event, atau kondisi), keberangkatan dibatalkan tanpa memotong uang, dan kamu tinggal bikin preview baru.

## Aturan mainnya

### Tiga tingkatan usaha

Kamu mulai sebagai usaha perorangan. Dari situ naik jadi CV, lalu PT.

Tier 1 (perorangan) cuma boleh pakai pick-up dan blind van, maksimal 5 truk, dan rutenya dalam kota sampai sekitar 150 km.

Tier 2 (CV) butuh reputasi 50 dan uang Rp 150 juta. Bisa pakai engkel, double, dan medium duty, maksimal 12 truk, jangkauannya satu pulau Jawa.

Tier 3 (PT) butuh reputasi 250, punya pool, dan uang Rp 1,2 miliar. Di sini baru bisa pakai tronton, head tractor, dump, dan heavy duty, maksimal 40 truk, dan boleh antar-pulau pakai kapal. Kargo kimia dan BBM butuh sertifikat B3 (Rp 150 juta) yang cuma bisa dibeli PT.

### Waktu

Dasarnya 100 km sama dengan 15 menit. Kalau lewat laut, kecepatannya setengah dari darat. Satu hari di game sama dengan 30 menit waktu asli. Perjalanan tetap jalan walau game kamu tutup, karena waktunya disimpan di MySQL.

Bawaannya aku set dipercepat 20 kali supaya enak dicoba. Kalau mau waktu asli, lihat bagian "Ubah kecepatan waktu" di bawah.

### Uang jalan (UJ)

UJ standar dihitung dari BBM, tol, tiket kapal, uang makan dan parkir, dan cadangan buat jalur rawan, lalu dikali 1,1. UJ dipotong dari kas **sekali saja** waktu berangkat. Waktu trip selesai, kalau UJ lebih besar dari biaya aslinya, sisanya dikembalikan supir sesuai tingkat kejujurannya. Kalau kurang, supir nombok dulu dan moralnya turun, lalu perusahaan menggantinya.

### BBM

Bio Solar murah (Rp 6.800 per liter) tapi bikin mesin dan filter cepat rusak. Dexlite lebih mahal (Rp 13.500 per liter) tapi mesin lebih awet. Truk Eropa seperti Mercy-Truck, Volvoo, dan Scanya wajib Dexlite. Kalau dipaksa pakai solar biasa, kerusakannya jauh lebih cepat. Kalau punya depo solar sendiri, harga BBM turun 15 persen.

### Perawatan dan rem blong

Ada lima komponen: oli, kanvas rem, ban, filter BBM, dan mesin. Tiap perjalanan bikin persentasenya turun. Yang paling bahaya kanvas rem. Kalau di bawah 15 persen, peluang rem blong naik drastis di jalur turunan dan tanjakan seperti Nagreg, Alas Roban, atau Situbondo. Ban di bawah 15 persen bisa pecah, mesin atau oli yang habis bisa bikin truk mogok.

Servis pakai mekanik panggilan itu mahal. Mekanik tetap lebih hemat, tapi butuh pool. Gudang sparepart sendiri memotong biaya servis 15 persen.

### Supir

SIM A untuk Tier 1, B1 untuk Tier 2, B2 Umum untuk Tier 3. SIM yang lebih tinggi boleh bawa truk yang lebih kecil. Atribut yang perlu diperhatikan: kecepatan, eco-driving, kejujuran, pengalaman, dan riwayat nabrak.

Biaya per supir itu gaji pokok, BPJS (sekitar 10 persen dari gaji), uang jalan tiap trip, dan komisi. Jumlah supir dibatasi sebanyak truk ditambah 1 cadangan. Soal moral:

- Moral 70 ke atas: lebih cepat, BBM irit, truk lebih awet, risiko lebih kecil.
- Moral di bawah 30: ugal-ugalan, jual BBM di jalan, bahkan bisa kabur bawa kargo.
- Gaji telat bikin moral turun dan supir bisa resign.

### Kontrak

Ada dua jenis. Spot charter itu sekali jalan, bayarannya per kilometer tinggi, tapi jumlahnya tidak menentu dan cepat kedaluwarsa. Kontrak borongan itu bulanan (1 sampai 12 bulan) dari pabrik atau BUMN. Margin sedang, tapi pemasukannya pasti.

Aturan payout borongan: kalau target trip per bulan tercapai, kamu dibayar penuh. Kalau kurang, bayarannya jadi 80% x bayaran bulanan x (trip selesai / target), dan reputasi turun 5. Trip yang masih berjalan saat tutup buku **tidak** dihitung untuk target. Tabel bayarannya ditampilkan di preview sebelum kamu menerima kontrak.

Setiap kontrak di papan diberi tanda **LAYAK** (ada truk, supir, bak, dan SIM yang cocok, plus estimasi laba) atau **BELUM LAYAK** (dengan alasannya). Estimasi laba memakai asumsi UJ standar, tanpa overload, dan tanpa kejadian acak.

Kontrak borongan ada syarat reputasinya. Uang top-up tidak bisa membeli reputasi, itu sengaja.

### Reputasi

Reputasi itu gabungan dari pengiriman sukses (maksimal 400) dan brand awareness dari kampanye sosmed (maksimal 100). Rating bintang sama dengan reputasi dibagi 100, maksimal 5. Kecelakaan, kargo hilang, dan tilang bakal mengurangi reputasi.

### Jalanan Indonesia

Ada empat rute Roro: Merak-Bakauheni, Ketapang-Gilimanuk, Tanjung Perak-Makassar, dan Tanjung Perak-Banjarmasin. Di dermaga ada antrean, dan kalau cuaca buruk kapal bisa tertunda.

Overload memang menguntungkan tapi bisa kena jembatan timbangan, apalagi pas ada razia Zero ODOL. Di jalur rawan ada risiko bajing loncat dan pungli. Uang keamanan Ormas menurunkan risikonya, begitu juga istirahat di rest area, walau perjalanan jadi lebih lama.

### Keuangan

Plafon pinjaman bank dihitung dari Rp 250 juta x tier x (1 + bintang), dikurangi utang yang masih jalan. Tenornya 12, 24, atau 36 bulan dengan bunga flat 1 persen per bulan.

Kalau cicilan telat dua kali, bank menyita truk termahalmu. Telat tiga kali, atau kas turun sampai di bawah minus Rp 100 juta, kamu bangkrut dan game over.

Asuransi diatur per truk lewat menu Armada. All Risk menanggung sekitar 90 persen kerusakan, TLO sekitar 80 persen nilai truk kalau total loss, dan Cargo Insurance sekitar 85 persen kerugian kargo. Setiap klaim bikin premi bulan depan naik, dan pelan-pelan turun lagi kalau tidak ada klaim.

### Tutup buku bulanan

Tiap 30 hari game ada tutup buku otomatis dengan urutan: terima bayaran borongan, bayar gaji dan BPJS, sewa pool, premi, lalu cicilan bank. Kalau kas tidak cukup untuk gaji, seluruh tagihan gaji jadi tunggakan yang ditagih lagi bulan depan, moral supir turun, dan sebagian bisa resign.

Satu bulan hanya diproses satu kali. Kalau proses gagal di tengah, tidak ada yang berubah dan tanggal tutup buku tidak maju.

Buka menu **Keuangan & Tutup Buku** untuk melihat tagihan berikutnya, saldo minimum yang perlu dijaga, dan jawaban cepat untuk pertanyaan "kalau tidak ada pemasukan sampai tutup buku, kas cukup atau tidak?". Piutang borongan dan UJ yang sedang di jalan **tidak** dianggap uang tunai.

### Buku kas

Setiap perubahan kas punya satu baris di buku kas (menu 11): tanggal, kategori, nominal masuk/keluar, saldo sesudahnya, dan tautan ke trip kalau ada. Menjumlahkan seluruh buku kas harus sama dengan kas perusahaan. Cek di menu 11 pilihan "Cek rekonsiliasi". Kalau hasilnya SELISIH, berarti ada perubahan kas di luar buku kas dan itu bug yang perlu dilaporkan.

Trip dari sebelum buku kas dibuat tidak punya rincian, jadi laporannya menampilkan "belum ada catatan", bukan angka nol karangan.

### Fasilitas

- Pool sewa: CV, Rp 10 juta + Rp 6 juta per bulan. Bisa rekrut mekanik tetap.
- Pool sendiri: PT, Rp 250 juta. Tidak ada sewa bulanan.
- Depo solar: butuh pool sendiri, Rp 400 juta. BBM jadi 15 persen lebih murah.
- Gudang sparepart: butuh pool sendiri, Rp 300 juta. Servis jadi 15 persen lebih murah.
- Mekanik tetap: butuh pool, Rp 5 juta per bulan, maksimal 5.
- Manajer operasional: CV, Rp 5 juta + Rp 15 juta per bulan. Dia yang servis dan berangkatin truk otomatis. Aturannya bisa kamu atur sendiri di menu Pool / Tier / Manajer: batas servis, kelonggaran UJ, dan margin minimal kontrak spot.

### Berita harian

Kadang muncul kejadian acak: solar langka di Pantura, panen raya sawit yang bikin tarif dump truck naik, larangan truk sumbu 3 masuk tol pas mudik, gelombang tinggi yang ganggu Roro, permintaan pabrik yang melonjak, atau operasi Zero ODOL.

### Toko

Semua di sini cuma simulasi, tidak ada uang sungguhan yang keluar. Top-up nambah kas tapi tidak nambah reputasi. Kosmetiknya ada livery, klakson Telolet, lampu LED, stiker kaca, dan skin pool. Kalau nonton iklan (juga simulasi), hadiahnya diskon sparepart 15 persen, refresh CV supir gratis, atau percepat karoseri 50 persen.

## Isi folder

```
NusantaraLogistics/
├─ GameLogistics.csproj
├─ Program.cs                 loop menu utama
├─ Readme.md
├─ Menu/                      tampilan dan input
│  ├─ Ui.cs                   fungsi bantu (input, garis, bar komponen)
│  ├─ MainMenu.cs             dashboard
│  ├─ FleetMenu.cs
│  ├─ DelarMenu.cs
│  ├─ ServiceMenu.cs
│  ├─ DriverMenu.cs
│  ├─ ContractMenu.cs         papan kontrak, preview, perencana berangkat
│  ├─ TripMenu.cs             perjalanan + laporan laba/rugi trip
│  ├─ NewsMenu.cs
│  ├─ BankMenu.cs
│  ├─ FacilitasMenu.cs
│  ├─ ShopMenu.cs
│  ├─ LedgerMenu.cs           buku kas
│  ├─ FinanceMenu.cs          arus kas, proyeksi, tutup buku
│  └─ TutorialMenu.cs         panduan pemula
├─ Services/                  logika dan aturan game
│  ├─ Rules.cs
│  ├─ Accounting.cs           rumus uang v3 (tutup buku, UJ, margin)
│  ├─ RoadGraph.cs
│  ├─ Playersvc.cs
│  ├─ FlettSvc.cs
│  ├─ HrSvc.cs
│  ├─ ContractSvc.cs
│  ├─ ContractPreview.cs      kelayakan kontrak + estimasi laba
│  ├─ TripSvc.cs
│  ├─ FinanceSvc.cs
│  ├─ LedgerSvc.cs            buku kas
│  ├─ TutorialSvc.cs
│  ├─ WorldSvc.cs
│  └─ ManagerSvc.cs
├─ Models/
│  └─ models.cs
├─ Database/
│  ├─ Db.cs                   koneksi MySQL, pencatatan kas, migrasi otomatis
│  ├─ schema.sql              pembuat tabel (aman, tidak menghapus)
│  └─ reset.sql               hapus database (hanya kalau sengaja)
└─ Tests/
   ├─ Nusantara.Tests.csproj
   ├─ RulesTests.cs
   └─ V3Tests.cs
```

Pembagiannya begini: folder `Menu` cuma urusan nampilin dan nanya ke pemain, sedangkan semua hitungan dan aturan ada di `Services`.

## Mau ubah sesuatu, edit di mana?

Teks menu atau urutan pertanyaan ada di folder `Menu`, file menu yang bersangkutan. Bar komponen dan garis ada di `Menu/Ui.cs`.

Harga BBM, rumus konsumsi, dan kecepatan waktu ada di `Services/Rules.cs`. Rumus tutup buku, margin, dan hadiah tutorial ada di `Services/Accounting.cs`. Biaya fasilitas, syarat naik tier, kampanye, dan paket top-up ada di `Services/Playersvc.cs`. Urusan truk, servis, dan karoseri ada di `Services/FlettSvc.cs`. Supir ada di `HrSvc.cs`, pembuatan kontrak di `ContractSvc.cs`, dan kelayakan kontrak di `ContractPreview.cs`.

Peluang kecelakaan, tilang, pungli, dan hasil akhir perjalanan ada di `Services/TripSvc.cs`. Gaji, cicilan, bunga, penyitaan, tutup buku, dan kebangkrutan ada di `FinanceSvc.cs`. Pencatatan buku kas ada di `LedgerSvc.cs` dan `Database/Db.cs`. Jenis berita ada di `WorldSvc.cs`, dan perilaku manajer otomatis di `ManagerSvc.cs`.

Daftar truk, bak, kargo, kota, jalan, dan kosmetik tersimpan di tabel MySQL (misalnya `truck_catalog`, `cargo_types`, `nodes`, `edges`, `cosmetics`). Kalau mau ubah harga atau tambah data, ubah langsung di MySQL Command Line Client pakai perintah `UPDATE` atau `INSERT`.

## Ubah kecepatan waktu

Buka `Services/Rules.cs`, cari baris ini:

```csharp
public const double DevSpeed = 20;
```

Angka 20 artinya semua waktu dipercepat 20 kali, jadi Jakarta ke Surabaya cuma beberapa menit. Ganti jadi 1 kalau mau waktu asli (100 km = 15 menit, 1 hari game = 30 menit). Simpan, lalu jalankan ulang.

## Mulai dari nol lagi

Kalau cuma mau mengulang progres game tanpa menghapus database, kosongkan tabel data pemain di MySQL Command Line Client:

```sql
USE nusantara_logistics;
SET FOREIGN_KEY_CHECKS = 0;
TRUNCATE TABLE trips;
TRUNCATE TABLE contracts;
TRUNCATE TABLE drivers;
TRUNCATE TABLE used_listings;
TRUNCATE TABLE trucks;
TRUNCATE TABLE world_events;
TRUNCATE TABLE loans;
TRUNCATE TABLE owned_cosmetics;
TRUNCATE TABLE topups;
TRUNCATE TABLE ledger;
TRUNCATE TABLE players;
SET FOREIGN_KEY_CHECKS = 1;
```

Perintah ini tidak menyentuh daftar truk, kargo, kota, jalan, dan kosmetik, jadi data bawaan game tetap aman.

Kalau mau hapus semuanya termasuk tabel, jalankan `Database/reset.sql` (isinya `DROP DATABASE nusantara_logistics;`) lalu impor ulang `Database/schema.sql`.

## Kalau ada error

**Access denied for user 'root'**: password MySQL salah. Cek `Password=` di `Database/Db.cs`.

**Unable to connect to any of the specified MySQL hosts**: MySQL belum nyala. Start dulu service MySQL-nya.

**Unknown database 'nusantara_logistics'**: database belum dibuat atau namanya beda. Cek lagi di MySQL Command Line Client dengan `SHOW DATABASES;`, atau impor `Database/schema.sql`.

**Table ... doesn't exist**: ada tabel yang belum terbuat. Cek dengan `SHOW TABLES;` dan bandingkan dengan 17 tabel yang seharusnya ada (16 dari schema.sql + `ledger` yang dibuat otomatis saat game dijalankan).

**Table ... already exists saat impor schema.sql**: tabelnya sudah ada, dan itu sengaja tidak ditimpa. Pakai `Database/reset.sql` kalau memang mau mengulang dari nol.

**Error merah CS0103 atau CS0246 di VS Code**: biasanya ada file yang belum dibuat, salah folder, atau lupa baris `using` di atas file.

**The type or namespace 'Dapper' could not be found**: package belum terpasang. Jalankan `dotnet add package Dapper`, lalu `dotnet restore`.

**Truk tidak bisa berangkat**: biasanya belum ada supir, bak tidak cocok dengan kargo, SIM supir kurang, atau muatan melebihi kapasitas. Pesan yang muncul di layar biasanya sudah menyebut penyebabnya, dan papan kontrak menandai alasan BELUM LAYAK.

**Biaya berubah sejak preview**: harga, event, atau kondisi berubah antara preview dan konfirmasi. Tidak ada uang yang dipotong. Buat preview baru.

**Rekonsiliasi buku kas menunjukkan SELISIH**: ada perubahan kas yang tidak tercatat. Catat langkah terakhir yang kamu lakukan dan laporkan.

**Tulisan atau simbol berantakan**: terminalnya tidak pakai UTF-8. Pakai terminal bawaan VS Code atau Windows Terminal.

**Kas kurang waktu mau berangkat**: UJ dan biaya lain lebih besar dari uangmu. Turunkan UJ, ambil kontrak yang lebih murah, atau pinjam ke bank.

Kalau error-nya tidak ada di daftar ini, salin teks lengkapnya beserta nama file dan nomor barisnya, biar gampang dicari penyebabnya.

## Catatan

- Top-up, kosmetik, dan iklan itu simulasi. Tidak ada transaksi uang sungguhan. Tabel `topups` cuma mencatat riwayat simulasinya.
- Data tersimpan per nama perusahaan di MySQL. Menutup game tidak menghapus progres, dan perjalanan truk tetap jalan sesuai waktu asli.
- Angka dan harga di game itu desain permainan, bukan data logistik sebenarnya.
- Angka ekonomi (tarif, biaya, hadiah) adalah sasaran desain untuk diuji lewat playtest, bukan klaim realistis.

## Rencana ke depan

- Jadwal servis dan kondisi armada, perkembangan supir (moral, pelatihan), dan insiden yang bisa dipulihkan di tengah perjalanan.
- Save/backup/restore, laporan statistik, pencapaian, dan mode kesulitan.
- Tampilan yang lebih menarik (GUI atau game engine).
- Pembayaran dan iklan sungguhan.
- Simpan data online biar bisa main dari perangkat lain.
- Lebih banyak rute, pulau, dan jenis kargo.
- Lebih banyak model truck nya pasti masih bosen.

## Funfact 
Walaupun aku yg buat game text ini tapi aku masih belum bisa atau menguasai semuanya padahal aku yg buat ide tapi aku sendiri bingung dengan itu bahkan ide aku awal gimana caranya yg buat game itu tuh sulit untuk mengatur seluruh fitur dan manajemen supaya game ini sukses malah waktu asku coba gamenya malah bankrut!.