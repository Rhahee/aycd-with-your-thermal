using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PrintToDo;

internal sealed record PreparedUpdate(string StagedExe, string TargetExe, string Version);
internal sealed record UpdateCheckResult(string Status, string Version, PreparedUpdate? Prepared);

internal static class UpdateService
{
    private const string ExeFile = "PrintToDo.exe";
    private const string MarkerFile = "update-commit.txt";
    private const string ReleaseApi = "https://api.github.com/repos/Rhahee/aycd-with-your-thermal/releases/latest";
    private const string AssetPrefix = "AYCD-Printer-";
    private const string AssetSuffix = "-win-x64.zip";
    private const long MaxArchiveBytes = 250L * 1024 * 1024;
    private const long MaxExpandedBytes = 400L * 1024 * 1024;
    private static readonly HttpClient SharedClient = CreateClient();

    // Kept for reading settings saved by older versions. Releases now come from this app's official repository.
    public static void ValidateSettings(UpdateSettings settings)
    {
        if (settings.RepositoryUrl?.Length > 2048 || settings.Branch?.Length > 100)
            throw new ArgumentException("Alamat repo atau nama branch terlalu panjang.");
    }

    public static string? GetInstalledCommit(string appDirectory)
    {
        var path = Path.Combine(appDirectory, MarkerFile);
        if (!File.Exists(path)) return null;
        var lines = File.ReadAllLines(path);
        return lines.Length >= 2 ? lines[1] : null;
    }

    public static async Task<UpdateCheckResult> PrepareAsync(
        string appDirectory, Version installedVersion, Action<string> progress,
        CancellationToken cancellationToken = default, bool prepareRelease = true,
        HttpClient? client = null, string? workspaceDirectory = null)
    {
        client ??= SharedClient;
        progress("Memeriksa rilis terbaru…");
        using var response = await client.GetAsync(ReleaseApi, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        var release = document.RootElement;
        var tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v[0-9]+\.[0-9]+\.[0-9]+$", RegexOptions.CultureInvariant) ||
            !Version.TryParse(tag[1..], out var releaseVersion))
            throw new InvalidDataException("Nomor versi rilis GitHub tidak valid.");

        if (releaseVersion <= installedVersion)
        {
            progress("Aplikasi sudah menggunakan rilis terbaru.");
            return new UpdateCheckResult("current", tag, null);
        }

        var assetName = AssetPrefix + releaseVersion + AssetSuffix;
        var asset = release.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(item => item.GetProperty("name").GetString() == assetName);
        if (asset.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException($"Rilis {tag} belum menyediakan paket update Windows.");
        var assetUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
        var expectedPrefix = "https://github.com/Rhahee/aycd-with-your-thermal/releases/download/" + tag + "/";
        if (assetUrl != expectedPrefix + assetName)
            throw new InvalidDataException("Alamat paket update tidak sesuai dengan rilis resmi.");
        var digest = asset.GetProperty("digest").GetString() ?? "";
        if (!Regex.IsMatch(digest, @"^sha256:[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Rilis belum menyediakan checksum SHA-256 paket update.");
        var expectedSize = asset.GetProperty("size").GetInt64();
        if (expectedSize is <= 0 or > MaxArchiveBytes)
            throw new InvalidDataException("Ukuran paket update tidak valid.");

        if (!prepareRelease)
            return new UpdateCheckResult("available", tag, null);

        var workspace = workspaceDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PrintToDo", "Updates", "release");
        Directory.CreateDirectory(workspace);
        var archivePath = Path.Combine(workspace, "update.zip");
        var stage = Path.Combine(workspace, "stage");
        if (File.Exists(archivePath)) File.Delete(archivePath);
        if (Directory.Exists(stage)) Directory.Delete(stage, true);

        try
        {
            progress($"Mengunduh {tag}…");
            using var archiveResponse = await client.GetAsync(assetUrl,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            archiveResponse.EnsureSuccessStatusCode();
            await using (var input = await archiveResponse.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += count;
                    if (total > MaxArchiveBytes)
                        throw new InvalidDataException("Paket update terlalu besar.");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                }
                if (total != expectedSize)
                    throw new InvalidDataException("Ukuran unduhan tidak sesuai dengan rilis.");
            }
            await using (var input = File.OpenRead(archivePath))
            {
                var actual = Convert.ToHexString(
                    await SHA256.HashDataAsync(input, cancellationToken));
                if (!actual.Equals(digest[7..], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Checksum paket update tidak cocok.");
            }

            progress("Menyiapkan paket update…");
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                if (archive.Entries.Count > 100 ||
                    archive.Entries.Sum(entry => entry.Length) > MaxExpandedBytes)
                    throw new InvalidDataException("Isi paket update terlalu besar.");
                archive.ExtractToDirectory(stage);
            }
            var stagedExe = Path.Combine(stage, ExeFile);
            if (!File.Exists(stagedExe) ||
                !File.Exists(Path.Combine(stage, "assets", "index.html")) ||
                !File.Exists(Path.Combine(stage, "assets", "app.js")) ||
                !File.Exists(Path.Combine(stage, "assets", "styles.css")) ||
                !File.Exists(Path.Combine(stage, MarkerFile)))
                throw new InvalidDataException("Paket update tidak berisi aplikasi lengkap.");
            var fileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(stagedExe)
                .ProductVersion?.Split('+', 2)[0];
            if (!Version.TryParse(fileVersion, out var stagedVersion) ||
                stagedVersion != releaseVersion)
                throw new InvalidDataException("Versi aplikasi dalam paket tidak sesuai dengan rilis.");
            progress("Rilis baru siap dipasang.");
            return new UpdateCheckResult("ready", tag,
                new PreparedUpdate(stagedExe, Path.Combine(appDirectory, ExeFile), tag));
        }
        catch
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            throw;
        }
        finally
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AYCD-Printer-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }
}
