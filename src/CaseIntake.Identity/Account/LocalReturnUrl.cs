namespace CaseIntake.Identity.Account;

/// <summary>Open-redirect protection for the login and logout return URL: only application-relative paths are accepted.</summary>
public static class LocalReturnUrl
{
    /// <summary>Where a successful sign-in goes when no (valid) return URL is given: the case list landing page.</summary>
    public const string Default = "/";

    /// <summary>
    /// Returns <paramref name="returnUrl"/> when it is a local path (<c>/x</c>, not <c>//host</c>, <c>/\host</c>, a scheme
    /// or a control character), otherwise <see cref="Default"/>.
    /// </summary>
    public static string Sanitize(string? returnUrl) => IsLocal(returnUrl) ? returnUrl! : Default;

    public static bool IsLocal(string? url)
    {
        if (string.IsNullOrEmpty(url) || url[0] != '/')
        {
            return false;
        }
        if (url.Length > 1 && (url[1] == '/' || url[1] == '\\'))
        {
            return false;
        }
        return !url.Any(c => char.IsControl(c) || c == '\\');
    }
}
