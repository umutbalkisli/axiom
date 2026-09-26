namespace Axiom.Tests.Runtime;

public class IncludeStepExecutorTests
{
    private static SharedStepsDefinition Group(string name, string run, params StepDefinition[] steps) =>
        new() { Name = name, Run = run, Steps = steps.ToList() };

    private static StepDefinition ProbeStep(string id, string? saveAs = null) => new() { Id = id, Name = id, Type = "probe", SaveAs = saveAs };

    private static (StepRunner Runner, SharedStepsLibrary Library) Setup(ProbeExecutor probe, Dictionary<string, SharedStepsDefinition> groups) =>
        (new StepRunner([probe, new IncludeStepExecutor()]), new SharedStepsLibrary(groups));

    private static async Task<(List<StepExecutionResult> Results, StepExecutionContext Context)> RunTest(StepRunner runner, SharedStepsLibrary library, params StepDefinition[] steps)
    {
        var context = Build.Context(Build.Collection(), runner, library);
        var results = await runner.RunAsync(context, steps, default);
        return (results, context);
    }

    [Fact]
    public async Task An_each_group_runs_inside_the_test_and_its_variables_reach_the_following_steps()
    {
        var probe = new ProbeExecutor();
        var (runner, library) = Setup(probe, new() { ["setup"] = Group("Setup data", "each", ProbeStep("s1", "saved")) });

        var (results, context) = await RunTest(runner, library, Build.Include("inc", "setup"));

        var include = Assert.Single(results);
        Assert.True(include.Passed);
        Assert.Equal("Setup data", include.Name);                       // no step name: the group's name is used
        Assert.Equal(["s1"], include.Children!.Select(c => c.Id));
        Assert.Equal("value-of-s1", context.Variables["saved"]);
    }

    [Fact]
    public async Task An_each_group_runs_again_for_every_test()
    {
        var probe = new ProbeExecutor();
        var (runner, library) = Setup(probe, new() { ["setup"] = Group("g", "each", ProbeStep("s1")) });

        await RunTest(runner, library, Build.Include("a", "setup"));
        await RunTest(runner, library, Build.Include("a", "setup"));

        Assert.Equal(2, probe.Runs);
    }

    [Fact]
    public async Task A_step_name_overrides_the_group_name()
    {
        var probe = new ProbeExecutor();
        var (runner, library) = Setup(probe, new() { ["g"] = Group("Group name", "each", ProbeStep("s")) });
        var step = Build.Include("inc", "g");
        step.Name = "My own name";

        var (results, _) = await RunTest(runner, library, step);

        Assert.Equal("My own name", results[0].Name);
    }

