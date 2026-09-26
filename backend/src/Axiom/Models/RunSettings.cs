using Axiom.Defaults;

namespace Axiom.Models;

public sealed class RunSettings
{
    public int MaxParallelTestCases { get; set; } = RunSettingsDefaults.MaxParallelTestCases;
    public int StepTimeoutSeconds { get; set; } = RunSettingsDefaults.StepTimeoutSeconds;
}