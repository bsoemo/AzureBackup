using System;
using System.Threading;
using System.Threading.Tasks;
using AzureBackup.Interfaces;
using AzureBackup.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AzureBackup.Services
{
    public sealed class AppRunOptions
    {
        public string ConfigPath { get; init; } = string.Empty;
        public bool DryRunOverride { get; init; } = false;
    }

    public sealed class BackupHostedService : IHostedService
    {
        private readonly IConfigLoader _loader;
        private readonly IBackupService _backup;
        private readonly AppRunOptions _options;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly ILogger<BackupHostedService> _log;

        public BackupHostedService(IConfigLoader loader,
                                   IBackupService backup,
                                   AppRunOptions options,
                                   IHostApplicationLifetime lifetime,
                                   ILogger<BackupHostedService> log)
        {
            _loader = loader;
            _backup = backup;
            _options = options;
            _lifetime = lifetime;
            _log = log;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _lifetime.ApplicationStarted.Register(async () =>
            {
                try
                {
                    var config = await _loader.LoadAsync(_options.ConfigPath, cancellationToken);
                    if (_options.DryRunOverride)
                        config.Default.DryRun = true;

                    await _backup.RunAsync(config, cancellationToken);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Backup run failed");
                }
                finally
                {
                    _lifetime.StopApplication();
                }
            });

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
