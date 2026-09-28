using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axiom.Hosting;
using Axiom.LocalSecrets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Axiom.Tests.Host;

/// <summary>
/// The host in UI mode, as <c>axiom ui</c> runs it: web UI files, the launch link and session cookie, local secrets,
/// preferences, the folder browser, and ending when the UI goes away.
/// </summary>
public sealed class UiHostTests : IAsyncLifetime
{
    private const string Token = "ui-token-0123456789";
    private readonly TempFolder _data = new();
    private readonly TempFolder _ui = new();
    private readonly FakeSecureStorage _storage = new();
    private WebApplication _app = null!;
    private Uri _base = null!;
    private CookieContainer _cookies = new();

    public async Task InitializeAsync() => await Start(TimeSpan.FromSeconds(30));

    private async Task Start(TimeSpan goodbyeGrace)
    {
        _ui.Write("index.html", "<!doctype html><title>Axiom UI</title>");
        _ui.Write("assets/app-1234.js", "console.log('ui')");
        _app = HostServerService.Build(new HostOptions
        {
            Port = 0,
            Token = Token,
            Ui = true,
            UiDirectory = _ui.Path,
            DataDirectory = _data.Path,
            SecureStorage = _storage,
            GoodbyeGrace = goodbyeGrace,
        });
        await _app.StartAsync();
        _base = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        _data.Dispose();
        _ui.Dispose();
    }

    private HttpClient Browser(bool followRedirects = true) =>
        new(new HttpClientHandler { CookieContainer = _cookies, UseCookies = true, AllowAutoRedirect = followRedirects }) { BaseAddress = _base };

    private async Task<HttpClient> OpenedBrowser()
    {
        var browser = Browser();
        (await browser.GetAsync($"/?token={Token}")).EnsureSuccessStatusCode();
        return browser;
    }

