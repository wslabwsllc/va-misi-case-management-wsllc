using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Domain.Cases;

namespace CaseIntake.Cases.Tests;

public class CaseListQueryTests
{
    [TestCase(1, 1, true)]
    [TestCase(1, 100, true)]
    [TestCase(0, 25, false)]
    [TestCase(-1, 25, false)]
    [TestCase(1, 0, false)]
    [TestCase(1, 101, false)]
    [TestCase(int.MaxValue, 100, false)]   // offset would overflow
    public void Paging_bounds(int page, int pageSize, bool valid)
    {
        var error = CaseListQueryValidator.Validate(new CaseListQuery(page, pageSize));

        Assert.Equal(valid, error is null);
        if (!valid)
        {
            Assert.Equal(ErrorKind.Validation, error!.Kind);
        }
    }

    [Test]
    public void Undefined_enum_values_are_rejected()
    {
        Assert.NotNull(CaseListQueryValidator.Validate(new CaseListQuery(Statuses: [(CaseStatus)9])));
        Assert.NotNull(CaseListQueryValidator.Validate(new CaseListQuery(SortBy: (CaseSortField)9)));
        Assert.NotNull(CaseListQueryValidator.Validate(new CaseListQuery(Direction: (SortDirection)9)));
    }

    [Test]
    public void Search_text_is_bounded_and_must_be_well_formed()
    {
        Assert.Null(CaseListQueryValidator.Validate(new CaseListQuery(Search: new string('s', 200))));
        Assert.NotNull(CaseListQueryValidator.Validate(new CaseListQuery(Search: new string('s', 201))));
        Assert.NotNull(CaseListQueryValidator.Validate(new CaseListQuery(Search: "wa\0ter")));
    }

    [TestCase("water", "water")]
    [TestCase("100%", "100\\%")]
    [TestCase("file_name", "file\\_name")]
    [TestCase("back\\slash", "back\\\\slash")]
    public void Like_wildcards_are_escaped(string input, string expected) =>
        Assert.Equal(expected, CaseListQueryBuilder.EscapeLike(input));
}
