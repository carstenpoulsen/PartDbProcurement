namespace PartDbProcurement;

internal static class Program
{
    /// <summary>Optional arguments: one or more .kicad_pro files to preload as one purchase.</summary>
    [STAThread]
    private static void Main(string[] args)
    {
        // Unexpected errors: plain message + log file instead of the .NET exception dialog.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ErrorLog.Show(e.Exception, Form.ActiveForm);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) ErrorLog.Show(ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) => { ErrorLog.Write(e.Exception); e.SetObserved(); };

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm(args));
    }
}
