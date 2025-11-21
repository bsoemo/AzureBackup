using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AzureBackup.Models
{
    public sealed class BackupConfig
    {
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("default")]
        public Defaults Default { get; set; } = new Defaults();

        [JsonPropertyName("jobs")]
        public List<BackupJob> Jobs { get; set; } = new();
    }

    public sealed class Defaults
    {
        [JsonPropertyName("concurrency")] public int Concurrency { get; set; } = Environment.ProcessorCount;
        [JsonPropertyName("tier")] public StorageTier Tier { get; set; } = StorageTier.Cool;
        [JsonPropertyName("dryRun")] public bool DryRun { get; set; } = false;
    }

    public sealed class BackupJob
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "job";
        [JsonPropertyName("source")] public SourceSpec Source { get; set; } = new();
        [JsonPropertyName("destination")] public DestinationSpec Destination { get; set; } = new();
        [JsonPropertyName("tier")] public StorageTier? Tier { get; set; } = null; // optional override
    }

    public sealed class SourceSpec
    {
        [JsonPropertyName("paths")] public List<string> Paths { get; set; } = new();
        [JsonPropertyName("include")] public List<string> Include { get; set; } = new() { "**/*" };
        [JsonPropertyName("exclude")] public List<string> Exclude { get; set; } = new();
        [JsonPropertyName("followSymlinks")] public bool FollowSymlinks { get; set; } = false;
    }

    public sealed class DestinationSpec
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "AzureBlob";
        [JsonPropertyName("azureBlob")] public AzureBlobDestination? AzureBlob { get; set; }
    }

    public sealed class AzureBlobDestination
    {
        [JsonPropertyName("serviceUri")] public string ServiceUri { get; set; } = string.Empty; // https://account.blob.core.windows.net/
        [JsonPropertyName("container")] public string Container { get; set; } = string.Empty;
        [JsonPropertyName("prefix")] public string? Prefix { get; set; } = null; // optional virtual folder/prefix
        [JsonPropertyName("tier")] public StorageTier? Tier { get; set; } = null; // optional override at destination
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum StorageTier
    {
        Hot,
        Cool,
        Archive
    }
}
