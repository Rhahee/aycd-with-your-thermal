using System.Globalization;
using System.Text.Json;

namespace PrintToDo;

internal sealed class AppStore
{
    private readonly string _path;
    public AppState State { get; private set; }

    public AppStore(string? dataDirectory = null)
    {
        var directory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintToDo");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "state.json");
        State = File.Exists(_path)
            ? JsonSerializer.Deserialize<AppState>(File.ReadAllText(_path), JsonConfig.Options)
                ?? throw new InvalidDataException("Data aplikasi tidak dapat dibaca.")
            : new AppState();
        if (State.SchemaVersion != 1)
            throw new InvalidDataException("Versi data aplikasi belum didukung.");
        State.Lists ??= [];
        State.Printer ??= new PrinterSettings();
        State.PrinterProfiles ??= [];
        State.Update ??= new UpdateSettings();
        State.Update.ApplyDefaults();
        if (!State.PrinterProfiles.Any(profile => profile.Id == State.Printer.Id))
            State.PrinterProfiles.Add(State.Printer);
    }

    public void SaveList(TodoList list)
    {
        ValidateList(list);
        var index = State.Lists.FindIndex(item => item.Id == list.Id);
        if (index >= 0)
        {
            list.CreatedAt = State.Lists[index].CreatedAt;
            list.UpdatedAt = DateTimeOffset.Now;
            State.Lists[index] = list;
        }
        else
        {
            list.CreatedAt = DateTimeOffset.Now;
            list.UpdatedAt = list.CreatedAt;
            State.Lists.Add(list);
        }
        Persist();
    }

    public void DeleteList(string id)
    {
        if (State.Lists.RemoveAll(item => item.Id == id) == 0)
            throw new InvalidOperationException("Daftar tidak ditemukan.");
        Persist();
    }

    public void SavePrinter(PrinterSettings settings)
    {
        ValidatePrinter(settings);
        var index = State.PrinterProfiles.FindIndex(profile => profile.Id == settings.Id);
        if (index >= 0) State.PrinterProfiles[index] = settings;
        else State.PrinterProfiles.Add(settings);
        State.Printer = settings;
        Persist();
    }

    public void ActivatePrinter(string id)
    {
        State.Printer = State.PrinterProfiles.FirstOrDefault(profile => profile.Id == id)
            ?? throw new InvalidOperationException("Profil printer tidak ditemukan.");
        Persist();
    }

    public void DeletePrinter(string id)
    {
        if (State.PrinterProfiles.Count <= 1)
            throw new InvalidOperationException("Setidaknya satu profil printer harus ada.");
        if (State.PrinterProfiles.RemoveAll(profile => profile.Id == id) == 0)
            throw new InvalidOperationException("Profil printer tidak ditemukan.");
        if (State.Printer.Id == id) State.Printer = State.PrinterProfiles[0];
        Persist();
    }

    public void SaveUpdateSettings(UpdateSettings settings)
    {
        settings.ApplyDefaults();
        UpdateService.ValidateSettings(settings);
        State.Update = settings;
        Persist();
    }

    public static void ValidateList(TodoList list)
    {
        if (list.Id.Length is < 1 or > 64 || !Guid.TryParse(list.Id, out _))
            throw new ArgumentException("ID daftar tidak valid.");
        if (list.Kind is not ("daily" or "weekly"))
            throw new ArgumentException("Jenis daftar tidak valid.");
        if (!DateOnly.TryParseExact(list.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            throw new ArgumentException("Tanggal tidak valid.");
        if (list.Kind == "weekly" && date.DayOfWeek != DayOfWeek.Monday)
            throw new ArgumentException("Tanggal daftar mingguan harus hari Senin.");
        if (list.Title is null || list.Title.Length > 100 || list.Tasks is null || list.Tasks.Count > 200 ||
            list.Sections is null || list.Sections.Count > 50)
            throw new ArgumentException("Daftar terlalu panjang.");
        var sectionIds = new HashSet<string>();
        foreach (var section in list.Sections)
        {
            if (section?.Id is null || !Guid.TryParse(section.Id, out _) ||
                !sectionIds.Add(section.Id) || section.Title is null || section.Title.Length > 100)
                throw new ArgumentException("Ada section yang tidak valid.");
        }
        var taskIds = new HashSet<string>();
        foreach (var task in list.Tasks)
        {
            if (task?.Id is null || task.Id.Length is < 1 or > 64 || !taskIds.Add(task.Id) ||
                task.Type is not ("checklist" or "heading" or "note") ||
                task.Text is null || task.Text.Length > 500 || task.Day is < -1 or > 6 ||
                task.SectionId is null ||
                (list.Sections.Count > 0 && !sectionIds.Contains(task.SectionId)) ||
                (list.Kind == "daily" && task.Day != -1))
                throw new ArgumentException("Ada tugas yang tidak valid.");
        }
    }

    public static void ValidatePrinter(PrinterSettings settings)
    {
        if (settings.Id.Length is < 1 or > 64 || !Guid.TryParse(settings.Id, out _))
            throw new ArgumentException("ID profil printer tidak valid.");
        if (settings.PortName.Length > 0 &&
            !System.Text.RegularExpressions.Regex.IsMatch(settings.PortName, @"^COM\d{1,3}$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new ArgumentException("Nama port harus berbentuk COM3, COM4, dan seterusnya.");
        if (settings.Name.Length > 80 || settings.PaperWidthMm is not (58 or 80) ||
            settings.PrintableDots is < 256 or > 832 || settings.PrintableDots % 8 != 0 ||
            settings.BaudRate is not (9600 or 19200 or 38400 or 57600 or 115200 or 460800))
            throw new ArgumentException("Profil printer tidak valid.");
    }

    private void Persist()
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(State, JsonConfig.Options));
        File.Move(temp, _path, true);
    }
}
