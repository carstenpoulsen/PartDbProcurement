namespace PartDbProcurement;

/// <summary>Writes technical details of unexpected errors to %AppData%\PartDbProcurement\error.log.</summary>
internal static class ErrorLog
{
    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PartDbProcurement", "error.log");

    /// <returns>The log path, or null if the log could not be written.</returns>
    public static string? Write(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            // Keep the log small: start over when it exceeds 1 MB.
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 1_000_000) File.Delete(FilePath);
            var version = typeof(ErrorLog).Assembly.GetName().Version;
            File.AppendAllText(FilePath, $"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss}  v{version}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            return FilePath;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Shows a plain message instead of the .NET exception dialog.</summary>
    public static void Show(Exception ex, IWin32Window? owner = null)
    {
        var log = Write(ex);
        var msg = $"Something went wrong:\n{ex.Message}\n\n" +
                  (log != null ? $"Technical details were written to:\n{log}\n\n" : "") +
                  "You can continue working. If it happens again, please report it with the log file.";
        MessageBox.Show(owner, msg, "Part-DB Procurement", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
