using System.Text.Json;

namespace PrintToDo;

internal static class JsonConfig
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}

internal sealed class AppState
{
    public int SchemaVersion { get; set; } = 1;
    public List<TodoList> Lists { get; set; } = [];
    public PrinterSettings Printer { get; set; } = new();
    public List<PrinterSettings> PrinterProfiles { get; set; } = [];
    public UpdateSettings Update { get; set; } = new();
}

internal sealed class UpdateSettings
{
    public string RepositoryUrl { get; set; } = "";
    public string Branch { get; set; } = "";
}

internal sealed class TodoList
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "daily";
    public string Date { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");
    public string Title { get; set; } = "";
    public List<TodoTask> Tasks { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

internal sealed class TodoTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    // -1 is a general weekly task. 0 through 6 are Monday through Sunday.
    public int Day { get; set; } = -1;
    public bool Completed { get; set; }
}

internal sealed class PrinterSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string PortName { get; set; } = "COM6";
    public string Name { get; set; } = "RPP02N (80 mm)";
    public int PaperWidthMm { get; set; } = 80;
    public int PrintableDots { get; set; } = 576;
    public int BaudRate { get; set; } = 115200;
}
