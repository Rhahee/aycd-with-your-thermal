using System.Drawing;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
    try
    {
        UpdateService.ValidateSettings(new UpdateSettings { RepositoryUrl = "http://example.com/app.git" });
        throw new Exception("URL repo tidak aman diterima.");
    }
    catch (ArgumentException) { }

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

    var fixture = Path.Combine(directory, "git-fixture");
    var fixtureAssets = Path.Combine(fixture, "assets");
    var install = Path.Combine(directory, "install");
    Directory.CreateDirectory(fixtureAssets);
    Directory.CreateDirectory(install);
    File.WriteAllText(Path.Combine(fixture, "PrintToDo.csproj"),
        "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>WinExe</OutputType>" +
        "<TargetFramework>net10.0-windows</TargetFramework></PropertyGroup>" +
        "<ItemGroup><Content Include=\"assets\\**\\*\" CopyToOutputDirectory=\"PreserveNewest\" />" +
        "</ItemGroup></Project>");
    File.WriteAllText(Path.Combine(fixture, "Program.cs"), "class Program { static void Main() { } }");
    File.WriteAllText(Path.Combine(fixtureAssets, "index.html"), "fixture v1");
    File.WriteAllText(Path.Combine(fixture, ".gitignore"), "bin/\nobj/\n");
    RunGit(fixture, "init", "-b", "main");
    RunGit(fixture, "add", ".");
    RunGit(fixture, "-c", "user.name=PrintToDoTest", "-c", "user.email=test@example.invalid",
        "commit", "-m", "initial");
    var fixtureUrl = new Uri(fixture + Path.DirectorySeparatorChar).AbsoluteUri;
    var fixtureSettings = new UpdateSettings { RepositoryUrl = fixtureUrl, Branch = "main" };
    var workspaceKey = Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(fixtureUrl + "\nmain")))[..16].ToLowerInvariant();
    var updateWorkspace = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "PrintToDo", "Updates", workspaceKey);
    try
    {
        var checkMessages = new List<string>();
        var checkedUpdate = await UpdateService.PrepareAsync(fixtureSettings, install,
            checkMessages.Add, prepareRelease: false);
        Assert(checkedUpdate.Status == "available" && checkedUpdate.Prepared is null &&
               !Directory.Exists(Path.Combine(updateWorkspace, "stage")) &&
               !checkMessages.Any(message => message.Contains("Membangun")),
            "Cek Update membangun atau memasang rilis sebelum tombol Update ditekan.");
        var first = await UpdateService.PrepareAsync(fixtureSettings, install, _ => { });
        Assert(first.Status == "ready" && File.Exists(first.Prepared?.StagedExe),
            "Clone dan build pertama gagal.");
        File.Copy(Path.Combine(Path.GetDirectoryName(first.Prepared!.StagedExe)!, "update-commit.txt"),
            Path.Combine(install, "update-commit.txt"));
        var same = await UpdateService.PrepareAsync(fixtureSettings, install, _ => { });
        Assert(same.Status == "current", "Commit yang sama tidak dikenali sebagai versi terbaru.");
        File.WriteAllText(Path.Combine(fixtureAssets, "index.html"), "fixture v2");
        RunGit(fixture, "add", ".");
        RunGit(fixture, "-c", "user.name=PrintToDoTest", "-c", "user.email=test@example.invalid",
            "commit", "-m", "second");
        var newer = await UpdateService.PrepareAsync(fixtureSettings, install, _ => { });
        Assert(newer.Status == "ready" && newer.Commit != first.Commit,
            "Pull commit baru tidak menghasilkan rilis baru.");
    }
    finally
    {
        TryDeleteTree(updateWorkspace);
    }
    Console.WriteLine("LULUS: data, cetak 58/80 mm, port salah, clone/pull/build, dan pemasangan update.");
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

static void RunGit(string workingDirectory, params string[] arguments)
{
    var info = new ProcessStartInfo("git")
    {
        WorkingDirectory = workingDirectory,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    foreach (var argument in arguments) info.ArgumentList.Add(argument);
    using var process = Process.Start(info) ?? throw new Exception("Git tidak dapat dijalankan.");
    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0) throw new Exception($"Git fixture gagal: {error} {output}");
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
