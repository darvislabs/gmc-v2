using Xunit;

// DemoPipelineData static test hooks (ForceGuardrailFail / LastPricing) are process-global;
// parallel collections raced on them even before the coding workbench (observed 25/26 on a
// clean checkout). The suite is IO-bound, so serializing is effectively free.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
