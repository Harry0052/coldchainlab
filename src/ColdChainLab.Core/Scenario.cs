namespace ColdChainLab.Core;

public enum FaultKind
{
    PowerLoss,
    DoorOpen,
    LoggerDropout,
}

public sealed record TimeSpec(DateTimeOffset OriginUtc, double DurationMinutes);

public sealed record ThermalSpec(
    double CoefficientPerMinute,
    double DoorCoefficientPerMinute,
    double InitialCelsius,
    double AmbientCelsius,
    double TargetCelsius);

public sealed record ControlSpec(string Mode, bool CoolingAvailable);

public sealed record LoggerSpec(
    double CadenceMinutes,
    double DropoutTimeoutMinutes,
    double LowCelsius,
    double HighCelsius);

/// <summary>
/// A fault interval is closed: a sample at minute t is inside when start &lt;= t &lt;= end.
/// </summary>
public sealed record FaultSpec(string FaultId, FaultKind Kind, double StartMinute, double EndMinute);

public sealed record Scenario(
    string SchemaVersion,
    string ScenarioId,
    string Description,
    TimeSpec Time,
    ThermalSpec Thermal,
    ControlSpec Control,
    LoggerSpec Logger,
    IReadOnlyList<FaultSpec> Faults);
