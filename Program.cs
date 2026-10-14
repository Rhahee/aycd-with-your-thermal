namespace PrintToDo;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (UpdateApplier.TryRun(args)) return;
        try
        {
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            MessageBox.Show("Aplikasi tidak dapat dibuka: " + ex.Message,
                "AYCD Printer", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
