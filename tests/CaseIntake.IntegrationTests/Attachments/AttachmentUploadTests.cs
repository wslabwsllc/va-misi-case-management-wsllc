using CaseIntake.Attachments.Storage;
using CaseIntake.Attachments;
using CaseIntake.Contracts.Common;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;
using CaseIntake.IntegrationTests.Cases;
using CaseIntake.ServiceDefaults;
using CaseIntake.TestKit;
using Microsoft.EntityFrameworkCore;
using static CaseIntake.IntegrationTests.Cases.CaseTestHarness;

namespace CaseIntake.IntegrationTests.Attachments;

/// <summary>E4-S4/S5 against PostgreSQL and a real filesystem root: upload, validation, compensation, list, durability.</summary>
public class AttachmentUploadTests(PostgresDatabaseFixture database) : IClassFixture<PostgresDatabaseFixture>
{
    [Fact]
    public async Task An_upload_stores_the_bytes_under_a_server_key_and_commits_metadata_with_one_audit_entry()
    {
        using var h = new AttachmentTestHarness(database);
        var c = await h.CreateCaseAsync();
        var bytes = AttachmentTestHarness.RandomBytes(150_000);
        var auditBefore = await h.Cases.CountAuditAsync(c.Id);

        var dto = await h.UploadAsync(c.Id, bytes, "../Quarterly report.PDF", "Application/PDF");

        Assert.Equal(c.Id, dto.CaseId);
        Assert.Equal("Quarterly report.PDF", dto.FileName);
        Assert.Equal("application/pdf", dto.ContentType);
        Assert.Equal(bytes.Length, dto.SizeBytes);
        Assert.Equal("caseworker@example.test", dto.UploadedBy);
        Assert.Equal(h.Time.GetUtcNow(), dto.UploadedAtUtc);
        Assert.Equal(TimeSpan.Zero, dto.UploadedAtUtc.Offset);

        await using var db = database.CreateContext();
        var row = await db.Attachments.AsNoTracking().SingleAsync(a => a.Id == dto.Id);
        Assert.Equal(AttachmentTestHarness.Sha256(bytes), row.Sha256);
        Assert.Equal(dto.Id.ToString("N"), row.StorageKey);
        Assert.Equal("cw-id", row.UploadedById);
        var stored = Assert.Single(h.FilesUnderRoot());
        Assert.Equal(Path.Combine(h.Root, "blobs", FileSystemAttachmentStore.ShardOf(row.StorageKey), row.StorageKey), stored);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(stored));

        Assert.Equal(auditBefore + 1, await h.Cases.CountAuditAsync(c.Id));
        var audit = Ok(await h.Cases.Service.GetAuditTrailAsync(c.Id, CancellationToken.None))[^1];
        Assert.Equal(AuditActions.AttachmentUploaded, audit.Action);
        Assert.Equal("caseworker@example.test", audit.Actor);
        Assert.Contains(dto.Id.ToString("D"), audit.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("Quarterly", audit.Details, StringComparison.Ordinal);
        Assert.Contains(h.Logs.Collector.GetSnapshot(), r => r.Id.Id == 4101);
    }

    [Fact]
    public async Task An_upload_does_not_change_the_case_version_so_it_never_causes_an_edit_conflict()
    {
        using var h = new AttachmentTestHarness(database);
        var c = await h.CreateCaseAsync();

        await h.UploadAsync(c.Id, [1, 2, 3], "notes.txt", "text/plain");

        Assert.Equal(c.Version, (await h.Cases.LoadAsync(c.Id)).Version);
    }

    [Fact]
    public async Task A_user_without_upload_permission_is_Forbidden_and_nothing_is_stored()
    {
        using var h = new AttachmentTestHarness(database);
        var c = await h.CreateCaseAsync();
        h.Actor = TestActors.ViewOnly;

        var result = await h.Service.UploadAsync(c.Id, "a.txt", "text/plain", 3, new MemoryStream([1, 2, 3]), CancellationToken.None);

        Fail(result, ErrorKind.Forbidden);
        Assert.Empty(h.FilesUnderRoot());
        var denial = Assert.Single(h.Logs.Collector.GetSnapshot(), r => r.Id.Id == SecurityEvents.AuthorizationDeniedId);
        Assert.Equal(Permissions.AttachmentsUpload, denial.GetStructuredStateValue("Permission"));
    }

