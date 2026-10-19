using CaseIntake.Attachments;
using CaseIntake.Contracts.Attachments;
using CaseIntake.Contracts.VeteranLookup;
using CaseIntake.VeteranLookup.Client;

namespace CaseIntake.IntegrationTests.Attachments;

/// <summary>DoD #3: the composed web app uses the real E4 modules, never the TestKit fakes.</summary>
public class E4CompositionTests(AttachmentWebFixture fixture) : IClassFixture<AttachmentWebFixture>
{
    [Fact]
    public async Task The_web_app_registers_the_real_lookup_client_and_attachment_service()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();

        Assert.IsType<VeteranLookupClient>(scope.ServiceProvider.GetRequiredService<IVeteranLookupClient>());
        Assert.IsType<AttachmentService>(scope.ServiceProvider.GetRequiredService<IAttachmentService>());
        Assert.IsType<AttachmentService>(scope.ServiceProvider.GetRequiredService<IAttachmentServiceFactory>()
            .Create(new TestKit.Fakes.FakeCurrentUser()));
    }
}
