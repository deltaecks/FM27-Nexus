using System.Windows;
using Velopack;
using Velopack.Windows;

namespace FM27Nexus;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must be the first application code (Velopack install/update/uninstall hooks).
        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ =>
            {
                try
                {
#pragma warning disable CS0618
                    new Shortcuts().CreateShortcutForThisExe(
                        ShortcutLocation.Desktop | ShortcutLocation.StartMenuRoot);
#pragma warning restore CS0618
                }
                catch
                {
                    // Install still succeeds; user can Repair Desktop Shortcut in-app.
                }
            })
            .Run();

        var app = new App();
        app.Run(new MainWindow());
    }
}
