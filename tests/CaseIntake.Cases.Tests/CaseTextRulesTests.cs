using CaseIntake.Contracts.Cases;
using CaseIntake.Contracts.Common;
using CaseIntake.Domain.Cases;

namespace CaseIntake.Cases.Tests;

public class CaseTextRulesTests
{
    private const string Supplementary = "\U0001D538";   // MATHEMATICAL DOUBLE-STRUCK CAPITAL A: one scalar, two UTF-16 units

    [Theory]
    [InlineData(0, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public void Title_length_boundaries(int length, bool valid)
    {
        var error = CaseRequestValidator.Validate(new CreateCaseRequest(new string('t', length), null, null));

        AssertTitle(valid, error);
    }

    [Theory]
    [InlineData(4000, true)]
    [InlineData(4001, false)]
    public void Description_length_boundaries(int length, bool valid)
    {
        var error = CaseRequestValidator.Validate(new CreateCaseRequest("Valid title", new string('d', length), null));

        if (valid)
        {
            Assert.Null(error);
        }
        else
        {
            Assert.NotNull(error);
            Assert.Equal(ErrorKind.Validation, error.Kind);
            Assert.Equal([CaseTextRules.DescriptionLengthMessage], error.FieldErrors![CaseFieldNames.Description]);
            Assert.False(error.FieldErrors.ContainsKey(CaseFieldNames.Title));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Description_is_optional(string? description) =>
        Assert.Null(CaseRequestValidator.Validate(new CreateCaseRequest("Valid title", description, null)));

    [Theory]
    [InlineData("     ")]
    [InlineData("\t\n  ")]
    public void Whitespace_only_title_is_required_error(string title)
    {
        var error = CaseRequestValidator.Validate(new CreateCaseRequest(title, null, null));

        Assert.Equal([CaseTextRules.TitleRequiredMessage], error!.FieldErrors![CaseFieldNames.Title]);
    }

    [Fact]
    public void Title_is_trimmed_before_counting()
    {
        Assert.NotNull(CaseRequestValidator.Validate(new CreateCaseRequest("   abcd   ", null, null)));
        Assert.Null(CaseRequestValidator.Validate(new CreateCaseRequest("   abcde   ", null, null)));
        Assert.Null(CaseRequestValidator.Validate(new CreateCaseRequest(" " + new string('x', 200) + " ", null, null)));
        Assert.Equal("abcde", CaseTextRules.NormalizeTitle("  abcde \t"));
    }

    [Fact]
    public void Lengths_count_unicode_scalar_values_not_utf16_units()
    {
        var five = string.Concat(Enumerable.Repeat(Supplementary, 5));          // 10 UTF-16 units
        var twoHundredOne = string.Concat(Enumerable.Repeat(Supplementary, 201));
        var fourThousand = string.Concat(Enumerable.Repeat(Supplementary, 4000));  // 8000 UTF-16 units

        Assert.Null(CaseRequestValidator.Validate(new CreateCaseRequest(five, fourThousand, null)));
        Assert.NotNull(CaseRequestValidator.Validate(new CreateCaseRequest(twoHundredOne, null, null)));
        Assert.Equal(5, CaseTextRules.CountScalarValues(five));
    }

    // Built in code and not pre-enumerated: attribute arguments and xUnit discovery serialize strings as UTF-8, which
    // would replace lone surrogates with U+FFFD.
    public static TheoryData<string> MalformedText() => new()
    {
        "Valid\0title",
        "Valid title " + (char)0xD800,   // unpaired high surrogate
        (char)0xDC00 + "Valid title",    // unpaired low surrogate
    };

    [Theory]
    [MemberData(nameof(MalformedText), DisableDiscoveryEnumeration = true)]
    public void Malformed_utf16_and_nul_are_rejected(string text)
    {
        Assert.Null(CaseTextRules.CountScalarValues(text));
        Assert.True(CaseRequestValidator.Validate(new CreateCaseRequest(text, null, null))!.FieldErrors!.ContainsKey(CaseFieldNames.Title));
        Assert.True(CaseRequestValidator.Validate(new CreateCaseRequest("Valid title", text, null))!.FieldErrors!.ContainsKey(CaseFieldNames.Description));
    }

    [Fact]
    public void Update_requests_use_the_same_rules()
    {
        Assert.NotNull(CaseRequestValidator.Validate(new UpdateCaseRequest("abcd", null, 1)));
        Assert.NotNull(CaseRequestValidator.Validate(new UpdateCaseRequest("Valid title", new string('d', 4001), 1)));
        Assert.Null(CaseRequestValidator.Validate(new UpdateCaseRequest("  Valid title  ", "", 1)));
    }

    [Fact]
    public void Multiple_field_errors_are_reported_together()
    {
        var error = CaseRequestValidator.Validate(new CreateCaseRequest("", new string('d', 4001), null));

        Assert.Equal(CaseRequestValidator.ValidationCode, error!.Code);
        Assert.Equal([CaseFieldNames.Description, CaseFieldNames.Title], error.FieldErrors!.Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("Pat Example", "1 Example Way", "1234", null)]
    [InlineData("", "1 Example Way", "1234", CaseFieldNames.VeteranName)]
    [InlineData("Pat Example", " ", "1234", CaseFieldNames.VeteranAddress)]
    [InlineData("Pat Example", "1 Example Way", "123", CaseFieldNames.VeteranSsnLast4)]
    [InlineData("Pat Example", "1 Example Way", "12345", CaseFieldNames.VeteranSsnLast4)]
    [InlineData("Pat Example", "1 Example Way", "12a4", CaseFieldNames.VeteranSsnLast4)]
    [InlineData("Pat Example", "1 Example Way", "١٢٣٤", CaseFieldNames.VeteranSsnLast4)]   // non-ASCII digits
    public void Veteran_snapshot_rules(string name, string address, string last4, string? failingField)
    {
        var error = CaseRequestValidator.Validate(new CreateCaseRequest("Valid title", null, new VeteranSnapshot(name, address, last4)));

        if (failingField is null)
        {
            Assert.Null(error);
        }
        else
        {
            Assert.Equal([failingField], error!.FieldErrors!.Keys);
        }
    }

    [Fact]
    public void Veteran_name_and_address_limits_are_200_and_500_scalar_values()
    {
        Assert.Null(CaseRequestValidator.Validate(new CreateCaseRequest("Valid title", null,
            new VeteranSnapshot(new string('n', 200), new string('a', 500), "0000"))));

        var error = CaseRequestValidator.Validate(new CreateCaseRequest("Valid title", null,
            new VeteranSnapshot(new string('n', 201), new string('a', 501), "0000")));

        Assert.Equal([CaseFieldNames.VeteranAddress, CaseFieldNames.VeteranName], error!.FieldErrors!.Keys.Order(StringComparer.Ordinal));
    }

    private static void AssertTitle(bool valid, Error? error)
    {
        if (valid)
        {
            Assert.Null(error);
            return;
        }

        Assert.NotNull(error);
        Assert.Equal(ErrorKind.Validation, error.Kind);
        var message = Assert.Single(error.FieldErrors![CaseFieldNames.Title]);
        Assert.True(message == CaseTextRules.TitleLengthMessage || message == CaseTextRules.TitleRequiredMessage, message);
    }
}
