namespace Axiom.Secrets;

/// <summary>
/// Thrown when declared secrets cannot be read. The message names secrets and providers, never values.
/// </summary>
public sealed class SecretResolutionException(string message) : Exception(message);
