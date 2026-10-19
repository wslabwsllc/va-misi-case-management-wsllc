using CaseIntake.Domain.Cases;
using CaseIntake.VeteranLookup.Mock;

namespace CaseIntake.VeteranLookup.Tests;

/// <summary>E4-S1: the embedded dataset is deterministic, synthetic and usable as a case veteran snapshot.</summary>
public class VeteranDatasetTests
{
    [Fact]
    [Trait("Category", Snapshots.Category)]
    public void The_committed_dataset_is_exactly_the_generator_output()
    {
        Snapshots.Verify(VeteranDatasetGenerator.ToJson(VeteranDatasetGenerator.Generate()),
            "src", "CaseIntake.VeteranLookup.Mock", "Data", "veterans.json");
    }

    [Fact]
    public void The_generator_is_deterministic()
    {
        Assert.Equal(VeteranDatasetGenerator.ToJson(VeteranDatasetGenerator.Generate()),
            VeteranDatasetGenerator.ToJson(VeteranDatasetGenerator.Generate()));
    }

    [Fact]
    public void The_embedded_dataset_has_fifty_unique_synthetic_records()
    {
        var dataset = VeteranDataset.LoadEmbedded();

        Assert.Equal(VeteranDatasetGenerator.RecordCount, dataset.Records.Count);
        Assert.Equal(dataset.Records.Count, dataset.Records.Select(r => r.Ssn).Distinct().Count());
        Assert.All(dataset.Records, r =>
        {
            Assert.Matches("^9[0-9]{2}-[0-9]{2}-[0-9]{4}$", r.Ssn);          // area 900-999: never issued
            Assert.Matches(", [A-Z]{2} 000[0-9]{2}$", r.Address);             // ZIP 000xx: unassigned
            Assert.Empty(CaseTextRules.ValidateVeteran(r.Name, r.Address, r.Ssn[^4..]));   // fits the case snapshot limits
        });
    }

    [Fact]
    public void The_documented_demo_SSNs_are_present_and_absent()
    {
        var dataset = VeteranDataset.LoadEmbedded();

        Assert.True(dataset.TryFind(VeteranDatasetGenerator.KnownFoundSsn, out _));
        Assert.True(dataset.TryFind(VeteranDatasetGenerator.KnownFoundSsn.Replace("-", "", StringComparison.Ordinal), out _));
        Assert.False(dataset.TryFind(VeteranDatasetGenerator.KnownAbsentSsn, out _));
    }
}