    [Fact]
    public async Task An_unknown_case_is_NotFound()
    {
        using var h = new AttachmentTestHarness(database);

        var result = await h.Service.UploadAsync(long.MaxValue, "a.txt", "text/plain", 3, new MemoryStream([1, 2, 3]), CancellationToken.None);

        Assert.Equal(AttachmentErrors.CaseNotFoundCode, Fail(result, ErrorKind.NotFound).Code);
        Assert.Empty(h.FilesUnderRoot());
    }

    [Fact]
    public async Task A_closed_case_accepts_no_attachments()
    {
        using var h = new AttachmentTestHarness(database);
        var c = await h.Cases.MoveAsync(await h.CreateCaseAsync(), CaseStatus.Triaged, CaseStatus.InProgress, CaseStatus.Resolved, CaseStatus.Closed);

        var result = await h.Service.UploadAsync(c.Id, "a.txt", "text/plain", 3, new MemoryStream([1, 2, 3]), CancellationToken.None);

        Assert.Equal(AttachmentErrors.CaseClosedCode, Fail(result, ErrorKind.Validation).Code);
        Assert.Empty(h.FilesUnderRoot());
    }

    public static TheoryData<string, string, long, int, string> InvalidUploads() => new()
    {
        { "script.html", "text/html", 3, 3, AttachmentErrors.TypeNotAllowedCode },
        { "report.pdf", "image/png", 3, 3, AttachmentErrors.TypeNotAllowedCode },          // pair mismatch
        { "report.exe", "application/pdf", 3, 3, AttachmentErrors.TypeNotAllowedCode },
        { "noextension", "application/pdf", 3, 3, AttachmentErrors.TypeNotAllowedCode },
        { "empty.txt", "text/plain", 0, 0, AttachmentErrors.EmptyCode },
        { "big.pdf", "application/pdf", 10_485_761, 0, AttachmentErrors.TooLargeCode },    // rejected before reading
        { "lying.pdf", "application/pdf", 10, 20, AttachmentErrors.SizeMismatchCode },     // more bytes than declared
        { "short.pdf", "application/pdf", 20, 10, AttachmentErrors.SizeMismatchCode },     // fewer bytes than declared
    };

    [Theory]
    [MemberData(nameof(InvalidUploads))]
    public async Task Invalid_uploads_are_rejected_with_a_clear_message_and_leave_nothing_committed_or_stored(
        string fileName, string contentType, long declaredSize, int actualSize, string expectedCode)
    {
        using var h = new AttachmentTestHarness(database);
        var c = await h.CreateCaseAsync();
        var auditBefore = await h.Cases.CountAuditAsync(c.Id);
        var attachmentsBefore = await h.CountAttachmentsAsync();

        var result = await h.Service.UploadAsync(c.Id, fileName, contentType, declaredSize, new MemoryStream(new byte[actualSize]), CancellationToken.None);

        var error = Fail(result, ErrorKind.Validation);
        Assert.Equal(expectedCode, error.Code);
        Assert.NotNull(error.FieldErrors?[AttachmentErrors.FileField]);
        Assert.Equal(auditBefore, await h.Cases.CountAuditAsync(c.Id));
        Assert.Equal(attachmentsBefore, await h.CountAttachmentsAsync());
        Assert.Empty(h.FilesUnderRoot());
        if (expectedCode is AttachmentErrors.TypeNotAllowedCode or AttachmentErrors.TooLargeCode)
        {
            Assert.Contains(".pdf, .png, .jpg, .jpeg, .txt, .docx", error.Message, StringComparison.Ordinal);
            Assert.Contains("10 MiB", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task An_interrupted_stream_is_rejected_and_leaves_no_temporary_file()
    {
        using var h = new AttachmentTestHarness(database);
        var c = await h.CreateCaseAsync();

        var result = await h.Service.UploadAsync(c.Id, "a.txt", "text/plain", 100_000, new BrokenStream(50_000), CancellationToken.None);

        Assert.Equal(AttachmentErrors.IncompleteCode, Fail(result, ErrorKind.Validation).Code);
        Assert.Empty(h.FilesUnderRoot());
    }

    [Fact]
    public async Task Cancellation_before_commit_propagates_and_leaves_nothing_behind()
    {
        using var h = new AttachmentTestHarness(database);
        var c = await h.CreateCaseAsync();
        using var cts = new CancellationTokenSource();
        var stream = new CancellingStream(cts, length: 20_000);
        var before = await h.CountAttachmentsAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            h.Service.UploadAsync(c.Id, "a.txt", "text/plain", 20_000, stream, cts.Token));

        Assert.Empty(h.FilesUnderRoot());
        Assert.Equal(before, await h.CountAttachmentsAsync());
    }

    [Fact]
    public async Task A_database_failure_after_the_file_is_written_commits_nothing_and_removes_the_file()
    {
        using var h = new AttachmentTestHarness(database);
        var c = await h.CreateCaseAsync();
        var auditBefore = await h.Cases.CountAuditAsync(c.Id);
        var attachmentsBefore = await h.CountAttachmentsAsync();
        h.Faults.FailBeforeCommit = true;

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            h.Service.UploadAsync(c.Id, "a.txt", "text/plain", 3, new MemoryStream([1, 2, 3]), CancellationToken.None));

        Assert.Equal(auditBefore, await h.Cases.CountAuditAsync(c.Id));
        Assert.Equal(attachmentsBefore, await h.CountAttachmentsAsync());
        Assert.Empty(h.FilesUnderRoot());
        Assert.Contains(h.Logs.Collector.GetSnapshot(), r => r.Id.Id == 4107);
    }

    [Fact]
    public async Task When_the_compensating_delete_fails_it_is_logged_and_the_reconciler_removes_the_file_later()
    {
        using var h = new AttachmentTestHarness(database);
        var c = await h.CreateCaseAsync();
        h.Faults.FailBeforeCommit = true;
        h.Store.FailDelete = true;

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            h.Service.UploadAsync(c.Id, "a.txt", "text/plain", 3, new MemoryStream([1, 2, 3]), CancellationToken.None));

        Assert.Contains(h.Logs.Collector.GetSnapshot(), r => r.Id.Id == 4104);
        var orphan = Assert.Single(h.FilesUnderRoot());
        AttachmentReconcilerTests.Age(orphan, TimeSpan.FromHours(2));

        await AttachmentReconcilerTests.Reconciler(h).RunAsync(CancellationToken.None);

        Assert.Empty(h.FilesUnderRoot());
    }

