using System.Reflection;
using CaseIntake.Contracts.Attachments;
using CaseIntake.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace CaseIntake.IntegrationTests.Identity;

/// <summary>
/// E3-S5 guard over the composed web host's real endpoint table, so endpoints added later (E4's attachment download)
/// are held to the authorization rules of ADR 0007 automatically.
/// </summary>
public class EndpointAuthorizationGuardTests(IdentityWebFixture fixture) : IClassFixture<IdentityWebFixture>
{
    private static readonly string[] AnonymousByDesign = ["/account/login", "/account/access-denied", "/account/logout", "/health/live", "/health/ready"];

    private IReadOnlyList<RouteEndpoint> Endpoints() =>
        [.. fixture.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()];

    private static string Route(RouteEndpoint e) => "/" + (e.RoutePattern.RawText ?? "").TrimStart('/');

    [Fact]
    public void Only_the_account_and_health_endpoints_allow_anonymous_access()
    {
        var anonymous = Endpoints()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(Route)
            .Distinct()
            .Order();

        Assert.Equal(AnonymousByDesign.Order(), anonymous);
    }

    [Fact]
    public void Minimal_API_endpoints_requiring_authorization_answer_401_or_403_instead_of_redirecting()
    {
        var offenders = Endpoints()
            .Where(e => e.Metadata.GetMetadata<MethodInfo>() is not null)            // minimal API route handlers
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Where(e => e.Metadata.GetMetadata<IDisableCookieRedirectMetadata>() is null)
            .Select(Route);

        Assert.Empty(offenders);
    }

    [Fact]
    public void Every_routable_page_requires_sign_in_or_is_an_explicit_account_page()
    {
        var pages = Endpoints().Where(e => e.Metadata.GetMetadata<ComponentTypeMetadata>() is not null).ToList();

        Assert.NotEmpty(pages);
        Assert.All(pages, page =>
        {
            var anonymous = page.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            Assert.True(!anonymous || AnonymousByDesign.Contains(Route(page)), $"{Route(page)} allows anonymous access");
            foreach (var policy in page.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).OfType<string>())
            {
                Assert.Contains(policy, RolePermissionMap.AllPermissions);   // page policies are permission names
            }
        });
    }

    [Fact]
    public void The_attachment_download_route_is_mapped_and_requires_attachments_download()
    {
        var sample = AttachmentRoutes.Content(1, Guid.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        var downloads = Endpoints().Where(e =>
        {
            var segments = e.RoutePattern.PathSegments;
            return segments.Count == sample.Length
                && segments.Zip(sample).All(p => p.First.IsSimple && p.First.Parts[0] is RoutePatternLiteralPart literal
                    ? string.Equals(literal.Content, p.Second, StringComparison.OrdinalIgnoreCase)
                    : p.First.Parts.Any(part => part.IsParameter));
        });

        Assert.NotEmpty(downloads);   // E4 maps the route; the rules below are no longer vacuous
        Assert.All(downloads, e =>
        {
            Assert.Contains(Permissions.AttachmentsDownload, e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy));
            Assert.NotNull(e.Metadata.GetMetadata<IDisableCookieRedirectMetadata>());
            Assert.Null(e.Metadata.GetMetadata<IAllowAnonymous>());
        });
    }
}
