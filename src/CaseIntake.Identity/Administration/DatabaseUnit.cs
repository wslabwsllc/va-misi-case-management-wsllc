using CaseIntake.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CaseIntake.Identity.Administration;

/// <summary>
/// Runs one administration change as a complete database unit (plan E1-S5): a transaction inside the provider's retrying
/// execution strategy, with a fresh DI scope (and so a fresh store context and <c>UserManager</c>/<c>RoleManager</c>) on
/// every attempt, so a retry starts from the database rather than from half-applied tracked state.
/// </summary>
internal static class DatabaseUnit
{
    /// <param name="work">Returns the result and whether to commit; anything else (or an exception) rolls back.</param>
    public static async Task<T> RunAsync<T>(IDbContextFactory<AppDbContext> contexts, IServiceScopeFactory scopes,
        Func<IServiceProvider, AppDbContext, CancellationToken, Task<(T Result, bool Commit)>> work, CancellationToken ct)
    {
        await using var probe = await contexts.CreateDbContextAsync(ct);
        return await probe.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var (result, commit) = await work(scope.ServiceProvider, db, token);
            if (commit)
            {
                await transaction.CommitAsync(token);
            }
            return result;
        }, ct);
    }
}
