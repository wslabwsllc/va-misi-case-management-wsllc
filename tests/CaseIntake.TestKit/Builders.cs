using CaseIntake.Contracts.Cases;

namespace CaseIntake.TestKit;

public static class Builders
{
    public static CreateCaseRequest CreateCase(string title = "Synthetic intake case", string? description = "Synthetic description.",
        VeteranSnapshot? veteran = null) => new(title, description, veteran);

    public static VeteranSnapshot Veteran(string name = "Pat Example", string address = "1 Example Way, Testville, ZZ 00000",
        string ssnLast4 = "0000") => new(name, address, ssnLast4);
}