    [Fact]
    public async Task A_once_group_runs_a_single_time_for_many_parallel_tests_and_every_test_gets_its_variables()
    {
        var probe = new ProbeExecutor
        {
            Behavior = async (context, step, _) =>
            {
                await Task.Delay(100);      // long enough that all tests are waiting for the first run
                context.Variables["token"] = "shared-token";
                return ProbeExecutor.Ok(step);
            },
        };
        var (runner, library) = Setup(probe, new() { ["login"] = Group("Login", "once", ProbeStep("l")) });

        var runs = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => RunTest(runner, library, Build.Include("i", "login"))));

        Assert.Equal(1, probe.Runs);
        Assert.All(runs, run =>
        {
            Assert.True(run.Results[0].Passed);
            Assert.Equal("shared-token", run.Context.Variables["token"]);
            Assert.Equal(["l"], run.Results[0].Children!.Select(c => c.Id));
        });
    }

    [Fact]
    public async Task A_once_group_does_not_see_the_variables_of_the_test_that_triggered_it_and_exports_only_what_it_changed()
    {
        var seen = new List<bool>();
        var probe = new ProbeExecutor
        {
            Behavior = (context, step, _) =>
            {
                seen.Add(context.Variables.ContainsKey("test_only"));
                context.Variables["produced"] = 1;
                return Task.FromResult(ProbeExecutor.Ok(step));
            },
        };
        var (runner, library) = Setup(probe, new() { ["g"] = Group("g", "once", ProbeStep("s")) });
        var context = Build.Context(Build.Collection(), runner, library, variables: new(StringComparer.OrdinalIgnoreCase) { ["test_only"] = "x" });

        await runner.RunAsync(context, [Build.Include("i", "g")], default);

        Assert.Equal([false], seen);
        Assert.Equal(1, context.Variables["produced"]);
        Assert.Equal("x", context.Variables["test_only"]);       // untouched
    }

    [Fact]
    public async Task A_saved_response_from_a_once_group_is_copied_per_test()
    {
        var probe = new ProbeExecutor
        {
            Behavior = (context, step, _) =>
            {
                context.Variables["session"] = new LazyJson("{\"a\":1}");
                return Task.FromResult(ProbeExecutor.Ok(step));
            },
        };
        var (runner, library) = Setup(probe, new() { ["g"] = Group("g", "once", ProbeStep("s")) });

        var first = (await RunTest(runner, library, Build.Include("i", "g"))).Context.Variables["session"];
        var second = (await RunTest(runner, library, Build.Include("i", "g"))).Context.Variables["session"];

        Assert.Equal(1, probe.Runs);
        Assert.NotSame(first, second);          // parallel tests must not share one JSON tree
        Assert.Equal("{\"a\":1}", ((LazyJson)second!).Text);
    }

    [Fact]
    public async Task When_a_once_group_fails_every_including_test_fails_with_the_same_result()
    {
        var probe = new ProbeExecutor { Behavior = (_, step, _) => Task.FromResult(new StepExecutionResult { Id = step.Id, Type = "probe", Name = step.Id, Assertions = [], Passed = false, Error = "login refused" }) };
        var (runner, library) = Setup(probe, new() { ["login"] = Group("Login", "once", ProbeStep("l")) });

        var first = await RunTest(runner, library, Build.Include("i", "login"));
        var second = await RunTest(runner, library, Build.Include("i", "login"));

        Assert.Equal(1, probe.Runs);
        Assert.All(new[] { first, second }, run =>
        {
            Assert.False(run.Results[0].Passed);
            Assert.Equal(RunOutcome.Error, run.Results[0].Outcome);       // the failing child could not run
            Assert.Equal("login refused", run.Results[0].Children![0].Error);
        });
    }

    [Fact]
    public async Task Groups_can_include_groups()
    {
        var probe = new ProbeExecutor();
        var (runner, library) = Setup(probe, new()
        {
            ["outer"] = Group("Outer", "each", Build.Include("in", "inner"), ProbeStep("o", "outer_var")),
            ["inner"] = Group("Inner", "each", ProbeStep("i", "inner_var")),
        });

        var (results, context) = await RunTest(runner, library, Build.Include("top", "outer"));

        Assert.True(results[0].Passed);
        Assert.Equal("Inner", results[0].Children![0].Name);
        Assert.Equal("value-of-i", context.Variables["inner_var"]);
        Assert.Equal("value-of-o", context.Variables["outer_var"]);
    }

    [Fact]
    public async Task A_cycle_is_reported_instead_of_running_forever()
    {
        var probe = new ProbeExecutor();
        var (runner, library) = Setup(probe, new()
        {
            ["a"] = Group("A", "each", Build.Include("x", "b")),
            ["b"] = Group("B", "each", Build.Include("y", "a")),
        });

        var (results, _) = await RunTest(runner, library, Build.Include("z", "a"));

        var error = results[0].Children![0].Children![0];   // z -> a (x) -> b (y) -> a: refused
        Assert.Equal(RunOutcome.Error, results[0].Outcome);
        Assert.Contains("include themselves", error.Error);
        Assert.Contains("a -> b -> a", error.Error);
    }

    [Theory]
    [InlineData(null, "requires ref")]
    [InlineData("  ", "requires ref")]
    [InlineData("nope", "were not found")]
    public async Task A_missing_or_unknown_reference_is_an_error(string? reference, string expectedMessage)
    {
        var (runner, library) = Setup(new ProbeExecutor(), new());

        var (results, _) = await RunTest(runner, library, Build.Include("i", reference!));

        Assert.Equal(RunOutcome.Error, results[0].Outcome);
        Assert.Contains(expectedMessage, results[0].Error);
    }
}
