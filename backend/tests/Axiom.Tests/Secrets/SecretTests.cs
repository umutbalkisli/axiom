using Axiom.Secrets;

namespace Axiom.Tests.Secrets;

public class RunSecretsTests
{
    private static RunSecrets Secrets(params (string, string)[] values) =>
        new(values.ToDictionary(v => v.Item1, v => v.Item2));

    [Fact]
    public void Expand_replaces_secret_tokens_only()
    {
        var secrets = Secrets(("token", "abc-123"));
        Assert.Equal("Bearer abc-123 and {{other}}", secrets.Expand("Bearer {{secret.token}} and {{other}}"));
        Assert.Equal("abc-123", secrets.Expand("{{ secret.TOKEN }}"));
        Assert.Equal("no tokens", secrets.Expand("no tokens"));
    }

    [Fact]
    public void Expand_rejects_an_undeclared_secret()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Secrets().Expand("{{secret.missing}}"));
        Assert.Contains("missing", ex.Message);
    }

    [Fact]
    public void Mask_replaces_values_of_four_or_more_characters_and_leaves_short_ones()
    {
        var secrets = Secrets(("long", "hunter2!"), ("short", "abc"));
        Assert.Equal("password is ******** ok abc", secrets.Mask("password is hunter2! ok abc"));
        Assert.Null(secrets.Mask((string?)null));
        Assert.Equal("", secrets.Mask(""));
    }

    [Fact]
    public void Mask_handles_overlapping_secrets_by_hiding_the_longest_first()
    {
        var secrets = Secrets(("a", "secret"), ("b", "secret-extended"));
        Assert.Equal("********", secrets.Mask("secret-extended"));
    }

    [Fact]
    public void Mask_hides_secrets_in_assertion_values_errors_and_nested_steps()
    {
        var secrets = Secrets(("t", "s3cr3t-value"));
        var step = new StepExecutionResult
        {
            Id = "a", Type = "include", Name = "n", Passed = false, Error = "failed with s3cr3t-value",
            Assertions =
            [
                new AssertionResult { Source = "body", Operator = "==", Expected = "s3cr3t-value", Actual = "x s3cr3t-value y", Outcome = RunOutcome.Failed, Error = "got s3cr3t-value" },
            ],
            Children = [new StepExecutionResult { Id = "c", Type = "request", Name = "c", Assertions = [], Passed = false, Error = "inner s3cr3t-value" }],
        };

        var masked = secrets.Mask(step);

        Assert.DoesNotContain("s3cr3t-value", masked.Error);
        Assert.Equal("********", masked.Assertions[0].Expected);
        Assert.Equal("x ******** y", masked.Assertions[0].Actual);
        Assert.DoesNotContain("s3cr3t-value", masked.Assertions[0].Error);
        Assert.DoesNotContain("s3cr3t-value", masked.Children![0].Error);
        Assert.Equal(RunOutcome.Failed, masked.Assertions[0].Outcome);   // masking keeps the outcome
    }

    [Fact]
    public void Mask_returns_the_same_step_when_there_is_nothing_to_hide()
    {
        var step = new StepExecutionResult { Id = "a", Type = "request", Name = "a", Assertions = [], Passed = true };
        Assert.Same(step, RunSecrets.Empty.Mask(step));
        Assert.True(RunSecrets.Empty.IsEmpty);
    }

    [Fact]
    public void Template_variables_are_case_insensitive()
    {
        var variables = Secrets(("Token", "abc")).AsTemplateVariables();
        Assert.Equal("abc", variables["token"]);
    }
}

public class SecretResolverTests
{
    private sealed class FakeProvider(string name, Dictionary<string, string?> values, Exception? failure = null) : ISecretProvider
    {
        public string Name => name;

        public string KeyFormat => "key";

