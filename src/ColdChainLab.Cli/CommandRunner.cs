using System.Text.Json;
using ColdChainLab.Core;

namespace ColdChainLab.Cli;

public static class CommandRunner
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            stderr.WriteLine("Missing command. Use validate <scenario.json> or --help.");
            WriteHelp(stdout);
            return 2;
        }

        if (args[0] is "--help" or "-h" or "help")
        {
            WriteHelp(stdout);
            return 0;
        }

        if (args[0] != "validate")
        {
            stderr.WriteLine($"Unknown command \"{args[0]}\".");
            WriteHelp(stdout);
            return 2;
        }

        var json = false;
        string? path = null;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--json")
            {
                json = true;
                continue;
            }

            if (args[i].StartsWith('-'))
            {
                stderr.WriteLine($"Unknown option \"{args[i]}\".");
                return 2;
            }

            if (path is not null)
            {
                stderr.WriteLine("validate accepts one scenario path.");
                return 2;
            }

            path = args[i];
        }

        if (path is null)
        {
            stderr.WriteLine("validate requires a scenario JSON path.");
            return 2;
        }

        var result = ScenarioFile.ReadPath(path);
        if (json)
        {
            stdout.Write(WriteJson(result));
        }
        else
        {
            WriteHuman(stdout, result);
        }

        return result.Accepted ? 0 : 2;
    }

    public static void WriteHelp(TextWriter stdout)
    {
        stdout.WriteLine("coldchainlab — local rehearsal for one synthetic refrigerator logger");
        stdout.WriteLine(Limits.Limitation);
        stdout.WriteLine();
        stdout.WriteLine("Usage:");
        stdout.WriteLine("  coldchainlab validate <scenario.json> [--json]");
        stdout.WriteLine("  coldchainlab --help");
        stdout.WriteLine();
        stdout.WriteLine("Exit codes: 0 accepted or help, 2 rejected input or usage error.");
        stdout.WriteLine("Time unit for scenario fields is minutes. Temperature unit is °C.");
    }

    private static void WriteHuman(TextWriter stdout, ScenarioValidation result)
    {
        if (result.Accepted && result.Scenario is not null)
        {
            var scenario = result.Scenario;
            stdout.WriteLine($"accepted {scenario.ScenarioId}");
            stdout.WriteLine($"schema {scenario.SchemaVersion}");
            stdout.WriteLine("simulation-status not-run");
            stdout.WriteLine($"time-unit {Limits.TimeUnit}");
            stdout.WriteLine($"temperature-unit {Limits.TemperatureUnit}");
            stdout.WriteLine($"faults {scenario.Faults.Count}");
            stdout.WriteLine($"limitation {Limits.Limitation}");
            return;
        }

        stdout.WriteLine("rejected");
        foreach (var error in result.Errors)
        {
            stdout.WriteLine($"{error.Category} {error.Field}: {error.Message}");
        }
    }

    private static string WriteJson(ScenarioValidation result)
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