    [Fact]
    public async Task A_failure_reported_after_the_commit_is_recognized_as_a_successful_upload()
    {
        using var h = new AttachmentTestHarness(database);
        var c = await h.CreateCaseAsync();
        h.CommitFaults.FailAfterCommit = true;

        var result = await h.Service.UploadAsync(c.Id, "a.txt", "text/plain", 3, new MemoryStream([1, 2, 3]), CancellationToken.None);

        var dto = Ok(result);
        await using var db = database.CreateContext();
        Assert.True(await db.Attachments.AnyAsync(a => a.Id == dto.Id));
        Assert.Single(h.FilesUnderRoot());
        Assert.Contains(h.Logs.Collector.GetSnapshot(), r => r.Id.Id == 4108);
    }

    [Fact]
    public async Task When_the_commit_cannot_be_confirmed_the_file_is_kept_and_the_outcome_is_reported_as_unknown()
    {
        using var h = new AttachmentTestHarness(database);
        var c = await h.CreateCaseAsync();
        h.Faults.FailBeforeCommit = true;
        h.Faults.BreakQueriesOnFailure = h.QueryFaults;

        var result = await h.Service.UploadAsync(c.Id, "a.txt", "text/plain", 3, new MemoryStream([1, 2, 3]), CancellationToken.None);
        h.QueryFaults.Fail = false;

        Assert.Equal(AttachmentErrors.OutcomeUnknownCode, Fail(result, ErrorKind.ExternalUnavailable).Code);
        Assert.Single(h.FilesUnderRoot());                                   // kept for reconciliation, not deleted
        Assert.Contains(h.Logs.Collector.GetSnapshot(), r => r.Id.Id == 4106);
    }

    [Fact]
    public async Task A_storage_failure_before_commit_commits_nothing_and_leaves_no_temporary_file()
    {
        using var h = new AttachmentTestHarness(database);
        var c = await h.CreateCaseAsync();
        h.Store.FailPromote = true;
        var before = await h.CountAttachmentsAsync();

        await Assert.ThrowsAsync<IOException>(() =>
            h.Service.UploadAsync(c.Id, "a.txt", "text/plain", 3, new MemoryStream([1, 2, 3]), CancellationToken.None));

        Assert.Empty(h.FilesUnderRoot());
        Assert.Equal(before, await h.CountAttachmentsAsync());
    }

