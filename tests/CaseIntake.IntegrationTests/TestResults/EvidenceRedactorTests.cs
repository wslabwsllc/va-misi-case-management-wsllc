using CaseIntake.TestResults;

namespace CaseIntake.IntegrationTests.TestResults;

/// <summary>E5: imported test text never keeps SSN-shaped values, passwords, tokens, cookies or known secrets.</summary>
[Category("Story:E5-S3")]
public class EvidenceRedactorTests
{
    [TestCase("Veteran 900-12-3456 not found", "Veteran [REDACTED-SSN] not found")]
    [TestCase("ssn=900123456;", "ssn=[REDACTED-SSN];")]
    [TestCase("(666-01-0001)", "([REDACTED-SSN])")]
    public void Ssn_shaped_values_are_masked(string input, string expected)
    {
        Assert.Equal(expected, EvidenceRedactor.Redact(input));
    }
    [TestCase("id 0190a8b2-0000-7000-8000-000123456789 ok")]
    [TestCase("hash a900123456b and x900-12-3456")]
    [TestCase("order 9001234567 has ten digits")]
    public void Digit_runs_inside_ids_hashes_and_longer_numbers_are_kept(string input)
    {
        Assert.Equal(input, EvidenceRedactor.Redact(input));
    }
    [TestCase("password=hunter22&next=1", "password=[REDACTED]&next=1")]
    [TestCase("Pwd: s3cr3t, user=a", "Pwd: [REDACTED], user=a")]
    [TestCase("{\"apiKey\": \"abc123\"}", "{\"apiKey\": \"[REDACTED]\"}")]
    [TestCase("__RequestVerificationToken=CfDJ8abc", "__RequestVerificationToken=[REDACTED]")]
    [TestCase("Authorization: Bearer eyJhbGciOi.payload.sig==", "Authorization: Bearer [REDACTED]")]
    [TestCase("Cookie: CaseIntake.Auth=abc; other=1\nnext line", "Cookie: [REDACTED]\nnext line")]
    [TestCase("sent .AspNetCore.Antiforgery.abcXYZ=CfDJ8; path=/", "sent .AspNetCore.Antiforgery.abcXYZ=[REDACTED]; path=/")]
    [TestCase("CaseIntake.Auth=chunk1 rest", "CaseIntake.Auth=[REDACTED] rest")]
    public void Passwords_tokens_and_cookies_are_masked(string input, string expected)
    {
        Assert.Equal(expected, EvidenceRedactor.Redact(input));
    }

    [Test]
    public void Known_secrets_of_four_or_more_characters_are_masked_wherever_they_appear()
    {
        var redacted = EvidenceRedactor.Redact("typed Demo!Pass into the box, then abc", ["Demo!Pass", "abc"]);

        Assert.Equal("typed [REDACTED] into the box, then abc", redacted);
    }

    [Test]
    public void Empty_and_missing_text_redacts_to_an_empty_string()
    {
        Assert.Equal(string.Empty, EvidenceRedactor.Redact(null));
        Assert.Equal(string.Empty, EvidenceRedactor.Redact(string.Empty));
    }

    [Test]
    public void Redact_and_cap_returns_null_for_empty_text_and_short_text_unchanged()
    {
        Assert.Null(EvidenceRedactor.RedactAndCap(null, 100));
        Assert.Null(EvidenceRedactor.RedactAndCap(string.Empty, 100));
        Assert.Equal("ok [REDACTED-SSN]", EvidenceRedactor.RedactAndCap("ok 900-12-3456", 100));
    }

    [Test]
    public void Long_text_is_capped_with_a_truncation_marker_after_redaction()
    {
        var capped = EvidenceRedactor.RedactAndCap("900-12-3456 " + new string('x', 100), 30)!;

        Assert.Equal(30, capped.Length);
        Assert.StartsWith("[REDACTED-SSN]", capped, StringComparison.Ordinal);
        Assert.EndsWith("\n[truncated]", capped, StringComparison.Ordinal);
    }

    [Test]
    public void Capping_never_splits_a_surrogate_pair()
    {
        // 20 - "\n[truncated]".Length = 8 characters kept; the 8th is the high surrogate of the emoji, so only 7 are kept.
        var capped = EvidenceRedactor.RedactAndCap("abcdefg\U0001F600" + new string('z', 30), 20)!;

        Assert.Equal("abcdefg\n[truncated]", capped);
    }
}
