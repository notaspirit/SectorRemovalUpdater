using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SectorRemovalUpdater.Services;

/// <summary>A downloadable file from a GitHub release.</summary>
public sealed record ReleaseAsset(
    string Name,
    string Version,
    Uri DownloadUrl,
    long Size,
    string? Sha256Digest)
{
    public string DisplayName => $"{Version} • {FormatSize(Size)}";

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes.ToString("N0", CultureInfo.InvariantCulture)} B";

        string[] units = ["KB", "MB", "GB", "TB"];
        var value = bytes / 1024d;
        var unit = 0;
        while (value >= 1024d && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        return $"{value.ToString("0.0", CultureInfo.InvariantCulture)} {units[unit]}";
    }
}

public readonly record struct DownloadProgress(long Received, long? Total);

/// <summary>Result of downloading and importing a precompiled hash database.</summary>
public sealed record ReleaseDatabaseLoadResult(string GameVersion, bool TemporaryFileDeleted);

/// <summary>
/// Reads the assets of the <c>PrecompiledBinaries</c> GitHub release, downloads them,
/// and imports them into the local hash database.
/// </summary>
public sealed class GitHubReleaseService
{
    public const string ReleaseTag = "PrecompiledBinaries";
    public const string ReleaseUrl = "https://github.com/notaspirit/SectorRemovalUpdater/releases/tag/" + ReleaseTag;

    private const string ReleaseApiUrl =
        "https://api.github.com/repos/notaspirit/SectorRemovalUpdater/releases/tags/" + ReleaseTag;

    private const string UserAgent = "SectorRemovalUpdater/1.0";

    private static readonly HttpClient Http = CreateHttpClient();

    public async Task<IReadOnlyList<ReleaseAsset>> GetAssetsAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseApiUrl);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var release = await response.Content
            .ReadFromJsonAsync<ReleaseResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (release?.Assets == null)
            return Array.Empty<ReleaseAsset>();

        var assets = new List<ReleaseAsset>();
        foreach (var asset in release.Assets)
        {
            if (string.IsNullOrWhiteSpace(asset.Name)
                || !asset.Name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
                || !Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var downloadUrl))
            {
                continue;
            }

            var version = Path.GetFileNameWithoutExtension(asset.Name);
            if (string.IsNullOrWhiteSpace(version))
                continue;

            assets.Add(new ReleaseAsset(
                asset.Name,
                version,
                downloadUrl,
                asset.Size,
                NormalizeSha256(asset.Digest)));
        }

        return assets
            .OrderBy(asset => ParseVersion(asset.Version))
            .ThenBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<long> DownloadAsync(
        ReleaseAsset asset,
        string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var total = response.Content.Headers.ContentLength;
        if (total is null or <= 0)
            total = asset.Size > 0 ? asset.Size : null;

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long received = 0;
        long lastReport = 0;

        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            hasher.AppendData(buffer, 0, read);
            received += read;

            // Throttle progress reports so consumers are not flooded on fast connections.
            if (received - lastReport >= 4 * 1024 * 1024)
            {
                lastReport = received;
                progress?.Report(new DownloadProgress(received, total));
            }
        }

        await target.FlushAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(new DownloadProgress(received, total ?? received));

        if (asset.Size > 0 && received != asset.Size)
        {
            throw new InvalidDataException(
                $"The download was incomplete (expected {ReleaseAsset.FormatSize(asset.Size)}, received {ReleaseAsset.FormatSize(received)}).");
        }

        if (!string.IsNullOrWhiteSpace(asset.Sha256Digest))
        {
            var digest = Convert.ToHexString(hasher.GetHashAndReset());
            if (!string.Equals(digest, asset.Sha256Digest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded file does not match the SHA-256 checksum published by GitHub.");
        }

        return received;
    }

    /// <summary>
    /// Downloads <paramref name="asset"/> to a temporary file, imports it into
    /// <paramref name="database"/>, and deletes the temporary file afterwards.
    /// </summary>
    public async Task<ReleaseDatabaseLoadResult> DownloadAndLoadDatabaseAsync(
        DatabaseService database,
        ReleaseAsset asset,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(asset);

        var directory = Path.Combine(Path.GetTempPath(), "SectorRemovalUpdater");
        var tempPath = Path.Combine(directory, $"{SanitizeFileName(asset.Version)}-{Guid.NewGuid():N}.bin");
        var gameVersion = string.Empty;
        var temporaryFileDeleted = false;

        try
        {
            Directory.CreateDirectory(directory);
            await DownloadAsync(asset, tempPath, progress, cancellationToken).ConfigureAwait(false);

            gameVersion = await Task.Run(
                () => database.LoadDatabaseFromFile(tempPath),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);

                temporaryFileDeleted = !File.Exists(tempPath);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Could not delete the temporary download {tempPath}: {e.Message}");
            }
        }

        return new ReleaseDatabaseLoadResult(gameVersion, temporaryFileDeleted);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            throw new HttpRequestException(
                "GitHub rejected the request. The unauthenticated API rate limit may be exhausted; try again later.");
        }

        string? detail = null;
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("message", out var message))
                detail = message.GetString();
        }
        catch
        {
            // The body may not be JSON; fall back to the status line.
        }

        var reason = string.IsNullOrWhiteSpace(detail) ? response.ReasonPhrase : detail;
        throw new HttpRequestException(
            $"GitHub request failed with status {(int)response.StatusCode} ({reason ?? "unknown error"}).");
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static Version ParseVersion(string version)
    {
        return Version.TryParse(version, out var parsed) ? parsed : new Version(0, 0);
    }

    private static string? NormalizeSha256(string? digest)
    {
        const string prefix = "sha256:";
        if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        return digest[prefix.Length..].Trim();
    }

    private static string SanitizeFileName(string name)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(name
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray())
            .Trim();

        return string.IsNullOrWhiteSpace(sanitized) ? "database" : sanitized;
    }

    private sealed class ReleaseResponse
    {
        [JsonPropertyName("assets")]
        public List<ReleaseAssetDto>? Assets { get; set; }
    }

    private sealed class ReleaseAssetDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("digest")]
        public string? Digest { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
