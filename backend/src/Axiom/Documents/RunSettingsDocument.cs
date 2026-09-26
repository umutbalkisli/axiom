using Axiom.Defaults;

namespace Axiom.Documents;

public sealed class RunSettingsDocument
{
    public int MaxParallelTestCases { get; set; } = RunSettingsDefaults.MaxParallelTestCases;
    public int StepTimeoutSeconds { get; set; } = RunSettingsDefaults.StepTimeoutSeconds;
}