# AYCD Printer

**All You Can Do with Your Thermal Printer** adalah aplikasi daftar tugas untuk Windows. Antarmukanya berjalan di WebView2, sedangkan WinForms menyimpan data lokal dan mengirim cetakan ke printer thermal Bluetooth serial yang mendukung ESC/POS. Aplikasi tidak memerlukan akun atau internet untuk membuat daftar dan mencetak.

## Instalasi langkah demi langkah

1. Siapkan file installer `All-You-Can-Do-with-Your-Thermal-Printer-Setup-<versi>-win-x64.exe`. Minta file tersebut dari pembuat aplikasi, atau [buat sendiri dari source](#membuat-installer). File installer berada di `installer\output\` setelah proses build dan tidak disimpan di Git.
2. Klik dua kali file installer di laptop Windows x64, lalu ikuti petunjuk yang muncul. Pemasangan dilakukan untuk akun Windows yang sedang dipakai, sehingga tidak memerlukan hak administrator.
3. Pilih apakah ingin membuat pintasan desktop, lalu klik **Install**. Installer membuat pintasan di Start Menu dan memasang WebView2 Runtime jika belum tersedia. Sediakan koneksi internet jika WebView2 perlu dipasang.
4. Setelah selesai, klik **Finish** dan buka **All You Can Do with Your Thermal Printer** dari Start Menu. Jika pintasan desktop dipilih, aplikasi juga bisa dibuka dari sana.
5. Lanjutkan ke [penggunaan pertama](#penggunaan-pertama) untuk memasangkan printer dan mencetak halaman uji.

## Penggunaan pertama

1. Pasangkan printer di pengaturan Bluetooth Windows. Buka **Settings** di aplikasi, pilih port COM keluar, lalu klik **Cetak halaman uji**. Pastikan hasilnya benar-benar keluar di kertas.
2. Pilih lebar kertas **58 mm** atau **80 mm**. Jika tepi cetakan terpotong, buka **Pengaturan lanjutan**, sesuaikan **Lebar area cetak (dot)**, lalu ulangi halaman uji.
3. Buka **To do list**, pilih tanggal, dan buat daftar. Isi judul, tambahkan section dan blok, lalu cetak dari pratinjau.

Printer RPP02N 80 mm pernah berhasil diuji pada laptop pengembangan melalui COM6 dengan kecepatan port Windows 115200. Port dan kecepatan pada perangkat lain bisa berbeda. Nilai *BT Baud* pada halaman self-test printer tidak selalu sama dengan kecepatan port COM Windows.

## Daftar dan cetakan

- **To do list:** setiap daftar memiliki judul dan tanggal. Tambahkan section dengan memilih blok pertama berupa **Judul bagian**, **Checklist**, atau **Catatan**. Tambahkan blok lain melalui **+ Tambah blok** dan atur urutannya dengan tombol panah.
- **History:** buka, ubah, atau cetak ulang daftar yang tersimpan. Daftar Weekly dari versi lama tetap dapat dibuka di sini; menu khusus Weekly sudah tidak tersedia.
- **Pratinjau:** mengikuti urutan section dan blok. Garis lurus memisahkan judul dari isi, garis putus-putus memisahkan section, dan nama penutup opsional dicetak setelah garis penutup. Kotak checklist pada kertas selalu kosong meskipun sudah ditandai selesai di aplikasi.
- **Settings:** pilih profil printer, port COM, ukuran kertas, dan satu nama penutup untuk semua daftar. Pengaturan lanjutan memuat nama profil, lebar cetak dalam dot, dan kecepatan port.

Daftar dan pengaturan disimpan di `%LOCALAPPDATA%\PrintToDo\state.json`. Untuk mencadangkan data, tutup aplikasi lalu salin file tersebut ke tempat lain. Uninstall aplikasi tidak menghapus data ini.

## Pembaruan aplikasi

Di bagian bawah **Settings**, klik **Cek Update**. Aplikasi mengambil commit terbaru dari [repo ini](https://github.com/Rhahee/aycd-with-your-thermal) pada branch `main`. Jika tersedia, tombol **Update** akan aktif. Tombol itu membangun aplikasi dari source, memasang hasilnya di folder aplikasi, lalu membuka ulang aplikasi. Versi dan commit yang terpasang ditampilkan di kiri bawah sidebar.

**Cek Update** memerlukan Git dan akses ke GitHub. **Update** juga memerlukan SDK .NET 10 dan izin menulis ke folder instalasi. Installer sudah membawa .NET Desktop Runtime untuk menjalankan aplikasi, tetapi runtime tersebut tidak menggantikan SDK yang diperlukan untuk membangun update. Jika update gagal, daftar tetap tersimpan; salinan executable sebelumnya tersedia sebagai `PrintToDo.exe.previous` bila penggantian file sudah dimulai. Karena source dari repo dibangun dan dijalankan di komputer, gunakan hanya repo yang Anda percayai.

## Dukungan printer

Aplikasi mengirim gambar raster ESC/POS melalui port serial Bluetooth. Memasangkan printer saja belum membuktikan dukungan ESC/POS; lakukan halaman uji dan periksa hasil fisiknya. Profil 58 mm dan 80 mm mengatur tata letak, tetapi tidak mengubah protokol printer. Pemotongan kertas otomatis tidak tersedia.

## Membangun dari source

Persyaratan pengembang: Windows 10 atau lebih baru (x64), SDK .NET 10, Git, dan WebView2 Runtime untuk menjalankan hasil build. Dari PowerShell di folder repo:

```powershell
dotnet publish PrintToDo.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

Jalankan `dist\PrintToDo.exe` bersama folder `dist\assets`. Hasil publish membawa .NET Desktop Runtime, tetapi WebView2 Runtime tetap diperlukan. Jika SDK dipasang hanya untuk pengguna Windows saat ini dan `dotnet` belum ada di `PATH`, gunakan `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"` sebagai pengganti `dotnet` pada perintah di atas.

Untuk menjalankan pengujian smoke:

```powershell
dotnet run --project tests/PrintToDo.Smoke.csproj -c Release
```

## Membuat installer

Pasang Inno Setup 6, lalu commit dan push perubahan ke `main`. Skrip installer mensyaratkan working tree bersih serta commit lokal sama dengan `origin/main`, karena commit tersebut ditandai sebagai versi terpasang untuk fitur update.

```powershell
.\installer\Build-Installer.ps1
```

Skrip melakukan publish aplikasi, mengunduh bootstrapper WebView2 dari Microsoft jika belum ada, memeriksa tanda tangan digitalnya, dan membuat installer di `installer\output\`. Folder `dist\`, `installer\output\`, dan `installer\prerequisites\` tidak dilacak Git.

Installer memasang aplikasi ke `%LOCALAPPDATA%\Programs\All You Can Do with Your Thermal Printer` untuk pengguna Windows saat ini, membuat pintasan Start Menu, dan menawarkan pintasan desktop. Koneksi internet diperlukan saat instalasi jika WebView2 Runtime perlu dipasang.
