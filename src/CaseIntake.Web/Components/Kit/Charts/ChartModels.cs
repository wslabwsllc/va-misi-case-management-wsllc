namespace CaseIntake.Web.Components.Kit.Charts;

public enum ChartKind
{
    /// <summary>Vertical bars, one group per category (trends over time).</summary>
    Column,

    /// <summary>Horizontal bars, one row per category (distributions with long labels).</summary>
    Bar,

    /// <summary>Lines with point markers (trends with several series).</summary>
    Line,
}

/// <summary>One data series. <see cref="ColorVar"/> is a CSS custom property name from app.css (for example --series-1).</summary>
public sealed record ChartSeries(string Name, IReadOnlyList<double> Values, string ColorVar = "--series-1");

/// <summary>Number formatting shared by axis labels, value labels, the text summary and the data table.</summary>
public static class ChartFormat
{
    public static string Integer(double value) => value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    public static string OneDecimal(double value) => value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    public static string Seconds(double value) => value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " s";

    public static string Percent(double value) => value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";

    /// <summary>A round axis maximum (1, 2, 2.5, 5 x 10^n) at or above <paramref name="max"/>.</summary>
    public static double NiceMax(double max)
    {
        if (max <= 0)
        {
            return 1;
        }
        var exponent = Math.Floor(Math.Log10(max));
        var magnitude = Math.Pow(10, exponent);
        var fraction = max / magnitude;
        var nice = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 2.5 ? 2.5 : fraction <= 5 ? 5 : 10;
        return nice * magnitude;
    }
}
