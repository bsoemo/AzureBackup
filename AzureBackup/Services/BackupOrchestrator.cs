using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AzureBackup.Interfaces;
using AzureBackup.Models;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Extensions.Logging;

namespace AzureBackup.Services
{
    public sealed class BackupOrchestrator : IBackupService
    {
        private readonly IStorageProviderFactory _factory;
        private readonly ILogger<BackupOrchestrator> _log;

        public BackupOrchestrator(IStorageProviderFactory factory, ILogger<BackupOrchestrator> log)
        {
            _factory = factory;
            _log = log;
        }

        public async Task RunAsync(BackupConfig config, CancellationToken cancellationToken)
        {
            if (config.Jobs.Count == 0)
            {
                _log.LogWarning("No jobs found in config.");
                return;
            }

            foreach (var job in config.Jobs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _log.LogInformation("Starting job: {Name}", job.Name);

                var provider = _factory.Create(job.Destination);
                await provider.EnsureReadyAsync(cancellationToken);

                var tier = job.Tier ?? job.Destination.AzureBlob?.Tier ?? config.Default.Tier;
                var isDryRun = config.Default.DryRun;

                var files = EnumerateFiles(job.Source);
                _log.LogInformation("Discovered {Count} files", files.Count);
                if (files.Count == 0)
                {
                    _log.LogWarning("No files matched include/exclude patterns for job {Name}", job.Name);
                }

                using var semaphore = new SemaphoreSlim(Math.Max(1, config.Default.Concurrency));
                var tasks = new List<Task>();
                int uploaded = 0, skipped = 0, errors = 0, dryRuns = 0;

                foreach (var (root, fullPath, rel) in files)
                {
                    await semaphore.WaitAsync(cancellationToken);
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var destPath = BuildDestinationPath(job.Destination, rel);

                            var info = await provider.TryGetInfoAsync(destPath, cancellationToken);
                            var sha256 = Hashing.ComputeSha256Hex(fullPath);

                            if (info?.Sha256 != null && string.Equals(info.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
                            {
                                _log.LogInformation("Skip unchanged: {Path} (sha256={Sha})", rel, sha256);
                                System.Threading.Interlocked.Increment(ref skipped);
                                return;
                            }

                            if (isDryRun)
                            {
                                _log.LogInformation("[DRY-RUN] Would upload {Rel} -> {Dest} (tier={Tier})", rel, destPath, tier);
                                System.Threading.Interlocked.Increment(ref dryRuns);
                                return;
                            }

                            await using var fs = File.OpenRead(fullPath);
                            await provider.UploadAsync(destPath, fs, new StorageUploadOptions
                            {
                                Tier = tier,
                                Sha256 = sha256,
                                Overwrite = true
                            }, cancellationToken);

                            _log.LogInformation("Uploaded: {Rel} -> {Dest} (tier={Tier})", rel, destPath, tier);
                            System.Threading.Interlocked.Increment(ref uploaded);
                        }
                        catch (Exception ex)
                        {
                            _log.LogError(ex, "Error processing {File}", rel);
                            System.Threading.Interlocked.Increment(ref errors);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, cancellationToken));
                }

                await Task.WhenAll(tasks);
                _log.LogInformation("Job finished: {Name}. Uploaded={Uploaded}, Skipped={Skipped}, DryRuns={DryRuns}, Errors={Errors}",
                    job.Name, uploaded, skipped, dryRuns, errors);
            }
        }

        private static string BuildDestinationPath(DestinationSpec destination, string relativePath)
        {
            string prefix = destination.AzureBlob?.Prefix ?? string.Empty;
            prefix = FormatPrefix(prefix);

            var dest = string.IsNullOrEmpty(prefix) ? relativePath : prefix.TrimEnd('/') + "/" + relativePath;
            // Normalize to forward slashes for blob paths
            return dest.Replace('\\', '/');
        }

        private static string FormatPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return string.Empty;
            var now = DateTime.UtcNow;
            return prefix.Replace("{yyyy}", now.ToString("yyyy"))
                         .Replace("{MM}", now.ToString("MM"))
                         .Replace("{dd}", now.ToString("dd"))
                         .Replace("{HH}", now.ToString("HH"));
        }

        private static List<(string Root, string FullPath, string Relative)> EnumerateFiles(SourceSpec source)
        {
            var results = new List<(string, string, string)>();
            var include = source.Include.Count == 0 ? new List<string> { "**/*" } : source.Include;
            foreach (var root in source.Paths)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                var dirInfo = new DirectoryInfo(root);
                var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
                matcher.AddIncludePatterns(include);
                if (source.Exclude.Count > 0)
                    matcher.AddExcludePatterns(source.Exclude);

                var matches = matcher.Execute(new DirectoryInfoWrapper(dirInfo));
                foreach (var file in matches.Files)
                {
                    var rel = file.Path;
                    var full = Path.Combine(root, rel);
                    if (File.Exists(full))
                    {
                        results.Add((root, full, rel.Replace('\\', '/')));
                    }
                }
            }
            return results;
        }
    }

    internal static class Hashing
    {
        public static string ComputeSha256Hex(string path)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var fs = File.OpenRead(path);
            var hash = sha.ComputeHash(fs);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
