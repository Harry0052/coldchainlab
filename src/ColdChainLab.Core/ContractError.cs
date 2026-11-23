namespace ColdChainLab.Core;

public static class ErrorCategory
{
    public const string InputTooLarge = "input-too-large";
    public const string UnreadableInput = "unreadable-input";
    public const string InvalidJson = "invalid-json";
    public const string UnsupportedSchema = "unsupported-schema";
    public const string MissingField = "missing-field";
    public const string UnexpectedField = "unexpected-field";
    public const string InvalidScenarioId = "invalid-scenario-id";
    public const string InvalidThermalCoefficient = "invalid-thermal-coefficient";
    public const string InvalidTemperature = "invalid-temperature";
    public const string InvalidTiming = "invalid-timing";
    public const string InvalidThreshold = "invalid-threshold";
    public const string InvalidFault = "invalid-fault";
    public const string InvalidControl = "invalid-control";
}

public sealed record ContractError(string Category, string Field, string Message);
