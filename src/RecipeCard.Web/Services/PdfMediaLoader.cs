using System.Buffers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace RecipeCard.Web.Services;

public enum PdfMediaRole
{
    Hero,
    Step
}

public sealed record PdfMediaRequest(
    string Key,
    int RecipeId,
    PdfMediaRole Role,
    int SortOrder,
    string? RemoteUrl,
    string? LocalFileName);

public sealed record PdfMediaResult(
    string Key,
    string? NormalizedFilePath);

public interface IPdfMediaObserver
{
    void OnRemoteRequestStarting() { }
    void OnRemoteRequestFinished() { }
    void OnDecodeStarting() { }
    void OnDecodeFinished() { }
}

public sealed class PdfMediaLoader
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<PdfMediaLoader> _logger;
    private readonly IPdfMediaObserver? _observer;

    private const int MaxRemoteConcurrency = 4;
    private const int MaxDecodeConcurrency = 2;
    private const long MaxInputByteLength = 8 * 1024 * 1024; // 8 MiB
    private const int MaxDimension = 6000;
    private const long MaxInputPixels = 16_000_000; // 16 MP
    private const long MaxBatchEncodedBytes = 64 * 1024 * 1024; // 64 MiB
    private const long MaxBatchOutputPixels = 75_000_000; // 75 MP
    private const long MaxNormalizedFileBytes = 750 * 1024; // 750 KiB
    private static readonly TimeSpan RemoteTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BatchMediaTimeout = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _remoteSemaphore = new(MaxRemoteConcurrency, MaxRemoteConcurrency);
    private readonly SemaphoreSlim _decodeSemaphore = new(MaxDecodeConcurrency, MaxDecodeConcurrency);

    public PdfMediaLoader(
        IHttpClientFactory httpClientFactory,
        IWebHostEnvironment env,
        ILogger<PdfMediaLoader> logger,
        IPdfMediaObserver? observer = null)
    {
        _httpClientFactory = httpClientFactory;
        _env = env;
        _logger = logger;
        _observer = observer;
    }

    public async Task<IReadOnlyDictionary<string, string?>> LoadBatchAsync(
        IReadOnlyList<PdfMediaRequest> requests,
        string tempDirectory,
        CancellationToken callerToken)
    {
        var results = new Dictionary<string, string?>();
        if (requests.Count == 0)
        {
            return results;
        }

        Directory.CreateDirectory(tempDirectory);

        using var batchCts = new CancellationTokenSource(BatchMediaTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(callerToken, batchCts.Token);
        var activeToken = linkedCts.Token;

        var tasks = requests.Select(req => ProcessSingleAsync(req, tempDirectory, activeToken, callerToken)).ToList();

        PdfMediaProcessed[] processedItems;
        try
        {
            processedItems = await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error processing media batch");
            processedItems = [];
        }

        var orderedRequests = requests
            .OrderByDescending(r => r.RecipeId)
            .ThenBy(r => r.Role == PdfMediaRole.Hero ? 0 : 1)
            .ThenBy(r => r.SortOrder)
            .ToList();

        var processedMap = processedItems.ToDictionary(p => p.Key);

        long currentBatchEncodedBytes = 0;
        long currentBatchOutputPixels = 0;

        foreach (var req in orderedRequests)
        {
            if (!processedMap.TryGetValue(req.Key, out var item) || item.TempFilePath == null)
            {
                results[req.Key] = null;
                continue;
            }

            if (currentBatchEncodedBytes + item.EncodedBytes <= MaxBatchEncodedBytes &&
                currentBatchOutputPixels + item.OutputPixels <= MaxBatchOutputPixels)
            {
                results[req.Key] = item.TempFilePath;
                currentBatchEncodedBytes += item.EncodedBytes;
                currentBatchOutputPixels += item.OutputPixels;
            }
            else
            {
                try
                {
                    if (File.Exists(item.TempFilePath))
                    {
                        File.Delete(item.TempFilePath);
                    }
                }
                catch
                {
                }
                results[req.Key] = null;
            }
        }

        return results;
    }

    private sealed record PdfMediaProcessed(
        string Key,
        string? TempFilePath,
        long EncodedBytes,
        long OutputPixels);

    private async Task<PdfMediaProcessed> ProcessSingleAsync(
        PdfMediaRequest req,
        string tempDirectory,
        CancellationToken activeToken,
        CancellationToken callerToken)
    {
        try
        {
            byte[]? rawBytes = null;

            if (!string.IsNullOrWhiteSpace(req.RemoteUrl))
            {
                rawBytes = await FetchRemoteBytesAsync(req.RemoteUrl, activeToken, callerToken);
            }
            else if (!string.IsNullOrWhiteSpace(req.LocalFileName))
            {
                rawBytes = await ReadLocalBytesAsync(req.LocalFileName, activeToken, callerToken);
            }

            if (rawBytes == null || rawBytes.Length == 0)
            {
                return new PdfMediaProcessed(req.Key, null, 0, 0);
            }

            return await NormalizeImageAsync(req, rawBytes, tempDirectory, activeToken, callerToken);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Error preparing media for key {Key}: {Message}", req.Key, ex.Message);
            return new PdfMediaProcessed(req.Key, null, 0, 0);
        }
    }

    private async Task<byte[]?> FetchRemoteBytesAsync(
        string url,
        CancellationToken activeToken,
        CancellationToken callerToken)
    {
        await _remoteSemaphore.WaitAsync(activeToken);
        _observer?.OnRemoteRequestStarting();
        try
        {
            using var perRequestCts = new CancellationTokenSource(RemoteTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(activeToken, perRequestCts.Token);

            var client = _httpClientFactory.CreateClient("MediaDelivery");
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, linked.Token);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            if (response.Content.Headers.ContentLength is { } length && length > MaxInputByteLength)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(linked.Token);
            using var ms = new MemoryStream();
            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            long totalRead = 0;
            try
            {
                while (true)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), linked.Token);
                    if (read == 0) break;
                    totalRead += read;
                    if (totalRead > MaxInputByteLength)
                    {
                        return null;
                    }
                    ms.Write(buffer, 0, read);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            return ms.ToArray();
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Remote fetch failed for media: {Message}", ex.Message);
            return null;
        }
        finally
        {
            _observer?.OnRemoteRequestFinished();
            _remoteSemaphore.Release();
        }
    }

    private async Task<byte[]?> ReadLocalBytesAsync(
        string localFileName,
        CancellationToken activeToken,
        CancellationToken callerToken)
    {
        try
        {
            var webRoot = _env.WebRootPath;
            if (string.IsNullOrEmpty(webRoot))
            {
                webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            }

            var allowedBase = Path.GetFullPath(Path.Combine(webRoot, "uploads", "steps"));
            var targetPath = Path.GetFullPath(Path.Combine(allowedBase, Path.GetFileName(localFileName)));

            if (!targetPath.StartsWith(allowedBase, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var fileInfo = new FileInfo(targetPath);
            if (!fileInfo.Exists || fileInfo.Length > MaxInputByteLength || fileInfo.Length == 0)
            {
                return null;
            }

            return await File.ReadAllBytesAsync(targetPath, activeToken);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Local read failed for media: {Message}", ex.Message);
            return null;
        }
    }

    private async Task<PdfMediaProcessed> NormalizeImageAsync(
        PdfMediaRequest req,
        byte[] rawBytes,
        string tempDirectory,
        CancellationToken activeToken,
        CancellationToken callerToken)
    {
        await _decodeSemaphore.WaitAsync(activeToken);
        _observer?.OnDecodeStarting();
        try
        {
            using var inputStream = new MemoryStream(rawBytes, writable: false);

            ImageInfo? info;
            try
            {
                info = await Image.IdentifyAsync(inputStream, activeToken);
            }
            catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Image identification failed: {Message}", ex.Message);
                return new PdfMediaProcessed(req.Key, null, 0, 0);
            }

            if (info == null || info.Width <= 0 || info.Height <= 0)
            {
                return new PdfMediaProcessed(req.Key, null, 0, 0);
            }

            if (info.Width > MaxDimension || info.Height > MaxDimension)
            {
                return new PdfMediaProcessed(req.Key, null, 0, 0);
            }

            long totalPixels = (long)info.Width * info.Height;
            if (totalPixels > MaxInputPixels)
            {
                return new PdfMediaProcessed(req.Key, null, 0, 0);
            }

            inputStream.Position = 0;

            var decoderOptions = new DecoderOptions
            {
                SkipMetadata = true,
                MaxFrames = 1
            };

            using var image = await Image.LoadAsync<Rgba32>(decoderOptions, inputStream, activeToken);

            var (maxBoxW, maxBoxH) = req.Role == PdfMediaRole.Hero ? (1200, 750) : (900, 600);

            if (image.Width > maxBoxW || image.Height > maxBoxH)
            {
                image.Mutate(ctx => ctx.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(maxBoxW, maxBoxH)
                }));
            }

            image.Mutate(ctx => ctx.BackgroundColor(Color.White));
            using var flatImage = image.CloneAs<Rgb24>();

            int[] qualitySteps = [82, 72, 62];
            byte[]? finalJpegBytes = null;

            foreach (var quality in qualitySteps)
            {
                using var outMs = new MemoryStream();
                var encoder = new JpegEncoder { Quality = quality };
                await flatImage.SaveAsync(outMs, encoder, activeToken);
                if (outMs.Length <= MaxNormalizedFileBytes)
                {
                    finalJpegBytes = outMs.ToArray();
                    break;
                }
            }

            while (finalJpegBytes == null && (flatImage.Width > 480 || flatImage.Height > 480))
            {
                int newW = (int)Math.Round(flatImage.Width * 0.85);
                int newH = (int)Math.Round(flatImage.Height * 0.85);
                if (newW < 1 || newH < 1) break;

                flatImage.Mutate(ctx => ctx.Resize(newW, newH));

                using var outMs = new MemoryStream();
                var encoder = new JpegEncoder { Quality = 62 };
                await flatImage.SaveAsync(outMs, encoder, activeToken);
                if (outMs.Length <= MaxNormalizedFileBytes)
                {
                    finalJpegBytes = outMs.ToArray();
                    break;
                }
            }

            if (finalJpegBytes == null)
            {
                return new PdfMediaProcessed(req.Key, null, 0, 0);
            }

            var tempFilePath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.jpg");
            await File.WriteAllBytesAsync(tempFilePath, finalJpegBytes, activeToken);

            long outputPixels = (long)flatImage.Width * flatImage.Height;
            return new PdfMediaProcessed(req.Key, tempFilePath, finalJpegBytes.Length, outputPixels);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Decode/resize failed: {Message}", ex.Message);
            return new PdfMediaProcessed(req.Key, null, 0, 0);
        }
        finally
        {
            _observer?.OnDecodeFinished();
            _decodeSemaphore.Release();
        }
    }
}
