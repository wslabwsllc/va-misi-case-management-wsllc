using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CaseIntake.Attachments.Tests;

/// <summary>E4-S4/D6: size and type limits come from configuration, defaults apply, and bad configuration fails at startup.</summary>
public class AttachmentOptionsTests
{
    private static AttachmentOptions Resolve(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration);
        services.AddAttachmentsModule(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<AttachmentOptions>>().Value;
    }

    [Fact]
    public void Defaults_are_10_MiB_and_the_five_adopted_types()
    {
        var options = Resolve(new() { ["Attachments:RootPath"] = "/tmp/a" });

        Assert.Equal(10_485_760, options.MaxBytes);
        Assert.Equal(["application/pdf", "image/png", "image/jpeg", "text/plain",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"], options.AllowedTypes.Select(t => t.ContentType));
    }

    [Fact]
    public void Configured_limits_replace_the_defaults()
    {
        var options = Resolve(new()
        {
            ["Attachments:RootPath"] = "/tmp/a",
            ["Attachments:MaxBytes"] = "1024",
            ["Attachments:AllowedTypes:0:ContentType"] = "text/csv",
            ["Attachments:AllowedTypes:0:Extensions:0"] = ".csv",
        });

        Assert.Equal(1024, options.MaxBytes);
        var only = Assert.Single(options.AllowedTypes);
        Assert.Equal("text/csv", only.ContentType);
        Assert.Equal("text/csv", options.MatchAllowedType("text/csv", ".csv"));
        Assert.Null(options.MatchAllowedType("application/pdf", ".pdf"));
    }

    [Theory]
    [InlineData("application/pdf", ".pdf", "application/pdf")]
    [InlineData("APPLICATION/PDF", ".pdf", "application/pdf")]
    [InlineData("text/plain; charset=utf-8", ".txt", "text/plain")]
    [InlineData("image/jpeg", ".jpg", "image/jpeg")]
    [InlineData("image/jpeg", ".jpeg", "image/jpeg")]
    [InlineData("image/png", ".pdf", null)]            // the pair must match
    [InlineData("application/pdf", ".exe", null)]
    [InlineData("text/html", ".html", null)]
    [InlineData("application/octet-stream", ".pdf", null)]
    [InlineData("", ".pdf", null)]
    [InlineData(null, ".pdf", null)]
    [InlineData("application/pdf", "", null)]
    public void Content_type_and_extension_must_match_an_allowed_pair(string? contentType, string extension, string? expected)
    {
        var options = Resolve(new() { ["Attachments:RootPath"] = "/tmp/a" });

        Assert.Equal(expected, options.MatchAllowedType(contentType, extension));
    }

    public static TheoryData<Dictionary<string, string?>, string> InvalidConfigurations() => new()
    {
        { new() { ["Attachments:RootPath"] = "/tmp/a", ["Attachments:MaxBytes"] = "0" }, "MaxBytes" },
        { new() { ["Attachments:RootPath"] = "/tmp/a", ["Attachments:OrphanGracePeriod"] = "-00:01:00" }, "OrphanGracePeriod" },
        { new() { ["Attachments:RootPath"] = "/tmp/a", ["Attachments:AllowedTypes:0:ContentType"] = "pdf", ["Attachments:AllowedTypes:0:Extensions:0"] = ".pdf" }, "invalid content type" },
        { new() { ["Attachments:RootPath"] = "/tmp/a", ["Attachments:AllowedTypes:0:ContentType"] = "application/pdf" }, "lists no extensions" },
        { new() { ["Attachments:RootPath"] = "/tmp/a", ["Attachments:AllowedTypes:0:ContentType"] = "application/pdf", ["Attachments:AllowedTypes:0:Extensions:0"] = "pdf" }, "must look like" },
        {
            new()
            {
                ["Attachments:RootPath"] = "/tmp/a",
                ["Attachments:AllowedTypes:0:ContentType"] = "application/pdf", ["Attachments:AllowedTypes:0:Extensions:0"] = ".pdf",
                ["Attachments:AllowedTypes:1:ContentType"] = "text/plain", ["Attachments:AllowedTypes:1:Extensions:0"] = ".pdf",
            },
            "maps '.pdf' to both"
        },
    };

    [Theory]
    [MemberData(nameof(InvalidConfigurations))]
    public void Inconsistent_configuration_is_rejected(Dictionary<string, string?> settings, string expectedMessage)
    {
        var failure = Assert.Throws<OptionsValidationException>(() => Resolve(settings));

        Assert.Contains(failure.Failures, f => f.Contains(expectedMessage, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_a_configured_root_one_fixed_folder_per_machine_is_used(string? rootPath)
    {
        var options = Resolve(new() { ["Attachments:RootPath"] = rootPath });

        Assert.Equal(Path.Combine(Path.GetTempPath(), "caseintake-attachments"), options.RootPath);
    }

    [Fact]
    public void Configured_types_are_normalized_so_padding_and_case_still_match()
    {
        var options = Resolve(new()
        {
            ["Attachments:RootPath"] = "/tmp/a",
            ["Attachments:AllowedTypes:0:ContentType"] = " Application/PDF ",
            ["Attachments:AllowedTypes:0:Extensions:0"] = " .PDF",
        });

        Assert.Equal("application/pdf", options.MatchAllowedType("application/pdf", ".pdf"));
    }

    [Fact]
    public void A_content_type_longer_than_the_column_fails_at_startup()
    {
        var failure = Assert.Throws<OptionsValidationException>(() => Resolve(new()
        {
            ["Attachments:RootPath"] = "/tmp/a",
            ["Attachments:AllowedTypes:0:ContentType"] = "application/" + new string('x', 100),
            ["Attachments:AllowedTypes:0:Extensions:0"] = ".x",
        }));

        Assert.Contains(failure.Failures, f => f.Contains("invalid content type", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(10_485_760, "10 MiB")]
    [InlineData(1_572_864, "1.5 MiB")]
    [InlineData(1024, "1 KiB")]
    [InlineData(500, "500 bytes")]
    public void The_size_limit_is_described_in_a_readable_unit(long maxBytes, string expected)
    {
        var options = Resolve(new() { ["Attachments:RootPath"] = "/tmp/a", ["Attachments:MaxBytes"] = maxBytes.ToString(System.Globalization.CultureInfo.InvariantCulture) });

        Assert.EndsWith($"Maximum size: {expected}.", options.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Registering_both_the_module_and_the_maintenance_does_not_duplicate_configured_types()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Attachments:RootPath"] = "/tmp/a",
            ["Attachments:AllowedTypes:0:ContentType"] = "text/csv",
            ["Attachments:AllowedTypes:0:Extensions:0"] = ".csv",
        }).Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration);
        services.AddAttachmentsModule(configuration).AddAttachmentsMaintenance();
        using var provider = services.BuildServiceProvider();

        Assert.Single(provider.GetRequiredService<IOptions<AttachmentOptions>>().Value.AllowedTypes);
    }

    [Fact]
    public void The_description_states_the_allowed_extensions_and_the_limit()
    {
        var options = Resolve(new() { ["Attachments:RootPath"] = "/tmp/a" });

        Assert.Equal("Allowed types: .pdf, .png, .jpg, .jpeg, .txt, .docx. Maximum size: 10 MiB.", options.Describe());
    }
}
