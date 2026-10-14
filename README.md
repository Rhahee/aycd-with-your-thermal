# All You Can Do with Your Thermal Printer

Aplikasi daftar tugas harian dan mingguan untuk Windows. Antarmuka HTML/CSS/JavaScript berjalan di WebView2; aplikasi C# WinForms menyimpan daftar secara lokal dan mengirim hasil cetak ESC/POS melalui port serial Bluetooth.

## Menjalankan

1. Bangun rilis dari sumber dengan perintah pada bagian **Build rilis lokal**, lalu jalankan `dist\PrintToDo.exe` dengan folder `dist\assets` tetap di tempatnya. WebView2 Runtime diperlukan; build ini sudah membawa .NET Desktop Runtime.
2. Pasangkan printer melalui pengaturan Bluetooth Windows. Di halaman **Printer**, pilih port COM keluar milik printer dan cetak halaman uji. Pada pengujian aplikasi ini, printer ESC/POS 80 mm berhasil mencetak melalui COM6 dengan kecepatan port Windows 115200. Port dan kecepatan pada laptop lain dapat berbeda.
3. Pilih profil 80 mm atau 58 mm. Jika hasil cetak terpotong, sesuaikan **Lebar area cetak (dot)** lalu ulangi halaman uji.

Data disimpan di `%LOCALAPPDATA%\PrintToDo\state.json`. Menghapus aplikasi tidak menghapus file tersebut. Salin file ini untuk mencadangkan daftar secara manual.

## Update dari repo Git

Buka **Settings** lalu klik **Periksa & pasang update**. Repo `https://github.com/Rhahee/aycd-with-your-thermal.git` dan branch `main` sudah terisi sebagai sumber bawaan; URL dan branch dapat diganti di Settings. Aplikasi melakukan clone pada penggunaan pertama, kemudian `git pull --ff-only` untuk update berikutnya. Jika ada commit baru, aplikasi membangun rilis, mengganti `dist\PrintToDo.exe` dan folder `dist\assets`, lalu membuka ulang dirinya. Versi `.exe` sebelumnya disimpan sebagai `PrintToDo.exe.previous`. Data daftar dan profil printer tetap berada di `%LOCALAPPDATA%\PrintToDo\state.json`.

Proses update memerlukan Git, SDK .NET 10, akses ke repo, dan hak tulis pada folder `dist`. Gunakan repo yang Anda percayai karena kode dari repo akan dibangun dan dijalankan di laptop.

## Dukungan printer

Aplikasi mengirim gambar raster ESC/POS ke port serial Bluetooth. Bluetooth yang sudah terpasang tidak menjamin printer mendukung protokol ini. Keberhasilan baru pasti setelah hasil halaman uji diperiksa pada kertas. Tidak ada pemotongan kertas otomatis.

## Build rilis lokal

SDK .NET 10 terpasang lokal di `%LOCALAPPDATA%\Microsoft\dotnet`. Dari PowerShell di folder proyek:

`& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist`

Hasilnya berada di `dist/`. Jalankan `PrintToDo.exe` dari folder ini agar folder `assets` tersedia. Build ini sudah membawa .NET Desktop Runtime; WebView2 Runtime tetap diperlukan.
