using System.Text.Json;
using System.Text.Json.Serialization;
using CaseIntake.VeteranHistory.Mock;

namespace CaseIntake.VeteranLookup.Tests;

/// <summary>
/// Generates the Service History mock's synthetic dataset (E4-C2-1), one record per Lookup veteran except the
/// documented no-record and outage SSNs (plan 0.4). The output is committed as
/// <c>src/CaseIntake.VeteranHistory.Mock/Data/service-history.json</c>; a drift test keeps the two identical. It has its
/// own <see cref="System.Random"/> seed, so the Lookup dataset (<see cref="VeteranDatasetGenerator"/>) never changes
/// because of it. The documented veteran gets the Government's example values.
/// </summary>
internal static class ServiceHistoryDatasetGenerator
{
    public const int Seed = 20261023;

    /// <summary>Lookup finds this veteran; Service History has no record (404).</summary>
    public const string KnownNoRecordSsn = "902-17-3062";

    /// <summary>Lookup finds this veteran; Service History is down for them (503, a constant in the mock).</summary>
    public const string KnownOutageSsn = ServiceHistoryDataset.OutageSsn;

    /// <summary>The Government's example response, for the documented veteran (<see cref="VeteranDatasetGenerator.KnownFoundSsn"/>).</summary>
    public static readonly ServiceHistoryRecord GovernmentExample = new(VeteranDatasetGenerator.KnownFoundSsn, "Army",
        new DateOnly(2001, 2, 15), new DateOnly(2005, 2, 14), "Honorable", new DateOnly(2026, 1, 5));

    private static readonly string[] Branches = ["Army", "Navy", "Air Force", "Marine Corps", "Coast Guard", "Space Force"];

    private static readonly string[] Characters =
        ["Honorable", "Honorable", "Honorable", "General (Under Honorable Conditions)", "Other Than Honorable", "Uncharacterized"];

    public static ServiceHistoryRecord[] Generate()
    {
        var random = new Random(Seed);
        return
        [
            .. VeteranDatasetGenerator.Generate()
                .Where(v => v.Ssn is not KnownNoRecordSsn and not KnownOutageSsn)
                .Select(v => v.Ssn == GovernmentExample.Ssn ? GovernmentExample : Create(v.Ssn, random)),
        ];
    }

    public static string ToJson(ServiceHistoryRecord[] records) =>
        JsonSerializer.Serialize(records, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            IndentSize = 2,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        }) + "\n";

    private static ServiceHistoryRecord Create(string ssn, Random random)
    {
        var start = new DateOnly(1970, 1, 1).AddDays(random.Next(0, 50 * 365));
        // About one in eight is still serving (no end date, D7); the rest served two to twenty-four years.
        DateOnly? end = random.Next(8) == 0 ? null : start.AddDays(random.Next(2 * 365, 24 * 365));
        if (end > new DateOnly(2025, 12, 31))
        {
            end = null;
        }
        // About one in ten has never been verified (no last-verified date, D7).
        DateOnly? verified = random.Next(10) == 0 ? null : new DateOnly(2024, 1, 1).AddDays(random.Next(0, 730));
        return new ServiceHistoryRecord(ssn, Pick(Branches, random), start, end, Pick(Characters, random), verified);
    }

    private static string Pick(string[] values, Random random) => values[random.Next(values.Length)];
}
