using CaseIntake.Attachments.Storage;
using System.Net;
using System.Net.Http.Headers;
using CaseIntake.Contracts.Attachments;
using CaseIntake.Contracts.Security;
using CaseIntake.IntegrationTests.Identity;
using CaseIntake.TestKit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Testing;

namespace CaseIntake.IntegrationTests.Attachments;

/// <summary>
/// The composed web app with real Identity sign-in, a private attachment root shared with an
/// <see cref="AttachmentTestHarness"/> that performs uploads into the same database.
/// </summary>
public sealed class AttachmentWebFixture : PostgresDatabaseFixture, IAsyncLifetime
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "caseintake-web-attachments-" + Guid.NewGuid().ToString("N"));
    public CaseIntakeWebFactory Factory { get; private set; } = null!;
    public FakeLogCollector Logs => Factory.Services.GetFakeLogCollector();
    internal AttachmentTestHarness Harness { get; private set; } = null!;

    public new async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await IdentityTestSupport.SeedAsync(ConnectionString);
        await IdentityTestSupport.CreateUserAsync(ConnectionString, IdentityWebFixture.NoPermissionsUser, IdentityTestSupport.DemoPassword);
        Factory = new CaseIntakeWebFactory(ConnectionString, "http://127.0.0.1:1",
            configureServices: services => services.AddFakeLogging(),
            extraConfiguration: new Dictionary<string, string?> { ["Attachments:RootPath"] = Root });
        Harness = new AttachmentTestHarness(this, Root);
    }

    public new async Task DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
        await base.DisposeAsync();
    }

    public async Task<BrowserSession> SignedInAsync(string? userName)
    {
        var session = new BrowserSession(Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true }));
        if (userName is not null)
        {
            await session.SignInAsync(userName);
        }
        return session;
    }
}

/// <summary>E4-S6 (and the E3-S5 endpoint rules) on the real download endpoint.</summary>
public class AttachmentDownloadEndpointTests : ClassFixture<AttachmentDownloadEndpointTests, AttachmentWebFixture>
{
    private static AttachmentWebFixture fixture => Shared;

    private const string CaseWorker = "caseworker@example.test";
    private const string Supervisor = "supervisor@example.test";

    private async Task<(long CaseId, AttachmentDto Attachment, byte[] Bytes)> UploadAsync(
        string fileName = "Quarterly report.pdf", string contentType = "application/pdf", int size = 64_000)
    {
        var c = await fixture.Harness.CreateCaseAsync();
        var bytes = AttachmentTestHarness.RandomBytes(size);
        return (c.Id, await fixture.Harness.UploadAsync(c.Id, bytes, fileName, contentType), bytes);
    }

