using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mertviy;

internal sealed class GitHubReleaseUpdater : IDisposable
{
    private const string Owner = "angelsky33rus-ai";
    private const string Repository = "mertviy";
    private const long MaxApiBytes = 1024 * 1024;
    private const long MaxAssetBytes = 500L * 1024 * 1024;
    private readonly HttpClient _apiClient;
    private readonly HttpClient _downloadClient;
    private readonly string _currentVersion;

    public GitHubReleaseUpdater(string currentVersion)
    {
        _currentVersion = NormalizeVersion(currentVersion) ?? "0.0.0";
        var apiHandler = new HttpClientHandler { AllowAutoRedirect = false };
        _apiClient = new HttpClient(apiHandler) { Timeout = TimeSpan.FromSeconds(5) };
        _apiClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mertviy/" + _currentVersion);
        _apiClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _apiClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        var downloadHandler = new HttpClientHandler { AllowAutoRedirect = false };
        _downloadClient = new HttpClient(downloadHandler) { Timeout = Timeout.InfiniteTimeSpan };
        _downloadClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mertviy/" + _currentVersion);
    }

    public async Task<UpdateCheckResult> CheckLatestAsync(CancellationToken cancellationToken)
    {
        try
        {
            var endpoint = new Uri($"https://api.github.com/repos/{Owner}/{Repository}/releases/latest");
            using var response = await _apiClient.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
                return UpdateCheckResult.Unavailable(_currentVersion, response.StatusCode == HttpStatusCode.NotFound
                    ? "Репозиторий или стабильный релиз пока недоступен."
                    : $"GitHub вернул HTTP {(int)response.StatusCode}.");

            if (response.Content.Headers.ContentLength is > MaxApiBytes)
                return UpdateCheckResult.Unavailable(_currentVersion, "Ответ GitHub превышает допустимый размер.");

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var json = await ReadLimitedAsync(responseStream, MaxApiBytes, cancellationToken);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("tag_name", out var tagElement) || tagElement.ValueKind != JsonValueKind.String)
                return UpdateCheckResult.Unavailable(_currentVersion, "GitHub не указал версию релиза.");

            var latest = NormalizeVersion(tagElement.GetString());
            if (latest is null || !Version.TryParse(latest, out var latestVersion) || !Version.TryParse(_currentVersion, out var currentVersion))
                return UpdateCheckResult.Unavailable(_currentVersion, "Версия релиза имеет неверный формат.");

            if (!root.TryGetProperty("html_url", out var pageElement) || !Uri.TryCreate(pageElement.GetString(), UriKind.Absolute, out var pageUri) ||
                pageUri.Scheme != Uri.UriSchemeHttps || !pageUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                return UpdateCheckResult.Unavailable(_currentVersion, "Ссылка на релиз не прошла проверку.");

            if (!root.TryGetProperty("assets", out var assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
                return UpdateCheckResult.Unavailable(_currentVersion, "В релизе отсутствуют файлы обновления.");

            var executableName = $"Mertviy-{latest}-win-x64.exe";
            var checksumName = executableName + ".sha256";
            var executable = FindAsset(assetsElement, executableName);
            var checksum = FindAsset(assetsElement, checksumName);
            if (executable is null || checksum is null || executable.Value.Size is <= 0 or > MaxAssetBytes)
                return UpdateCheckResult.Unavailable(_currentVersion, "В релизе нет exe и SHA-256 файлов ожидаемого формата.");

            var available = latestVersion > currentVersion;
            return new UpdateCheckResult(_currentVersion, latest, pageUri.ToString(), executable.Value.Url, checksum.Value.Url,
                executable.Value.Size, available, available ? "Доступна новая версия." : "Установлена актуальная версия.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidOperationException or ArgumentException or ObjectDisposedException)
        {
            return UpdateCheckResult.Unavailable(_currentVersion, "Не удалось проверить обновления: " + ex.Message);
        }
    }

    public async Task<string> DownloadUpdateAsync(UpdateCheckResult update, string destinationDirectory, IProgress<long> progress, CancellationToken cancellationToken)
    {
        if (!update.IsUpdateAvailable || update.LatestVersion is null || update.AssetUrl is null || update.ChecksumUrl is null)
            throw new InvalidOperationException("Нет доступного обновления для скачивания.");
        ValidateInitialAssetUrl(update.AssetUrl);
        ValidateInitialAssetUrl(update.ChecksumUrl);
        Directory.CreateDirectory(destinationDirectory);

        var finalPath = Path.Combine(destinationDirectory, $"Mertviy-{update.LatestVersion}-win-x64.exe");
        var temporaryPath = finalPath + ".download";
        try
        {
            using var checksumResponse = await GetReleaseAssetResponseAsync(update.ChecksumUrl, cancellationToken);
            EnsureFinalAssetHost(checksumResponse);
            checksumResponse.EnsureSuccessStatusCode();
            if (checksumResponse.Content.Headers.ContentLength is > 4096)
                throw new InvalidDataException("Файл контрольной суммы слишком большой.");
            var checksumText = await ReadLimitedAsync(await checksumResponse.Content.ReadAsStreamAsync(cancellationToken), 4096, cancellationToken);
            var expectedHash = Regex.Match(System.Text.Encoding.UTF8.GetString(checksumText), @"\b[0-9a-fA-F]{64}\b").Value;
            if (expectedHash.Length != 64)
                throw new InvalidDataException("В релизе некорректный SHA-256.");

            using var response = await GetReleaseAssetResponseAsync(update.AssetUrl, cancellationToken);
            EnsureFinalAssetHost(response);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxAssetBytes)
                throw new InvalidDataException("Размер exe превышает допустимый предел.");

            long totalBytes = 0;
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                while (true)
                {
                    var read = await input.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    totalBytes += read;
                    if (totalBytes > MaxAssetBytes || update.AssetSize > 0 && totalBytes > update.AssetSize)
                        throw new InvalidDataException("Размер скачанного файла превысил ожидаемый предел.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    progress.Report(totalBytes);
                }
            }
            if (totalBytes != update.AssetSize)
                throw new InvalidDataException("Размер скачанного файла не совпадает с размером в релизе.");

            await using var file = File.OpenRead(temporaryPath);
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expectedHash), Convert.FromHexString(actualHash)))
                throw new InvalidDataException("SHA-256 скачанного файла не совпадает с релизом.");

            File.Move(temporaryPath, finalPath, overwrite: true);
            return finalPath;
        }
        catch
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
            throw;
        }
    }

    private static (Uri Url, long Size)? FindAsset(JsonElement assets, string expectedName)
    {
        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var name) || name.GetString() != expectedName ||
                !asset.TryGetProperty("browser_download_url", out var url) ||
                !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                !asset.TryGetProperty("size", out var size) || !size.TryGetInt64(out var length)) continue;
            return (uri, length);
        }
        return null;
    }

    private static string? NormalizeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var version = value.Trim();
        if (version.StartsWith('v') || version.StartsWith('V')) version = version[1..];
        var metadata = version.IndexOf('+');
        if (metadata >= 0) version = version[..metadata];
        return version;
    }

    private static void ValidateInitialAssetUrl(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Ссылка на файл обновления не с github.com.");
    }

    private static void EnsureFinalAssetHost(HttpResponseMessage response)
    {
        var uri = response.RequestMessage?.RequestUri;
        if (uri is null || uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("GitHub перенаправил скачивание на неизвестный сервер.");
    }

    private async Task<HttpResponseMessage> GetReleaseAssetResponseAsync(Uri initialUri, CancellationToken cancellationToken)
    {
        ValidateInitialAssetUrl(initialUri);
        var current = initialUri;
        for (var redirect = 0; redirect <= 5; redirect++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            var response = await _downloadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is < 300 or > 399)
                return response;

            var location = response.Headers.Location;
            response.Dispose();
            if (redirect == 5 || location is null)
                throw new InvalidDataException("Слишком много перенаправлений при скачивании обновления.");

            var next = location.IsAbsoluteUri ? location : new Uri(current, location);
            if (next.Scheme != Uri.UriSchemeHttps ||
                !(next.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
                  next.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("GitHub перенаправил скачивание на неизвестный сервер.");
            current = next;
        }

        throw new InvalidDataException("Не удалось перенаправить запрос загрузки обновления.");
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream stream, long maximumBytes, CancellationToken cancellationToken)
    {
        await using (stream)
        using (var output = new MemoryStream())
        {
            var buffer = new byte[8192];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                if (output.Length + read > maximumBytes) throw new InvalidDataException("Ответ сервера превышает допустимый размер.");
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }
    }

    public void Dispose()
    {
        _apiClient.Dispose();
        _downloadClient.Dispose();
    }
}
