namespace CaseIntake.Contracts.Common;

/// <summary>Configuration names shared by the persistence registration and the readiness check, so they cannot drift.</summary>
public static class ConnectionStringNames
{
    /// <summary><c>ConnectionStrings:CaseIntake</c> (environment: <c>ConnectionStrings__CaseIntake</c>).</summary>
    public const string CaseIntake = "CaseIntake";
}