    [Fact]
    public async Task A_case_closed_while_the_file_is_streaming_does_not_get_the_attachment()
    {
        using var h = new AttachmentTestHarness(database);
        var resolved = await h.Cases.MoveAsync(await h.CreateCaseAsync(), CaseStatus.Triaged, CaseStatus.InProgress, CaseStatus.Resolved);
        var auditBefore = await h.Cases.CountAuditAsync(resolved.Id);
        var before = await h.CountAttachmentsAsync();
        var stream = new ActionOnFirstReadStream(20_000, () => h.Cases.MoveAsync(resolved, CaseStatus.Closed));

        var result = await h.Service.UploadAsync(resolved.Id, "a.txt", "text/plain", 20_000, stream, CancellationToken.None);

        Assert.Equal(AttachmentErrors.CaseClosedCode, Fail(result, ErrorKind.Validation).Code);
        Assert.Equal(before, await h.CountAttachmentsAsync());
        Assert.Equal(auditBefore + 1, await h.Cases.CountAuditAsync(resolved.Id));   // only the Closed transition
        Assert.Empty(h.FilesUnderRoot());
    }

    [Fact]
    public async Task When_a_retry_finds_the_case_closed_after_its_first_commit_landed_the_upload_is_reported_as_done()
    {
        using var h = new AttachmentTestHarness(database);
        var c = await h.CreateCaseAsync();
        h.RetrySimulator.Mode = RetryAfterCommitMode.Committed;

        var dto = Ok(await h.Service.UploadAsync(c.Id, "a.txt", "text/plain", 3, new MemoryStream([1, 2, 3]), CancellationToken.None));

        await using var db = database.CreateContext();
        Assert.True(await db.Attachments.AnyAsync(a => a.Id == dto.Id));
        Assert.Single(h.FilesUnderRoot());                                   // committed content is never deleted
        Assert.Contains(h.Logs.Collector.GetSnapshot(), r => r.Id.Id == 4101);
        Assert.DoesNotContain(h.Logs.Collector.GetSnapshot(),
            r => r.Id.Id == 4102 && (string?)r.GetStructuredStateValue("ErrorCode") == AttachmentErrors.CaseClosedCode);
    }

    [Fact]
    public async Task When_a_retry_finds_the_case_closed_and_the_commit_cannot_be_confirmed_the_file_is_kept()
    {
        using var h = new AttachmentTestHarness(database);
        var c = await h.CreateCaseAsync();
        h.RetrySimulator.Mode = RetryAfterCommitMode.Uncertain;

        var result = await h.Service.UploadAsync(c.Id, "a.txt", "text/plain", 3, new MemoryStream([1, 2, 3]), CancellationToken.None);
        h.QueryFaults.Fail = false;

        Assert.Equal(AttachmentErrors.OutcomeUnknownCode, Fail(result, ErrorKind.ExternalUnavailable).Code);
        Assert.Single(h.FilesUnderRoot());
        Assert.Contains(h.Logs.Collector.GetSnapshot(), r => r.Id.Id == 4106);
    }

