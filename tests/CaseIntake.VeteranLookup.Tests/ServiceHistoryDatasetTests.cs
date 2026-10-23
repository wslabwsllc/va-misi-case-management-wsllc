using CaseIntake.Domain.Cases;
using CaseIntake.VeteranHistory.Mock;
using CaseIntake.VeteranLookup.Mock;

namespace CaseIntake.VeteranLookup.Tests;

/// <summary>E4-C2-1: the embedded service-history dataset is deterministic, synthetic and storable with a case.</summary>
[Category("Story:E4-C2-1")]
[Category("Req:R-CR2-7")]
public class ServiceHistoryDatasetTests
{
    [Test]
    [Category(Snapshots.Category)]
    public void The_committed_dataset_is_exactly_the_generator_output()
    {
        Snapshots.Verify(ServiceHistoryDatasetGenerator.ToJson(ServiceHistoryDatasetGenerator.Generate()),
            "src", "CaseIntake.VeteranHistory.Mock", "Data", "service-history.json");
    }

    [Test]
    public void The_generator_is_deterministic()
    {
        Assert.Equal(ServiceHistoryDatasetGenerator.ToJson(ServiceHistoryDatasetGenerator.Generate()),
            ServiceHistoryDatasetGenerator.ToJson(ServiceHistoryDatasetGenerator.Generate()));
    }

    [Test]
    public void Every_record_belongs_to_a_synthetic_Lookup_veteran_and_fits_the_case_snapshot()
    {
        var dataset = ServiceHistoryDataset.LoadEmbedded();
        var veterans = VeteranDataset.LoadEmbedded();

        Assert.Equal(veterans.Records.Count - 2, dataset.Records.Count);   // all but the no-record and outage veterans
        Assert.Equal(dataset.Records.Count, dataset.Records.Select(r => r.Ssn).Distinct().Count());
        Assert.All(dataset.Records, r =>
        {
            Assert.Matches("^9[0-9]{2}-[0-9]{2}-[0-9]{4}$", r.Ssn);          // area 900-999: never issued
            Assert.True(veterans.TryFind(r.Ssn, out _), "every history belongs to a Lookup veteran");
            var history = new ServiceHistory(r.Branch, r.ServiceStartDate, r.ServiceEndDate, r.CharacterOfService, r.LastVerifiedDate);
            Assert.Empty(CaseTextRules.ValidateServiceHistory(ServiceHistoryStatus.Found, history));
        });
        Assert.Contains(dataset.Records, r => r.ServiceEndDate is null);     // still serving (D7)
        Assert.Contains(dataset.Records, r => r.LastVerifiedDate is null);   // never verified (D7)
    }

    [Test]
    public void The_documented_demo_SSNs_have_their_documented_outcomes()
    {
        var dataset = ServiceHistoryDataset.LoadEmbedded();

        Assert.True(dataset.TryFind(VeteranDatasetGenerator.KnownFoundSsn, out var found));
        Assert.Equal(ServiceHistoryDatasetGenerator.GovernmentExample, found);
        Assert.True(dataset.TryFind(VeteranDatasetGenerator.KnownFoundSsn.Replace("-", "", StringComparison.Ordinal), out _));
        Assert.False(dataset.TryFind(ServiceHistoryDatasetGenerator.KnownNoRecordSsn, out _));
        Assert.False(dataset.TryFind(ServiceHistoryDatasetGenerator.KnownOutageSsn, out _));
        Assert.False(dataset.TryFind(VeteranDatasetGenerator.KnownAbsentSsn, out _));
    }

    [Test]
    public void The_Lookup_veterans_with_no_history_or_an_outage_exist_in_the_Lookup_dataset()
    {
        var veterans = VeteranDataset.LoadEmbedded();

        Assert.True(veterans.TryFind(ServiceHistoryDatasetGenerator.KnownNoRecordSsn, out _));
        Assert.True(veterans.TryFind(ServiceHistoryDatasetGenerator.KnownOutageSsn, out _));
    }
}
