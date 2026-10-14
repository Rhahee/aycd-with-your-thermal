using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;

namespace PrintToDo;

internal static class ReceiptRenderer
{
    private static readonly CultureInfo Indonesian = new("id-ID");
    private static readonly string[] DayNames =
        ["Senin", "Selasa", "Rabu", "Kamis", "Jumat", "Sabtu", "Minggu"];

    private enum RowKind { Header, Meta, Section, Task, Rule, Footer }
    private sealed record Row(RowKind Kind, string Text, float Top, float Height);

    public static Bitmap Render(TodoList list, PrinterSettings settings)
    {
        AppStore.ValidateList(list);
        AppStore.ValidatePrinter(settings);

        var width = settings.PrintableDots;
        var margin = width <= 400 ? 14 : 18;
        var bodySize = width <= 400 ? 21f : 24f;
        using var titleFont = new Font("Segoe UI", width <= 400 ? 26f : 32f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var bodyFont = new Font("Segoe UI", bodySize, FontStyle.Regular, GraphicsUnit.Pixel);
        using var sectionFont = new Font("Segoe UI", bodySize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var smallFont = new Font("Segoe UI", width <= 400 ? 17f : 19f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var measuringBitmap = new Bitmap(1, 1);
        using var measuring = Graphics.FromImage(measuringBitmap);
        measuring.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var format = new StringFormat(StringFormat.GenericDefault)
        {
            Trimming = StringTrimming.Word,
            FormatFlags = StringFormatFlags.LineLimit
        };

        var rows = new List<Row>();
        float y = 12;
        void Add(RowKind kind, string text, Font font, int textWidth, float gap = 8)
        {
            var measured = measuring.MeasureString(text, font, textWidth, format);
            var height = Math.Max(measured.Height + 4, kind == RowKind.Task ? 30 : 0);
            rows.Add(new Row(kind, text, y, height));
            y += height + gap;
        }

        Add(RowKind.Header, string.IsNullOrWhiteSpace(list.Title)
            ? (list.Kind == "daily" ? "TO-DO HARIAN" : "TO-DO MINGGUAN")
            : list.Title.Trim(), titleFont, width - margin * 2, 5);

        var date = DateOnly.ParseExact(list.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dateLabel = list.Kind == "daily"
            ? date.ToString("dddd, dd MMMM yyyy", Indonesian)
            : $"{date.ToString("dd MMM yyyy", Indonesian)} – {date.AddDays(6).ToString("dd MMM yyyy", Indonesian)}";
        Add(RowKind.Meta, dateLabel, smallFont, width - margin * 2, 14);
        rows.Add(new Row(RowKind.Rule, "", y, 2));
        y += 17;

        var addedTasks = 0;
        if (list.Kind == "daily")
        {
            foreach (var task in list.Tasks.Where(task => !string.IsNullOrWhiteSpace(task.Text)))
            {
                Add(RowKind.Task, task.Text.Trim(), bodyFont, width - margin * 2 - 38, 11);
                addedTasks++;
            }
        }
        else
        {
            AddGroup(-1, "UMUM");
            for (var day = 0; day < 7; day++)
                AddGroup(day, $"{DayNames[day].ToUpperInvariant()} · " +
                    date.AddDays(day).ToString("dd MMM", Indonesian).ToUpperInvariant());
        }

        void AddGroup(int day, string label)
        {
            var tasks = list.Tasks.Where(task => task.Day == day && !string.IsNullOrWhiteSpace(task.Text)).ToList();
            if (tasks.Count == 0) return;
            Add(RowKind.Section, label, sectionFont, width - margin * 2, 5);
            foreach (var task in tasks)
            {
                Add(RowKind.Task, task.Text.Trim(), bodyFont, width - margin * 2 - 38, 11);
                addedTasks++;
            }
            y += 5;
        }

        if (addedTasks == 0)
            Add(RowKind.Meta, "Belum ada tugas.", bodyFont, width - margin * 2, 10);
        y += 4;
        rows.Add(new Row(RowKind.Rule, "", y, 2));
        y += 13;
        Add(RowKind.Footer, "Print-To-Do", smallFont, width - margin * 2, 0);
        var heightPixels = (int)Math.Ceiling(y + 24);
        if (heightPixels > 30000)
            throw new InvalidOperationException("Daftar terlalu panjang untuk satu cetakan.");

        var bitmap = new Bitmap(width, heightPixels, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        foreach (var row in rows)
        {
            var rect = row.Kind == RowKind.Task
                ? new RectangleF(margin + 38, row.Top, width - margin * 2 - 38, row.Height)
                : new RectangleF(margin, row.Top, width - margin * 2, row.Height);
            switch (row.Kind)
            {
                case RowKind.Header:
                    graphics.DrawString(row.Text, titleFont, Brushes.Black, rect, format);
                    break;
                case RowKind.Meta:
                    graphics.DrawString(row.Text, row.Text == "Belum ada tugas." ? bodyFont : smallFont,
                        Brushes.Black, rect, format);
                    break;
                case RowKind.Section:
                    graphics.DrawString(row.Text, sectionFont, Brushes.Black, rect, format);
                    break;
                case RowKind.Task:
                    using (var pen = new Pen(Color.Black, 2))
                        graphics.DrawRectangle(pen, margin + 2, (int)row.Top + 4, 21, 21);
                    graphics.DrawString(row.Text, bodyFont, Brushes.Black, rect, format);
                    break;
                case RowKind.Rule:
                    using (var pen = new Pen(Color.Black, 2))
                        graphics.DrawLine(pen, margin, row.Top, width - margin, row.Top);
                    break;
                case RowKind.Footer:
                    graphics.DrawString(row.Text, smallFont, Brushes.Black, rect, format);
                    break;
            }
        }
        return bitmap;
    }

    public static Bitmap RenderTest(PrinterSettings settings)
    {
        var sample = new TodoList
        {
            Kind = "daily",
            Date = DateTime.Today.ToString("yyyy-MM-dd"),
            Title = "UJI CETAK",
            Tasks =
            [
                new TodoTask { Text = "Kotak centang dan teks harus terlihat jelas." },
                new TodoTask { Text = "Jika tepi kiri atau kanan terpotong, ubah lebar area cetak pada pengaturan printer." }
            ]
        };
        return Render(sample, settings);
    }

    public static string ToDataUrl(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
    }
}
