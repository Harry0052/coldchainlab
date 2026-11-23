using System.Globalization;
using System.Text.Json;
using ColdChainLab.Core;

namespace ColdChainLab.Cli;

public static class CommandRunner
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            stderr.WriteLine("Missing command. Use validate or run, or pass --help.");
            WriteHelp(stdout);
            return 2;
        }

        if (args[0] is "--help" or "-h" or "help")
        {
            WriteHelp(stdout);
            return 0;
        }

        if (args[0] is not ("validate" or "run" or "demo"))
        {
            stderr.WriteLine($"Unknown command \"{args[0]}\".");
            WriteHelp(stdout);
            return 2;
        }

        var command = args[0];
        var json = false;
        string? path = null;
        string? output = null;
        string? bundledName = null;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--json")
            {
                json = true;
                continue;
            }

            if (args[i] == "--out")
            {
                if (i + 1 >= args.Length)
                {
                    stderr.WriteLine("--out requires a directory.");
                    return 2;
                }

                output = args[++i];
                continue;
            }

            if (args[i].StartsWith('-'))
            {
                stderr.WriteLine($"Unknown option \"{args[i]}\".");
                return 2;
            }

            if (command == "demo" && args[i] is "dropout" or "power-loss")
            {
                bundledName = args[i];
                continue;
            }

            if (path is not null)
            {
                stderr.WriteLine($"{command} accepts one scenario path.");
                return 2;
            }

            path = args[i];
        }

        string? sourceLabel = null;
        if (command == "demo")
        {
            if (path is not null)
            {
                stderr.WriteLine("demo does not take a scenario path. Use power-loss or dropout.");
                return 2;
            }

            bundledName ??= "power-loss";
            var scenarioName = bundledName == "dropout" ? "power-loss-dropout" : "power-loss-drill";
            path = MaterializeBundled(scenarioName, stderr);
            if (path is null)
            {
                return 1;
            }

            sourceLabel = "bundled:" + scenarioName;
            output ??= Path.Combine("artifacts", bundledName == "dropout" ? "demo-dropout" : "demo");
        }

        if (path is null)
        {
            stderr.WriteLine($"{command} requires a scenario JSON path.");
            return 2;
        }

        if (command == "run" && string.IsNullOrWhiteSpace(output))
        {
            stderr.WriteLine("run requires --out <directory>.");
            return 2;
        }

        var result = ScenarioFile.ReadPath(path);
        if (!result.Accepted || result.Scenario is null)
        {
            if (json)
            {
                stdout.Write(WriteValidationJson(result));
            }
            else
            {
                WriteRejected(stdout, result);
            }

            return 2;
        }

        if (command == "validate")
        {
            if (json)
            {
                stdout.Write(WriteValidationJson(result));
            }
            else
            {
                WriteAccepted(stdout, result.Scenario, simulated: false);
            }

            return 0;
        }

        var simulation = SimulationEngine.Run(result.Scenario);
        IReadOnlyList<string> written;
        try
        {
            written = ResultWriter.Write(simulation, output!, sourceLabel ?? path);
            CopyScenario(path, output!, result.Scenario.ScenarioId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            stderr.WriteLine($"Could not write the output directory. {ex.Message}");
            return 1;
        }

        var eventsPath = written.Single(item => item.EndsWith(".events.json", StringComparison.Ordinal));
        if (json)
        {
            stdout.Write(EventReportWriter.Write(simulation.Report));
        }
        else
        {
            WriteSimulated(stdout, simulation, eventsPath);
        }

        return 0;
    }

    public static void WriteHelp(TextWriter stdout)
    {
        stdout.WriteLine("coldchainlab — local rehearsal for one synthetic refrigerator logger");
        stdout.WriteLine(Limits.Limitation);
        stdout.WriteLine();
        stdout.WriteLine("Usage:");
        stdout.WriteLine("  coldchainlab demo [power-loss|dropout] [--out <directory>] [--json]");
        stdout.WriteLine("  coldchainlab run <scenario.json> --out <directory> [--json]");
        stdout.WriteLine("  coldchainlab validate <scenario.json> [--json]");
        stdout.WriteLine("  coldchainlab --help");
        stdout.WriteLine();
        stdout.WriteLine("Exit codes: 0 accepted or help, 2 rejected input or usage error, 1 could not write output.");
        stdout.WriteLine("Time unit for scenario fields and reports is minutes. Temperature unit is °C.");
        stdout.WriteLine("demo runs a bundled synthetic drill. dropout drops the minute-15 and minute-20 samples.");
        stdout.WriteLine("Output files: <scenarioId>.scenario.json, .temperature.csv, .observations.csv, .events.json, and .summary.txt.");
    }

    private static string? MaterializeBundled(string scenarioName, TextWriter stderr)
    {
        var resource = $"ColdChainLab.Cli.Bundled.{scenarioName}.scenario.json";
        var stream = typeof(CommandRunner).Assembly.GetManifestResourceStream(resource);
        if (stream is null)
        {
            stderr.WriteLine($"Bundled drill \"{scenarioName}\" is missing from the application.");
            return null;
        }

        var directory = Directory.CreateTempSubdirectory("coldchainlab-bundled").FullName;
        var path = Path.Combine(directory, scenarioName + ".scenario.json");
        using (stream)
        using (var file = File.Create(path))
        {
            stream.CopyTo(file);
        }

        return path;
    }

    private static void CopyScenario(string sourcePath, string outputDirectory, string scenarioId)
    {
        var destination = Path.Combine(Path.GetFullPath(outputDirectory), scenarioId + ".scenario.json");
        File.Copy(sourcePath, destination, overwrite: true);
    }

    private static void WriteAccepted(TextWriter stdout, Scenario scenario, bool simulated)
    {
        stdout.WriteLine($"accepted {scenario.ScenarioId}");
        stdout.WriteLine($"schema {scenario.SchemaVersion}");
        stdout.WriteLine(simulated ? "simulation-status completed" : "simulation-status not-run");
        stdout.WriteLine($"time-unit {Limits.TimeUnit}");
        stdout.WriteLine($"temperature-unit {Limits.TemperatureUnit}");
        stdout.WriteLine($"faults {scenario.Faults.Count}");
        stdout.WriteLine($"limitation {Limits.Limitation}");
    }

    private static void WriteSimulated(TextWriter stdout, SimulationResult result, string eventsPath)
    {
        var report = result.Report;
        var alerts = report.Events.Where(item => item.Kind == "excursion-alert").ToArray();
        stdout.WriteLine($"scenario {result.Scenario.ScenarioId}");
        stdout.WriteLine("simulation-status completed");
        stdout.WriteLine($"limitation {Limits.Limitation}");
        stdout.WriteLine($"time-unit {report.TimeUnit}");
        stdout.WriteLine($"temperature-unit {report.TemperatureUnit}");
        stdout.WriteLine($"thresholds {report.LowCelsius.ToString("0.###", CultureInfo.InvariantCulture)} to {report.HighCelsius.ToString("0.###", CultureInfo.InvariantCulture)} C");
        stdout.WriteLine($"true-excursions {report.Events.Count(item => item.Kind == "true-excursion")}");
        stdout.WriteLine($"excursion-alerts {alerts.Length}");
        stdout.WriteLine($"missed-excursions {report.Events.Count(item => item.Kind == "missed-excursion")}");
        stdout.WriteLine($"gap-alerts {report.Events.Count(item => item.Kind == "gap-alert")}");
        foreach (var alert in alerts)
        {
            stdout.WriteLine($"detection-delay-min {alert.DelayMinutes?.ToString("0.######", CultureInfo.InvariantCulture)} fault {alert.FaultId}");
        }

        stdout.WriteLine($"events {eventsPath}");
    }

    private static void WriteRejected(TextWriter stdout, ScenarioValidation result)
    {
        stdout.WriteLine("rejected");
        foreach (var error in result.Errors)
        {
            stdout.WriteLine($"{error.Category} {error.Field}: {error.Message}");
        }
    }

    private static string WriteValidationJson(ScenarioValidation result)
    {
        var payload = new
        {
            schemaVersion = Limits.SchemaVersion,
            command = "validate",
            status = result.Accepted ? "accepted" : "rejected",
            scenarioId = result.Scenario?.ScenarioId,
            simulation = true,
            simulationStatus = "not-run",
            limitation = Limits.Limitation,
            timeUnit = Limits.TimeUnit,
            temperatureUnit = Limits.TemperatureUnit,
            errors = result.Errors.Select(error => new
            {
                category = error.Category,
                field = error.Field,
                message = error.Message,
            }),
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }
}
