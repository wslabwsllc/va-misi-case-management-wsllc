using CaseIntake.Contracts.Attachments;
using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Security;
using CaseIntake.Contracts.VeteranLookup;
using CaseIntake.TestKit.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CaseIntake.TestKit;

public static class FakeModules
{
    /// <summary>
    /// Replaces every contract with an in-memory fake. For Development and tests only; the composed application
    /// must never register this (plan 0.5 item 3).
    /// </summary>
    public static IServiceCollection AddFakeModules(this IServiceCollection services)
    {
        services.RemoveAll<ICurrentUser>();
        services.RemoveAll<ICaseService>();
        services.RemoveAll<IAttachmentService>();
        services.RemoveAll<IVeteranLookupClient>();
        services.RemoveAll<IVeteranServiceHistoryClient>();
        services.AddSingleton<FakeCurrentUser>();
        services.AddSingleton<ICurrentUser>(sp => sp.GetRequiredService<FakeCurrentUser>());
        services.AddSingleton<ICaseService, InMemoryCaseService>();
        services.AddSingleton<IAttachmentService, InMemoryAttachmentService>();
        services.AddSingleton<FakeVeteranLookupClient>();
        services.AddSingleton<IVeteranLookupClient>(sp => sp.GetRequiredService<FakeVeteranLookupClient>());
        services.AddSingleton<FakeVeteranServiceHistoryClient>();
        services.AddSingleton<IVeteranServiceHistoryClient>(sp => sp.GetRequiredService<FakeVeteranServiceHistoryClient>());
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
