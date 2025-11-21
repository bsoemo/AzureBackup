# AzureBackup

Cross-platform, CLI-first backup tool targeting Azure Blob Storage with support for Cool and Archive tiers. Designed with DI and clean abstractions so a GUI can be added later.

Highlights

- Windows and Linux support (net8.0)
- JSON-driven configuration (pass with --config)
- Uses Azure AD auth via DefaultAzureCredential (works with Azure Arc managed identity)
- Cost-optimized storage: upload directly to Cool or Archive tiers
- Skips unchanged files using SHA-256 metadata
- Include/exclude patterns via globbing

Quick start

1) Create a container on your storage account (or let the app create it).

2) Ensure the machine identity has Blob Data Contributor on the container/account.
   - Azure Arc: use managed identity on the Arc-enabled server.

3) Prepare a config file (see sample at `AzureBackup/sample.config.json`).

Run

```pwsh
dotnet run --project AzureBackup/AzureBackup.csproj -- --config AzureBackup/sample.config.json
```

Config schema (v1)

- default
  - concurrency: int (default: CPU count)
  - tier: Hot|Cool|Archive (default: Cool)
  - dryRun: bool

- jobs[]
  - name: string
  - source
    - paths: string[] (folders)
    - include: string[] (globs)
    - exclude: string[] (globs)
  - tier: optional per-job override
  - destination
    - type: "AzureBlob"
    - azureBlob
  - serviceUri: `https://ACCOUNT_NAME.blob.core.windows.net/`
    - container: string
    - prefix: optional path prefix, supports {yyyy}/{MM}/{dd}

Security

- Auth uses DefaultAzureCredential. In Arc-enabled servers, the managed identity flows automatically.
- Least privilege: assign Blob Data Contributor.

Notes on Archive tier

- New uploads can be set to Archive immediately.
- Overwriting an archived blob triggers rehydration back to Hot before upload.
- Restores/downloads from Archive require rehydration time (minutes to hours depending on priority). Restore workflows will be added next.

License

- MIT (planned). Contributions welcome.
