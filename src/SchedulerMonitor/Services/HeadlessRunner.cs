using SchedulerMonitor.Infrastructure;

namespace SchedulerMonitor.Services;

internal static class HeadlessRunner
{
    public static async Task<int> RunAsync(AppPaths paths, ConfigStore store, FileLogger logger)
    {
        try
        {
            var config = store.Load();
            if (!config.MonitoredTasks.Any(task => task.Enabled))
                throw new InvalidOperationException("No monitored tasks are selected.");
            logger.ApplyRetention(config.Monitoring.LogRetentionDays, config.Monitoring.ReportRetentionDays);
            var query = new RemoteTaskQuery(logger);
            var monitor = new MonitoringService(query, logger);
            var run = await monitor.RunAsync(config);
            var report = new ReportBuilder(paths).BuildAndSave(run);
            logger.Info($"Report saved: {Path.GetFileName(report.FilePath)}");

            // Long running alerts go out on their own, whether or not the daily report is enabled.
            var alerts = new AlertService(new EmailService(logger), new AlertStateStore(paths, logger), logger);
            var sent = await alerts.SendAsync(config, run);
            if (sent > 0) logger.Info($"{sent} long running alert(s) sent");

            if (config.Email.Enabled)
                await new EmailService(logger).SendAsync(config.Email, report.Subject, report.Html);
            else
                logger.Info("Email sending is disabled");

            return run.Tasks.Any(task => task.Status is Models.MonitorStatus.Failed or Models.MonitorStatus.Unreachable or Models.MonitorStatus.LongRunning or Models.MonitorStatus.Abnormal) ? 2 : 0;
        }
        catch (Exception ex)
        {
            logger.Error("Headless monitoring failed", ex);
            return 1;
        }
    }
}
