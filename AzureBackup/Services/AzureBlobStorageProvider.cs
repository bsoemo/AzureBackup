using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using AzureBackup.Interfaces;
using AzureBackup.Models;

namespace AzureBackup.Services
{
    public sealed class AzureBlobStorageProviderFactory : IStorageProviderFactory
    {
        private readonly DefaultAzureCredential _credential;

        public AzureBlobStorageProviderFactory(DefaultAzureCredential credential)
        {
            _credential = credential;
        }

        public IStorageProvider Create(DestinationSpec destination)
        {
            if (!string.Equals(destination.Type, "AzureBlob", StringComparison.OrdinalIgnoreCase) || destination.AzureBlob is null)
                throw new ArgumentException("Destination type must be AzureBlob with configuration");

            var uri = new Uri(destination.AzureBlob.ServiceUri);
            var container = destination.AzureBlob.Container;
            return new AzureBlobStorageProvider(new BlobServiceClient(uri, _credential), container);
        }
    }

    internal sealed class AzureBlobStorageProvider : IStorageProvider
    {
        private readonly BlobServiceClient _serviceClient;
        private readonly BlobContainerClient _container;

        public AzureBlobStorageProvider(BlobServiceClient serviceClient, string container)
        {
            _serviceClient = serviceClient;
            _container = _serviceClient.GetBlobContainerClient(container);
        }

        public async Task EnsureReadyAsync(CancellationToken cancellationToken)
        {
            await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        }

        public async Task<StoredObjectInfo?> TryGetInfoAsync(string destinationPath, CancellationToken cancellationToken)
        {
            var blob = _container.GetBlobClient(destinationPath);
            try
            {
                var props = await blob.GetPropertiesAsync(cancellationToken: cancellationToken);
                props.Value.Metadata.TryGetValue("sha256", out var sha);
                return new StoredObjectInfo(destinationPath, sha, props.Value.AccessTier);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task UploadAsync(string destinationPath, Stream content, StorageUploadOptions options, CancellationToken cancellationToken)
        {
            var blob = _container.GetBlobClient(destinationPath);

            // If existing blob is in Archive and content differs, rehydrate before overwrite
            try
            {
                var props = await blob.GetPropertiesAsync(cancellationToken: cancellationToken);
                if (string.Equals(props.Value.AccessTier, AccessTier.Archive.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    await blob.SetAccessTierAsync(AccessTier.Hot, cancellationToken: cancellationToken);
                    // wait for rehydration to complete
                    while (true)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                        props = await blob.GetPropertiesAsync(cancellationToken: cancellationToken);
                        if (string.IsNullOrEmpty(props.Value.ArchiveStatus)) break;
                    }
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // doesn't exist, fine
            }

            var md = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(options.Sha256)) md["sha256"] = options.Sha256!;

            var uploadOptions = new BlobUploadOptions
            {
                AccessTier = MapTier(options.Tier),
                Metadata = md
            };

            // best-effort content-type
            if (!string.IsNullOrWhiteSpace(options.ContentType))
            {
                uploadOptions.HttpHeaders = new BlobHttpHeaders { ContentType = options.ContentType };
            }

            await blob.UploadAsync(content, uploadOptions, cancellationToken);
        }

        private static AccessTier MapTier(StorageTier tier) => tier switch
        {
            StorageTier.Hot => AccessTier.Hot,
            StorageTier.Cool => AccessTier.Cool,
            StorageTier.Archive => AccessTier.Archive,
            _ => AccessTier.Cool
        };
    }
}
