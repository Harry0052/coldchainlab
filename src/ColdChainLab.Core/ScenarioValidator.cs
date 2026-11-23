using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ColdChainLab.Core;

public sealed class ScenarioValidation
{
    private ScenarioValidation(bool accepted, Scenario? scenario, IReadOnlyList<ContractError> errors)
    {
        Accepted = accepted;
        Scenario = scenario;
        Errors = errors;
    }

    public bool Accepted { get; }

    public Scenario? Scenario { get; }

    public IReadOnlyList<ContractError> Errors { get; }

    public static ScenarioValidation Accept(Scenario scenario) =>
        new(true, scenario, []);

    public static ScenarioValidation Reject(IReadOnlyList<ContractError> errors) =>
        new(false, null, errors);
}

public static class ScenarioValidator
{
    private static readonly Regex ScenarioIdPattern = new(
        "^[a-z0-9][a-z0-9-]{0,63}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        MaxDepth = 16,
    };

    public static ScenarioValidation Read(string json, int? byteLength = null)
    {
        var length = byteLength ?? System.Text.Encoding.UTF8.GetByteCount(json);
        if (length > Limits.MaxScenarioBytes)
        {
            return ScenarioValidation.Reject(
            [
                new ContractError(
                    ErrorCategory.InputTooLarge,
                    "scenario",
                    $"Scenario JSON is {length} bytes. The maximum is {Limits.MaxScenarioBytes} bytes."),
            ]);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, DocumentOptions);
        }
        catch (JsonException ex)
        {
            return ScenarioValidation.Reject(
            [
                new ContractError(
                    ErrorCategory.InvalidJson,
                    "scenario",
                    $"Scenario JSON could not be parsed: {ex.Message}"),
            ]);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return ScenarioValidation.Reject(
                [
                    new ContractError(
                        ErrorCategory.InvalidJson,
                        "scenario",
                        "Scenario JSON must be an object."),
                ]);
            }

            var errors = new List<ContractError>();
            var scenario = ReadScenario(document.RootElement, errors);
            if (errors.Count > 0 || scenario is null)
            {
                return ScenarioValidation.Reject(errors);
            }

