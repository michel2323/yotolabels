using Microsoft.UI.Xaml;

namespace YotoLabels;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => WriteCrashLog(e.Exception, e.Message);
        if (Environment.GetEnvironmentVariable("YOTOLABELS_TRACE") == "1")
            AppDomain.CurrentDomain.FirstChanceException += (_, e) => WriteCrashLog(e.Exception, "first chance");
    }

    /// <summary>Folder for logs, next to the per-session working folders.</summary>
    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YotoLabels");

    private static void WriteCrashLog(Exception ex, string message)
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            File.AppendAllText(Path.Combine(DataFolder, "crash.log"),
                $"{DateTime.Now:O}\n{message}\n{ex}\n\n");
        }
        catch { /* never throw from the crash handler */ }
    }

    public static MainWindow? MainWindow { get; private set; }

    public static IntPtr WindowHandle { get; private set; }

    /// <summary>A .yotolabels file passed on the command line, for example by double-clicking it.</summary>
    public static string? StartupFile { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        StartupFile = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(a =>
            a.EndsWith(Services.ProjectService.FileExtension, StringComparison.OrdinalIgnoreCase));

        MainWindow = new MainWindow();
        WindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);
        MainWindow.Activate();
    }
}
