using CaseIntake.Contracts.Security;
using CaseIntake.Identity.Account;
using CaseIntake.Identity.Administration;
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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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
            // A visitor opening the site root sees the landing page; deep links still go to sign-in with a return URL.
            var redirectToLogin = options.Events.OnRedirectToLogin;
            options.Events.OnRedirectToLogin = context =>
            {
                if (context.Request.Path == "/")
                {
                    context.RedirectUri = context.Request.PathBase + AccountRoutes.Welcome;
                }
                return redirectToLogin(context);
            };
        });
        // Cookie-authenticated HTTP requests re-check the security stamp as often as circuits do.
        services.AddOptions<SecurityStampValidatorOptions>().Configure<IOptions<IdentityModuleOptions>>(
            (options, module) => options.ValidationInterval = module.Value.RevalidationInterval);

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
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPermissionStore, DatabasePermissionStore>();
        services.AddSingleton<PermissionResolver>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, DenialLoggingAuthorizationResultHandler>();

        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CircuitCurrentUser>();
        services.AddKeyedScoped<ICurrentUser, HttpCurrentUser>(CurrentUserSources.Http);
        services.AddScoped<AccountSignIn>();
        services.AddScoped<AccountPasswordChange>();
        services.AddScoped<IUserAdministrationService, UserAdministrationService>();
        services.AddScoped<IRoleAdministrationService, RoleAdministrationService>();
        services.AddScoped<Contracts.Auditing.IAuditLogService, Auditing.AuditLogService>();
        services.AddScoped<Contracts.Diagnostics.IErrorLogService, Diagnostics.ErrorLogService>();
        services.AddScoped<Contracts.Diagnostics.ISystemHealthService, Diagnostics.SystemHealthService>();
        // Captured Error and Critical log events are stored by one background writer (ADR 0015).
        services.AddSingleton<Diagnostics.ErrorLogWriter>();
        services.AddSingleton<Contracts.Diagnostics.IErrorLogSink>(sp => sp.GetRequiredService<Diagnostics.ErrorLogWriter>());
        services.AddHostedService(sp => sp.GetRequiredService<Diagnostics.ErrorLogWriter>());
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

    /// <summary>Identity's default password rules, which <see cref="ConfigureIdentity"/> leaves unchanged; shown as a form hint.</summary>
    public const string PasswordHint =
        "At least 6 characters, with an uppercase letter, a lowercase letter, a number and a symbol.";

    internal static void ConfigureIdentity(IdentityOptions options)
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = IdentityModuleOptions.MaxFailedAccessAttempts;
        options.Lockout.DefaultLockoutTimeSpan = IdentityModuleOptions.LockoutDuration;
    }
}
