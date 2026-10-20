using CaseIntake.Domain.Cases;

namespace CaseIntake.Cases.Seeding;

/// <summary>One step in a seeded case's history after creation.</summary>
internal sealed record SeedStep(DateTimeOffset AtUtc, string ActorUserName, CaseStatus? TargetStatus, string? EditedDescription);

/// <summary>A synthetic demo case and its history, keyed by a stable <see cref="Key"/>.</summary>
internal sealed record SeedCase(string Key, string Title, string? Description, DateTimeOffset CreatedAtUtc,
    string CreatorUserName, IReadOnlyList<SeedStep> Steps, CaseTriage Triage);

/// <summary>
/// Deterministic, clearly fictional demo cases (E2-S9). A fixed <see cref="Random"/> seed and fixed UTC base time
/// make every clean start produce the same fixtures. Histories follow the workflow and the role matrix: the
/// Supervisor triages, closes and reopens; the CaseWorker creates, starts, resolves and edits.
/// </summary>
internal static class CaseSeedData
{
    public const int RandomSeed = 20261016;
    public const int CaseCount = 72;
    public const string CaseWorkerUserName = "caseworker@example.test";
    public const string SupervisorUserName = "supervisor@example.test";

    public static readonly DateTimeOffset BaseTimeUtc = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

    private static readonly string[] Subjects =
    [
        "Water damage", "Benefit inquiry", "Address change", "Appointment scheduling", "Prescription refill",
        "Travel reimbursement", "Records request", "Claim status", "Equipment request", "Facility access",
        "Billing question", "Eligibility review",
    ];

    /// <summary>The category of each entry of <see cref="Subjects"/> (same order).</summary>
    private static readonly CaseCategory[] SubjectCategories =
    [
        CaseCategory.Facilities, CaseCategory.Benefits, CaseCategory.ContactDetails, CaseCategory.Appointments, CaseCategory.Pharmacy,
        CaseCategory.Travel, CaseCategory.Records, CaseCategory.Claims, CaseCategory.Equipment, CaseCategory.Facilities,
        CaseCategory.Billing, CaseCategory.Eligibility,
    ];

    private static readonly CasePriority[] PriorityCycle = [CasePriority.Medium, CasePriority.High, CasePriority.Low, CasePriority.Urgent, CasePriority.Medium];

    /// <summary>
    /// Seeded due dates are spread from <see cref="FirstDueOn"/> over <see cref="DueSpreadDays"/> days, so the demo shows a
    /// mix of overdue, due-soon and later cases for about a year instead of every open case being overdue. The E6
    /// migration applies the same formula to seeded rows of existing databases.
    /// </summary>
    public static readonly DateOnly FirstDueOn = new(2026, 6, 1);
    public const int DueSpreadDays = 420;
    public const int DueStepDays = 37;

    /// <summary>Triage fields of the seeded case at <paramref name="index"/> (0-based), derived from the index only.</summary>
    public static CaseTriage TriageFor(int index) => new(
        PriorityCycle[index % PriorityCycle.Length],
        SubjectCategories[index % Subjects.Length],
        (index % 3) switch { 0 => CaseWorkerUserName, 1 => SupervisorUserName, _ => null },
        FirstDueOn.AddDays(index * DueStepDays % DueSpreadDays));

    private static readonly string[] Qualifiers =
    [
        "follow-up needed", "initial contact", "missing form", "callback requested", "second request",
        "priority review", "documents received", "awaiting reply", "routine check", "clarification needed",
    ];

    private static readonly string[] Places =
    [
        "Example County office", "Testville clinic", "Sampleton center", "Demo Harbor annex", "Placeholder Springs site",
    ];

    private static readonly string[] Details =
    [
        "Caller reported water leaking near the main entrance.",
        "Requester asked for a status update by phone.",
        "Paperwork was submitted without a signature page.",
        "A follow-up visit was requested for next month.",
        "The mailing address on file appears to be outdated.",
        "Receipts were attached for review.",
    ];

    // Paths after creation; every status appears, including a reopened (Resolved -> InProgress) history.
    private static readonly CaseStatus[][] Paths =
    [
        [],
        [CaseStatus.Triaged],
        [CaseStatus.Triaged, CaseStatus.InProgress],
        [CaseStatus.Triaged, CaseStatus.InProgress, CaseStatus.Resolved],
        [CaseStatus.Triaged, CaseStatus.InProgress, CaseStatus.Resolved, CaseStatus.Closed],
        [CaseStatus.Triaged, CaseStatus.InProgress, CaseStatus.Resolved, CaseStatus.InProgress],
    ];

    public static IReadOnlyList<SeedCase> Build()
    {
        var random = new Random(RandomSeed);
        List<SeedCase> cases = [];
        for (var i = 0; i < CaseCount; i++)
        {
            var number = i + 1;
            var title = $"{Subjects[i % Subjects.Length]} - {Qualifiers[random.Next(Qualifiers.Length)]} ({number:000})";
            var description = i % 6 == 5
                ? null
                : $"Synthetic demo case for the {Places[random.Next(Places.Length)]}. {Details[random.Next(Details.Length)]}";
            var created = BaseTimeUtc.AddHours(i * 9).AddMinutes(random.Next(0, 60));

            List<SeedStep> steps = [];
            var at = created;
            if (description is not null && i % 5 == 0)
            {
                at = at.AddMinutes(random.Next(10, 120));
                steps.Add(new SeedStep(at, CaseWorkerUserName, null, description + " Contact preference confirmed."));
            }

            var from = CaseWorkflow.InitialStatus;
            foreach (var target in Paths[i % Paths.Length])
            {
                at = at.AddHours(random.Next(2, 48));
                steps.Add(new SeedStep(at, ActorFor(from, target), target, null));
                from = target;
            }

            cases.Add(new SeedCase($"case-{number:000}", title, description, created, CaseWorkerUserName, steps, TriageFor(i)));
        }

        return cases;
    }

    private static string ActorFor(CaseStatus from, CaseStatus to) =>
        (from, to) is (CaseStatus.New, CaseStatus.Triaged) or (CaseStatus.Resolved, _)
            ? SupervisorUserName
            : CaseWorkerUserName;
}
