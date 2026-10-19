using CaseIntake.Attachments;
using CaseIntake.Cases;
using CaseIntake.Identity;
using CaseIntake.Identity.Account;
using CaseIntake.Identity.Security;
using CaseIntake.Persistence;
using CaseIntake.ServiceDefaults;
using CaseIntake.VeteranLookup.Client;
using CaseIntake.Web.Components;
using CaseIntake.Web.Endpoints.Attachments;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddCasesModule(builder.Configuration);           // E2
builder.Services.AddIdentityModule(builder.Configuration);        // E3
builder.Services.AddAttachmentsModule(builder.Configuration);     // E4
builder.Services.AddVeteranLookupClient(builder.Configuration);   // E4

// Persist Data Protection keys on the 'dataprotection' volume so auth cookies survive web-container recreation.
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
{
    builder.Services.AddDataProtection()
        .SetApplicationName("CaseIntake")
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseOperationContext();
app.UseSecurityHeaders();                                         // E3
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapDefaultEndpoints();
app.MapAccountEndpoints();                                        // E3
app.MapAttachmentEndpoints();                                     // E4
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

/// <summary>Entry point type for WebApplicationFactory.</summary>
public partial class Program;
