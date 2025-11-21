using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AzureBackup.Interfaces;
using AzureBackup.Models;

namespace AzureBackup.Services
{
    public sealed class JsonConfigLoader : IConfigLoader
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        public async Task<BackupConfig> LoadAsync(string path, CancellationToken cancellationToken)
        {
            await using var fs = File.OpenRead(path);
            var config = await JsonSerializer.DeserializeAsync<BackupConfig>(fs, Options, cancellationToken)
                         ?? new BackupConfig();
            return config;
        }
    }
}
