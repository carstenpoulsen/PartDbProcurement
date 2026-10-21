namespace PartDbProcurement;

internal static class Program
{
    /// <summary>Optional argument: path to a .kicad_pro file to preload.</summary>
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
    }
}
