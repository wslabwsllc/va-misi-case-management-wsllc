using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Domain.Cases;

namespace CaseIntake.Cases.Tests;

public class CaseListQueryTests
{
    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1, 100, true)]
    [InlineData(0, 25, false)]
    [InlineData(-1, 25, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, 101, false)]
    [InlineData(int.MaxValue, 100, false)]   // offset would overflow
    public void Paging_bounds(int page, int pageSize, bool valid)
    {
        var error = CaseListQueryValidator.Validate(new CaseListQuery(page, pageSize));

        Assert.Equal(valid, error is null);
        if (!valid)
        {
            Assert.Equal(ErrorKind.Validation, error!.Kind);
        }
    }

    [Fact]
    public void Undefined_enum_values_are_rejected()
    {
        Assert.NotNull(CaseListQueryValidator.Validate(new CaseListQuery(Statuses: [(CaseStatus)9])));
        Assert.NotNull(CaseListQueryValidator.Validate(new CaseListQuery(SortBy: (CaseSortField)9)));
        Assert.NotNull(CaseListQueryValidator.Validate(new CaseListQuery(Direction: (SortDirection)9)));
    }

    [Fact]
    public void Search_text_is_bounded_and_must_be_well_formed()
    {
        Assert.Null(CaseListQueryValidator.Validate(new CaseListQuery(Search: new string('s', 200))));
        Assert.NotNull(CaseListQueryValidator.Validate(new CaseListQuery(Search: new string('s', 201))));
        Assert.NotNull(CaseListQueryValidator.Validate(new CaseListQuery(Search: "wa\0ter")));
    }

    [Theory]
    [InlineData("water", "water")]
    [InlineData("100%", "100\\%")]
    [InlineData("file_name", "file\\_name")]
    [InlineData("back\\slash", "back\\\\slash")]
    public void Like_wildcards_are_escaped(string input, string expected) =>
        Assert.Equal(expected, CaseListQueryBuilder.EscapeLike(input));
}
