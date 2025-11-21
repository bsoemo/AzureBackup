using System.Threading;
using System.Threading.Tasks;
using AzureBackup.Models;

namespace AzureBackup.Interfaces
{
    public interface IBackupService
    {
        Task RunAsync(BackupConfig config, CancellationToken cancellationToken);
    }
}
