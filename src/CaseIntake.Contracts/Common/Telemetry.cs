namespace CaseIntake.Contracts.Common;

public static class Telemetry
{
    public const string ActivitySourceName = "CaseIntake";
    public const string MeterName = "CaseIntake";
}

/// <summary>EventId ranges reserved per epic so log events never collide.</summary>
public static class EventIdRanges
{
    public const int E1Start = 1000, E1End = 1999;
    public const int E2Start = 2000, E2End = 2999;
    public const int E3Start = 3000, E3End = 3999;
    public const int E4Start = 4000, E4End = 4999;
    public const int E5Start = 5000, E5End = 5999;
}
