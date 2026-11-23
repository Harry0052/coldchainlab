using ColdChainLab.Core;

namespace ColdChainLab.Tests;

public class ScenarioValidatorTests
{
    [Fact]
    public void Valid_power_loss_fixture_is_accepted_and_keeps_fault_identity()
    {
        var json = File.ReadAllText(RepoFiles.Path("fixtures/valid/power-loss-drill.scenario.json"));
        var result = ScenarioValidator.Read(json);

        Assert.True(result.Accepted);
        var scenario = Assert.IsType<Scenario>(result.Scenario);
        Assert.Equal("power-loss-drill", scenario.ScenarioId);
        Assert.Equal("min", Limits.TimeUnit);
        var fault = Assert.Single(scenario.Faults);
        Assert.Equal("power-loss-1", fault.FaultId);
        Assert.Equal(FaultKind.PowerLoss, fault.Kind);
        Assert.Equal(5, scenario.Logger.CadenceMinutes);
        Assert.Equal(8, scenario.Logger.HighCelsius);
    }

    [Fact]
    public void Negative_coefficient_fixture_is_rejected_with_an_actionable_error()
    {
        var json = File.ReadAllText(RepoFiles.Path("fixtures/invalid/negative-coefficient.scenario.json"));
        var result = ScenarioValidator.Read(json);

        Assert.False(result.Accepted);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCategory.InvalidThermalCoefficient, error.Category);
        Assert.Equal("thermal.coefficientPerMinute", error.Field);
        Assert.Contains("greater than 0", error.Message, StringComparison.Ordinal);
        Assert.Contains("-0.2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Oversized_payload_is_rejected_before_it_is_treated_as_a_scenario()
    {
        var result = ScenarioValidator.Read("{}", Limits.MaxScenarioBytes + 1);

        Assert.False(result.Accepted);
        Assert.Equal(ErrorCategory.InputTooLarge, Assert.Single(result.Errors).Category);
    }

    [Fact]
    public void Unreasonably_long_runs_and_unsafe_ids_are_rejected()
    {
        var json = File.ReadAllText(RepoFiles.Path("fixtures/valid/stable-hold.scenario.json"));
        var tooLong = ScenarioValidator.Read(json.Replace("\"durationMinutes\": 20", "\"durationMinutes\": 1441", StringComparison.Ordinal));
        Assert.Contains(tooLong.Errors, error => error.Category == ErrorCategory.InvalidTiming
            && error.Field == "time.durationMinutes");

        var badId = ScenarioValidator.Read(json.Replace("\"scenarioId\": \"stable-hold\"", "\"scenarioId\": \"../escape\"", StringComparison.Ordinal));
        Assert.Contains(badId.Errors, error => error.Category == ErrorCategory.InvalidScenarioId);
    }

    [Fact]
    public void Non_positive_cadence_is_invalid_timing()
    {
        var json = File.ReadAllText(RepoFiles.Path("fixtures/valid/stable-hold.scenario.json"))
            .Replace("\"cadenceMinutes\": 5", "\"cadenceMinutes\": 0", StringComparison.Ordinal);
        var result = ScenarioValidator.Read(json);

        Assert.False(result.Accepted);
        Assert.Contains(result.Errors, error => error.Category == ErrorCategory.InvalidTiming
            && error.Field == "logger.cadenceMinutes");
    }

    [Theory]
    [InlineData("fixtures/valid/stable-hold.scenario.json", "stable-hold", 0)]
    [InlineData("fixtures/valid/power-loss-dropout.scenario.json", "power-loss-dropout", 2)]
    public void Labeled_valid_fixtures_parse(string relativePath, string scenarioId, int faultCount)
    {
        var result = ScenarioValidator.Read(File.ReadAllText(RepoFiles.Path(relativePath)));

        Assert.True(result.Accepted);
        Assert.Equal(scenarioId, result.Scenario!.ScenarioId);
        Assert.Equal(faultCount, result.Scenario.Faults.Count);
    }
}
