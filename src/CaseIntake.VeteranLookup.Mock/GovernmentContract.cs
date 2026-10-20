namespace CaseIntake.VeteranLookup.Mock;

/// <summary>
/// The Government's Veteran Lookup definition (Technical Build Specification 4.6), committed verbatim as
/// <c>docs/contracts/veteran-lookup.spec.openapi.yaml</c> and embedded at build time. It is the service's contract and is
/// served byte for byte; the document generated from the endpoint declarations is only conformance evidence (ADR 0008).
/// </summary>
internal static class GovernmentContract
{
    public const string Route = "/openapi/v1.yaml";

    /// <summary>RFC 9512 media type for YAML.</summary>
    public const string ContentType = "application/yaml";

    private const string ResourceName = "CaseIntake.VeteranLookup.Mock.veteran-lookup.spec.openapi.yaml";

    /// <summary>Read once, when the class is first used.</summary>
    public static readonly byte[] Bytes = Load();

    private static byte[] Load()
    {
        using var stream = typeof(GovernmentContract).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
