using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AzureBackup.Models;

namespace AzureBackup.Interfaces
{
    public record StoredObjectInfo(string Path, string? Sha256, string? AccessTier);

    public sealed class StorageUploadOptions
    {
        public StorageTier Tier { get; init; } = StorageTier.Cool;
        public string? Sha256 { get; init; }
        public bool Overwrite { get; init; } = true;
        public string? ContentType { get; init; }
    }

    public interface IStorageProvider
    {
        Task EnsureReadyAsync(CancellationToken cancellationToken);
        Task<StoredObjectInfo?> TryGetInfoAsync(string destinationPath, CancellationToken cancellationToken);
        Task UploadAsync(string destinationPath, Stream content, StorageUploadOptions options, CancellationToken cancellationToken);
    }

    public interface IStorageProviderFactory
    {
        IStorageProvider Create(DestinationSpec destination);
    }
}
