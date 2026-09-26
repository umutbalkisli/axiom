using Axiom.Defaults;

namespace Axiom.Models;

/// <summary>
/// Settings that control how a collection runs.
/// </summary>
public sealed class RunSettings
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