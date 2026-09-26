using Axiom.Documents;
using Axiom.Validation;

namespace Axiom.Tests.Validation;

public class TestCaseValidatorTests
{
    private static TestCaseValidator Validator() => new(
        [new RequestStepValidator(), new DbQueryStepValidator(), new IncludeStepValidator()],
        BuiltInAssertionAggregations.All,
        BuiltInAssertionOperators.All);

    private static StepDocument Request(string id = "r", string? url = "http://x", string? method = "GET", params AssertionDocument[] assertions) =>
        new() { Id = id, Type = "request", Method = method, Url = url, Assert = assertions.ToList() };

    private static AssertionDocument Assertion(string op = "==", string source = "status", string? aggregate = null) =>
        new() { Source = source, Operator = op, Aggregate = aggregate };

    private static SaveTestCaseRequest Test(params StepDocument[] steps) =>
        new() { Name = "t", Endpoint = "/e", Steps = steps.ToList() };

    private static IEnumerable<string> Fields(IEnumerable<FieldError> errors) => errors.Select(e => e.Field);

    [Fact]
    public void A_complete_test_is_valid() =>
        Assert.Empty(Validator().Validate(Test(Request(assertions: Assertion()))));

    [Fact]
    public void Name_endpoint_and_at_least_one_step_are_required()
    {
        var errors = Validator().Validate(new SaveTestCaseRequest());
        Assert.Equal(["name", "endpoint", "steps"], Fields(errors));
    }

    [Fact]
    public void Steps_need_an_id_and_a_known_type()
    {
        var errors = Validator().Validate(Test(new StepDocument { Id = "", Type = "nope" }));
        Assert.Contains("steps[0].id", Fields(errors));
        var type = Assert.Single(errors, e => e.Field == "steps[0].type");
        Assert.Contains("request", type.Message);
    }

    [Fact]
    public void Each_step_type_checks_its_own_required_fields()
    {
        var errors = Validator().Validate(Test(
            Request(url: null, method: null),
            new StepDocument { Id = "q", Type = "db_query" },
            new StepDocument { Id = "i", Type = "include" }));

        Assert.Equal(
            ["steps[0].method", "steps[0].url", "steps[1].connection", "steps[1].sql", "steps[2].ref"],
            Fields(errors));
    }

    [Fact]
    public void Assertions_need_a_source_and_a_known_operator_and_aggregation()
    {
        var errors = Validator().Validate(Test(Request(assertions:
        [
            new AssertionDocument { Source = "", Operator = "" },
            Assertion(op: "equals"),
            Assertion(aggregate: "median"),
            Assertion(aggregate: "SUM"),   // known, case-insensitive
        ])));

        Assert.Equal(
            ["steps[0].assert[0].source", "steps[0].assert[0].operator", "steps[0].assert[1].operator", "steps[0].assert[2].aggregate"],
            Fields(errors));
        Assert.Contains("Use one of", errors.First(e => e.Field.EndsWith("assert[1].operator")).Message);
    }

    [Fact]
    public void Variable_and_save_as_names_follow_the_naming_rule()
    {
        var request = Test(new StepDocument { Id = "r", Type = "request", Method = "GET", Url = "x", SaveAs = "r-1" });
        request.Variables = new Dictionary<string, object?> { ["a1"] = 1, ["good_one"] = 2 };

        var errors = Validator().Validate(request);

        Assert.Contains("variables", Fields(errors));
        Assert.Contains("steps[0].saveAs", Fields(errors));
        Assert.Equal(1, errors.Count(e => e.Field == "variables"));
    }

    [Fact]
    public void Shared_steps_need_a_name_a_valid_run_mode_and_steps()
    {
        var errors = Validator().Validate(new SaveSharedStepsRequest { Run = "sometimes" });
        Assert.Equal(["name", "run", "steps"], Fields(errors));

        Assert.Empty(Validator().Validate(new SaveSharedStepsRequest { Name = "n", Run = "ONCE", Steps = [Request()] }));
        Assert.Empty(Validator().Validate(new SaveSharedStepsRequest { Name = "n", Steps = [Request()] }));
    }

    [Fact]
    public void Include_references_must_exist()
    {
        var steps = new List<StepDocument> { new() { Id = "a", Type = "include", Ref = "known" }, new() { Id = "b", Type = "include", Ref = "gone" }, Request() };
        var errors = TestCaseValidator.ValidateIncludes(steps, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "KNOWN" }).ToList();
        Assert.Equal(["steps[1].ref"], Fields(errors));
    }
}

public class CollectionSettingsValidatorTests
{
    [Fact]
    public void Variable_names_are_checked()
    {
        var errors = CollectionSettingsValidator.ValidateVariables(new() { ["good"] = 1, ["bad name"] = 2, [""] = 3, ["secret"] = 4 });
        Assert.Equal(3, errors.Count);
        Assert.All(errors, e => Assert.Equal("variables", e.Field));
    }

    [Fact]
    public void Connection_names_cannot_be_empty()
    {
        Assert.Empty(CollectionSettingsValidator.ValidateConnections(new() { ["db"] = new object() }));
        Assert.Single(CollectionSettingsValidator.ValidateConnections(new() { [" "] = new object() }));
    }

    [Fact]
    public void Secrets_need_a_valid_name_provider_key_and_valid_environment_overrides()
    {
        var errors = CollectionSettingsValidator.ValidateSecrets(new()
        {
            ["fine"] = new SecretReference { Provider = "env", Key = "X" },
            ["1bad"] = new SecretReference { Provider = "env", Key = "X" },
            ["nokey"] = new SecretReference { Provider = "env", Key = "" },
            ["noprovider"] = new SecretReference { Provider = "", Key = "X" },
            ["envs"] = new SecretReference
            {
                Provider = "env",
                Key = "X",
                Environments = new() { ["ci"] = new SecretSource { Provider = "k8s", Key = "a/b" }, ["bad"] = new SecretSource() },
            },
        });

        var fields = errors.Select(e => e.Field).ToList();
        Assert.Contains("secrets", fields);                                  // the invalid name
        Assert.Contains("secrets.nokey.key", fields);
        Assert.Contains("secrets.noprovider.provider", fields);
        Assert.Contains("secrets.envs.environments.bad.provider", fields);
        Assert.Contains("secrets.envs.environments.bad.key", fields);
        Assert.DoesNotContain(fields, f => f.Contains("fine") || f.Contains("environments.ci"));
    }
}
