namespace MacroDeckHost.Integrations.System.Metrics;

internal abstract record CounterReadResult
{
	internal sealed record Failed : CounterReadResult;

	internal sealed record FirstCollect : CounterReadResult;

	internal sealed record Instances(IReadOnlyList<CounterEntry> Entries) : CounterReadResult;
}

internal readonly record struct CounterEntry(string InstanceName, double Value);
