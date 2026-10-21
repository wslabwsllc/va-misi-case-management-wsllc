using CaseIntake.ServiceDefaults;
using CaseIntake.TestResults;

namespace CaseIntake.IntegrationTests.Logging;

/// <summary>
/// The log scrubber (<see cref="SsnRedaction"/>) and the test-evidence redactor (<see cref="EvidenceRedactor"/>) detect
/// the same SSN shapes, and neither treats a digit run inside a GUID or hex identifier as an SSN. Before, about 1.5% of
/// GUIDs and 6% of operation IDs matched, which partly redacted IDs in logs and made log assertions fail at random.
/// </summary>
public class SsnDetectionTests
{
    private const string GuidWithNineDigitRun = "3f2a1c4e-9b7d-4e21-8c3a-a900456789bc";

    public static TheoryData<string, bool> Samples() => new()
    {
        { "value 900-45-6789 end", true },
        { "Veteran 900-45-6789 secret.txt", true },
        { "ssn=900456789.", true },
        { "{\"ssn\":\"900456789\"}", true },
        { "(900-45-6789)", true },
        { "/lookup/900456789", true },
        { "900456789", true },
        { "ssn900456789", false },                       // glued to letters: documented tradeoff
        { $"Attachment {GuidWithNineDigitRun} uploaded", false },
        { "operation a900456789bc4e218c3a3f2a1c4e9b7d", false },
        { "1234567890", false },
        { "12-345-6789", false },
        { "case 42 of 72", false },
    };

    [TestCaseSource(nameof(Samples))]
    public void Log_scrubber_and_evidence_redactor_agree(string text, bool isSsn)
    {
        Assert.Equal(isSsn, SsnRedaction.ContainsSsn(text));
        Assert.Equal(isSsn, SsnRedaction.Redact(text).Contains(SsnRedaction.Replacement, StringComparison.Ordinal));
        Assert.Equal(isSsn, EvidenceRedactor.Redact(text).Contains("[REDACTED-SSN]", StringComparison.Ordinal));
    }

    [Test]
    public void Random_GUIDs_and_operation_ids_are_never_treated_as_SSNs()
    {
        for (var i = 0; i < 10_000; i++)
        {
            var id = Guid.NewGuid();
            foreach (var text in new[] { id.ToString("D"), id.ToString("N"), $"op={id:N} attachment {id}" })
            {
                Assert.False(SsnRedaction.ContainsSsn(text), $"log scrubber matched {text}");
                Assert.Equal(text, EvidenceRedactor.Redact(text));
            }
        }
    }

    [Test]
    public void Redaction_replaces_the_SSN_and_keeps_an_adjacent_identifier_intact()
    {
        var text = $"lookup 900-45-6789 for attachment {GuidWithNineDigitRun}";

        Assert.Equal($"lookup {SsnRedaction.Replacement} for attachment {GuidWithNineDigitRun}", SsnRedaction.Redact(text));
    }
}
