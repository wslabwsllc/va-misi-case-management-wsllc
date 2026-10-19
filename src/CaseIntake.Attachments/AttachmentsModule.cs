using CaseIntake.Attachments.Storage;
using CaseIntake.Contracts.Attachments;
using CaseIntake.Contracts.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CaseIntake.Attachments;

/// <summary>Builds an attachment service bound to an explicitly chosen actor (the HTTP adapter for the download endpoint).</summary>
public interface IAttachmentServiceFactory
{
    IAttachmentService Create(ICurrentUser currentUser);
}

internal sealed class AttachmentServiceFactory(IServiceProvider services) : IAttachmentServiceFactory
{
    // Built once: matching the constructor by reflection on every download is avoidable work.
    private static readonly ObjectFactory<AttachmentService> Factory =
        ActivatorUtilities.CreateFactory<AttachmentService>([typeof(ICurrentUser)]);

    public IAttachmentService Create(ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        return Factory(services, [currentUser]);
    }
}

public static class AttachmentsModule
{
    /// <summary>
    /// Registers <see cref="IAttachmentService"/> (scoped, so it sees the circuit's <c>ICurrentUser</c>) and
    /// <see cref="IAttachmentServiceFactory"/> for HTTP endpoints. Requires <c>AddPersistence</c> and an
    /// <c>ICurrentUser</c> registration (E3). Options are validated at startup.
    /// </summary>
    public static IServiceCollection AddAttachmentsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        AddStorage(services);
        services.TryAddScoped<IAttachmentService, AttachmentService>();
        services.TryAddScoped<IAttachmentServiceFactory, AttachmentServiceFactory>();
        return services;
    }

    /// <summary>Registers the startup storage reconciler for the migrator; run it with <see cref="ReconcileAttachmentStorageAsync"/>.</summary>
    public static IServiceCollection AddAttachmentsMaintenance(this IServiceCollection services)
    {
        AddStorage(services);
        services.TryAddTransient<AttachmentStorageReconciler>();
        return services;
    }

    /// <summary>Removes stale temporary and unreferenced attachment files (never committed content). Never throws for storage problems.</summary>
    public static async Task ReconcileAttachmentStorageAsync(this IServiceProvider services, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AttachmentStorageReconciler>().RunAsync(ct);
    }

    private static void AddStorage(IServiceCollection services)
    {
        // Idempotent: binding configuration twice would append every configured AllowedTypes entry a second time.
        if (services.Any(d => d.ServiceType == typeof(StorageRegistered)))
        {
            return;
        }
        services.AddSingleton<StorageRegistered>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<AttachmentOptions>().BindConfiguration(AttachmentOptions.SectionName).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<AttachmentOptions>, AttachmentOptionsSetup>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AttachmentOptions>, AttachmentOptionsSetup>());
        services.TryAddSingleton<IAttachmentStore, FileSystemAttachmentStore>();
    }

    /// <summary>Marker: the options binding in <see cref="AddStorage"/> has been registered.</summary>
    private sealed class StorageRegistered;
}
