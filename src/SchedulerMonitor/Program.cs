using SchedulerMonitor.Infrastructure;
using SchedulerMonitor.Services;
using SchedulerMonitor.UI;

namespace SchedulerMonitor;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var paths = new AppPaths(AppContext.BaseDirectory);
        var logger = new FileLogger(paths);
        var store = new ConfigStore(paths, logger);

        Application.ThreadException += (_, eventArgs) =>
        {
            logger.Error("Unhandled UI error", eventArgs.Exception);
            MessageBox.Show(eventArgs.Exception.Message, "Scheduler Monitor",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            logger.Error("Unhandled application error", eventArgs.ExceptionObject as Exception);

        if (args.Any(a => a.Equals("--run", StringComparison.OrdinalIgnoreCase)))
        {
            return HeadlessRunner.RunAsync(paths, store, logger).GetAwaiter().GetResult();
        }

        Application.Run(new MainForm(paths, store, logger));
        return 0;
    }
}
