using System.Security.Cryptography;
using System.Text;
using CaseIntake.Attachments.Storage;
using Microsoft.Extensions.Options;

namespace CaseIntake.Attachments.Tests;

/// <summary>E4-S4: the filesystem store streams, limits, hashes, promotes and cleans up, using a temporary root.</summary>
public sealed class FileSystemAttachmentStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "caseintake-store-" + Guid.NewGuid().ToString("N"));
    private readonly FileSystemAttachmentStore _store;

    public FileSystemAttachmentStoreTests()
    {
        _store = new FileSystemAttachmentStore(Options.Create(new AttachmentOptions { RootPath = _root }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static string Key() => Guid.NewGuid().ToString("N");

    private string[] AllFiles() => Directory.Exists(_root) ? Directory.GetFiles(_root, "*", SearchOption.AllDirectories) : [];

    [Test]
    public async Task Staging_counts_bytes_and_computes_the_SHA256()
    {
        var bytes = Encoding.UTF8.GetBytes("abc");

        var staged = await _store.StageAsync(new MemoryStream(bytes), maxBytes: 3, CancellationToken.None);

        Assert.False(staged.ExceededLimit);
        Assert.Equal(3, staged.SizeBytes);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", staged.Sha256);   // FIPS 180-2 "abc"
        Assert.StartsWith(Path.Combine(_root, "tmp"), staged.TempPath, StringComparison.Ordinal);
        Assert.EndsWith(".part", staged.TempPath, StringComparison.Ordinal);
    }

    [Test]
    public async Task Staging_captures_the_leading_bytes_for_the_signature_check()
    {
        var staged = await _store.StageAsync(new MemoryStream("%PDF-1.7 rest of file"u8.ToArray()), 100, CancellationToken.None);
        var tiny = await _store.StageAsync(new MemoryStream([0xFF, 0xD8]), 2, CancellationToken.None);

        Assert.Equal("%PDF-1.7"u8.ToArray(), staged.Header);
        Assert.Equal(new byte[] { 0xFF, 0xD8 }, tiny.Header);
    }

    [Test]
    public async Task Promoting_moves_the_file_to_a_sharded_key_path_and_it_reads_back_identically()
    {
        var bytes = RandomNumberGenerator.GetBytes(200_000);
        var key = Key();
        var staged = await _store.StageAsync(new MemoryStream(bytes), bytes.Length, CancellationToken.None);

        _store.Promote(staged, key);

        Assert.False(File.Exists(staged.TempPath));
        Assert.True(File.Exists(Path.Combine(_root, "blobs", FileSystemAttachmentStore.ShardOf(key), key)));
        await using var read = _store.OpenRead(key)!;
        using var copy = new MemoryStream();
        await read.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), staged.Sha256);
    }

    [Test]
    public async Task Reading_stops_once_the_limit_is_exceeded_and_nothing_is_left_behind()
    {
        var stream = new CountingStream(new MemoryStream(new byte[1_000_000]));

        var staged = await _store.StageAsync(stream, maxBytes: 100, CancellationToken.None);

        Assert.True(staged.ExceededLimit);
        Assert.True(stream.BytesRead < 1_000_000, "the store kept reading after the limit");
        Assert.Empty(AllFiles());
    }

    [Test]
    public async Task An_interrupted_stream_leaves_no_temporary_file()
    {
        var failure = await Assert.ThrowsAsync<UploadSourceException>(() => _store.StageAsync(new FailingStream(after: 10_000), 1_000_000, CancellationToken.None));

        Assert.IsType<IOException>(failure.InnerException);

        Assert.Empty(AllFiles());
    }

    [Test]
    public async Task Cancellation_while_staging_leaves_no_temporary_file()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _store.StageAsync(new MemoryStream(new byte[10]), 10, cts.Token));

        Assert.Empty(AllFiles());
    }

    [Test]
    public void Missing_content_reads_as_null_and_deleting_it_is_harmless()
    {
        var key = Key();

        Assert.Null(_store.OpenRead(key));
        _store.Delete(key);
    }

    [TestCase("../../../etc/passwd")]
    [TestCase("ABCDEF0123456789ABCDEF0123456789")]
    [TestCase("0123456789abcdef")]
    [TestCase("")]
    public void Keys_that_are_not_server_generated_are_refused_before_building_a_path(string key)
    {
        Assert.Throws<ArgumentException>(() => _store.OpenRead(key));
        Assert.Throws<ArgumentException>(() => _store.Delete(key));
    }

    [Test]
    public async Task Enumeration_reports_temporary_files_and_content_with_their_keys()
    {
        var key = Key();
        _store.Promote(await _store.StageAsync(new MemoryStream([1, 2, 3]), 3, CancellationToken.None), key);
        var pending = await _store.StageAsync(new MemoryStream([4]), 1, CancellationToken.None);

        Assert.Equal([pending.TempPath], _store.EnumerateTemporaryFiles().Select(f => f.Path));
        Assert.Equal([key], _store.EnumerateContent().Select(f => f.Key));
    }

    [Test]
    public async Task Files_outside_the_root_are_never_deleted_through_the_store()
    {
        var outside = Path.Combine(Path.GetTempPath(), "outside-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(outside, "keep");
        try
        {
            Assert.Throws<ArgumentException>(() => _store.DeleteFile(new StoredFile(outside, null, DateTimeOffset.MinValue)));
            Assert.Throws<ArgumentException>(() => _store.DeleteFile(new StoredFile(Path.Combine(_root, "..", Path.GetFileName(outside)), null, DateTimeOffset.MinValue)));
            Assert.True(File.Exists(outside));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Test]
    public async Task A_root_configured_with_a_trailing_separator_still_deletes_its_own_files()
    {
        var store = new FileSystemAttachmentStore(Options.Create(new AttachmentOptions { RootPath = _root + Path.DirectorySeparatorChar }));
        await store.StageAsync(new MemoryStream([1]), 1, CancellationToken.None);

        var temp = Assert.Single(store.EnumerateTemporaryFiles());
        store.DeleteFile(temp);

        Assert.Empty(AllFiles());
    }

    [Test]
    public void Version7_keys_spread_over_shards_instead_of_sharing_their_timestamp_prefix()
    {
        var now = TimeProvider.System.GetUtcNow();
        var shards = Enumerable.Range(0, 2000)
            .Select(i => FileSystemAttachmentStore.ShardOf(Guid.CreateVersion7(now.AddSeconds(i)).ToString("N")))
            .Distinct()
            .Count();

        Assert.True(shards > 200, $"only {shards} shards used");
    }

    [Test]
    public void An_absent_root_enumerates_nothing()
    {
        Assert.Empty(_store.EnumerateTemporaryFiles());
        Assert.Empty(_store.EnumerateContent());
    }

    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Count(int read)
        {
            BytesRead += read;
            return read;
        }
    }

    /// <summary>Yields zeros, then fails like a dropped browser upload.</summary>
    internal sealed class FailingStream(long after) : Stream
    {
        private long _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => Next(count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Next(buffer.Length));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Next(int count)
        {
            if (_position >= after)
            {
                throw new IOException("The client disconnected.");
            }
            var n = (int)Math.Min(count, after - _position);
            _position += n;
            return n;
        }
    }
}
