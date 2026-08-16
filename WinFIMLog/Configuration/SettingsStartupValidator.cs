using System;
using Microsoft.Extensions.Hosting;

namespace WinFIMLog.Configuration
{
    internal sealed class SettingsStartupValidator : IHostedService
    {
        public SettingsStartupValidator(Settings settings)
        {
            if (!settings.Success)
                throw new ConfigurationValidationException(
                    $"WinFIMLog configuration could not be loaded: {settings.FailureReason ?? "unknown error"}");
        }

        public System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken) =>
            System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken) =>
            System.Threading.Tasks.Task.CompletedTask;
    }
}
