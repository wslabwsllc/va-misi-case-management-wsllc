using CaseIntake.Contracts.Security;
using CaseIntake.Identity.Account;
using CaseIntake.Identity.Authorization;
using CaseIntake.Identity.CurrentUser;
using CaseIntake.Identity.Seeding;
using CaseIntake.Persistence;
using CaseIntake.Persistence.Seeding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CaseIntake.Identity;

public static class IdentityModule
{
    public const string AuthCookieName = "CaseIntake.Auth";

    /// <summary>
    /// Registers local ASP.NET Core Identity with cookie authentication, the permission policies (one per
    /// <see cref="RolePermissionMap.AllPermissions"/> entry, policy name = permission name), a fallback policy that
    /// requires an authenticated user, the revalidating authentication state provider and both <see cref="ICurrentUser"/>
    /// adapters: the circuit adapter unkeyed, the HTTP adapter under <see cref="CurrentUserSources.Http"/>.
    /// Requires <c>AddPersistence</c>. The Identity EF stores use the request-scoped <see cref="AppDbContext"/> that
    /// <c>AddDbContextFactory</c> registers; only static SSR account requests resolve them.
    /// </summary>
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(IdentityModuleOptions.SectionName);
        services.AddOptions<IdentityModuleOptions>().Bind(section);
        var revalidationInterval = section.GetValue<TimeSpan?>(nameof(IdentityModuleOptions.RevalidationInterval))
            ?? new IdentityModuleOptions().RevalidationInterval;

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies();
        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = AccountRoutes.Login;
            options.LogoutPath = AccountRoutes.Logout;
            options.AccessDeniedPath = AccountRoutes.AccessDenied;
            options.ReturnUrlParameter = "ReturnUrl";
            options.Cookie.Name = AuthCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            // Spec 4.5 serves the app over plain HTTP on localhost:8080, so the cookie cannot be Secure-only.
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
        });
        // Cookie-authenticated HTTP requests re-check the security stamp as often as circuits do.
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = revalidationInterval);

        services.AddIdentityCore<AppUser>(ConfigureIdentity)
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager();

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            foreach (var permission in RolePermissionMap.AllPermissions)
            {
                options.AddPolicy(permission, policy => policy
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(permission)));
            }
        });
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, DenialLoggingAuthorizationResultHandler>();

        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CircuitCurrentUser>();
        services.AddKeyedScoped<ICurrentUser, HttpCurrentUser>(CurrentUserSources.Http);
        services.AddScoped<AccountSignIn>();
        return services;
    }

    /// <summary>
    /// Registers the Identity stores (no cookies, no sign-in) and the demo role/user seed (order 10, before the case
    /// seed) with the migrator. The password comes from <c>Seed:DemoPassword</c>.
    /// </summary>
    public static IServiceCollection AddIdentitySeed(this IServiceCollection services)
    {
        services.AddIdentityCore<AppUser>(ConfigureIdentity)
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AppDbContext>();
        return services.AddSeedContributor<IdentitySeedContributor>();
    }

    internal static void ConfigureIdentity(IdentityOptions options)
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = IdentityModuleOptions.MaxFailedAccessAttempts;
        options.Lockout.DefaultLockoutTimeSpan = IdentityModuleOptions.LockoutDuration;
    }
}