        public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken) =>
            failure is not null ? throw failure : Task.FromResult(values.GetValueOrDefault(key));
    }

    private static SecretResolver Resolver(params ISecretProvider[] providers) => new(providers);

    private static SecretReference Ref(string provider, string key, Dictionary<string, SecretSource>? environments = null) =>
        new() { Provider = provider, Key = key, Environments = environments };

    [Fact]
    public async Task Reads_every_declared_secret_from_its_provider()
    {
        var resolver = Resolver(new FakeProvider("a", new() { ["k1"] = "one" }), new FakeProvider("b", new() { ["k2"] = "two" }));
        var secrets = await resolver.ResolveAsync(
            new Dictionary<string, SecretReference> { ["first"] = Ref("a", "k1"), ["second"] = Ref("B", "k2") },   // provider names are case-insensitive
            null,
            CancellationToken.None);

        Assert.Equal("one and two", secrets.Expand("{{secret.first}} and {{secret.second}}"));
    }

    [Fact]
    public async Task An_environment_override_replaces_the_default_source_and_others_fall_back()
    {
        var resolver = Resolver(new FakeProvider("dev", new() { ["local-key"] = "dev-value" }), new FakeProvider("ci", new() { ["CI_KEY"] = "ci-value" }));
        var declared = new Dictionary<string, SecretReference>
        {
            ["token"] = Ref("dev", "local-key", new() { ["ci"] = new SecretSource { Provider = "ci", Key = "CI_KEY" } }),
            ["plain"] = Ref("dev", "local-key"),
        };

        var asCi = await resolver.ResolveAsync(declared, "CI", CancellationToken.None);          // environment names are case-insensitive
        var asDefault = await resolver.ResolveAsync(declared, null, CancellationToken.None);
        var asOther = await resolver.ResolveAsync(declared, "staging", CancellationToken.None);   // no override for it: default source

        Assert.Equal("ci-value", asCi.Expand("{{secret.token}}"));
        Assert.Equal("dev-value", asCi.Expand("{{secret.plain}}"));
        Assert.Equal("dev-value", asDefault.Expand("{{secret.token}}"));
        Assert.Equal("dev-value", asOther.Expand("{{secret.token}}"));
    }

    [Fact]
    public async Task Problems_are_collected_and_never_include_values()
    {
        var resolver = Resolver(
            new FakeProvider("ok", new() { ["present"] = "the-actual-secret", ["blank"] = "" }),
            new FakeProvider("broken", new(), new InvalidOperationException("VAULT_ADDR is not set")));
        var declared = new Dictionary<string, SecretReference>
        {
            ["good"] = Ref("ok", "present"),
            ["absent"] = Ref("ok", "nope"),
            ["blank"] = Ref("ok", "blank"),
            ["unknown"] = Ref("nowhere", "x"),
            ["down"] = Ref("broken", "x"),
            ["incomplete"] = Ref("ok", ""),
        };

        var ex = await Assert.ThrowsAsync<SecretResolutionException>(() => resolver.ResolveAsync(declared, null, CancellationToken.None));

        Assert.Contains("absent: not found", ex.Message);
        Assert.Contains("blank: not found (or empty)", ex.Message);       // an empty value counts as missing
        Assert.Contains("unknown: unknown provider 'nowhere'", ex.Message);
        Assert.Contains("down: provider 'broken' failed: VAULT_ADDR is not set", ex.Message);
        Assert.Contains("incomplete: provider and key are required", ex.Message);
        Assert.DoesNotContain("the-actual-secret", ex.Message);
        Assert.DoesNotContain("good:", ex.Message);
    }

    [Fact]
    public async Task No_declared_secrets_gives_an_empty_set()
    {
        var secrets = await Resolver().ResolveAsync(new Dictionary<string, SecretReference>(), null, CancellationToken.None);
        Assert.True(secrets.IsEmpty);
    }
}

[Collection("Environment")]
public class BuiltInSecretProviderTests
{
    [Fact]
    public async Task Env_reads_an_environment_variable()
    {
        using var env = new EnvironmentScope().Set("AXIOM_TEST_SECRET", "from-env");
        var provider = new EnvSecretProvider();
        Assert.Equal("from-env", await provider.GetSecretAsync("AXIOM_TEST_SECRET", default));
        Assert.Null(await provider.GetSecretAsync("AXIOM_TEST_NOT_SET", default));
    }

