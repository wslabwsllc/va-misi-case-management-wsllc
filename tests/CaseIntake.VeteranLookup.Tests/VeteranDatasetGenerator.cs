using System.Text.Json;
using CaseIntake.VeteranLookup.Mock;

namespace CaseIntake.VeteranLookup.Tests;

/// <summary>
/// Generates the mock's synthetic dataset (D5: fixed <see cref="System.Random"/> seed, no Bogus, like the E2 case seed).
/// The output is committed as <c>src/CaseIntake.VeteranLookup.Mock/Data/veterans.json</c>; a drift test keeps the
/// two identical. All values are fictional: SSN area numbers 900-999 are never issued, ZIP codes 000xx are
/// unassigned, and names are random first/last combinations.
/// </summary>
internal static class VeteranDatasetGenerator
{
    public const int Seed = 20261016;
    public const int RecordCount = 50;

    /// <summary>Documented positive lookup (README, demo script).</summary>
    public const string KnownFoundSsn = "900-12-3456";

    /// <summary>Documented absent lookup: never in the dataset.</summary>
    public const string KnownAbsentSsn = "999-99-9999";

    private static readonly string[] FirstNames =
    [
        "Avery", "Jordan", "Riley", "Morgan", "Casey", "Quinn", "Harper", "Rowan", "Emerson", "Sawyer",
        "Dakota", "Reese", "Parker", "Hayden", "Finley", "Kendall", "Marlow", "Sloane", "Tatum", "Linden",
    ];

    private static readonly string[] LastNames =
    [
        "Ashgrove", "Bellwether", "Calloway", "Dunmore", "Everly", "Fairbanks", "Galloway", "Hartwell", "Ingram", "Juniper",
        "Kestrel", "Larkspur", "Merriweather", "Northcott", "Oakhurst", "Pemberton", "Quillon", "Ravenscroft", "Stonebridge", "Thistlewood",
    ];

    private static readonly string[] Streets =
    [
        "Synthetic", "Placeholder", "Sample", "Fixture", "Testbed", "Mockingbird", "Example", "Demo", "Lantern", "Willowmere",
    ];

    private static readonly string[] StreetTypes = ["Street", "Avenue", "Lane", "Court", "Way", "Drive"];

    private static readonly string[] Towns =
    [
        "Fernwick", "Brambleton Falls", "Coldharbor Mill", "Duskmoor", "Elderglen", "Foxhollow", "Glimmerdale", "Hollowmere",
    ];

    private static readonly string[] States = ["VA", "NC", "MD", "PA", "OH", "TX", "CA", "WA"];

    public static VeteranRecord[] Generate()
    {
        var random = new Random(Seed);
        var used = new HashSet<string>(StringComparer.Ordinal) { KnownFoundSsn };
        List<VeteranRecord> records = [Create(KnownFoundSsn, random)];
        while (records.Count < RecordCount)
        {
            var ssn = $"{random.Next(900, 999):D3}-{random.Next(1, 100):D2}-{random.Next(1, 10000):D4}";
            if (ssn != KnownAbsentSsn && used.Add(ssn))
            {
                records.Add(Create(ssn, random));
            }
        }
        return [.. records];
    }

    public static string ToJson(VeteranRecord[] records) =>
        JsonSerializer.Serialize(records, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true, IndentSize = 2 }) + "\n";

    private static VeteranRecord Create(string ssn, Random random)
    {
        var name = $"{Pick(FirstNames, random)} {Pick(LastNames, random)}";
        var address = $"{random.Next(100, 9900)} {Pick(Streets, random)} {Pick(StreetTypes, random)}, " +
            $"{Pick(Towns, random)}, {Pick(States, random)} 000{random.Next(10, 100):D2}";
        return new VeteranRecord(ssn, name, address);
    }

    private static string Pick(string[] values, Random random) => values[random.Next(values.Length)];
}
