using System.Text.Json.Serialization;

namespace Axiom.Runtime;

/// <summary>
/// What became of an assertion, a step or a test. <c>Failed</c> means it ran and the API did not behave as expected;
/// <c>Error</c> means it could not be evaluated at all (a typo in an operator or source, an unresolved variable,
/// an unreachable server), which is a problem with the test or its environment rather than with the API.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RunOutcome
{
    Passed,
    Failed,
    Error,
}
