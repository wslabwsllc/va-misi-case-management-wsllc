using CaseIntake.Persistence;
using CaseIntake.ServiceDefaults;
using CaseIntake.Web.Components;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddPersistence(builder.Configuration);
// Module registrations (one line each, added by the owning epic):
//   builder.Services.AddIdentityModule(builder.Configuration);   // E3
//   builder.Services.AddCasesModule(builder.Configuration);      // E2
//   builder.Services.AddAttachmentsModule(builder.Configuration);// E4
//   builder.Services.AddVeteranLookupClient(builder.Configuration); // E4

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
app.UseStaticFiles();
app.UseAntiforgery();

app.MapDefaultEndpoints();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

/// <summary>Entry point type for WebApplicationFactory.</summary>
public partial class Program;
