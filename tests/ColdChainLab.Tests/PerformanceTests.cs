using System.Diagnostics;
using ColdChainLab.Core;

namespace ColdChainLab.Tests;

public class PerformanceTests
{
    [Fact]
    public void Bundled_drill_stays_inside_the_interactive_budget()
    {
        var scenario = ScenarioValidator.Read(
            File.ReadAllText(RepoFiles.Path("fixtures/valid/power-loss-drill.scenario.json"))).Scenario!;
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 50; i++)
        {
            var result = SimulationEngine.Run(scenario);
            Assert.Equal(3, result.Report.Events.Single(item => item.Kind == "excursion-alert").DelayMinutes);
        }

        watch.Stop();
        Assert.True(watch.ElapsedMilliseconds < 2000, $"50 drills took {watch.ElapsedMilliseconds} ms");
    }
}
