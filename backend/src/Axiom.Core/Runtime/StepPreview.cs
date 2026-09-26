namespace Axiom.Runtime;

/// <summary>
/// The outcome of trying a test's steps up to one of them, with what that step received, so a test can be
/// built by looking at a real response.
/// </summary>
public sealed class StepPreview
{
    /// <summary>
    /// The results of the steps that ran; the run stops early when a step before the target does not pass.
    /// </summary>
    public required TestCaseExecutionResult Result { get; init; }

    /// <summary>
    /// What the last step that ran sent and received; null when it sent nothing (for example an include step, or a
    /// request whose URL could not be resolved).
    /// </summary>
    public StepResponse? Response { get; init; }
}

/// <summary>
/// What a request or SQL step sent and received. Secret values are masked, and large bodies and result sets are cut short.
/// </summary>
public sealed class StepResponse
{
    /// <summary>
    /// The id of the step that received it.
    /// </summary>
    public required string StepId { get; init; }

    /// <summary>
    /// The HTTP request as it was sent, with every template resolved (request steps). Present even when no response
    /// came back (unreachable server, timeout), which is when it helps most.
    /// </summary>
    public SentRequest? Request { get; init; }

    /// <summary>
    /// The SQL as it was run, with every template resolved (SQL steps).
    /// </summary>
    public string? Sql { get; init; }

    /// <summary>
    /// The HTTP status code (request steps).
    /// </summary>
    public int? Status { get; init; }

    /// <summary>
    /// The response headers (request steps).
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// The raw response text (request steps), cut to <see cref="StepPreviewLimits.MaxBodyChars"/>.
    /// </summary>
    public string? Body { get; init; }

    /// <summary>
    /// True when <see cref="Body"/> was cut short.
    /// </summary>
    public bool BodyTruncated { get; init; }

    /// <summary>
    /// The rows (SQL steps), at most <see cref="StepPreviewLimits.MaxRows"/>.
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>>? Rows { get; init; }

    /// <summary>
    /// The total number of rows (SQL steps), including those left out of <see cref="Rows"/>.
    /// </summary>
    public int? RowCount { get; init; }

    /// <summary>
    /// How long the step took.
    /// </summary>
    public double? DurationMs { get; init; }
}

/// <summary>
/// An HTTP request as it was sent.
/// </summary>
public sealed class SentRequest
{
    /// <summary>
    /// The HTTP method.
    /// </summary>
    public required string Method { get; init; }

    /// <summary>
    /// The full URL, including query parameters.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// Every header sent, including defaults such as <c>Accept</c> and the body's <c>Content-Type</c>.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    /// <summary>
    /// The body sent, cut to <see cref="StepPreviewLimits.MaxBodyChars"/>; null when there was none.
    /// </summary>
    public string? Body { get; init; }

    /// <summary>
    /// True when <see cref="Body"/> was cut short.
    /// </summary>
    public bool BodyTruncated { get; init; }
}

/// <summary>
/// How much of a response a preview carries.
/// </summary>
public static class StepPreviewLimits
{
    /// <summary>
    /// Characters of a response body kept in a preview.
    /// </summary>
    public const int MaxBodyChars = 512 * 1024;

    /// <summary>
    /// Rows of a SQL result kept in a preview.
    /// </summary>
    public const int MaxRows = 200;
}
