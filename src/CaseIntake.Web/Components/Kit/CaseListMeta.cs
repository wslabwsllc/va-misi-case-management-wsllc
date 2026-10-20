namespace CaseIntake.Web.Components.Kit;

/// <summary>Which time a <see cref="CaseListCard"/> shows under each case.</summary>
public enum CaseListMeta
{
    Created,
    Updated,

    /// <summary>The due date and how far past or ahead of it the case is.</summary>
    Due,
}