            return ScenarioValidation.Accept(scenario);
        }
    }

    private static Scenario? ReadScenario(JsonElement root, List<ContractError> errors)
    {
        RejectUnknown(
            root,
            errors,
            "scenario",
            ["schemaVersion", "scenarioId", "description", "time", "thermal", "control", "logger", "faults"]);

        var schemaVersion = ReadString(root, "schemaVersion", "schemaVersion", errors);
        if (schemaVersion is not null && schemaVersion != Limits.SchemaVersion)
        {
            errors.Add(new ContractError(
                ErrorCategory.UnsupportedSchema,
                "schemaVersion",
                $"schemaVersion must be \"{Limits.SchemaVersion}\". Received \"{schemaVersion}\"."));
        }

        var scenarioId = ReadString(root, "scenarioId", "scenarioId", errors);
        if (scenarioId is not null && !ScenarioIdPattern.IsMatch(scenarioId))
        {
            errors.Add(new ContractError(
                ErrorCategory.InvalidScenarioId,
                "scenarioId",
                "scenarioId must be 1 to 64 characters of lowercase letters, digits, and hyphens, starting with a letter or digit."));
        }

        var description = ReadString(root, "description", "description", errors);
        if (description is not null && description.Length > Limits.MaxDescriptionLength)
        {
            errors.Add(new ContractError(
                ErrorCategory.InvalidJson,
                "description",
                $"description must be at most {Limits.MaxDescriptionLength} characters."));
        }

        TimeSpec? time = null;
        if (TryObject(root, "time", "time", errors, out var timeElement))
        {
            time = ReadTime(timeElement, errors);
        }

        ThermalSpec? thermal = null;
        if (TryObject(root, "thermal", "thermal", errors, out var thermalElement))
        {
            thermal = ReadThermal(thermalElement, errors);
        }

        ControlSpec? control = null;
        if (TryObject(root, "control", "control", errors, out var controlElement))
        {
            control = ReadControl(controlElement, errors);
        }

        LoggerSpec? logger = null;
        if (TryObject(root, "logger", "logger", errors, out var loggerElement))
        {
            logger = ReadLogger(loggerElement, errors);
        }

        var faults = ReadFaults(root, time?.DurationMinutes, errors);

        if (time is null || thermal is null || control is null || logger is null || scenarioId is null
            || schemaVersion is null || description is null || errors.Count > 0)
        {
            return null;
        }

        return new Scenario(schemaVersion, scenarioId, description, time, thermal, control, logger, faults);
    }

    private static TimeSpec? ReadTime(JsonElement element, List<ContractError> errors)
    {
        RejectUnknown(element, errors, "time", ["origin", "durationMinutes", "unit"]);
        var originText = ReadString(element, "origin", "time.origin", errors);
        DateTimeOffset origin = default;
        var originOk = false;
        if (originText is not null)
        {
            if (!originText.EndsWith("Z", StringComparison.Ordinal)
                || !DateTimeOffset.TryParse(
                    originText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out origin))
            {
                errors.Add(new ContractError(
                    ErrorCategory.InvalidTiming,
                    "time.origin",
                    "time.origin must be an ISO-8601 UTC timestamp ending in Z, for example 2026-01-01T00:00:00Z."));
            }
            else
            {
                originOk = true;
            }
        }

        var duration = ReadFiniteNumber(element, "durationMinutes", "time.durationMinutes", errors, ErrorCategory.InvalidTiming);
        if (duration is not null && (duration <= 0 || duration > Limits.MaxDurationMinutes))
        {
            errors.Add(new ContractError(
                ErrorCategory.InvalidTiming,
                "time.durationMinutes",
                $"time.durationMinutes must be greater than 0 and at most {Limits.MaxDurationMinutes.ToString(CultureInfo.InvariantCulture)} minutes. Received {Format(duration.Value)}."));
            duration = null;
        }

        var unit = ReadString(element, "unit", "time.unit", errors);
        if (unit is not null && unit != Limits.TimeUnit)
        {
            errors.Add(new ContractError(
                ErrorCategory.InvalidTiming,
                "time.unit",
                $"time.unit must be \"{Limits.TimeUnit}\". Received \"{unit}\"."));
        }

        if (!originOk || duration is null || unit != Limits.TimeUnit)
        {
            return null;
        }

        return new TimeSpec(origin, duration.Value);
    }

    private static ThermalSpec? ReadThermal(JsonElement element, List<ContractError> errors)
    {
        RejectUnknown(
            element,
            errors,
            "thermal",
            ["coefficientPerMinute", "doorCoefficientPerMinute", "initialCelsius", "ambientCelsius", "targetCelsius"]);

        var coefficient = ReadCoefficient(element, "coefficientPerMinute", "thermal.coefficientPerMinute", errors);
        var doorCoefficient = ReadCoefficient(element, "doorCoefficientPerMinute", "thermal.doorCoefficientPerMinute", errors);
        var initial = ReadTemperature(element, "initialCelsius", "thermal.initialCelsius", errors);
        var ambient = ReadTemperature(element, "ambientCelsius", "thermal.ambientCelsius", errors);
        var target = ReadTemperature(element, "targetCelsius", "thermal.targetCelsius", errors);
        if (coefficient is null || doorCoefficient is null || initial is null || ambient is null || target is null)
        {
            return null;
        }

        return new ThermalSpec(coefficient.Value, doorCoefficient.Value, initial.Value, ambient.Value, target.Value);
    }

    private static ControlSpec? ReadControl(JsonElement element, List<ContractError> errors)
    {
        RejectUnknown(element, errors, "control", ["mode", "coolingAvailable"]);
        var mode = ReadString(element, "mode", "control.mode", errors);
        if (mode is not null && mode != "hold-target")
        {
            errors.Add(new ContractError(
                ErrorCategory.InvalidControl,
                "control.mode",
                "control.mode must be \"hold-target\"."));
            mode = null;
        }

        bool? cooling = null;
        if (!element.TryGetProperty("coolingAvailable", out var coolingElement))
        {
            errors.Add(new ContractError(
                ErrorCategory.MissingField,
                "control.coolingAvailable",
                "control.coolingAvailable is required."));
        }
        else if (coolingElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            errors.Add(new ContractError(
                ErrorCategory.InvalidControl,
                "control.coolingAvailable",
                "control.coolingAvailable must be true or false."));
        }
        else
        {
            cooling = coolingElement.GetBoolean();
        }

        if (mode is null || cooling is null)
        {
            return null;
        }

        return new ControlSpec(mode, cooling.Value);
    }

    private static LoggerSpec? ReadLogger(JsonElement element, List<ContractError> errors)
    {
        RejectUnknown(
            element,
            errors,
            "logger",
            ["cadenceMinutes", "dropoutTimeoutMinutes", "lowCelsius", "highCelsius"]);

        var cadence = ReadFiniteNumber(element, "cadenceMinutes", "logger.cadenceMinutes", errors, ErrorCategory.InvalidTiming);
        if (cadence is not null && (cadence <= 0 || cadence > Limits.MaxCadenceMinutes))
        {
            errors.Add(new ContractError(
                ErrorCategory.InvalidTiming,
                "logger.cadenceMinutes",
                $"logger.cadenceMinutes must be greater than 0 and at most {Limits.MaxCadenceMinutes.ToString(CultureInfo.InvariantCulture)} minutes. Received {Format(cadence.Value)}."));
            cadence = null;
        }

        var timeout = ReadFiniteNumber(element, "dropoutTimeoutMinutes", "logger.dropoutTimeoutMinutes", errors, ErrorCategory.InvalidTiming);
        if (timeout is not null && timeout <= 0)
        {
            errors.Add(new ContractError(
                ErrorCategory.InvalidTiming,
                "logger.dropoutTimeoutMinutes",
                $"logger.dropoutTimeoutMinutes must be greater than 0 minutes. Received {Format(timeout.Value)}."));
            timeout = null;
        }

        var low = ReadTemperature(element, "lowCelsius", "logger.lowCelsius", errors);
        var high = ReadTemperature(element, "highCelsius", "logger.highCelsius", errors);
        if (low is not null && high is not null && low >= high)
        {
            errors.Add(new ContractError(
                ErrorCategory.InvalidThreshold,
                "logger.highCelsius",
                $"logger.lowCelsius must be below logger.highCelsius. Received low {Format(low.Value)} and high {Format(high.Value)}."));
            low = null;
            high = null;
        }

        if (cadence is null || timeout is null || low is null || high is null)
        {
            return null;
        }

        return new LoggerSpec(cadence.Value, timeout.Value, low.Value, high.Value);
    }

    private static IReadOnlyList<FaultSpec> ReadFaults(JsonElement root, double? duration, List<ContractError> errors)
    {
        if (!root.TryGetProperty("faults", out var faultsElement))
        {
            errors.Add(new ContractError(ErrorCategory.MissingField, "faults", "faults is required."));
            return [];
        }

        if (faultsElement.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new ContractError(ErrorCategory.InvalidFault, "faults", "faults must be an array."));
            return [];
        }

        if (faultsElement.GetArrayLength() > Limits.MaxFaults)
        {
            errors.Add(new ContractError(
                ErrorCategory.InvalidFault,
                "faults",
                $"faults must contain at most {Limits.MaxFaults} entries. Received {faultsElement.GetArrayLength()}."));
            return [];
        }

        var faults = new List<FaultSpec>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var item in faultsElement.EnumerateArray())
        {
            var field = $"faults[{index}]";
            index++;
            if (item.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new ContractError(ErrorCategory.InvalidFault, field, $"{field} must be an object."));
                continue;
            }

            RejectUnknown(item, errors, field, ["faultId", "kind", "startMinute", "endMinute"]);
            var faultId = ReadString(item, "faultId", $"{field}.faultId", errors);
            if (faultId is not null && !ScenarioIdPattern.IsMatch(faultId))
            {
                errors.Add(new ContractError(
                    ErrorCategory.InvalidFault,
                    $"{field}.faultId",
                    $"{field}.faultId must be 1 to 64 characters of lowercase letters, digits, and hyphens, starting with a letter or digit."));
                faultId = null;
            }

            if (faultId is not null && !seen.Add(faultId))
            {
                errors.Add(new ContractError(
                    ErrorCategory.InvalidFault,
                    $"{field}.faultId",
                    $"faultId \"{faultId}\" is duplicated. Fault identity must be unique so alerts can name the fault."));
                faultId = null;
            }

            var kindText = ReadString(item, "kind", $"{field}.kind", errors);
            FaultKind? kind = kindText switch
            {
                "power-loss" => FaultKind.PowerLoss,
                "door-open" => FaultKind.DoorOpen,
                "logger-dropout" => FaultKind.LoggerDropout,
                null => null,
                _ => UnexpectedKind(),
            };

            FaultKind? UnexpectedKind()
            {
                errors.Add(new ContractError(
                    ErrorCategory.InvalidFault,
                    $"{field}.kind",
                    $"{field}.kind must be \"power-loss\", \"door-open\", or \"logger-dropout\". Received \"{kindText}\"."));
                return null;
            }

            var start = ReadFiniteNumber(item, "startMinute", $"{field}.startMinute", errors, ErrorCategory.InvalidTiming);
            var end = ReadFiniteNumber(item, "endMinute", $"{field}.endMinute", errors, ErrorCategory.InvalidTiming);
            if (start is not null && end is not null)
            {
                if (start < 0 || (duration is not null && end > duration) || start >= end)
                {
                    errors.Add(new ContractError(
                        ErrorCategory.InvalidTiming,
                        $"{field}.startMinute",
                        $"{field} must satisfy 0 <= startMinute < endMinute <= time.durationMinutes. Received {Format(start.Value)} to {Format(end.Value)}."));
                    start = null;
                }
            }

            if (faultId is not null && kind is not null && start is not null && end is not null)
            {
                faults.Add(new FaultSpec(faultId, kind.Value, start.Value, end.Value));
            }
        }

        return faults;
    }

    private static double? ReadCoefficient(JsonElement element, string name, string field, List<ContractError> errors)
    {
        var value = ReadFiniteNumber(element, name, field, errors, ErrorCategory.InvalidThermalCoefficient);
        if (value is null)
        {
            return null;
        }

        if (value <= 0 || value > Limits.MaxCoefficientPerMinute)
        {
            errors.Add(new ContractError(
                ErrorCategory.InvalidThermalCoefficient,
                field,
                $"{field} must be a finite number greater than 0 and at most {Limits.MaxCoefficientPerMinute.ToString(CultureInfo.InvariantCulture)} per minute. Received {Format(value.Value)}."));
            return null;
        }

        return value;
    }

    private static double? ReadTemperature(JsonElement element, string name, string field, List<ContractError> errors)
    {
        var value = ReadFiniteNumber(element, name, field, errors, ErrorCategory.InvalidTemperature);
        if (value is null)
        {
            return null;
        }

        if (value < Limits.MinTemperatureCelsius || value > Limits.MaxTemperatureCelsius)
        {
            errors.Add(new ContractError(
                ErrorCategory.InvalidTemperature,
                field,
                $"{field} must be between {Format(Limits.MinTemperatureCelsius)} and {Format(Limits.MaxTemperatureCelsius)} °C. Received {Format(value.Value)}."));
            return null;
        }

        return value;
    }

    private static double? ReadFiniteNumber(
        JsonElement element,
        string name,
        string field,
        List<ContractError> errors,
        string category)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            errors.Add(new ContractError(ErrorCategory.MissingField, field, $"{field} is required."));
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number))
        {
            errors.Add(new ContractError(category, field, $"{field} must be a finite JSON number."));
            return null;
        }

        return number;
    }

    private static string? ReadString(JsonElement element, string name, string field, List<ContractError> errors)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            errors.Add(new ContractError(ErrorCategory.MissingField, field, $"{field} is required."));
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            errors.Add(new ContractError(ErrorCategory.InvalidJson, field, $"{field} must be a string."));
            return null;
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            errors.Add(new ContractError(ErrorCategory.MissingField, field, $"{field} must not be empty."));
            return null;
        }

        return text;
    }

    private static bool TryObject(
        JsonElement parent,
        string name,
        string field,
        List<ContractError> errors,
        out JsonElement element)
    {
        element = default;
        if (!parent.TryGetProperty(name, out var value))
        {
            errors.Add(new ContractError(ErrorCategory.MissingField, field, $"{field} is required."));
            return false;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ContractError(ErrorCategory.InvalidJson, field, $"{field} must be a JSON object."));
            return false;
        }

        element = value;
        return true;
    }

    private static void RejectUnknown(JsonElement element, List<ContractError> errors, string field, string[] allowed)
    {
        var known = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!known.Contains(property.Name))
            {
                errors.Add(new ContractError(
                    ErrorCategory.UnexpectedField,
                    $"{field}.{property.Name}",
                    $"Unexpected field \"{property.Name}\" on {field}. Supported fields: {string.Join(", ", allowed)}."));
            }
        }
    }

    private static string Format(double value) => value.ToString("G", CultureInfo.InvariantCulture);
}
