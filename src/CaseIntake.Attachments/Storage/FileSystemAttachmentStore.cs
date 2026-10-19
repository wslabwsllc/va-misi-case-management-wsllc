using System.Buffers;
using System.Security.Cryptography;
using CaseIntake.Domain.Attachments;
using Microsoft.Extensions.Options;

namespace CaseIntake.Attachments.Storage;

/// <summary>
/// An upload written to a temporary file: its path, byte count, SHA-256 and first bytes (for the signature check),
/// or <see cref="ExceededLimit"/>.
/// </summary>
internal sealed record StagedFile(string TempPath, long SizeBytes, string Sha256, bool ExceededLimit, byte[] Header);

/// <summary>A file in the store as seen by the reconciler.</summary>
internal sealed record StoredFile(string Path, string? Key, DateTimeOffset LastWriteUtc);

/// <summary>
/// The upload's source stream failed (client disconnected, browser stream timed out). Distinguishes the client's
/// failure from a storage failure (disk full, permissions), which surfaces as an ordinary <see cref="IOException"/>.
/// </summary>
internal sealed class UploadSourceException(Exception inner) : Exception("The upload stream could not be read.", inner);

/// <summary>
/// Where attachment bytes live (decision: filesystem on a named volume, ADR 0009). Internal so it can be swapped
/// (object storage, <c>bytea</c>) without touching the service contract.
/// </summary>
internal interface IAttachmentStore
{
    /// <summary>
    /// Streams <paramref name="content"/> into a new temporary file, hashing as it goes, and forces it to disk. Stops
    /// reading as soon as more than <paramref name="maxBytes"/> arrive (the temporary file is then deleted). Deletes the
    /// temporary file and rethrows on failure: <see cref="UploadSourceException"/> when the source stream failed,
    /// the storage exception otherwise, or <see cref="OperationCanceledException"/> on cancellation.
    /// </summary>
    Task<StagedFile> StageAsync(Stream content, long maxBytes, CancellationToken ct);

    /// <summary>Atomically moves a staged file to its final location under <paramref name="key"/>.</summary>
    void Promote(StagedFile staged, string key);

    /// <summary>Opens committed content for reading, or returns null when it does not exist.</summary>
    Stream? OpenRead(string key);

    void Delete(string key);

    void DeleteStaged(StagedFile staged);

    /// <summary>Deletes a file returned by one of the enumerations (used by the reconciler).</summary>
    void DeleteFile(StoredFile file);

    IEnumerable<StoredFile> EnumerateTemporaryFiles();

    IEnumerable<StoredFile> EnumerateContent();
}

/// <summary>
/// Layout under <c>Attachments:RootPath</c>: <c>tmp/{random}.part</c> for uploads in progress and
/// <c>blobs/{last two hex digits of key}/{key}</c> for committed content. Keys are version-7 GUIDs whose leading
/// digits are a timestamp, so the shard comes from the random tail to spread files over 256 directories. Keys are
/// server-generated 32-hex-digit GUIDs and are validated before any path is built, so no user input reaches a path.
/// </summary>
internal sealed class FileSystemAttachmentStore : IAttachmentStore
{
    private const int BufferSize = 81920;
    private const string TempExtension = ".part";

    // Options are validated at startup and the store is a singleton, so the layout is resolved once.
    private readonly string Root;
    private readonly string TempDirectory;
    private readonly string ContentDirectory;

    public FileSystemAttachmentStore(IOptions<AttachmentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        // Without a trailing separator, so "/attachments/" and "/attachments" give the same containment checks.
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Value.RootPath));
        TempDirectory = Path.Combine(Root, "tmp");
        ContentDirectory = Path.Combine(Root, "blobs");
    }

    public async Task<StagedFile> StageAsync(Stream content, long maxBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        Directory.CreateDirectory(TempDirectory);
        var path = Path.Combine(TempDirectory, Guid.NewGuid().ToString("N") + TempExtension);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var header = new byte[FileSignatures.HeaderLength];
            var headerLength = 0;
            long total = 0;
            var exceeded = false;
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous))
            {
                while (true)
                {
                    int read;
                    try
                    {
                        read = await content.ReadAsync(buffer.AsMemory(0, BufferSize), ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        throw new UploadSourceException(ex);
                    }
                    if (read == 0)
                    {
                        break;
                    }
                    total += read;
                    if (total > maxBytes)
                    {
                        exceeded = true;
                        break;
                    }
                    hash.AppendData(buffer, 0, read);
                    if (headerLength < header.Length)
                    {
                        var count = Math.Min(header.Length - headerLength, read);
                        buffer.AsSpan(0, count).CopyTo(header.AsSpan(headerLength));
                        headerLength += count;
                    }
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                if (!exceeded)
                {
                    await file.FlushAsync(ct);
                    file.Flush(flushToDisk: true);   // durable before the metadata that points at it is committed
                }
            }

            if (exceeded)
            {
                TryDelete(path);   // the client's error: a failing delete must not turn it into a storage failure
                return new StagedFile(path, total, string.Empty, ExceededLimit: true, []);
            }
            return new StagedFile(path, total, Convert.ToHexStringLower(hash.GetHashAndReset()), ExceededLimit: false, header[..headerLength]);
        }
        catch
        {
            TryDelete(path);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void Promote(StagedFile staged, string key)
    {
        ArgumentNullException.ThrowIfNull(staged);
        var destination = ContentPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(staged.TempPath, destination, overwrite: false);
    }

    public Stream? OpenRead(string key)
    {
        try
        {
            return new FileStream(ContentPath(key), FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    public void Delete(string key)
    {
        var path = ContentPath(key);
        try
        {
            File.Delete(path);   // a missing file is not an error
        }
        catch (DirectoryNotFoundException)
        {
            // Its shard directory was never created: nothing to delete.
        }
    }

    public void DeleteStaged(StagedFile staged)
    {
        ArgumentNullException.ThrowIfNull(staged);
        TryDelete(staged.TempPath);
    }

    public void DeleteFile(StoredFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var path = Path.GetFullPath(file.Path);
        if (!path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("The file is not inside the attachment root.", nameof(file));
        }
        File.Delete(path);
    }

    public IEnumerable<StoredFile> EnumerateTemporaryFiles() =>
        Directory.Exists(TempDirectory)
            ? new DirectoryInfo(TempDirectory).EnumerateFiles("*" + TempExtension).Select(f => new StoredFile(f.FullName, null, LastWrite(f)))
            : [];

    public IEnumerable<StoredFile> EnumerateContent() =>
        Directory.Exists(ContentDirectory)
            ? new DirectoryInfo(ContentDirectory).EnumerateFiles("*", SearchOption.AllDirectories)
                .Select(f => new StoredFile(f.FullName, f.Name, LastWrite(f)))
            : [];

    internal string ContentPath(string key)
    {
        if (!Attachment.IsLowerHex(key, AttachmentFieldLimits.StorageKeyLength))
        {
            throw new ArgumentException("Storage keys are 32 lowercase hex digits.", nameof(key));
        }
        return Path.Combine(ContentDirectory, ShardOf(key), key);
    }

    /// <summary>The shard directory for a key: its last two hex digits (random in a version-7 GUID).</summary>
    internal static string ShardOf(string key) => key[^2..];

    // FileInfo from enumeration already carries the timestamp: no second stat per file.
    private static DateTimeOffset LastWrite(FileInfo file) => new(file.LastWriteTimeUtc, TimeSpan.Zero);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the startup reconciler (temporary files older than the grace period are removed).
        }
    }
}
