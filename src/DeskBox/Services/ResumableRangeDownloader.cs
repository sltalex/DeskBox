using System.Net;
using System.Net.Http.Headers;
using DeskBox.Models;

namespace DeskBox.Core.Persistence;

/// <summary>
/// Downloads a large immutable file as independent HTTP Range parts. Part files and a
/// small manifest are deliberately kept beside the normal .tmp file, so cancellation
/// or an application restart only repeats missing parts.
/// </summary>
internal static class ResumableRangeDownloader
{
    internal const int DefaultConcurrency = 4;
    internal const long DefaultPartSize = 4L * 1024 * 1024;

    internal enum Result { Completed, Unsupported }

    internal static void DiscardState(string targetPath)
    {
        try
        {
            string root = targetPath + ".parts";
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch { }
    }

    internal sealed record Options(int Concurrency = DefaultConcurrency, long PartSize = DefaultPartSize);

    internal static async Task<Result> DownloadAsync(
        HttpClient client,
        Uri source,
        string targetPath,
        long expectedSize,
        IProgress<AppUpdateDownloadProgress>? progress,
        Options? options = null,
        CancellationToken cancellationToken = default)
    {
        if (expectedSize <= 0) return Result.Unsupported;
        options ??= new Options();
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        string root = targetPath + ".parts";
        string metadataPath = Path.Combine(root, "metadata.json");

        var probe = new HttpRequestMessage(HttpMethod.Get, source);
        probe.Headers.Range = new RangeHeaderValue(0, 0);
        using HttpResponseMessage probeResponse = await client.SendAsync(probe, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (probeResponse.StatusCode != HttpStatusCode.PartialContent ||
            probeResponse.Content.Headers.ContentRange?.From != 0 ||
            probeResponse.Content.Headers.ContentRange?.Length is not long remoteSize ||
            remoteSize != expectedSize)
        {
            return Result.Unsupported;
        }
        string? etag = probeResponse.Headers.ETag?.Tag;
        await using (Stream ignored = await probeResponse.Content.ReadAsStreamAsync(cancellationToken))
        {
            byte[] one = new byte[1];
            _ = await ignored.ReadAsync(one, cancellationToken);
        }

        int partCount = checked((int)((expectedSize + options.PartSize - 1) / options.PartSize));
        Metadata metadata = await LoadMetadataAsync(metadataPath, cancellationToken) ?? new Metadata();
        if (!string.Equals(metadata.Url, source.AbsoluteUri, StringComparison.Ordinal) ||
            metadata.Size != expectedSize ||
            !string.Equals(metadata.ETag ?? string.Empty, etag ?? string.Empty, StringComparison.Ordinal))
        {
            TryDeleteDirectory(root);
            Directory.CreateDirectory(root);
            metadata = new Metadata { Url = source.AbsoluteUri, Size = expectedSize, ETag = etag, PartSize = options.PartSize, Completed = [] };
        }
        metadata.PartSize = options.PartSize;
        metadata.Completed ??= [];
        metadata.Completed.RemoveAll(index => index < 0 || index >= partCount || !File.Exists(PartPath(root, index)));
        using var metadataGate = new SemaphoreSlim(1, 1);
        await SaveMetadataAsync(metadataPath, metadata, metadataGate, cancellationToken);

        long completedBytes = metadata.Completed.Sum(index => PartLength(index, expectedSize, options.PartSize));
        progress?.Report(new AppUpdateDownloadProgress(completedBytes, expectedSize));
        using var gate = new SemaphoreSlim(Math.Clamp(options.Concurrency, 1, 8));
        var tasks = Enumerable.Range(0, partCount).Where(index => !metadata.Completed.Contains(index)).Select(async index =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                long start = index * options.PartSize;
                long end = Math.Min(expectedSize - 1, start + options.PartSize - 1);
                Exception? last = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, source);
                        request.Headers.Range = new RangeHeaderValue(start, end);
                        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        if (response.StatusCode != HttpStatusCode.PartialContent || response.Content.Headers.ContentRange?.From != start || response.Content.Headers.ContentRange?.To != end)
                            throw new NotSupportedException("The update server did not honor the requested byte range.");
                        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
                        await using var output = new FileStream(PartPath(root, index), FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);
                        long written = 0;
                        byte[] buffer = new byte[128 * 1024];
                        int read;
                        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                        {
                            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                            written += read;
                        }
                        if (written != end - start + 1) throw new IOException("The update server returned an incomplete range.");
                        await output.FlushAsync(cancellationToken);
                        lock (metadata)
                        {
                            if (!metadata.Completed.Contains(index)) metadata.Completed.Add(index);
                            completedBytes += written;
                        }
                        await SaveMetadataAsync(metadataPath, metadata, metadataGate, cancellationToken);
                        progress?.Report(new AppUpdateDownloadProgress(Interlocked.Read(ref completedBytes), expectedSize));
                        return;
                    }
                    catch (Exception ex) when (ex is HttpRequestException or HttpIOException or IOException) { last = ex; }
                }
                throw last ?? new IOException("The update range failed.");
            }
            finally { gate.Release(); }
        });
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (NotSupportedException)
        {
            // A proxy can honor the probe but ignore later ranges. Let the caller use
            // its proven single-stream fallback instead.
            return Result.Unsupported;
        }

        metadata.Completed.Sort();
        if (metadata.Completed.Count != partCount) throw new IOException("The update is missing one or more ranges.");
        await using (var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true))
        {
            for (int index = 0; index < partCount; index++)
            {
                await using var input = new FileStream(PartPath(root, index), FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true);
                await input.CopyToAsync(output, cancellationToken);
            }
            await output.FlushAsync(cancellationToken);
        }
        TryDeleteDirectory(root);
        return Result.Completed;
    }

    private static long PartLength(int index, long total, long partSize) => Math.Min(partSize, total - index * partSize);
    private static string PartPath(string root, int index) => Path.Combine(root, $"part-{index:D6}.bin");

    private static async Task<Metadata?> LoadMetadataAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            string[] lines = await File.ReadAllLinesAsync(path, cancellationToken);
            if (lines.Length < 5 || !long.TryParse(lines[1], out long size) || !long.TryParse(lines[3], out long partSize)) return null;
            var completed = lines[4].Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Where(value => int.TryParse(value, out _)).Select(int.Parse).ToList();
            return new Metadata { Url = lines[0], Size = size, ETag = string.IsNullOrEmpty(lines[2]) ? null : lines[2], PartSize = partSize, Completed = completed };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static async Task SaveMetadataAsync(string path, Metadata metadata, SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            string temporary = path + ".tmp";
            int[] completed;
            lock (metadata) completed = metadata.Completed.Order().ToArray();
            string serialized = string.Join('\n', metadata.Url, metadata.Size, metadata.ETag ?? string.Empty, metadata.PartSize, string.Join(',', completed));
            await File.WriteAllTextAsync(temporary, serialized, cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally { gate.Release(); }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }

    private sealed class Metadata
    {
        public string Url { get; set; } = string.Empty;
        public long Size { get; set; }
        public string? ETag { get; set; }
        public long PartSize { get; set; }
        public List<int> Completed { get; set; } = [];
    }
}
