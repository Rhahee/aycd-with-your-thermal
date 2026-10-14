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
    public const string DefaultRepositoryUrl = "https://github.com/Rhahee/aycd-with-your-thermal.git";
    public const string DefaultBranch = "main";

    public string RepositoryUrl { get; set; } = DefaultRepositoryUrl;
    public string Branch { get; set; } = DefaultBranch;

    public void ApplyDefaults()
    {
        RepositoryUrl = RepositoryUrl?.Trim() ?? "";
        Branch = Branch?.Trim() ?? "";
        if (RepositoryUrl.Length == 0)
        {
            RepositoryUrl = DefaultRepositoryUrl;
            Branch = DefaultBranch;
        }
    }
}

internal sealed class TodoList
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "daily";
    public string Date { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");
    public string Title { get; set; } = "";
    public List<TodoSection> Sections { get; set; } = [];
    public List<TodoTask> Tasks { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

internal sealed class TodoSection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
}

internal sealed class TodoTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    // Existing tasks without this field remain checklist blocks.
    public string Type { get; set; } = "checklist";
    public string Text { get; set; } = "";
    public string SectionId { get; set; } = "";
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
