using System.Globalization;
using System.Text;
using CaseIntake.Contracts.Auditing;
using CaseIntake.Contracts.Security;

namespace CaseIntake.Web.Components.Pages.Admin;

/// <summary>Display text and links for the audit log pages (ADR 0014).</summary>
public static class AuditText
{
    /// <summary>"UserPermissionsChanged" → "User permissions changed".</summary>
    public static string Action(string action)
    {
        var text = new StringBuilder(action.Length + 8);
        for (var i = 0; i < action.Length; i++)
        {
            var c = action[i];
            if (i > 0 && char.IsUpper(c))
            {
                text.Append(' ').Append(char.ToLowerInvariant(c));
            }
            else
            {
                text.Append(c);
            }
        }
        return text.ToString();
    }

    public static string Outcome(AuditOutcome outcome) => outcome switch
    {
        AuditOutcome.Failure => "Failed",
        AuditOutcome.Denied => "Denied",
        _ => "Succeeded",
    };

    /// <summary>A permission name in a change list is shown with its label; other field names as stored.</summary>
    public static string Field(string field) => PermissionCatalog.Find(field)?.Label ?? field;

    /// <summary>Where the entry's subject is shown in the app, or null when it has no page.</summary>
    public static string? EntityHref(string? entityType, string? entityId) => (entityType, entityId) switch
    {
        (AuditEntityTypes.User, { } id) => $"admin/users/{Uri.EscapeDataString(id)}",
        (AuditEntityTypes.Role, { } id) => $"admin/roles/{Uri.EscapeDataString(id)}",
        (AuditEntityTypes.Case, { } id) => $"cases/{Uri.EscapeDataString(id)}",
        (AuditEntityTypes.TestRun, { } id) => $"test-results/runs/{Uri.EscapeDataString(id)}",
        _ => null,
    };

    /// <summary>The audit log URL that shows exactly what <paramref name="query"/> selects (page 1 and newest first omitted).</summary>
    public static string ListHref(AuditQuery query, string path = "admin/audit") =>
        Query(query) is { Length: > 0 } q ? $"{path}?{q}" : path;

    /// <summary>The query string for <paramref name="query"/>, also used by the CSV export link.</summary>
    public static string Query(AuditQuery query)
    {
        var parts = new List<string>();
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add($"{name}={Uri.EscapeDataString(value)}");
            }
        }
        Add("from", query.FromDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("to", query.ToDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("category", query.Category);
        Add("actor", query.Actor);
        Add("entityType", query.EntityType);
        Add("entityId", query.EntityId);
        Add("outcome", query.Outcome?.ToString());
        Add("q", query.Search);
        Add("order", query.OldestFirst ? "oldest" : null);
        Add("page", query.Page > 1 ? query.Page.ToString(CultureInfo.InvariantCulture) : null);
        return string.Join("&", parts);
    }

    /// <summary>Reads the query string format of <see cref="Query"/>; unreadable values are ignored.</summary>
    public static AuditQuery Parse(Func<string, string?> get, int pageSize)
    {
        static DateOnly? Date(string? value) =>
            DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        return new AuditQuery(
            Date(get("from")),
            Date(get("to")),
            Text(get("category")) is { } c && AuditCategories.All.Contains(c) ? c : null,
            Text(get("actor")),
            Text(get("entityType")),
            Text(get("entityId")),
            Enum.TryParse<AuditOutcome>(get("outcome"), ignoreCase: false, out var o) && Enum.IsDefined(o) ? o : null,
            Text(get("q")),
            get("order") == "oldest",
            int.TryParse(get("page"), NumberStyles.None, CultureInfo.InvariantCulture, out var p) && p > 0 ? p : 1,
            pageSize);
    }
}
