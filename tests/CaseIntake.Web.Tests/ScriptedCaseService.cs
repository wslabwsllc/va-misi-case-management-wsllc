using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.TestKit.Fakes;

namespace CaseIntake.Web.Tests;

/// <summary>
/// Wraps the TestKit <see cref="InMemoryCaseService"/>, recording the queries the pages send and letting a test force
/// a failure (an unavailable service, a conflict) without changing the fake's rules.
/// </summary>
public sealed class ScriptedCaseService(InMemoryCaseService inner) : ICaseService
{
    public List<CaseListQuery> ListQueries { get; } = [];
    public List<UpdateCaseRequest> Updates { get; } = [];

    /// <summary>When set, ListAsync fails with this error.</summary>
    public Error? ListError { get; set; }

    /// <summary>When set, the next UpdateAsync fails with this error (then the setting clears).</summary>
    public Error? NextUpdateError { get; set; }

    /// <summary>When set, CreateAsync fails with this error.</summary>
    public Error? CreateError { get; set; }

    /// <summary>When set, the next ChangeStatusAsync fails with this error (then the setting clears).</summary>
    public Error? NextStatusError { get; set; }

    public Task<Result<CaseDetailDto>> CreateAsync(CreateCaseRequest request, CancellationToken ct) =>
        CreateError is { } error ? Task.FromResult(Result<CaseDetailDto>.Failure(error)) : inner.CreateAsync(request, ct);

    public Task<Result<CaseDetailDto>> GetAsync(long id, CancellationToken ct) => inner.GetAsync(id, ct);

    public Task<Result<CaseDetailDto>> UpdateAsync(long id, UpdateCaseRequest request, CancellationToken ct)
    {
        Updates.Add(request);
        if (NextUpdateError is { } error)
        {
            NextUpdateError = null;
            return Task.FromResult(Result<CaseDetailDto>.Failure(error));
        }
        return inner.UpdateAsync(id, request, ct);
    }

    public Task<Result<CaseDetailDto>> ChangeStatusAsync(long id, ChangeStatusRequest request, CancellationToken ct)
    {
        if (NextStatusError is { } error)
        {
            NextStatusError = null;
            return Task.FromResult(Result<CaseDetailDto>.Failure(error));
        }
        return inner.ChangeStatusAsync(id, request, ct);
    }

    public Task<Result<PagedResult<CaseSummaryDto>>> ListAsync(CaseListQuery query, CancellationToken ct)
    {
        ListQueries.Add(query);
        return ListError is { } error ? Task.FromResult(Result<PagedResult<CaseSummaryDto>>.Failure(error)) : inner.ListAsync(query, ct);
    }

    public Task<Result<IReadOnlyList<AuditEntryDto>>> GetAuditTrailAsync(long id, CancellationToken ct) => inner.GetAuditTrailAsync(id, ct);
}
