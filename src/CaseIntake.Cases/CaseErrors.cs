using CaseIntake.Contracts.Common;

namespace CaseIntake.Cases;

/// <summary>The errors returned by <see cref="CaseService"/>. Messages are safe to show and never echo submitted values.</summary>
public static class CaseErrors
{
    public const string ForbiddenCode = "Forbidden";
    public const string NotFoundCode = "CaseNotFound";
    public const string ConflictCode = "VersionConflict";
    public const string CaseClosedCode = "CaseClosed";
    public const string InvalidTransitionCode = "InvalidTransition";
    public const string InvalidQueryCode = "InvalidQuery";
    public const string InvalidStatusCode = "InvalidStatus";

    public static Error Forbidden { get; } =
        new(ErrorKind.Forbidden, ForbiddenCode, "You do not have permission to perform this action.");

    public static Error NotFound { get; } = new(ErrorKind.NotFound, NotFoundCode, "The case was not found.");

    public static Error Conflict { get; } = new(ErrorKind.Conflict, ConflictCode,
        "This case was changed by someone else. Reload it to see the latest version, then try again.");

    public static Error CaseClosed { get; } = new(ErrorKind.Validation, CaseClosedCode, "Closed cases cannot be edited.");

    public static Error InvalidTransition { get; } = new(ErrorKind.InvalidTransition, InvalidTransitionCode,
        "The case cannot move to that status from its current status.");

    public static Error InvalidStatus { get; } = new(ErrorKind.Validation, InvalidStatusCode, "The requested status is not valid.",
        new Dictionary<string, string[]> { ["TargetStatus"] = ["Choose a valid status."] });

    public static Error InvalidQuery(IReadOnlyDictionary<string, string[]> fieldErrors) =>
        new(ErrorKind.Validation, InvalidQueryCode, "The list query is not valid.", fieldErrors);
}
