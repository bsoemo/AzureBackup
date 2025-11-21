// Program.cs
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AzureBackup.Interfaces;
using AzureBackup.Models;
using AzureBackup.Services;
using Serilog;
using System.Runtime.InteropServices;

namespace AzureBackup
{
    internal static class Program
    {
        public static async Task Main(string[] args)
        {
            // Simple CLI: --config <path> [--dry-run]
            string? configPath = null;
            bool dryRunOverride = args.Any(a => string.Equals(a, "--dry-run", StringComparison.OrdinalIgnoreCase));

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] is "--config" or "-c")
                {
                    if (i + 1 < args.Length)
                    {
                        configPath = args[i + 1];
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(configPath))
            {
                Console.WriteLine("Usage: AzureBackup --config <config.json> [--dry-run]");
                return;
            }

            // Configure Serilog for console + rolling file logs
            var logDir = ResolveLogDirectory();
            Directory.CreateDirectory(logDir);
            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Information()
                .WriteTo.Console(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(Path.Combine(logDir, "backup-.log"),
                              rollingInterval: RollingInterval.Day,
                              retainedFileCountLimit: 30,
                              shared: true,
                              outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            using IHost host = Host.CreateDefaultBuilder(args)
                .ConfigureLogging(log =>
                {
                    log.ClearProviders();
                    log.AddSerilog(Log.Logger, dispose: true);
                })
                .ConfigureServices((ctx, services) =>
                {
                    // DI registrations
                    services.AddSingleton<IConfigLoader, JsonConfigLoader>();
                    services.AddSingleton<IBackupService, BackupOrchestrator>();

                    // Providers
                    services.AddSingleton<AzureBlobStorageProviderFactory>();
                    services.AddSingleton<IStorageProviderFactory>(sp => sp.GetRequiredService<AzureBlobStorageProviderFactory>());

                    // Azure credential (DefaultAzureCredential works on Windows/Linux, incl. Arc-enabled servers via managed identity)
                    services.AddSingleton(new DefaultAzureCredential());

                    // App run options and hosted service
                    services.AddSingleton(new AppRunOptions { ConfigPath = configPath!, DryRunOverride = dryRunOverride });
                    services.AddHostedService<BackupHostedService>();
                })
                .Build();

            await host.RunAsync();
        }

        private static string ResolveLogDirectory()
        {
            var fromEnv = Environment.GetEnvironmentVariable("AZUREBACKUP_LOGDIR");
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv!;

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                    if (!string.IsNullOrWhiteSpace(programData))
                        return Path.Combine(programData, "AzureBackup", "logs");
                }
                else
                {
                    var preferred = "/var/log/azurebackup";
                    // If writable by current user, use it
                    if (HasWriteAccess(preferred)) return preferred;

                    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    if (!string.IsNullOrWhiteSpace(home))
                        return Path.Combine(home, ".local", "state", "azurebackup", "logs");
                }
            }
            catch
            {
                // ignore and fall back
            }

            return Path.Combine(AppContext.BaseDirectory, "logs");
        }

        private static bool HasWriteAccess(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                var test = Path.Combine(path, ".write_test");
                File.WriteAllText(test, "ok");
                File.Delete(test);
                return true;
            }
            catch { return false; }
        }
    }
}