    [Fact]
    public async Task The_launch_link_becomes_a_strict_http_only_cookie_and_leaves_the_address_bar()
    {
        using var browser = Browser(followRedirects: false);

        using var launch = await browser.GetAsync($"/?token={Token}");

        Assert.Equal(HttpStatusCode.Redirect, launch.StatusCode);
        Assert.Equal("/", launch.Headers.Location?.OriginalString);
        var cookie = Assert.Single(launch.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/preferences")).StatusCode);   // the cookie now opens the API
    }

    [Fact]
    public async Task Without_the_cookie_neither_the_ui_nor_the_api_answers()
    {
        using var browser = Browser();

        using var page = await browser.GetAsync("/");
        using var wrongToken = await browser.GetAsync("/?token=guess");
        using var api = await browser.GetAsync("/api/preferences");

        Assert.Equal(HttpStatusCode.Unauthorized, page.StatusCode);
        Assert.Contains("axiom ui", await page.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, wrongToken.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
    }

    [Fact]
    public async Task A_request_for_another_host_name_is_refused_even_with_the_cookie()
    {
        using var browser = await OpenedBrowser();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/preferences");
        request.Headers.Host = $"attacker.example:{_base.Port}";     // a DNS-rebinding page pointing its name at 127.0.0.1

        using var response = await browser.SendAsync(request);

        Assert.Equal(HttpStatusCode.MisdirectedRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ui_files_are_served_with_the_page_for_any_view_and_long_caching_for_built_assets()
    {
        using var browser = await OpenedBrowser();

        using var index = await browser.GetAsync("/");
        using var view = await browser.GetAsync("/collection/tests");
        using var asset = await browser.GetAsync("/assets/app-1234.js");
        using var missing = await browser.GetAsync("/assets/missing.js");
        using var unknownApi = await browser.GetAsync("/api/no-such-thing");

        Assert.Contains("Axiom UI", await index.Content.ReadAsStringAsync());
        Assert.Equal("no-cache", index.Headers.CacheControl?.ToString());
        Assert.Contains("Axiom UI", await view.Content.ReadAsStringAsync());
        Assert.Equal("text/javascript", asset.Content.Headers.ContentType?.MediaType);
        Assert.True(asset.Headers.CacheControl?.MaxAge > TimeSpan.FromDays(300));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownApi.StatusCode);        // the API never falls back to the page
    }

    [Fact]
    public async Task Local_secrets_go_to_secure_storage_and_runs_read_them_there()
    {
        using var browser = await OpenedBrowser();
        using var folder = new TempFolder();
        folder.Write("collection.yaml", "name: C\nsecrets:\n  api_key: { provider: local, key: api_key }\n");
        var query = $"folderPath={Uri.EscapeDataString(folder.Path)}";

        var before = await RunEvents(browser, query);
        (await browser.PostAsJsonAsync($"/api/local-secrets?{query}", new { name = "api_key", value = "s3cr3t-value" })).EnsureSuccessStatusCode();
        var names = await browser.GetFromJsonAsync<JsonElement>($"/api/local-secrets?{query}");
        var after = await RunEvents(browser, query);

        Assert.Equal("failed", before[^1]);                                        // not stored yet: the run says so
        Assert.Equal(["api_key"], names.GetProperty("names").EnumerateArray().Select(n => n.GetString()));
        Assert.Equal("completed", after[^1]);
        Assert.Equal(["s3cr3t-value"], _storage.Values);
        Assert.DoesNotContain("s3cr3t-value", File.ReadAllText(Path.Combine(_data.Path, "local-secrets.json")));   // only names on disk

        (await browser.DeleteAsync($"/api/local-secrets/api_key?{query}")).EnsureSuccessStatusCode();
        Assert.Empty(_storage.Values);
    }

    private static async Task<string[]> RunEvents(HttpClient browser, string query)
    {
        using var response = await browser.PostAsync($"/api/run?{query}", null);
        return (await response.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.GetProperty("type").GetString()!)
            .ToArray();
    }

    [Fact]
    public async Task Preferences_are_merged_and_a_null_removes_one()
    {
        using var browser = await OpenedBrowser();

        await browser.PostAsJsonAsync("/api/preferences", new { language = "tr", theme = "dark" });
        await browser.PostAsJsonAsync("/api/preferences", new { theme = (string?)null, recent = new[] { "/a" } });
        var preferences = await browser.GetFromJsonAsync<JsonElement>("/api/preferences");

        Assert.Equal("tr", preferences.GetProperty("language").GetString());
        Assert.False(preferences.TryGetProperty("theme", out _));
        Assert.Equal("/a", preferences.GetProperty("recent")[0].GetString());
    }

    [Fact]
    public async Task The_folder_browser_lists_visible_folders_and_marks_collections()
    {
        using var browser = await OpenedBrowser();
        using var root = new TempFolder();
        root.Write("orders-api/collection.yaml", "name: C\n");
        root.Write("notes/readme.txt", "x");
        root.Write(".hidden/file", "x");

        var listing = await browser.GetFromJsonAsync<JsonElement>($"/api/fs/list?path={Uri.EscapeDataString(root.Path)}");
        using var created = await browser.PostAsJsonAsync("/api/fs/mkdir", new { parent = root.Path, name = "new-collection" });
        using var again = await browser.PostAsJsonAsync("/api/fs/mkdir", new { parent = root.Path, name = "new-collection" });
        using var escaping = await browser.PostAsJsonAsync("/api/fs/mkdir", new { parent = root.Path, name = "../outside" });
        var check = await browser.GetFromJsonAsync<JsonElement>($"/api/fs/check?path={Uri.EscapeDataString(Path.Combine(root.Path, "orders-api"))}");

        var directories = listing.GetProperty("directories").EnumerateArray().ToList();
        Assert.Equal(["notes", "orders-api"], directories.Select(d => d.GetProperty("name").GetString()));
        Assert.Equal([false, true], directories.Select(d => d.GetProperty("hasCollection").GetBoolean()));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.True(Directory.Exists(Path.Combine(root.Path, "new-collection")));
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, escaping.StatusCode);
        Assert.True(check.GetProperty("hasCollection").GetBoolean());
    }

    [Fact]
    public async Task Closing_the_ui_ends_the_program_but_a_reload_does_not()
    {
        await _app.DisposeAsync();
        _cookies = new CookieContainer();
        await Start(goodbyeGrace: TimeSpan.FromMilliseconds(1500));
        using var browser = await OpenedBrowser();
        var stopped = new TaskCompletionSource();
        _app.Lifetime.ApplicationStopping.Register(() => stopped.TrySetResult());

        // A reload: goodbye from the old page, a ping from the new one within the grace period.
        await browser.PostAsync("/api/session/goodbye", null);
        await browser.PostAsync("/api/session/ping", null);
        await Task.Delay(3000);
        Assert.False(stopped.Task.IsCompleted);

        // The window closes: goodbye and nothing after it.
        await browser.PostAsync("/api/session/goodbye", null);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class FakeSecureStorage : ISecureStorage
    {
        private readonly Dictionary<string, string> _values = [];

        public IReadOnlyCollection<string> Values => _values.Values;

        public void Write(string key, string value) => _values[key] = value;

        public string? Read(string key) => _values.GetValueOrDefault(key);

        public void Delete(string key) => _values.Remove(key);
    }
}

public sealed class LocalSecretStoreTests
{
    [Fact]
    public void A_value_the_store_refuses_to_read_is_left_out_instead_of_failing()
    {
        using var data = new TempFolder();
        var store = new LocalSecretStore(new RefusingStorage(), data.Path);
        store.Set("/collections/a", "token", "value");

        Assert.Equal(["token"], store.Names("/collections/a"));
        Assert.Empty(store.Values("/collections/a"));          // the run then reports the secret as missing
        Assert.Empty(store.Names("/collections/b"));
    }

    private sealed class RefusingStorage : ISecureStorage
    {
        public void Write(string key, string value)
        {
        }

        public string? Read(string key) => throw new InvalidOperationException("locked");

        public void Delete(string key)
        {
        }
    }
}

public sealed class AppLaunchTests
{
    [Theory]
    [InlineData("/Applications/Axiom.app/Contents/MacOS/axiom", new string[0], false, true)]              // Finder, Dock
    [InlineData("/Applications/Axiom.app/Contents/MacOS/axiom", new[] { "-psn_0_12345" }, false, true)]   // older macOS
    [InlineData("/Applications/Axiom.app/Contents/MacOS/axiom", new[] { "run", "folder" }, false, false)] // the CLI inside the bundle
    [InlineData("/usr/local/bin/axiom", new string[0], false, false)]                                      // typed in a terminal: help
    [InlineData(@"C:\Tools\axiom.exe", new string[0], true, true)]                                          // Explorer double-click
    [InlineData(@"C:\Tools\axiom.exe", new[] { "ui" }, true, false)]
    public void The_app_opens_only_when_started_as_an_app(string processPath, string[] args, bool doubleClickedOnWindows, bool expected)
    {
        Assert.Equal(expected, Axiom.ProgramEntry.IsAppLaunch(processPath, args, doubleClickedOnWindows));
    }
}

public sealed class LocalSecretsForRunTests
{
    [Fact]
    public void Only_secrets_whose_source_is_local_in_the_chosen_environment_are_read()
    {
        using var data = new TempFolder();
        using var collection = new TempFolder();
        collection.Write("collection.yaml", """
            name: C
            secrets:
              token:
                provider: local
                key: token
                environments:
                  ci: { provider: env, key: TOKEN }
            """);
        var storage = new CountingStorage();
        var store = new LocalSecretStore(storage, data.Path);
        store.Set(collection.Path, "token", "value");

        var onLaptop = store.ValuesForRun(collection.Path, null, out _);
        storage.Reads = 0;
        var onCi = store.ValuesForRun(collection.Path, "ci", out _);

        Assert.Equal("value", onLaptop["token"]);
        Assert.Empty(onCi);
        Assert.Equal(0, storage.Reads);           // the ci run never touched the secure storage
    }

    private sealed class CountingStorage : ISecureStorage
    {
        private readonly Dictionary<string, string> _values = [];

        public int Reads { get; set; }

        public void Write(string key, string value) => _values[key] = value;

        public string? Read(string key)
        {
            Reads++;
            return _values.GetValueOrDefault(key);
        }

        public void Delete(string key) => _values.Remove(key);
    }
}
