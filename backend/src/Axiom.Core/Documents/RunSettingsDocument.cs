using Axiom.Defaults;

namespace Axiom.Documents;

/// <summary>
/// Run settings as exchanged with the desktop app.
/// </summary>
public sealed class RunSettingsDocument
{
    /// <summary>
    /// How many tests may run at the same time.
    /// </summary>
    public int MaxParallelTestCases { get; set; } = RunSettingsDefaults.MaxParallelTestCases;
    /// <summary>
    /// How long a single step may take before it is cancelled.
    /// </summary>
    public int StepTimeoutSeconds { get; set; } = RunSettingsDefaults.StepTimeoutSeconds;
}