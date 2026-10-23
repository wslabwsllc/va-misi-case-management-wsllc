using System.Globalization;
using CaseIntake.Attachments;
using CaseIntake.Cases;
using CaseIntake.Identity;
using CaseIntake.Identity.Account;
using CaseIntake.Identity.Security;
using CaseIntake.Persistence;
using CaseIntake.ServiceDefaults;
using CaseIntake.TestResults;
using CaseIntake.VeteranLookup.Client;
using CaseIntake.Web;
using CaseIntake.Web.Components;
using CaseIntake.Web.Endpoints.Attachments;
using CaseIntake.Web.Endpoints.Exports;
using CaseIntake.Web.Endpoints.TestResults;
using CaseIntake.Web.Health;
using CaseIntake.Web.Services;
using Microsoft.AspNetCore.DataProtection;

// Every date, time and number is shown en-US (Change 1, part 4), whatever the server's or the browser's culture. Circuits
// run on pool threads, so the process defaults are set too, not only the request culture.
CultureInfo.DefaultThreadCurrentCulture = UiText.Culture;
CultureInfo.DefaultThreadCurrentUICulture = UiText.Culture;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddCasesModule(builder.Configuration);           // E2
builder.Services.AddIdentityModule(builder.Configuration);        // E3
builder.Services.AddAttachmentsModule(builder.Configuration);     // E4
builder.Services.AddVeteranLookupClient(builder.Configuration);   // E4
builder.Services.AddTestResultsModule(builder.Configuration);     // E5 test results and evidence
builder.Services.AddHealthChecks().AddCheck<AttachmentStorageHealthCheck>("attachment-storage",
    tags: [ServiceDefaultsExtensions.ReadyTag], timeout: ServiceDefaultsExtensions.CheckTimeout);

// Persist Data Protection keys on the 'dataprotection' volume so auth cookies survive web-container recreation.
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
{
    builder.Services.AddDataProtection()
        .SetApplicationName("CaseIntake")
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddWebUi(builder.Configuration);                 // E5 component kit and UI services

var app = builder.Build();

// The framework script, CSS and JS modules are served from the content root's wwwroot. A process started from another
// working directory (the shared Docker image's WORKDIR is /src) would otherwise answer every asset request with the
// sign-in redirect and the browser would never start Blazor. Fail at startup with a clear reason instead.
WebRootGuard.EnsureWebRootExists(app.Environment);

// Outermost: the operation ID must still be current when the exception handler logs an unhandled exception, so the
// stored error and the reference on the error page match (ADR 0015).
app.UseOperationContext();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
}
// A URL that matches no endpoint gets the accessible Not Found page. Matched endpoints keep their own status codes
// and bodies (the download endpoints answer 401/403/404 without a page, E4-S6).
app.UseStatusCodePagesWithReExecute(NotFoundPages.Route, createScopeForStatusCodePages: true);
app.Use(NotFoundPages.OnlyForUnmatchedRequests);

app.UseRequestLocalization(options =>
{
    options.SetDefaultCulture(UiText.Culture.Name);
    options.SupportedCultures = options.SupportedUICultures = [UiText.Culture];
    options.RequestCultureProviders.Clear();          // Accept-Language, cookies and query strings never change it
});
app.UseSecurityHeaders();                                         // E3
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Fingerprinted, compressed static assets (including _framework/blazor.web.js). They carry no data and must load on the
// anonymous sign-in page, so they are exempt from the authenticated-user fallback policy.
app.MapStaticAssets().AllowAnonymous();
app.MapDefaultEndpoints();
app.MapAccountEndpoints();                                        // E3
app.MapAttachmentEndpoints();                                     // E4
app.MapCaseExportEndpoints();                                     // E5 case list CSV export
app.MapAuditExportEndpoints();                                    // Change 1 audit log CSV export (ADR 0014)
app.MapTestResultEndpoints();                                     // E5 test evidence downloads and exports
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

await app.RunAsync();
