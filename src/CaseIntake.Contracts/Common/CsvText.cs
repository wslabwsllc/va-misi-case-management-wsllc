namespace CaseIntake.Contracts.Common;

/// <summary>CSV helpers shared by every export.</summary>
public static class CsvText
{
    /// <summary>Quotes a cell and neutralizes leading formula characters (=, +, -, @, tab, carriage return).</summary>
    public static string Cell(string? value)
    {
        value ??= string.Empty;
        var safe = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
        return "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
