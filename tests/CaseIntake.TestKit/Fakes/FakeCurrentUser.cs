using System.Collections.Immutable;
using CaseIntake.Contracts.Security;
using CaseIntake.Domain.Cases;

namespace CaseIntake.TestKit.Fakes;

/// <summary>Settable <see cref="ICurrentUser"/>. Defaults to an authenticated user holding every permission.</summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    private static readonly CaseStatus[] AllStatuses = Enum.GetValues<CaseStatus>();

    public static IEnumerable<string> AllPermissions { get; } =
    [
        Permissions.CasesView, Permissions.CasesCreate, Permissions.CasesEdit,
        Permissions.AttachmentsUpload, Permissions.AttachmentsDownload, Permissions.VeteranLookup,
        .. from f in AllStatuses from t in AllStatuses select Permissions.Transition(f, t),
    ];

    public OperationActor Actor { get; set; } = Authenticated("user-1", "Test User", AppRoles.Supervisor, AllPermissions);

    public static OperationActor Anonymous { get; } =
        new(null, null, ImmutableHashSet<string>.Empty, ImmutableHashSet<string>.Empty);

    public static OperationActor Authenticated(string userId, string displayName, string role, IEnumerable<string> permissions) =>
        new(userId, displayName, [role], [.. permissions]);

    public Task<OperationActor> GetAsync(CancellationToken ct) => Task.FromResult(Actor);
}