    [Fact]
    public async Task A_storage_failure_while_receiving_is_logged_as_a_server_error_not_blamed_on_the_client()
    {
        var file = Path.Combine(Path.GetTempPath(), "caseintake-not-a-dir-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(file, "x");   // the root is a file, so tmp/ cannot be created
        try
        {
            using var h = new AttachmentTestHarness(database, file);
            var c = await h.CreateCaseAsync();

            await Assert.ThrowsAnyAsync<IOException>(() =>
                h.Service.UploadAsync(c.Id, "a.txt", "text/plain", 3, new MemoryStream([1, 2, 3]), CancellationToken.None));

            var failure = Assert.Single(h.Logs.Collector.GetSnapshot(), r => r.Id.Id == 4109);
            Assert.Equal(LogLevel.Error, failure.Level);
            Assert.DoesNotContain(h.Logs.Collector.GetSnapshot(), r => r.Id.Id == 4102);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task A_source_stream_that_times_out_is_reported_as_an_incomplete_upload()
    {
        using var h = new AttachmentTestHarness(database);
        var c = await h.CreateCaseAsync();

        var result = await h.Service.UploadAsync(c.Id, "a.txt", "text/plain", 10, new TimingOutStream(), CancellationToken.None);

        Assert.Equal(AttachmentErrors.IncompleteCode, Fail(result, ErrorKind.Validation).Code);
        Assert.Empty(h.FilesUnderRoot());
    }

    public static TheoryData<string, string, byte[]> MismatchedContent() => new()
    {
        { "report.pdf", "application/pdf", "<html><script>alert(1)</script></html>"u8.ToArray() },
        { "scan.png", "image/png", "%PDF-1.7 not a png"u8.ToArray() },
        { "photo.jpg", "image/jpeg", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 } },
        { "letter.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "MZ executable"u8.ToArray() },
    };

    [Theory]
    [MemberData(nameof(MismatchedContent))]
    public async Task Content_that_does_not_start_like_its_claimed_type_is_rejected(string fileName, string contentType, byte[] bytes)
    {
        using var h = new AttachmentTestHarness(database);
        var c = await h.CreateCaseAsync();
        var before = await h.CountAttachmentsAsync();

        var result = await h.Service.UploadAsync(c.Id, fileName, contentType, bytes.Length, new MemoryStream(bytes), CancellationToken.None);

        Assert.Equal(AttachmentErrors.ContentMismatchCode, Fail(result, ErrorKind.Validation).Code);
        Assert.Equal(before, await h.CountAttachmentsAsync());
        Assert.Empty(h.FilesUnderRoot());
    }

    public static TheoryData<string, string, byte[]> GenuineContent() => new()
    {
        { "scan.png", "image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2 } },
        { "photo.jpeg", "image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2 } },
        { "letter.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", new byte[] { 0x50, 0x4B, 0x03, 0x04, 1 } },
        { "notes.txt", "text/plain", "any text, no signature"u8.ToArray() },
    };

    [Theory]
    [MemberData(nameof(GenuineContent))]
    public async Task Content_that_starts_like_its_type_is_accepted(string fileName, string contentType, byte[] bytes)
    {
        using var h = new AttachmentTestHarness(database);
        var c = await h.CreateCaseAsync();

        var dto = await h.UploadAsync(c.Id, bytes, fileName, contentType);

        Assert.Equal(contentType, dto.ContentType);
    }

    [Fact]
    public async Task Stored_content_whose_size_no_longer_matches_is_not_served()
    {
        using var h = new AttachmentTestHarness(database);
        var c = await h.CreateCaseAsync();
        var dto = await h.UploadAsync(c.Id, AttachmentTestHarness.RandomBytes(1000));
        var blob = Assert.Single(h.FilesUnderRoot());
        await File.WriteAllBytesAsync(blob, AttachmentTestHarness.RandomBytes(10));   // truncated or replaced

        Fail(await h.Service.OpenReadAsync(c.Id, dto.Id, CancellationToken.None), ErrorKind.NotFound);

        var corrupt = Assert.Single(h.Logs.Collector.GetSnapshot(), r => r.Id.Id == 4112);
        Assert.Equal("10", corrupt.GetStructuredStateValue("ActualBytes"));
        Assert.Equal("1000", corrupt.GetStructuredStateValue("ExpectedBytes"));
    }

    [Fact]
    public async Task The_list_is_newest_first_and_needs_cases_view()
    {
        using var h = new AttachmentTestHarness(database);
        var c = await h.CreateCaseAsync();
        var first = await h.UploadAsync(c.Id, [1], "first.txt", "text/plain");
        var second = await h.UploadAsync(c.Id, [2], "second.txt", "text/plain");
        var other = await h.CreateCaseAsync();
        await h.UploadAsync(other.Id, [3], "other.txt", "text/plain");

        Assert.Equal([second.Id, first.Id], Ok(await h.Service.ListAsync(c.Id, CancellationToken.None)).Select(a => a.Id));

        h.Actor = TestActors.NoPermissions;
        Fail(await h.Service.ListAsync(c.Id, CancellationToken.None), ErrorKind.Forbidden);
        h.Actor = TestActors.ViewOnly;
        Assert.Equal(2, Ok(await h.Service.ListAsync(c.Id, CancellationToken.None)).Count);
        Fail(await h.Service.ListAsync(long.MaxValue, CancellationToken.None), ErrorKind.NotFound);
    }

    [Fact]
    public async Task Content_is_read_back_only_through_its_own_case()
    {
        using var h = new AttachmentTestHarness(database);
        var c = await h.CreateCaseAsync();
        var other = await h.CreateCaseAsync();
        var bytes = AttachmentTestHarness.RandomBytes(5000);
        var dto = await h.UploadAsync(c.Id, bytes);

        var content = Ok(await h.Service.OpenReadAsync(c.Id, dto.Id, CancellationToken.None));
        await using (content.Content)
        {
            using var copy = new MemoryStream();
            await content.Content.CopyToAsync(copy);
            Assert.Equal(bytes, copy.ToArray());
        }

        Assert.Equal(AttachmentErrors.AttachmentNotFoundCode, Fail(await h.Service.OpenReadAsync(other.Id, dto.Id, CancellationToken.None), ErrorKind.NotFound).Code);
        Fail(await h.Service.OpenReadAsync(c.Id, Guid.NewGuid(), CancellationToken.None), ErrorKind.NotFound);
        h.Actor = TestActors.ViewOnly;
        Fail(await h.Service.OpenReadAsync(c.Id, dto.Id, CancellationToken.None), ErrorKind.Forbidden);
    }

    [Fact]
    public async Task Missing_stored_content_is_NotFound_with_a_diagnostic_event_and_no_path()
    {
        using var h = new AttachmentTestHarness(database);
        var c = await h.CreateCaseAsync();
        var dto = await h.UploadAsync(c.Id, [1, 2, 3]);
        File.Delete(Assert.Single(h.FilesUnderRoot()));

        Fail(await h.Service.OpenReadAsync(c.Id, dto.Id, CancellationToken.None), ErrorKind.NotFound);

        var missing = Assert.Single(h.Logs.Collector.GetSnapshot(), r => r.Id.Id == 4105);
        Assert.DoesNotContain(h.Root, missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Uploads_survive_a_restart_of_the_service_and_store()
    {
        using var first = new AttachmentTestHarness(database);
        var c = await first.CreateCaseAsync();
        var bytes = AttachmentTestHarness.RandomBytes(20_000);
        var dto = await first.UploadAsync(c.Id, bytes);

        using var second = new AttachmentTestHarness(database, first.Root);   // new instances, same root and database

        Assert.Contains(Ok(await second.Service.ListAsync(c.Id, CancellationToken.None)), a => a.Id == dto.Id);
        var content = Ok(await second.Service.OpenReadAsync(c.Id, dto.Id, CancellationToken.None));
        await using var stream = content.Content;
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        Assert.Equal(AttachmentTestHarness.Sha256(bytes), AttachmentTestHarness.Sha256(copy.ToArray()));
    }

    [Fact]
    public async Task Logs_and_audit_never_contain_the_file_name_or_an_SSN_from_it()
    {
        using var h = new AttachmentTestHarness(database);
        var c = await h.CreateCaseAsync();

        var dto = await h.UploadAsync(c.Id, [1, 2, 3], "Veteran 900-45-6789 secret.txt");

        Assert.Equal("Veteran 900-45-6789 secret.txt", dto.FileName);   // the display name is kept as metadata only
        foreach (var record in h.Logs.Collector.GetSnapshot())
        {
            var text = record.Message + " " + string.Join(" ", record.StructuredState?.Select(kv => kv.Value) ?? []);
            Assert.False(SsnRedaction.ContainsSsn(text));
            Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
        }
        var audit = Ok(await h.Cases.Service.GetAuditTrailAsync(c.Id, CancellationToken.None))[^1];
        Assert.False(SsnRedaction.ContainsSsn(audit.Details));
    }

    /// <summary>Yields zeros, then fails like a browser connection dropping mid-upload.</summary>
    private sealed class BrokenStream(long failAfter) : MemoryStream(new byte[failAfter])
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Position >= failAfter ? throw new IOException("The client disconnected.") : base.ReadAsync(buffer, cancellationToken);
    }

    /// <summary>Runs an action (another user's change) when the upload is first read, then yields zeros.</summary>
    private sealed class ActionOnFirstReadStream(int length, Func<Task> action) : MemoryStream(new byte[length])
    {
        private bool _done;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_done)
            {
                _done = true;
                await action();
            }
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }

    /// <summary>Like a Blazor Server file stream whose browser stopped sending data.</summary>
    private sealed class TimingOutStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new TimeoutException("Did not receive any data in the allotted time.");
    }

    /// <summary>Cancels the operation's token partway through the upload.</summary>
    private sealed class CancellingStream(CancellationTokenSource cts, int length) : MemoryStream(new byte[length])
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position > 0)
            {
                cts.Cancel();
            }
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, 4096)], cancellationToken);
        }
    }
}
