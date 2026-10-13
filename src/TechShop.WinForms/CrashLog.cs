using System.Globalization;
using System.Text;

namespace TechShop.WinForms;

/// <summary>
/// Last-resort logging. A till that dies silently at the counter leaves the shopkeeper
/// with nothing to report, so anything that escapes the UI thread or the app domain is
/// written next to the database before the process goes.
/// </summary>
public static class CrashLog
{
    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TechShop",
        "crash.log");

    public static void Install()
    {
        WinFormsApp.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        WinFormsApp.ThreadException += (_, e) =>
        {
            Write("UI thread", e.Exception);
            Report(e.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Write("App domain", ex);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("Background task", e.Exception);
            e.SetObserved();
        };
    }

    public static void Write(string source, Exception ex)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var entry = new StringBuilder()
                .AppendLine(CultureInfo.InvariantCulture, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}")
                .AppendLine(ex.ToString())
                .AppendLine(new string('-', 72))
                .ToString();

            File.AppendAllText(Path, entry);
        }
        catch (Exception writeFailure) when (writeFailure is IOException or UnauthorizedAccessException)
        {
            // There is nowhere left to report to; losing the log must not mask the
            // original fault.
        }
    }

    private static void Report(Exception ex)
        => MessageBox.Show(
            $"TechShop hit an unexpected error.\n\n{ex.Message}\n\nDetails were written to:\n{Path}",
            "TechShop",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
}
