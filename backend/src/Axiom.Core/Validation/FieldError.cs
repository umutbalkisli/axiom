namespace Axiom.Validation;

/// <summary>
/// A validation problem in one field.
/// </summary>
/// <summary>
/// Creates the error.
/// </summary>
/// <summary>
/// The field the problem is in, such as <c>steps[0].url</c>.
/// </summary>
/// <summary>
/// What is wrong.
/// </summary>
public sealed record FieldError(string Field, string Message);
