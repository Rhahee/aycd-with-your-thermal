using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PrintToDo;

internal sealed record PreparedUpdate(string StagedExe, string TargetExe, string Commit);
internal sealed record UpdateCheckResult(string Status, string Commit, PreparedUpdate? Prepared);

internal static class UpdateService
{
    private const string ProjectFile = "PrintToDo.csproj";
    private const string ExeFile = "PrintToDo.exe";
    private const string MarkerFile = "update-commit.txt";

    public static void ValidateSettings(UpdateSettings settings)
    {
        var url = settings.RepositoryUrl?.Trim() ?? "";
        var branch = settings.Branch?.Trim() ?? "";
        if (url.Length > 2048 || branch.Length > 100)
            throw new ArgumentException("Alamat repo atau nama branch terlalu panjang.");
        if (url.Length > 0 && !IsSupportedRepository(url))
            throw new ArgumentException("Gunakan URL repo HTTPS, SSH, atau file:// yang valid.");
        if (branch.Length > 0 &&
            (!Regex.IsMatch(branch, @"^[A-Za-z0-9][A-Za-z0-9._/-]*$") ||
             branch.Contains("..", StringComparison.Ordinal) ||
             branch.EndsWith('/') || branch.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Nama branch tidak valid.");
    }

    private static bool IsSupportedRepository(string url)
    {
        if (Regex.IsMatch(url, @"^git@[A-Za-z0-9.-]+:[A-Za-z0-9._/-]+(?:\.git)?$"))
            return true;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "ssh" or "file"))
            return false;
        return uri.Scheme == "file" ||
               (!string.IsNullOrWhiteSpace(uri.Host) && string.IsNullOrEmpty(uri.UserInfo));
    }

    public static string? GetInstalledCommit(string appDirectory)
    {
        var path = Path.Combine(appDirectory, MarkerFile);
        if (!File.Exists(path)) return null;
        var lines = File.ReadAllLines(path);
        return lines.Length >= 2 ? lines[1] : null;
    }

    public static async Task<UpdateCheckResult> PrepareAsync(
        UpdateSettings settings, string appDirectory, Action<string> progress,
        CancellationToken cancellationToken = default)
    {
        ValidateSettings(settings);
        var url = settings.RepositoryUrl.Trim();
        var branch = settings.Branch.Trim();
        if (url.Length == 0)
            throw new InvalidOperationException("Isi URL repo pada Settings terlebih dahulu.");

        var key = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(url + "\n" + branch)))[..16].ToLowerInvariant();
        var workspace = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PrintToDo", "Updates", key);
        var source = Path.Combine(workspace, "source");
        var stage = Path.Combine(workspace, "stage");
        Directory.CreateDirectory(workspace);

        if (!Directory.Exists(source))
        {
            progress("Mengambil repo untuk pertama kali…");
            var partial = Path.Combine(workspace, "source.partial");
            if (Directory.Exists(partial)) Directory.Delete(partial, true);
            var arguments = new List<string> { "clone" };
            if (branch.Length > 0) arguments.AddRange(["--branch", branch]);
            arguments.AddRange(["--", url, partial]);
            try
            {
                await RunAsync("git", arguments, workspace, TimeSpan.FromMinutes(5), cancellationToken);
                Directory.Move(partial, source);
            }
            catch
            {
                if (Directory.Exists(partial)) Directory.Delete(partial, true);
                throw;
            }
        }
        else
        {
            if (!Directory.Exists(Path.Combine(source, ".git")))
                throw new InvalidOperationException("Folder update bukan checkout Git yang valid.");
            var configuredUrl = (await RunAsync("git", ["-C", source, "remote", "get-url", "origin"],
                source, TimeSpan.FromSeconds(30), cancellationToken)).Trim();
            if (!string.Equals(configuredUrl, url, StringComparison.Ordinal))
                throw new InvalidOperationException("Alamat remote checkout update tidak cocok dengan Settings.");
            var changes = (await RunAsync("git", ["-C", source, "status", "--porcelain"],
                source, TimeSpan.FromSeconds(30), cancellationToken)).Trim();
            if (changes.Length > 0)
                throw new InvalidOperationException("Checkout update memiliki perubahan lokal. Periksa folder update sebelum menarik versi baru.");
            progress("Menarik perubahan repo…");
            await RunAsync("git", ["-C", source, "pull", "--ff-only"],
                source, TimeSpan.FromMinutes(5), cancellationToken);
        }

        if (!File.Exists(Path.Combine(source, ProjectFile)))
            throw new InvalidOperationException("Repo tidak berisi PrintToDo.csproj.");
        var commit = (await RunAsync("git", ["-C", source, "rev-parse", "HEAD"],
            source, TimeSpan.FromSeconds(30), cancellationToken)).Trim();
        var marker = Path.Combine(appDirectory, MarkerFile);
        if (File.Exists(marker) &&
            string.Equals(File.ReadAllText(marker).Trim(), key + "\n" + commit,
                StringComparison.OrdinalIgnoreCase))
        {
            progress("Aplikasi sudah menggunakan commit terbaru.");
            return new UpdateCheckResult("current", commit, null);
        }

        progress("Membangun aplikasi dari repo…");
        if (Directory.Exists(stage)) Directory.Delete(stage, true);
        var dotnet = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "dotnet", "dotnet.exe");
        if (!File.Exists(dotnet)) dotnet = "dotnet";
        await RunAsync(dotnet,
            ["publish", Path.Combine(source, ProjectFile), "-c", "Release", "-r", "win-x64",
             "--self-contained", "true", "-p:PublishSingleFile=true",
             "-p:IncludeNativeLibrariesForSelfExtract=true", "-o", stage, "--nologo", "-v:q"],
            source, TimeSpan.FromMinutes(10), cancellationToken);
        var stagedExe = Path.Combine(stage, ExeFile);
        if (!File.Exists(stagedExe) || !File.Exists(Path.Combine(stage, "assets", "index.html")))
            throw new InvalidOperationException("Hasil build tidak berisi aplikasi lengkap.");
        File.WriteAllText(Path.Combine(stage, MarkerFile), key + "\n" + commit + "\n");
        progress("Rilis baru siap dipasang.");
        return new UpdateCheckResult("ready", commit,
            new PreparedUpdate(stagedExe, Path.Combine(appDirectory, ExeFile), commit));
    }

    private static async Task<string> RunAsync(string command, IEnumerable<string> arguments,
        string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(command)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        process.StartInfo.Environment["GCM_INTERACTIVE"] = "never";
        process.StartInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        try { process.Start(); }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"Perintah {Path.GetFileName(command)} tidak dapat dijalankan. Pastikan Git dan SDK .NET terpasang.", ex);
        }
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try { await process.WaitForExitAsync(timeoutSource.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Perintah {Path.GetFileName(command)} terlalu lama atau dibatalkan.");
        }
        var standardOutput = await output;
        var standardError = await error;
        if (process.ExitCode != 0)
        {
            var detail = (standardError + "\n" + standardOutput).Trim();
            if (detail.Length > 1800) detail = detail[^1800..];
            throw new InvalidOperationException($"{Path.GetFileName(command)} gagal: {detail}");
        }
        return standardOutput;
    }
}
