using System.Diagnostics;

namespace PrintToDo;

internal static class UpdateApplier
{
    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] != "--apply-update") return false;
        try
        {
            if (args.Length != 3 || !int.TryParse(args[2], out var oldProcessId))
                throw new ArgumentException("Parameter pembaruan tidak valid.");
            var targetExe = Path.GetFullPath(args[1]);
            if (!string.Equals(Path.GetFileName(targetExe), "PrintToDo.exe",
                    StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Lokasi aplikasi target tidak valid.");
            var stageDirectory = Path.GetFullPath(AppContext.BaseDirectory);
            var targetDirectory = Path.GetDirectoryName(targetExe)
                ?? throw new ArgumentException("Folder aplikasi target tidak valid.");
            if (string.Equals(stageDirectory.TrimEnd(Path.DirectorySeparatorChar),
                    targetDirectory.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Folder update dan aplikasi target harus berbeda.");
            if (!File.Exists(Path.Combine(stageDirectory, "assets", "index.html")) ||
                !File.Exists(Path.Combine(targetDirectory, "assets", "index.html")))
                throw new InvalidOperationException("Folder aplikasi atau paket update tidak lengkap.");

            try
            {
                using var oldProcess = Process.GetProcessById(oldProcessId);
                if (!oldProcess.WaitForExit(45000))
                    throw new TimeoutException("Aplikasi lama belum tertutup dalam 45 detik.");
            }
            catch (ArgumentException) { } // The old process has already exited.

            ApplyFiles(stageDirectory, targetExe);
            Process.Start(new ProcessStartInfo(targetExe)
            {
                WorkingDirectory = targetDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Update gagal dipasang: " + ex.Message +
                "\n\nVersi sebelumnya tersimpan sebagai PrintToDo.exe.previous bila penggantian sudah dimulai.",
                "Print-To-Do", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        return true;
    }

    internal static void ApplyFiles(string stageDirectory, string targetExe)
    {
        var targetDirectory = Path.GetDirectoryName(targetExe)!;
        var currentAssets = Path.Combine(targetDirectory, "assets");
        var incomingAssets = Path.Combine(targetDirectory, "assets.incoming");
        var previousAssets = Path.Combine(targetDirectory, "assets.previous");
        var incomingExe = targetExe + ".incoming";
        var previousExe = targetExe + ".previous";
        if (Directory.Exists(incomingAssets)) Directory.Delete(incomingAssets, true);
        CopyDirectory(Path.Combine(stageDirectory, "assets"), incomingAssets);
        File.Copy(Path.Combine(stageDirectory, "PrintToDo.exe"), incomingExe, true);

        var oldAssetsMoved = false;
        var exeSwapped = false;
        try
        {
            if (Directory.Exists(previousAssets)) Directory.Delete(previousAssets, true);
            if (Directory.Exists(currentAssets))
            {
                Directory.Move(currentAssets, previousAssets);
                oldAssetsMoved = true;
            }
            Directory.Move(incomingAssets, currentAssets);
            if (File.Exists(targetExe))
            {
                if (File.Exists(previousExe)) File.Delete(previousExe);
                File.Replace(incomingExe, targetExe, previousExe, true);
            }
            else
                File.Move(incomingExe, targetExe);
            exeSwapped = true;
            var stageMarker = Path.Combine(stageDirectory, "update-commit.txt");
            if (File.Exists(stageMarker))
            {
                try { File.Copy(stageMarker, Path.Combine(targetDirectory, "update-commit.txt"), true); }
                catch (IOException) { } // A missing marker only causes an extra build next time.
            }
        }
        catch
        {
            if (!exeSwapped && oldAssetsMoved && Directory.Exists(previousAssets))
            {
                if (Directory.Exists(currentAssets)) Directory.Delete(currentAssets, true);
                Directory.Move(previousAssets, currentAssets);
            }
            throw;
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
