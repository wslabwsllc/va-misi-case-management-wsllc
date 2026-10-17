namespace CaseIntake.Identity;

/// <summary>Settings bound from the <c>Identity</c> configuration section. Defaults need no configuration.</summary>
public sealed class IdentityModuleOptions
{
    public const string SectionName = "Identity";

    /// <summary>Failed password attempts before lockout (E3-S6).</summary>
    public const int MaxFailedAccessAttempts = 5;

    /// <summary>Lockout duration (E3-S6).</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    /// <summary>How often a connected circuit re-checks its user's security stamp. Default one minute.</summary>
    public TimeSpan RevalidationInterval { get; set; } = TimeSpan.FromMinutes(1);
}
