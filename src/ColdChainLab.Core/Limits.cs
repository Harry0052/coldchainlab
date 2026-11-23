namespace ColdChainLab.Core;

/// <summary>
/// Bounds for demonstration fixtures. These are input guards, not storage specifications.
/// </summary>
public static class Limits
{
    public const string SchemaVersion = "1";

    public const int MaxScenarioBytes = 65_536;

    public const double MaxDurationMinutes = 1_440;

    public const double MaxCoefficientPerMinute = 5;

    public const double MaxCadenceMinutes = 60;

    public const double MinTemperatureCelsius = -40;

    public const double MaxTemperatureCelsius = 60;

    public const int MaxFaults = 16;

    public const int MaxDescriptionLength = 400;

    public const string Limitation =
        "Synthetic demonstration only. Not a vaccine viability, disposition, or compliance judgment.";

    public const string TimeUnit = "min";

    public const string TemperatureUnit = "C";
}
