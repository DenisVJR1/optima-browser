using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace OptimaBrowser;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptimaBrowser"));
                File.WriteAllText(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptimaBrowser", "crash.log"),
                    DateTime.Now.ToString("s") + "\n" + args.Exception);
            }
            catch { }
        };
        base.OnStartup(e);
    }
}