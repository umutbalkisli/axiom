using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Axiom.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Axiom.Tests.Host;

/// <summary>
/// The local host on a free port, as the desktop app starts it.
/// </summary>
public sealed class HostServerTests : IAsyncLifetime
{
    private const string Token = "test-token-0123456789";
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _app = HostServerService.Build(port: 0, Token);
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _client = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private HttpRequestMessage Request(HttpMethod method, string path, string? token = Token, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-token")]
    [InlineData("test-token-012345678")]
    public async Task Requests_without_the_token_are_refused(string? token)
    {
        using var response = await _client.SendAsync(Request(HttpMethod.Get, "/health", token));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_run_started_without_the_token_does_not_run()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", "name: C\n");
        folder.Write("tests/a.test.yaml", "name: A\nsteps: []\n");

        // What a web page could send: a simple POST with no body and no Authorization header.
        using var response = await _client.SendAsync(Request(HttpMethod.Post, $"/api/run?folderPath={Uri.EscapeDataString(folder.Path)}", token: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Requests_with_the_token_are_served()
    {
        using var response = await _client.SendAsync(Request(HttpMethod.Get, "/health"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_run_streams_started_one_event_per_test_and_completed()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", "name: C\n");
        folder.Write("tests/a.test.yaml", "name: A\nsteps: []\n");
        folder.Write("tests/b.test.yaml", "name: B\nsteps: []\n");
        folder.Write("tests/c.test.yaml", "name: C\nsteps: []\n");

        using var response = await _client.SendAsync(Request(HttpMethod.Post, $"/api/run?folderPath={Uri.EscapeDataString(folder.Path)}",
            body: new { tests = new[] { "a", "b.test.yaml" } }));
        var events = (await response.Content.ReadAsStringAsync())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement)
            .ToList();

        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(["started", "test", "test", "completed"], events.Select(e => e.GetProperty("type").GetString()));
        Assert.Equal(2, events[0].GetProperty("total").GetInt32());
        Assert.Equal(["A", "B"], events.Skip(1).Take(2).Select(e => e.GetProperty("test").GetProperty("name").GetString()).Order());
        Assert.Equal(0, events[3].GetProperty("exitCode").GetInt32());
        Assert.Equal("Passed", events[1].GetProperty("test").GetProperty("outcome").GetString());
        Assert.Equal(2, events[3].GetProperty("result").GetProperty("testCases").GetArrayLength());
    }

    [Fact]
    public async Task A_run_that_cannot_start_streams_a_failed_event()
    {
        using var folder = new TempFolder();       // no collection.yaml

        using var response = await _client.SendAsync(Request(HttpMethod.Post, $"/api/run?folderPath={Uri.EscapeDataString(folder.Path)}"));
        var line = (await response.Content.ReadAsStringAsync()).Trim();
        var failed = JsonDocument.Parse(line).RootElement;

        Assert.Equal("failed", failed.GetProperty("type").GetString());
        Assert.Contains("collection.yaml", failed.GetProperty("message").GetString());
    }

    [Fact]
    public async Task A_preview_of_an_unsaved_step_returns_its_result()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", "name: C\nconnections:\n  db: { provider: sqlite, connection_string: 'Data Source=:memory:' }\n");
        var test = new
        {
            name = "draft",
            steps = new object[]
            {
                new { id = "q", type = "db_query", connection = "db", sql = "SELECT 42 AS answer", assert = new[] { new { source = "row_count", @operator = "==", expected = "1" } } },
            },
        };

        using var response = await _client.SendAsync(Request(HttpMethod.Post, $"/api/tests/preview?folderPath={Uri.EscapeDataString(folder.Path)}",
            body: new { test, stepIndex = 0 }));
        var preview = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Passed", preview.GetProperty("result").GetProperty("outcome").GetString());
        Assert.Equal(42, preview.GetProperty("response").GetProperty("rows")[0].GetProperty("answer").GetInt32());
    }

    [Fact]
    public async Task A_preview_with_a_bad_step_index_is_a_bad_request()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", "name: C\n");

        using var response = await _client.SendAsync(Request(HttpMethod.Post, $"/api/tests/preview?folderPath={Uri.EscapeDataString(folder.Path)}",
            body: new { test = new { name = "draft", steps = Array.Empty<object>() }, stepIndex = 0 }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_test_can_be_cloned_under_a_new_name()
    {
        using var folder = new TempFolder();
        folder.Write("tests/a.test.yaml", "# note\nname: A\nsteps: []\n");
        var route = $"/api/tests/clone?folderPath={Uri.EscapeDataString(folder.Path)}";

        using var response = await _client.SendAsync(Request(HttpMethod.Post, route, body: new { fileName = "a.test.yaml", name = "A (copy)" }));
        var clone = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        using var missing = await _client.SendAsync(Request(HttpMethod.Post, route, body: new { fileName = "b.test.yaml", name = "B" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("a-copy.test.yaml", clone.GetProperty("fileName").GetString());
        Assert.Matches("^id: [0-9a-f]{16}\n# note\nname: A \\(copy\\)\nsteps: \\[\\]\n$", folder.Read("tests/a-copy.test.yaml"));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task Tests_in_folders_are_read_moved_and_deleted_by_relative_path()
    {
        using var folder = new TempFolder();
        folder.Write("tests/orders/create.test.yaml", "name: Create\nsteps: []\n");
        var query = $"folderPath={Uri.EscapeDataString(folder.Path)}";

        using var read = await _client.SendAsync(Request(HttpMethod.Get, $"/api/tests/orders/create.test.yaml?{query}"));
        using var moved = await _client.SendAsync(Request(HttpMethod.Post, $"/api/tests/move?{query}", body: new { fileName = "orders/create.test.yaml", folder = "archive" }));
        using var renamed = await _client.SendAsync(Request(HttpMethod.Post, $"/api/folders/rename?{query}", body: new { folder = "archive", newFolder = "old" }));
        using var refused = await _client.SendAsync(Request(HttpMethod.Post, $"/api/tests/move?{query}", body: new { fileName = "old/create.test.yaml", folder = "../outside" }));
        using var deleted = await _client.SendAsync(Request(HttpMethod.Post, $"/api/folders/delete?{query}", body: new { folder = "old" }));

        Assert.Equal("Create", JsonDocument.Parse(await read.Content.ReadAsStringAsync()).RootElement.GetProperty("name").GetString());
        Assert.Equal("archive/create.test.yaml", JsonDocument.Parse(await moved.Content.ReadAsStringAsync()).RootElement.GetProperty("fileName").GetString());
        Assert.Equal("old", JsonDocument.Parse(await renamed.Content.ReadAsStringAsync()).RootElement.GetProperty("folder").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(1, JsonDocument.Parse(await deleted.Content.ReadAsStringAsync()).RootElement.GetProperty("deleted").GetInt32());
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(folder.Path, "tests")));
    }
}