    [Test]
    public async Task An_authorized_download_returns_the_original_bytes_as_an_attachment_with_safe_headers()
    {
        var (caseId, attachment, bytes) = await UploadAsync("Résumé – 2026.pdf");
        using var session = await fixture.SignedInAsync(CaseWorker);

        using var response = await session.Client.GetAsync(AttachmentRoutes.Content(caseId, attachment.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(AttachmentTestHarness.Sha256(bytes), AttachmentTestHarness.Sha256(body));
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var disposition = response.Content.Headers.ContentDisposition!;
        Assert.Equal("attachment", disposition.DispositionType);
        Assert.Equal("Résumé – 2026.pdf", disposition.FileNameStar);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
    }

    [Test]
    public async Task Both_roles_may_download()
    {
        var (caseId, attachment, _) = await UploadAsync();

        foreach (var user in new[] { CaseWorker, Supervisor })
        {
            using var session = await fixture.SignedInAsync(user);
            using var response = await session.Client.GetAsync(AttachmentRoutes.Content(caseId, attachment.Id));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Test]
    public async Task Anonymous_requests_get_401_without_a_login_redirect()
    {
        var (caseId, attachment, _) = await UploadAsync();
        using var session = await fixture.SignedInAsync(null);

        using var response = await session.Client.GetAsync(AttachmentRoutes.Content(caseId, attachment.Id));

        // A 401, not a 302: browsers and API clients never follow it. (The framework's cookie handler still adds a
        // Location header naming the login page on a 401; it has no effect without a 3xx status.)
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Test]
    public async Task A_signed_in_user_without_the_download_permission_gets_403_and_the_denial_is_logged()
    {
        var (caseId, attachment, _) = await UploadAsync();
        using var session = await fixture.SignedInAsync(IdentityWebFixture.NoPermissionsUser);
        var url = AttachmentRoutes.Content(caseId, attachment.Id);

        using var response = await session.Client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(fixture.Logs.GetSnapshot(), r => r.Id.Id == SecurityEvents.AuthorizationDeniedId
            && (string?)r.GetStructuredStateValue("Permission") == Permissions.AttachmentsDownload
            && (string?)r.GetStructuredStateValue("ResourceId") == url);
    }

    [Test]
    public async Task An_attachment_requested_through_another_case_or_an_unknown_ID_is_404()
    {
        var (_, attachment, _) = await UploadAsync();
        var otherCase = await fixture.Harness.CreateCaseAsync();
        using var session = await fixture.SignedInAsync(CaseWorker);

        using var crossCase = await session.Client.GetAsync(AttachmentRoutes.Content(otherCase.Id, attachment.Id));
        using var unknown = await session.Client.GetAsync(AttachmentRoutes.Content(otherCase.Id, Guid.NewGuid()));
        using var unknownCase = await session.Client.GetAsync(AttachmentRoutes.Content(long.MaxValue, attachment.Id));

        Assert.Equal(HttpStatusCode.NotFound, crossCase.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownCase.StatusCode);
    }

    [Test]
    public async Task Missing_stored_content_is_a_controlled_404_with_a_diagnostic_event()
    {
        var (caseId, attachment, _) = await UploadAsync();
        var key = attachment.Id.ToString("N");
        File.Delete(Path.Combine(fixture.Root, "blobs", FileSystemAttachmentStore.ShardOf(key), key));
        using var session = await fixture.SignedInAsync(CaseWorker);

        using var response = await session.Client.GetAsync(AttachmentRoutes.Content(caseId, attachment.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(fixture.Root, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains(fixture.Logs.GetSnapshot(), r => r.Id.Id == 4105 && (string?)r.GetStructuredStateValue("AttachmentId") == attachment.Id.ToString());
    }

    [Test]
    public async Task A_completed_download_is_logged_and_a_failed_one_is_not()
    {
        var (caseId, attachment, _) = await UploadAsync();
        using var session = await fixture.SignedInAsync(CaseWorker);

        using (var notFound = await session.Client.GetAsync(AttachmentRoutes.Content(caseId, Guid.NewGuid())))
        {
            Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        }
        using var ok = await session.Client.GetAsync(AttachmentRoutes.Content(caseId, attachment.Id));
        await ok.Content.ReadAsByteArrayAsync();

        var downloaded = await WaitForAsync(() => fixture.Logs.GetSnapshot()
            .Where(r => r.Id.Id == 4103 && (string?)r.GetStructuredStateValue("CaseId") == caseId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToList());
        var single = Assert.Single(downloaded);
        Assert.Equal(attachment.Id.ToString(), single.GetStructuredStateValue("AttachmentId"));
        Assert.Equal("200", single.GetStructuredStateValue("StatusCode"));
        Assert.False(string.IsNullOrEmpty(single.GetStructuredStateValue("ActorId")));          // who downloaded
        Assert.Equal(ok.Headers.GetValues("X-Operation-Id").Single(), single.GetStructuredStateValue("OperationId"));
    }

    [Test]
    public async Task Range_requests_are_served_partially()
    {
        var (caseId, attachment, bytes) = await UploadAsync(size: 1000);
        using var session = await fixture.SignedInAsync(CaseWorker);
        using var request = new HttpRequestMessage(HttpMethod.Get, AttachmentRoutes.Content(caseId, attachment.Id));
        request.Headers.Range = new RangeHeaderValue(0, 9);

        using var response = await session.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(bytes[..10], await response.Content.ReadAsByteArrayAsync());
    }

    [Test]
    public async Task Text_files_are_still_sent_as_downloads_never_rendered_inline()
    {
        var (caseId, attachment, _) = await UploadAsync("notes.txt", "text/plain", 100);
        using var session = await fixture.SignedInAsync(CaseWorker);

        using var response = await session.Client.GetAsync(AttachmentRoutes.Content(caseId, attachment.Id));

        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
    }

    /// <summary>The completion callback runs after the client has the response, so poll briefly.</summary>
    private static async Task<List<FakeLogRecord>> WaitForAsync(Func<List<FakeLogRecord>> query)
    {
        for (var i = 0; i < 50; i++)
        {
            var records = query();
            if (records.Count > 0)
            {
                return records;
            }
            await Task.Delay(50);
        }
        return query();
    }
}
