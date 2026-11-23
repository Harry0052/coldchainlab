using ColdChainLab.Cli;
using ColdChainLab.Core;

namespace ColdChainLab.Tests;

public class WorkflowTests
{
    [Fact]
    public void Summary_names_the_delay_and_the_source_fixture()
    {
        var path = RepoFiles.Path("fixtures/valid/power-loss-drill.scenario.json");
        var result = SimulationEngine.Run(ScenarioValidator.Read(File.ReadAllText(path)).Scenario!);
        var summary = SummaryWriter.Write(result, path);

        Assert.Contains("simulation-status: completed", summary, StringComparison.Ordinal);
        Assert.Contains(Limits.Limitation, summary, StringComparison.Ordinal);
        Assert.Contains("time-unit: min", summary, StringComparison.Ordinal);
        Assert.Contains("temperature-unit: C", summary, StringComparison.Ordinal);
        Assert.Contains("detection delay: 3 min, fault power-loss-1", summary, StringComparison.Ordinal);
        Assert.Contains("minute_min  truth_c   observed_c  evidence         finding", summary, StringComparison.Ordinal);
        Assert.Contains("true-excursion power-loss-1 onset", summary, StringComparison.Ordinal);
        Assert.Contains(path, summary, StringComparison.Ordinal);
        Assert.Contains("missed excursions: 0", summary, StringComparison.Ordinal);
        Assert.Contains("measurement gaps: 0", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Dropout_summary_keeps_the_miss_and_the_gap_apart()
    {
        var path = RepoFiles.Path("fixtures/valid/power-loss-dropout.scenario.json");
        var result = SimulationEngine.Run(ScenarioValidator.Read(File.ReadAllText(path)).Scenario!);
        var summary = SummaryWriter.Write(result, path);

        Assert.Contains("missed excursion: fault power-loss-1", summary, StringComparison.Ordinal);
        Assert.Contains("measurement gap: fault logger-dropout-1", summary, StringComparison.Ordinal);
        Assert.Contains("recovery: no sample arrived after the gap", summary, StringComparison.Ordinal);
        Assert.Contains("  gap  ", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("detection delay: 3", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Demo_writes_an_inspectable_export()
    {
        var output = Directory.CreateTempSubdirectory("coldchainlab-demo");
        try
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exit = CommandRunner.Run(["demo", "--out", output.FullName], stdout, stderr);

            Assert.Equal(0, exit);
            Assert.Equal(string.Empty, stderr.ToString());
            Assert.Contains("detection-delay-min 3 fault power-loss-1", stdout.ToString(), StringComparison.Ordinal);
            var summary = File.ReadAllText(Path.Combine(output.FullName, "power-loss-drill.summary.txt"));
            Assert.Contains("source: bundled:power-loss-drill", summary, StringComparison.Ordinal);
            Assert.Contains("\"faultId\": \"power-loss-1\"", File.ReadAllText(Path.Combine(output.FullName, "power-loss-drill.scenario.json")), StringComparison.Ordinal);
            Assert.Contains("\"delayMinutes\": 3", File.ReadAllText(Path.Combine(output.FullName, "power-loss-drill.events.json")), StringComparison.Ordinal);
        }
        finally
        {
            output.Delete(true);
        }
    }

    [Fact]
    public void Invalid_run_explains_the_coefficient_and_writes_nothing()
    {
        var output = Directory.CreateTempSubdirectory("coldchainlab-bad");
        try
        {
            var stdout = new StringWriter();
            var exit = CommandRunner.Run(
                ["run", RepoFiles.Path("fixtures/invalid/negative-coefficient.scenario.json"), "--out", output.FullName],
                stdout,
                new StringWriter());

            Assert.Equal(2, exit);
            var text = stdout.ToString();
            Assert.Contains("rejected", text, StringComparison.Ordinal);
            Assert.Contains("thermal.coefficientPerMinute", text, StringComparison.Ordinal);
            Assert.Contains("greater than 0", text, StringComparison.Ordinal);
            Assert.Empty(output.GetFileSystemInfos());
        }
        finally
        {
            output.Delete(true);
        }
    }
}
