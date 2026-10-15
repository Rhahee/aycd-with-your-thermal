using System.Drawing;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using PrintToDo;

var directory = Path.Combine(Path.GetTempPath(), "PrintToDoSmoke", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var store = new AppStore(directory);
    Assert(store.State.Update.RepositoryUrl == UpdateSettings.DefaultRepositoryUrl &&
           store.State.Update.Branch == UpdateSettings.DefaultBranch,
        "Repo update bawaan tidak terpasang.");
    var legacyDirectory = Path.Combine(directory, "legacy-settings");
    Directory.CreateDirectory(legacyDirectory);
    File.WriteAllText(Path.Combine(legacyDirectory, "state.json"),
        """{"schemaVersion":1,"update":{"repositoryUrl":"","branch":""}}""");
    var legacyStore = new AppStore(legacyDirectory);
    Assert(legacyStore.State.Update.RepositoryUrl == UpdateSettings.DefaultRepositoryUrl &&
           legacyStore.State.Update.Branch == UpdateSettings.DefaultBranch,
        "Pengaturan repo kosong dari versi lama tidak memakai sumber bawaan.");
    legacyStore.SaveUpdateSettings(new UpdateSettings
    {
        RepositoryUrl = "https://example.com/custom.git",
        Branch = ""
    });
    Assert(new AppStore(legacyDirectory).State.Update.RepositoryUrl ==
           "https://example.com/custom.git" &&
           new AppStore(legacyDirectory).State.Update.Branch == "",
        "Repo pilihan pengguna berubah setelah aplikasi dibuka ulang.");

    var daily = new TodoList
    {
        Kind = "daily",
        Date = "2026-10-14",
        Title = "Rencana harian",
        Tasks = [new TodoTask { Text = "Selesaikan laporan sebelum sore." }]
    };
    store.SaveList(daily);
    daily.Tasks[0].Completed = true;
    store.SaveList(daily);
    Assert(new AppStore(directory).State.Lists.Single().Tasks[0].Completed,
        "Perubahan daftar harian tidak tersimpan.");

    var legacyTask = System.Text.Json.JsonSerializer.Deserialize<TodoTask>(
        """{"id":"d92a2df4111747deaf0469b8bf22c72a","text":"Tugas lama","day":-1,"completed":false}""",
        JsonConfig.Options)!;
    Assert(legacyTask.Type == "checklist", "Tugas versi lama tidak menjadi blok checklist.");
    daily.Tasks.Add(new TodoTask { Type = "heading", Text = "Prioritas" });
    daily.Tasks.Add(new TodoTask { Type = "note", Text = "Kerjakan sebelum makan siang.\nJangan lupa istirahat." });
    store.SaveList(daily);
    Assert(new AppStore(directory).State.Lists.Single().Tasks.Select(task => task.Type)
        .SequenceEqual(["checklist", "heading", "note"]),
        "Jenis dan urutan blok tidak tersimpan.");
    using (var blocksImage = ReceiptRenderer.Render(daily, new PrinterSettings()))
    {
        using var checklistOnly = ReceiptRenderer.Render(new TodoList
        {
            Kind = daily.Kind, Date = daily.Date, Title = daily.Title,
            Tasks = [daily.Tasks[0]]
        }, new PrinterSettings());
        Assert(blocksImage.Height > checklistOnly.Height,
            "Judul bagian dan catatan tidak muncul pada cetakan.");
    }

    var firstSection = new TodoSection { Title = "Pagi" };
    var secondSection = new TodoSection { Title = "Sore" };
    daily.Sections = [firstSection, secondSection];
    daily.Tasks[0].SectionId = firstSection.Id;
    daily.Tasks[1].SectionId = firstSection.Id;
    daily.Tasks[2].SectionId = secondSection.Id;
    store.SaveList(daily);
    var savedSections = new AppStore(directory).State.Lists.Single();
    Assert(savedSections.Sections.Select(section => section.Title).SequenceEqual(["Pagi", "Sore"]) &&
           savedSections.Tasks[2].SectionId == secondSection.Id,
        "Section dan hubungan bloknya tidak tersimpan.");
    using (var ordered = ReceiptRenderer.Render(daily, new PrinterSettings()))
    {
        daily.Sections.Reverse();
        using var reordered = ReceiptRenderer.Render(daily, new PrinterSettings());
        Assert(!Hash(ordered).SequenceEqual(Hash(reordered)),
            "Urutan section tidak mengubah pratinjau cetak.");
        daily.Sections.Reverse();
    }
    store.SavePrintSettings(new PrinterSettings(), "  Rahee  ");
    Assert(new AppStore(directory).State.FooterName == "Rahee",
        "Nama penutup global tidak tersimpan.");
    using (var withoutFooter = ReceiptRenderer.Render(daily, new PrinterSettings()))
    using (var withFooter = ReceiptRenderer.Render(daily, new PrinterSettings(), "Rahee"))
        Assert(withFooter.Height > withoutFooter.Height && HasClosingRule(withFooter) &&
               !HasClosingRule(withoutFooter),
            "Nama atau garis penutup tidak muncul pada cetakan.");

    var weekly = new TodoList
    {
        Kind = "weekly",
        Date = "2026-10-12",
        Title = "Rencana minggu ini",
        Tasks =
        [
            new TodoTask { Day = -1, Text = "Tugas umum yang panjang dan harus membungkus teks di kertas kecil." },
            new TodoTask { Day = 0, Text = "Senin: menyiapkan bahan." },
            new TodoTask { Day = 6, Text = "Minggu: tinjau hasil." }
        ]
    };
    store.SaveList(weekly);
    var reopened = new AppStore(directory);
    Assert(reopened.State.Lists.Count == 2, "Riwayat daftar tidak tersimpan.");
    Assert(reopened.State.Lists.Single(list => list.Kind == "weekly").Tasks.Count == 3,
        "Tugas umum dan per hari tidak tersimpan.");

    foreach (var (paper, dots) in new[] { (58, 384), (80, 576) })
    {
        var printer = new PrinterSettings { PaperWidthMm = paper, PrintableDots = dots };
        using var image = ReceiptRenderer.Render(weekly, printer);
        Assert(image.Width == dots && image.Height > 100, $"Pratinjau {paper} mm tidak valid.");
        var before = Hash(image);
        weekly.Tasks[0].Completed = true;
        using var completedImage = ReceiptRenderer.Render(weekly, printer);
        Assert(before.SequenceEqual(Hash(completedImage)),
            "Status selesai mengubah kotak pada cetakan.");
        weekly.Tasks[0].Completed = false;
        store.SavePrinter(printer);
        Assert(new AppStore(directory).State.Printer.PrintableDots == dots,
            "Profil printer tidak tersimpan.");
    }

    var longWeek = new TodoList
    {
        Kind = "weekly",
        Date = "2026-10-12",
        Tasks = Enumerable.Range(0, 50)
            .Select(index => new TodoTask
            {
                Day = index % 7,
                Text = $"Tugas {index + 1}: periksa rincian pekerjaan yang memerlukan beberapa baris pada kertas."
            }).ToList()
    };
    using (var longReceipt = ReceiptRenderer.Render(longWeek,
               new PrinterSettings { PaperWidthMm = 58, PrintableDots = 384 }))
        Assert(longReceipt.Height > 1000 && longReceipt.Height <= 30000,
            "Daftar mingguan panjang tidak dirender dengan benar.");

    var invalidPort = new PrinterSettings { PortName = "COM999" };
    using (var image = ReceiptRenderer.Render(daily, invalidPort))
    {
        try { EscPosPrinter.Print(image, invalidPort); throw new Exception("Port salah tidak ditolak."); }
        catch (IOException) { }
    }
    Assert(new AppStore(directory).State.Lists.Count == 2,
        "Kegagalan cetak menghapus daftar.");
    store.DeleteList(daily.Id);
    Assert(new AppStore(directory).State.Lists.Single().Id == weekly.Id,
        "Penghapusan daftar tidak tersimpan.");

    var updateSettings = new UpdateSettings
    {
        RepositoryUrl = "https://example.com/print-to-do.git",
        Branch = "main"
    };
    store.SaveUpdateSettings(updateSettings);
    Assert(new AppStore(directory).State.Update.Branch == "main",
        "Pengaturan repo tidak tersimpan.");
    var stage = Path.Combine(directory, "stage");
    var target = Path.Combine(directory, "target");
    Directory.CreateDirectory(Path.Combine(stage, "assets"));
    Directory.CreateDirectory(Path.Combine(target, "assets"));
    File.WriteAllText(Path.Combine(stage, "PrintToDo.exe"), "new-version");
    File.WriteAllText(Path.Combine(stage, "assets", "index.html"), "new-ui");
    File.WriteAllText(Path.Combine(stage, "update-commit.txt"), "key\nnew-commit\n");
    File.WriteAllText(Path.Combine(target, "PrintToDo.exe"), "old-version");
    File.WriteAllText(Path.Combine(target, "assets", "index.html"), "old-ui");
    UpdateApplier.ApplyFiles(stage, Path.Combine(target, "PrintToDo.exe"));
    Assert(File.ReadAllText(Path.Combine(target, "PrintToDo.exe")) == "new-version" &&
           File.ReadAllText(Path.Combine(target, "assets", "index.html")) == "new-ui" &&
           File.ReadAllText(Path.Combine(target, "PrintToDo.exe.previous")) == "old-version",
        "Penggantian berkas aplikasi atau cadangan versi lama gagal.");

    var install = Path.Combine(directory, "install");
    Directory.CreateDirectory(install);
    var fixtureExe = Path.Combine(AppContext.BaseDirectory, "PrintToDo.Smoke.exe");
    var productVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(fixtureExe)
        .ProductVersion!.Split('+', 2)[0];
    var releaseVersion = Version.Parse(productVersion);
    var releaseTag = "v" + releaseVersion;
    var assetName = $"AYCD-Printer-{releaseVersion}-win-x64.zip";
    var assetUrl = $"https://github.com/Rhahee/aycd-with-your-thermal/releases/download/{releaseTag}/{assetName}";
    byte[] package;
    using (var output = new MemoryStream())
    {
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntryFromFile(fixtureExe, "PrintToDo.exe");
            foreach (var name in new[] { "index.html", "app.js", "styles.css" })
            {
                using var writer = new StreamWriter(archive.CreateEntry("assets/" + name).Open());
                writer.Write(name);
            }
            using var marker = new StreamWriter(archive.CreateEntry("update-commit.txt").Open());
            marker.Write("key\nnew-commit\n");
        }
        package = output.ToArray();
    }
    var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant();
    using var client = new HttpClient(new ReleaseHandler(releaseTag, assetName, assetUrl, digest, package));
    var updateWorkspace = Path.Combine(directory, "update-workspace");
    var oldVersion = new Version(0, 0, 0);
    var current = await UpdateService.PrepareAsync(install, releaseVersion, _ => { },
        client: client, workspaceDirectory: updateWorkspace);
    Assert(current.Status == "current", "Versi rilis yang sama tidak dikenali.");
    var checkedUpdate = await UpdateService.PrepareAsync(install, oldVersion, _ => { },
        prepareRelease: false, client: client, workspaceDirectory: updateWorkspace);
    Assert(checkedUpdate.Status == "available" && checkedUpdate.Prepared is null &&
           !Directory.Exists(Path.Combine(updateWorkspace, "stage")),
        "Cek Update mengunduh paket sebelum tombol Update ditekan.");
    var prepared = await UpdateService.PrepareAsync(install, oldVersion, _ => { },
        client: client, workspaceDirectory: updateWorkspace);
    Assert(prepared.Status == "ready" && File.Exists(prepared.Prepared?.StagedExe) &&
           !File.Exists(Path.Combine(updateWorkspace, "update.zip")),
        "Paket rilis tidak disiapkan atau arsip sementara belum dibersihkan.");
    var badDigest = "sha256:" + new string('0', 64);
    using var badClient = new HttpClient(new ReleaseHandler(releaseTag, assetName, assetUrl,
        badDigest, package));
    try
    {
        await UpdateService.PrepareAsync(install, oldVersion, _ => { },
            client: badClient, workspaceDirectory: updateWorkspace);
        throw new Exception("Checksum salah diterima.");
    }
    catch (InvalidDataException) { }
    Assert(!Directory.Exists(Path.Combine(updateWorkspace, "stage")),
        "Paket dengan checksum salah meninggalkan aplikasi yang siap dipasang.");
    Console.WriteLine("LULUS: data, cetak 58/80 mm, port salah, rilis biner, checksum, dan pemasangan update.");
}
finally
{
    TryDeleteTree(directory);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static byte[] Hash(Bitmap bitmap)
{
    using var stream = new MemoryStream();
    bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
    return SHA256.HashData(stream.ToArray());
}

static bool HasClosingRule(Bitmap bitmap)
{
    var left = 24;
    var right = bitmap.Width - 24;
    for (var y = Math.Max(0, bitmap.Height - 110); y < bitmap.Height - 24; y++)
    {
        var dark = 0;
        for (var x = left; x < right; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel.R < 100 && pixel.G < 100 && pixel.B < 100) dark++;
        }
        if (dark > (right - left) * .9) return true;
    }
    return false;
}

static void TryDeleteTree(string path)
{
    if (!Directory.Exists(path)) return;
    try
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(path, true);
    }
    catch (Exception error)
    {
        Console.Error.WriteLine("Pembersihan folder uji gagal: " + error.Message);
    }
}

sealed class ReleaseHandler(string tag, string assetName, string assetUrl, string digest,
    byte[] package) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri!.AbsoluteUri == assetUrl)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new ByteArrayContent(package) });
        var json = JsonSerializer.Serialize(new
        {
            tag_name = tag,
            assets = new[]
            {
                new { name = assetName, browser_download_url = assetUrl,
                    digest, size = package.LongLength }
            }
        });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(json) });
    }
}
