using FluentAssertions;
using System.Text.RegularExpressions;

namespace Wintime.Control.Tests.Unit.Deployment;

public class SmokePackageContractTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void Report_emulator_launcher_uses_report_compose_project()
    {
        var path = PackagePath("3-run-emulator-report.bat");

        File.Exists(path).Should().BeTrue("the report stack needs its own emulator launcher");
        var script = File.ReadAllText(path);
        script.Should().Contain(
            "docker compose -f docker-compose.test-report.yml run --rm -it --use-aliases emulator --config-dir /app/config");
    }

    [Fact]
    public void Live_smoke_stacks_detect_uc2_long_pause_within_shortest_gap()
    {
        var connector = File.ReadAllText(PackagePath("config-connector", "machine-01.json"));
        var connectorGapSeconds = ReadInt(connector, "seriesGapMs") / 1000;
        var apiSettings = File.ReadAllText(Path.Combine(RepoRoot, "Wintime.Control.API", "appsettings.json"));
        var pollingSeconds = ReadInt(apiSettings, "PollingIntervalSeconds");
        var preset = File.ReadAllText(PackagePath("config-emulator", "presets", "uc2_semi_auto.preset"));
        var shortestPause = Regex.Match(preset, @"AT \+rand\((\d+),60000\)");
        shortestPause.Success.Should().BeTrue("UC2 must declare its randomized long pause");
        var shortestPauseSeconds = int.Parse(shortestPause.Groups[1].Value) / 1000;

        foreach (var composeFile in new[] { "docker-compose.test.yml", "docker-compose.pzp.yml" })
        {
            var compose = File.ReadAllText(PackagePath(composeFile));
            var controlThresholdSeconds = ReadInt(compose, "Downtime__IdleThresholdSeconds");

            (connectorGapSeconds + controlThresholdSeconds + pollingSeconds).Should().BeLessThan(
                shortestPauseSeconds,
                "Control starts its own timer only after the connector publishes Idle; worker polling adds another delay");
        }
    }

    [Fact]
    public void Uc1_starts_with_three_cycles_without_injection()
    {
        var preset = File.ReadAllText(PackagePath("config-emulator", "presets", "uc1_normal_auto.preset"));
        preset.Should().Contain("REPEAT CYCLE_SETUP FROM \"macros/cycle_setup\" x3");
        preset.Should().Contain("REPEAT CYCLE_NORMAL FROM \"macros/cycle_normal\" x495");

        var setupPath = PackagePath("config-emulator", "presets", "macros", "cycle_setup.mac");
        File.Exists(setupPath).Should().BeTrue("setup cycles need a macro distinct from automatic production");
        var setup = File.ReadAllText(setupPath);
        setup.Should().NotContain("SET Injection 1", "injection makes the connector classify a cycle as Auto");

        var connector = File.ReadAllText(PackagePath("config-connector", "machine-01.json"));
        var candidateGapMs = ReadInt(connector, "seriesGapMs");
        var pollingMarginMs = 3 * ReadInt(connector, "pollIntervalMs");
        var closedAtMs = ReadLastMacroTimeMs(setup, "SET MoldClosed 1");
        var openedAtMs = ReadLastMacroTimeMs(setup, "SET MoldOpened 1");
        (openedAtMs - closedAtMs + pollingMarginMs).Should().BeLessThan(
            candidateGapMs,
            "MoldOpened must arrive before S1_Candidate expires and becomes Idle");
    }

    [Fact]
    public void Db_password_contract_does_not_impose_character_whitelist()
    {
        foreach (var launcher in new[] { "2-start.bat", "2-start-report.bat" })
        {
            var script = File.ReadAllText(PackagePath(launcher));
            script.Should().NotContain(
                "$values[0] -notmatch '^DB_PASSWORD=[A-Za-z0-9]+$'",
                "the launchers must accept the existing database password without inventing character restrictions");
            script.Should().NotContain("Некорректный DB_PASSWORD");
        }

        var template = File.ReadAllText(PackagePath(".env.example"));
        template.Should().Contain("Пароль PostgreSQL (любая надёжная строка)");
    }

    [Fact]
    public void Normal_cycle_holds_its_declared_cushion_for_polling()
    {
        var macro = File.ReadAllText(PackagePath("config-emulator", "presets", "macros", "cycle_normal.mac"));

        macro.Should().Contain("# Подушка 1200 мВ");
        macro.Should().Contain("AT 13600 SET Injection 0");
        macro.Should().Contain("AT 13600 RAMP InjectionPosition 8000.0 24400");
    }

    [Fact]
    public void Readme_compose_commands_select_the_development_file()
    {
        var readme = File.ReadAllText(Path.Combine(RepoRoot, "README.md"));

        readme.Should().Contain("docker compose -f docker-compose.dev.yml up");
        readme.Should().Contain("docker compose -f docker-compose.dev.yml ps -a");
        readme.Should().Contain("docker compose -f docker-compose.dev.yml logs migrations");
        readme.Should().NotContain("docker-compose up");
    }

    private static int ReadInt(string text, string key)
    {
        var match = Regex.Match(text, $"\"?{Regex.Escape(key)}\"?\\s*:\\s*\"?(\\d+)\"?");
        match.Success.Should().BeTrue($"expected numeric setting {key}");
        return int.Parse(match.Groups[1].Value);
    }

    private static int ReadLastMacroTimeMs(string macro, string instruction)
    {
        var matches = Regex.Matches(macro, $"AT\\s+(\\d+)\\s+{Regex.Escape(instruction)}");
        matches.Count.Should().BeGreaterThan(0, $"expected macro instruction {instruction}");
        return int.Parse(matches[matches.Count - 1].Groups[1].Value);
    }

    private static string PackagePath(params string[] segments)
    {
        var parts = new[] { RepoRoot, "deploy", "smoke-test", "package" }.Concat(segments).ToArray();
        return Path.Combine(parts);
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Wintime.Control.sln")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }
}
