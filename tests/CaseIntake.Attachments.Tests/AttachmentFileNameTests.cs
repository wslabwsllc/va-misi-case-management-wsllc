using CaseIntake.Domain.Attachments;

namespace CaseIntake.Attachments.Tests;

/// <summary>E4-S5: user-supplied names are reduced to a safe display name; storage never uses them.</summary>
public class AttachmentFileNameTests
{
    [TestCase("report.pdf", "report.pdf")]
    [TestCase("../../etc/passwd", "passwd")]
    [TestCase("..\\..\\windows\\system.ini", "system.ini")]
    [TestCase("C:\\Users\\someone\\Desktop\\scan.png", "scan.png")]
    [TestCase("/var/tmp/notes.txt", "notes.txt")]
    [TestCase("line\r\nbreak.txt", "linebreak.txt")]
    [TestCase("tab\tand\u0000nul.txt", "tabandnul.txt")]
    [TestCase("invoice\u202Etxt.exe", "invoicetxt.exe")]        // right-to-left override removed
    [TestCase("  padded name.pdf  ", "padded name.pdf")]
    [TestCase("trailing dots.pdf...", "trailing dots.pdf")]
    [TestCase("caf\u00e9 r\u00e9sum\u00e9.docx", "caf\u00e9 r\u00e9sum\u00e9.docx")]
    [TestCase("\ud83d\udcc4 notes.txt", "\ud83d\udcc4 notes.txt")]      // supplementary characters kept
    public void Names_are_reduced_to_a_safe_leaf(string input, string expected)
    {
        Assert.Equal(expected, AttachmentFileName.Sanitize(input));
    }

    [Test]
    public void Unpaired_surrogates_are_removed()
    {
        // Built in code: test discovery would replace a lone surrogate in attribute data with U+FFFD.
        var input = "broken" + (char)0xD800 + "half" + (char)0xDC00 + ".txt";

        Assert.Equal("brokenhalf.txt", AttachmentFileName.Sanitize(input));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("/")]
    [TestCase("folder/")]
    [TestCase("..")]
    [TestCase(".")]
    [TestCase("\r\n\u0007")]
    public void Names_that_sanitize_to_nothing_become_the_fallback(string? input)
    {
        Assert.Equal(AttachmentFileName.Fallback, AttachmentFileName.Sanitize(input));
    }

    [Test]
    public void Long_names_are_capped_at_200_scalar_values_and_keep_their_extension()
    {
        var sanitized = AttachmentFileName.Sanitize(new string('a', 300) + ".pdf");

        Assert.Equal(200, sanitized.Length);
        Assert.EndsWith(".pdf", sanitized, StringComparison.Ordinal);
    }

    [TestCase("")]                                      // no extension
    [TestCase(".averyveryverylongextension")]            // longer than 16: not treated as an extension to keep
    public void Long_names_without_a_short_extension_are_simply_cut(string suffix)
    {
        var sanitized = AttachmentFileName.Sanitize(new string('a', 300) + suffix);

        Assert.Equal(new string('a', 200), sanitized);
    }

    [Test]
    public void Long_names_are_counted_in_scalar_values_not_UTF16_units()
    {
        var sanitized = AttachmentFileName.Sanitize(string.Concat(Enumerable.Repeat("\ud83d\udcc4", 250)) + ".txt");

        Assert.Equal(200, sanitized.EnumerateRunes().Count());
        Assert.EndsWith(".txt", sanitized, StringComparison.Ordinal);
    }

    [TestCase("Report.PDF", ".pdf")]
    [TestCase("archive.tar.gz", ".gz")]
    [TestCase("noextension", "")]
    public void Extension_is_lowercase_with_the_dot(string name, string expected)
    {
        Assert.Equal(expected, AttachmentFileName.Extension(name));
    }
}