    [Fact]
    public async Task File_reads_a_file_inside_the_secrets_directory_and_trims_the_trailing_newline()
    {
        using var folder = new TempFolder();
        folder.Write("db/password", "s3cret\n");
        using var env = new EnvironmentScope().Set(FileSecretProvider.DirectoryVariable, folder.Path);
        var provider = new FileSecretProvider();

        Assert.Equal("s3cret", await provider.GetSecretAsync("db/password", default));
        Assert.Null(await provider.GetSecretAsync("db/other", default));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("../../etc/passwd")]
    public async Task File_cannot_escape_the_secrets_directory(string key)
    {
        using var folder = new TempFolder();
        using var env = new EnvironmentScope().Set(FileSecretProvider.DirectoryVariable, folder.Path);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new FileSecretProvider().GetSecretAsync(key, default));
    }

    [Fact]
    public async Task File_needs_the_directory_variable()
    {
        using var env = new EnvironmentScope().Set(FileSecretProvider.DirectoryVariable, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new FileSecretProvider().GetSecretAsync("x", default));
    }

    [Fact]
    public async Task Local_serves_the_values_it_was_handed_for_the_run()
    {
        var provider = new LocalSecretProvider();
        Assert.Null(await provider.GetSecretAsync("a", default));
        provider.Load(new Dictionary<string, string> { ["a"] = "1" });
        Assert.Equal("1", await provider.GetSecretAsync("a", default));
        provider.Load(null);
        Assert.Null(await provider.GetSecretAsync("a", default));
    }

    [Fact]
    public async Task Vault_reads_a_kv2_field_and_sends_the_token()
    {
        using var server = new FakeServer((path, headers) =>
            path == "/v1/secret/data/app/db" && headers.GetValueOrDefault("X-Vault-Token") == "root"
                ? (200, "{\"data\":{\"data\":{\"password\":\"vault-pw\"}}}")
                : (404, "{}"));
        using var env = new EnvironmentScope().Set("VAULT_ADDR", server.Url).Set("VAULT_TOKEN", "root").Set("AXIOM_VAULT_KV_VERSION", null);
        using var provider = new VaultSecretProvider();

        Assert.Equal("vault-pw", await provider.GetSecretAsync("secret/app/db#password", default));
        Assert.Null(await provider.GetSecretAsync("secret/app/db#nothing", default));
        Assert.Null(await provider.GetSecretAsync("secret/app/other#password", default));   // 404 is "not found"
    }

    [Fact]
    public async Task Vault_supports_kv1_and_reports_a_bad_key_or_missing_configuration()
    {
        using var server = new FakeServer((path, _) => path == "/v1/kv/app" ? (200, "{\"data\":{\"pw\":\"v1-pw\"}}") : (403, "{}"));
        using var env = new EnvironmentScope().Set("VAULT_ADDR", server.Url).Set("VAULT_TOKEN", "t").Set("AXIOM_VAULT_KV_VERSION", "1");
        using var provider = new VaultSecretProvider();

        Assert.Equal("v1-pw", await provider.GetSecretAsync("kv/app#pw", default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetSecretAsync("no-field", default));
        var denied = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetSecretAsync("kv/other#pw", default));
        Assert.Contains("403", denied.Message);

        env.Set("VAULT_TOKEN", null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetSecretAsync("kv/app#pw", default));
    }

    [Fact]
    public async Task Kubernetes_reads_and_decodes_a_secret_data_entry()
    {
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("k8s-secret-pw"));
        using var server = new FakeServer((path, headers) =>
            path == "/api/v1/namespaces/ns1/secrets/mysecret" && headers.GetValueOrDefault("Authorization") == "Bearer k8stoken"
                ? (200, $"{{\"data\":{{\"pw\":\"{encoded}\"}}}}")
                : (404, "{}"));
        using var env = new EnvironmentScope()
            .Set("AXIOM_K8S_API_URL", server.Url).Set("AXIOM_K8S_TOKEN", "k8stoken").Set("AXIOM_K8S_NAMESPACE", "ns1");
        using var provider = new KubernetesSecretProvider();

        Assert.Equal("k8s-secret-pw", await provider.GetSecretAsync("mysecret/pw", default));          // namespace from the environment
        Assert.Equal("k8s-secret-pw", await provider.GetSecretAsync("ns1/mysecret/pw", default));      // explicit namespace
        Assert.Null(await provider.GetSecretAsync("mysecret/missing", default));                        // no such data key
        Assert.Null(await provider.GetSecretAsync("other/pw", default));                                 // 404
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetSecretAsync("just-one-part", default));
    }
}
