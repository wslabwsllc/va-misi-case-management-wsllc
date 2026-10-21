using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.TestKit.Fakes;

namespace CaseIntake.Web.Tests;

/// <summary>
/// Wraps the TestKit <see cref="InMemoryCaseService"/>, recording the queries the pages send and letting a test force
/// a failure (an unavailable service, a conflict, an unexpected exception) without changing the fake's rules.
/// </summary>
public sealed class ScriptedCaseService(InMemoryCaseService inner) : ICaseService, ICaseAssignmentService
{
    public List<CaseListQuery> ListQueries { get; } = [];
    public List<UpdateCaseRequest> Updates { get; } = [];

    /// <summary>When set, ListAsync fails with this error.</summary>
    public Error? ListError { get; set; }

    /// <summary>When set, ListAsync throws this exception.</summary>
    public Exception? ListException { get; set; }

    /// <summary>When set, ListAsync waits for this task first, so a page can be observed while it is loading.</summary>
    public TaskCompletionSource? ListGate { get; set; }

    /// <summary>When set, GetAsync fails with this error.</summary>
    public Error? GetError { get; set; }

    /// <summary>When set, GetAsync throws this exception.</summary>
    public Exception? GetException { get; set; }

    /// <summary>When set, GetAsync waits for this task first.</summary>
    public TaskCompletionSource? GetGate { get; set; }

    /// <summary>When set, GetAuditTrailAsync fails with this error.</summary>
    public Error? AuditError { get; set; }

    /// <summary>When set, the next UpdateAsync fails with this error (then the setting clears).</summary>
    public Error? NextUpdateError { get; set; }

    /// <summary>When set, the next UpdateAsync throws this exception (then the setting clears).</summary>
    public Exception? NextUpdateException { get; set; }

    /// <summary>When set, CreateAsync fails with this error.</summary>
    public Error? CreateError { get; set; }

    /// <summary>When set, CreateAsync throws this exception.</summary>
    public Exception? CreateException { get; set; }

    /// <summary>When set, the next ChangeStatusAsync fails with this error (then the setting clears).</summary>
    public Error? NextStatusError { get; set; }

    /// <summary>When set, the next ChangeStatusAsync throws this exception (then the setting clears).</summary>
    public Exception? NextStatusException { get; set; }

    /// <summary>When set, the next AssignAsync fails with this error (then the setting clears).</summary>
    public Error? NextAssignError { get; set; }

    public Task<Result<CaseDetailDto>> CreateAsync(CreateCaseRequest request, CancellationToken ct)
    {
        if (CreateException is { } ex)
        {
            throw ex;
        }
        return CreateError is { } error ? Task.FromResult(Result<CaseDetailDto>.Failure(error)) : inner.CreateAsync(request, ct);
    }

    public async Task<Result<CaseDetailDto>> GetAsync(long id, CancellationToken ct)
    {
        if (GetGate is { } gate)
        {
            await gate.Task;
        }
        if (GetException is { } ex)
        {
            throw ex;
        }
        return GetError is { } error ? Result<CaseDetailDto>.Failure(error) : await inner.GetAsync(id, ct);
    }

    public Task<Result<CaseDetailDto>> UpdateAsync(long id, UpdateCaseRequest request, CancellationToken ct)
    {
        Updates.Add(request);
        if (NextUpdateException is { } ex)
        {
            NextUpdateException = null;
            throw ex;
        }
        if (NextUpdateError is { } error)
        {
            NextUpdateError = null;
            return Task.FromResult(Result<CaseDetailDto>.Failure(error));
        }
        return inner.UpdateAsync(id, request, ct);
    }

    public Task<Result<CaseDetailDto>> ChangeStatusAsync(long id, ChangeStatusRequest request, CancellationToken ct)
    {
        if (NextStatusException is { } ex)
        {
            NextStatusException = null;
            throw ex;
        }
        if (NextStatusError is { } error)
        {
            NextStatusError = null;
            return Task.FromResult(Result<CaseDetailDto>.Failure(error));
        }
        return inner.ChangeStatusAsync(id, request, ct);
    }

    public async Task<Result<PagedResult<CaseSummaryDto>>> ListAsync(CaseListQuery query, CancellationToken ct)
    {
        ListQueries.Add(query);
        if (ListGate is { } gate)
        {
            await gate.Task;
        }
        if (ListException is { } ex)
        {
            throw ex;
        }
        return ListError is { } error ? Result<PagedResult<CaseSummaryDto>>.Failure(error) : await inner.ListAsync(query, ct);
    }

    public Task<Result<IReadOnlyList<AuditEntryDto>>> GetAuditTrailAsync(long id, CancellationToken ct) =>
        AuditError is { } error ? Task.FromResult(Result<IReadOnlyList<AuditEntryDto>>.Failure(error)) : inner.GetAuditTrailAsync(id, ct);

    public Task<Result<CaseDetailDto>> AssignAsync(long id, AssignCaseRequest request, CancellationToken ct)
    {
        if (NextAssignError is { } error)
        {
            NextAssignError = null;
            return Task.FromResult(Result<CaseDetailDto>.Failure(error));
        }
        return inner.AssignAsync(id, request, ct);
    }
}
