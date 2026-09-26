using System.Net;
using System.Text;

namespace Axiom.Tests.Support;

/// <summary>
/// What a stub server saw: enough to assert on without holding on to disposed request objects.
/// </summary>
public sealed record RecordedRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string? Body, string? ContentType);

/// <summary>
/// An <see cref="HttpMessageHandler"/> that answers from a function and records every request.
/// </summary>
public sealed class StubHandler(Func<RecordedRequest, HttpResponseMessage> respond) : HttpMessageHandler
{
    private readonly List<RecordedRequest> _requests = [];

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return _requests.ToList();
            }
        }
    }

    public int Count => Requests.Count;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(request.Method, request.RequestUri!, headers, body, request.Content?.Headers.ContentType?.ToString());
        lock (_requests)
        {
            _requests.Add(recorded);
        }

        return respond(recorded);
    }
}

public static class Http
{
    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK, params (string Name, string Value)[] headers) =>
        Text(json, "application/json", status, headers);

    public static HttpResponseMessage Text(string text, string contentType = "text/plain", HttpStatusCode status = HttpStatusCode.OK, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, contentType) };
        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }
}
