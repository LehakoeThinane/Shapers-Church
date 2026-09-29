using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shapers.Media.Application;

namespace Shapers.Media.Infrastructure;

public sealed class StorageOptions
{
    public const string SectionName = "Media:Storage";

    /// <summary>"Local" (development) or "Azure".</summary>
    public string Provider { get; set; } = "Local";

    /// <summary>Local: folder for files, relative to the API's content root.</summary>
    public string LocalPath { get; set; } = "App_Data/media";

    /// <summary>Azure: storage account connection string (needs the account key to sign upload URLs).</summary>
    public string? ConnectionString { get; set; }

    public string Container { get; set; } = "media";

    /// <summary>Public base URL for files, e.g. the Cloudflare CDN hostname. Defaults to the storage or API address.</summary>
    public string? PublicBaseUrl { get; set; }
}

/// <summary>Azure Blob Storage. Uploads use short-lived write-only SAS URLs; published files are served through the CDN.</summary>
internal sealed class AzureBlobFileStorage : IFileStorage
{
    private readonly BlobContainerClient _container;
    private readonly string? _publicBaseUrl;

    public AzureBlobFileStorage(IOptions<StorageOptions> options)
    {
        var o = options.Value;
        _container = new BlobServiceClient(o.ConnectionString ?? throw new InvalidOperationException("Media:Storage:ConnectionString is required for Azure storage."))
            .GetBlobContainerClient(o.Container);
        _publicBaseUrl = o.PublicBaseUrl?.TrimEnd('/');
    }

    public UploadTarget CreateUpload(string key, string contentType, long sizeBytes, TimeSpan lifetime)
    {
        var expires = DateTimeOffset.UtcNow + lifetime;
        var sas = _container.GetBlobClient(key).GenerateSasUri(BlobSasPermissions.Create | BlobSasPermissions.Write, expires);
        return new UploadTarget(
            sas.ToString(),
            "PUT",
            new Dictionary<string, string> { ["x-ms-blob-type"] = "BlockBlob", ["Content-Type"] = contentType },
            expires);
    }

    public async Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var properties = await _container.GetBlobClient(key).GetPropertiesAsync(cancellationToken: cancellationToken);
            return properties.Value.ContentLength;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public string PublicUrl(string key) =>
        _publicBaseUrl is null ? _container.GetBlobClient(key).Uri.ToString() : $"{_publicBaseUrl}/{key}";

    public Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        _container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: cancellationToken);
}

/// <summary>
/// Development storage on the local disk. Mimics the Azure flow: a signed, expiring upload URL
/// (handled by <see cref="LocalStorageEndpoints"/>) and a public URL for reading.
/// </summary>
public sealed class LocalFileStorage(IOptions<StorageOptions> options, IHostEnvironment environment, IHttpContextAccessor http) : IFileStorage
{
    // A per-process key is enough: upload URLs only live for minutes.
    private static readonly byte[] SigningKey = RandomNumberGenerator.GetBytes(32);

    public string Root => Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.LocalPath));

    public UploadTarget CreateUpload(string key, string contentType, long sizeBytes, TimeSpan lifetime)
    {
        var expires = DateTimeOffset.UtcNow + lifetime;
        var expiresUnix = expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var size = sizeBytes.ToString(CultureInfo.InvariantCulture);
        var query = $"key={Uri.EscapeDataString(key)}&size={size}&expires={expiresUnix}&sig={Sign(key, size, expiresUnix)}";
        // The CSRF header lets a signed-in admin browser upload to the API origin; Azure uploads go to another origin and need none.
        return new UploadTarget(
            $"{BaseUrl()}/api/media/local-upload?{query}",
            "PUT",
            new Dictionary<string, string> { ["Content-Type"] = contentType, ["X-Shapers-CSRF"] = "1" },
            expires);
    }

    public Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken)
    {
        var file = new FileInfo(PathFor(key));
        return Task.FromResult<long?>(file.Exists ? file.Length : null);
    }

    public string PublicUrl(string key) => $"{BaseUrl()}/media-files/{key}";

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        File.Delete(PathFor(key));
        return Task.CompletedTask;
    }

    /// <summary>Resolves a storage key to a file inside the storage root, refusing anything that escapes it.</summary>
    public string PathFor(string key)
    {
        var full = Path.GetFullPath(Path.Combine(Root, key));
        return full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? full
            : throw new InvalidOperationException("Invalid storage key.");
    }

    public static bool Verify(string key, string size, string expires, string signature) =>
        long.TryParse(expires, CultureInfo.InvariantCulture, out var unix)
        && DateTimeOffset.FromUnixTimeSeconds(unix) > DateTimeOffset.UtcNow
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(key, size, expires)), Encoding.ASCII.GetBytes(signature));

    private static string Sign(string key, string size, string expires) =>
        Convert.ToHexString(HMACSHA256.HashData(SigningKey, Encoding.UTF8.GetBytes($"{key}|{size}|{expires}")));

    private string BaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(options.Value.PublicBaseUrl))
        {
            return options.Value.PublicBaseUrl.TrimEnd('/');
        }

        var request = http.HttpContext?.Request ?? throw new InvalidOperationException("Set Media:Storage:PublicBaseUrl to build file URLs outside a request.");
        return $"{request.Scheme}://{request.Host}{request.PathBase}";
    }
}
