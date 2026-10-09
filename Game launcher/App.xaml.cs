using System.Windows;

namespace Game_launcher
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Schon vor dem Hauptfenster protokollieren: Fehler beim Laden der Einstellungen oder der
            // Oberfläche passieren, bevor MainWindow seine eigenen Fehler-Haken setzt.
            System.AppDomain.CurrentDomain.UnhandledException += (s, args) =>
                Game_launcher.MainWindow.LogError("Programm (Absturz)", args.ExceptionObject as System.Exception);

            DispatcherUnhandledException += (s, args) =>
            {
                if (!Game_launcher.MainWindow.ErrorHooksReady) Game_launcher.MainWindow.LogError("Start", args.Exception);
            };

            base.OnStartup(e);
        }
    }
}
