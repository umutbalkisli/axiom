using Axiom.Secrets;

namespace Axiom.Tests.Runtime;

public class StepRunnerTests
{
    private static StepDefinition Probe(string id, string type = "probe") => new() { Id = id, Name = id, Type = type };

    private static StepExecutionResult Failure(StepDefinition step, string? error = null) => new()
    {
        Id = step.Id, Type = step.Type, Name = step.Id, Assertions = [], Passed = false, Error = error,
    };

    [Fact]
    public async Task Runs_steps_in_order_and_stops_at_the_first_one_that_does_not_pass()
    {
        var probe = new ProbeExecutor { Behavior = (_, step, _) => Task.FromResult(step.Id == "b" ? Failure(step) : ProbeExecutor.Ok(step)) };
        var runner = new StepRunner([probe]);

        var results = await runner.RunAsync(Build.Context(Build.Collection(), runner), [Probe("a"), Probe("b"), Probe("c")], default);

        Assert.Equal(["a", "b"], results.Select(r => r.Id));
        Assert.Equal(2, probe.Runs);
    }

    [Fact]
    public async Task An_unknown_step_type_is_an_error_result()
    {
        var runner = new StepRunner([new ProbeExecutor()]);
        var results = await runner.RunAsync(Build.Context(Build.Collection(), runner), [Probe("a", "teleport")], default);

        var result = Assert.Single(results);
        Assert.Equal(RunOutcome.Error, result.Outcome);
        Assert.Contains("Unsupported step type 'teleport'", result.Error);
    }

    [Fact]
    public async Task An_exception_becomes_an_error_result_instead_of_ending_the_run()
    {
        var probe = new ProbeExecutor { Behavior = (_, _, _) => throw new InvalidOperationException("boom") };
        var runner = new StepRunner([probe]);

        var result = Assert.Single(await runner.RunAsync(Build.Context(Build.Collection(), runner), [Probe("a")], default));

        Assert.Equal(RunOutcome.Error, result.Outcome);
        Assert.Equal("boom", result.Error);
    }

    [Fact]
    public async Task A_step_that_takes_longer_than_the_step_timeout_is_cancelled()
    {
        var probe = new ProbeExecutor
        {
            Behavior = async (_, step, token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return ProbeExecutor.Ok(step);
            },
        };
        var runner = new StepRunner([probe]);
        var started = DateTime.UtcNow;

        var result = Assert.Single(await runner.RunAsync(Build.Context(Build.Collection(timeoutSeconds: 1), runner), [Probe("slow")], default));

        Assert.Equal(RunOutcome.Error, result.Outcome);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task A_step_that_manages_its_own_timeout_is_not_cut_off_by_the_step_timeout()
    {
        var probe = new ProbeExecutor
        {
            ManagesTimeoutOverride = true,
            Behavior = async (_, step, token) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1500), token);
                return ProbeExecutor.Ok(step);
            },
        };
        var runner = new StepRunner([probe]);

        var result = Assert.Single(await runner.RunAsync(Build.Context(Build.Collection(timeoutSeconds: 1), runner), [Probe("long")], default));

        Assert.True(result.Passed);
    }

    [Fact]
    public async Task Secrets_are_masked_in_the_results()
    {
        var secrets = new RunSecrets(new Dictionary<string, string> { ["k"] = "very-secret-value" });
        var probe = new ProbeExecutor { Behavior = (_, step, _) => Task.FromResult(Failure(step, "leaked very-secret-value")) };
        var runner = new StepRunner([probe]);

        var result = Assert.Single(await runner.RunAsync(Build.Context(Build.Collection(), runner, secrets: secrets), [Probe("a")], default));

        Assert.Equal("leaked ********", result.Error);
    }

    [Fact]
    public async Task Step_types_are_matched_case_insensitively()
    {
        var runner = new StepRunner([new ProbeExecutor("Probe")]);
        var results = await runner.RunAsync(Build.Context(Build.Collection(), runner), [Probe("a", "PROBE")], default);
        Assert.True(results[0].Passed);
    }
}
