using System.Threading;
using System.Threading.Tasks;
using AzureBackup.Models;

namespace AzureBackup.Interfaces
{
    public interface IConfigLoader
    {
        Task<BackupConfig> LoadAsync(string path, CancellationToken cancellationToken);
    }
}
