using System.IO.Ports;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace PrintToDo;

internal sealed class MainForm : Form
{
    private const string AppOrigin = "https://printtodo.local";
    private readonly AppStore _store = new();
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly SemaphoreSlim _printGate = new(1, 1);
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private PreparedUpdate? _preparedUpdate;

    public MainForm()
    {
        Text = "Print-To-Do";
        MinimumSize = new Size(900, 620);
        Size = new Size(1240, 820);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(_webView);
        Shown += async (_, _) => await InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PrintToDo", "WebView2");
            Directory.CreateDirectory(userData);
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            await _webView.EnsureCoreWebView2Async(environment);
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "printtodo.local", Path.Combine(AppContext.BaseDirectory, "assets"),
                CoreWebView2HostResourceAccessKind.DenyCors);
            _webView.CoreWebView2.NavigationStarting += (_, args) =>
            {
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) ||
                    uri.Scheme != "https" || uri.Host != "printtodo.local")
                    args.Cancel = true;
            };
            _webView.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
            _webView.CoreWebView2.WebMessageReceived += async (_, args) =>
                await HandleMessageAsync(args);
            _webView.CoreWebView2.Navigate(AppOrigin + "/index.html");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "WebView2 tidak dapat dibuka: " + ex.Message,
                "Print-To-Do", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private async Task HandleMessageAsync(CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (args.Source != AppOrigin + "/index.html") return;
        int id = 0;
        try
        {
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            var request = document.RootElement;
            id = request.GetProperty("id").GetInt32();
            var type = request.GetProperty("type").GetString();
            var payload = request.TryGetProperty("payload", out var value) ? value : default;
            object? data;
            switch (type)
            {
                case "getState":
                    data = _store.State;
                    break;
                case "saveList":
                    var list = JsonSerializer.Deserialize<TodoList>(
                        payload.GetProperty("list").GetRawText(), JsonConfig.Options)
                        ?? throw new ArgumentException("Data daftar kosong.");
                    _store.SaveList(list);
                    data = list;
                    break;
                case "deleteList":
                    _store.DeleteList(payload.GetProperty("id").GetString() ?? "");
                    data = null;
                    break;
                case "savePrinter":
                    var printer = JsonSerializer.Deserialize<PrinterSettings>(
                        payload.GetProperty("printer").GetRawText(), JsonConfig.Options)
                        ?? throw new ArgumentException("Profil printer kosong.");
                    _store.SavePrinter(printer);
                    data = _store.State;
                    break;
                case "activatePrinter":
                    _store.ActivatePrinter(payload.GetProperty("id").GetString() ?? "");
                    data = _store.State;
                    break;
                case "deletePrinter":
                    _store.DeletePrinter(payload.GetProperty("id").GetString() ?? "");
                    data = _store.State;
                    break;
                case "listPorts":
                    data = SerialPortDiscovery.List();
                    break;
                case "saveUpdateSettings":
                    var updateSettings = JsonSerializer.Deserialize<UpdateSettings>(
                        payload.GetProperty("settings").GetRawText(), JsonConfig.Options)
                        ?? throw new ArgumentException("Pengaturan update kosong.");
                    _store.SaveUpdateSettings(updateSettings);
                    _preparedUpdate = null;
                    data = _store.State.Update;
                    break;
                case "updateStatus":
                    data = new { installedCommit = UpdateService.GetInstalledCommit(AppContext.BaseDirectory) };
                    break;
                case "checkUpdate":
                    if (!await _updateGate.WaitAsync(0))
                        throw new InvalidOperationException("Update sedang berjalan.");
                    try
                    {
                        _preparedUpdate = null;
                        var result = await UpdateService.PrepareAsync(_store.State.Update,
                            AppContext.BaseDirectory, PostUpdateProgress);
                        _preparedUpdate = result.Prepared;
                        data = new { result.Status, result.Commit };
                    }
                    finally { _updateGate.Release(); }
                    break;
                case "installUpdate":
                    if (_preparedUpdate is null || !File.Exists(_preparedUpdate.StagedExe))
                        throw new InvalidOperationException("Belum ada rilis update yang siap dipasang.");
                    data = "Aplikasi akan dibuka ulang setelah update terpasang.";
                    break;
                case "preview":
                    var previewList = JsonSerializer.Deserialize<TodoList>(
                        payload.GetProperty("list").GetRawText(), JsonConfig.Options)
                        ?? throw new ArgumentException("Data daftar kosong.");
                    using (var bitmap = ReceiptRenderer.Render(previewList, _store.State.Printer))
                        data = ReceiptRenderer.ToDataUrl(bitmap);
                    break;
                case "testPrint":
                    if (!await _printGate.WaitAsync(0))
                        throw new InvalidOperationException("Printer sedang mencetak.");
                    try
                    {
                        using var bitmap = ReceiptRenderer.RenderTest(_store.State.Printer);
                        await Task.Run(() => EscPosPrinter.Print(bitmap, _store.State.Printer));
                    }
                    finally { _printGate.Release(); }
                    data = "Data uji dikirim ke printer.";
                    break;
                case "printList":
                    var listId = payload.GetProperty("id").GetString();
                    var savedList = _store.State.Lists.FirstOrDefault(item => item.Id == listId)
                        ?? throw new InvalidOperationException("Simpan daftar sebelum mencetak.");
                    if (!await _printGate.WaitAsync(0))
                        throw new InvalidOperationException("Printer sedang mencetak.");
                    try
                    {
                        using var bitmap = ReceiptRenderer.Render(savedList, _store.State.Printer);
                        await Task.Run(() => EscPosPrinter.Print(bitmap, _store.State.Printer));
                    }
                    finally { _printGate.Release(); }
                    data = "Data cetak dikirim ke printer.";
                    break;
                default:
                    throw new ArgumentException("Perintah aplikasi tidak dikenal.");
            }
            Reply(id, true, data, null);
            if (type == "installUpdate")
                BeginInvoke(new Action(StartPreparedUpdate));
        }
        catch (Exception ex)
        {
            Reply(id, false, null, ex.Message);
        }
    }

    private void PostUpdateProgress(string message)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(new Action(() =>
        {
            if (_webView.CoreWebView2 is not null && !IsDisposed)
                _webView.CoreWebView2.PostWebMessageAsJson(
                    JsonSerializer.Serialize(new { type = "updateProgress", message }, JsonConfig.Options));
        }));
    }

    private void StartPreparedUpdate()
    {
        if (_preparedUpdate is null) return;
        try
        {
            var process = Process.GetCurrentProcess();
            var info = new ProcessStartInfo(_preparedUpdate.StagedExe)
            {
                WorkingDirectory = Path.GetDirectoryName(_preparedUpdate.StagedExe)!,
                UseShellExecute = false
            };
            info.ArgumentList.Add("--apply-update");
            info.ArgumentList.Add(_preparedUpdate.TargetExe);
            info.ArgumentList.Add(process.Id.ToString());
            Process.Start(info);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Pemasang update tidak dapat dijalankan: " + ex.Message,
                "Print-To-Do", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Reply(int id, bool ok, object? data, string? error)
    {
        if (_webView.CoreWebView2 is null || IsDisposed) return;
        _webView.CoreWebView2.PostWebMessageAsJson(
            JsonSerializer.Serialize(new { id, ok, data, error }, JsonConfig.Options));
    }
}
