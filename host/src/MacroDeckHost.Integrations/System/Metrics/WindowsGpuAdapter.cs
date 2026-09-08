namespace MacroDeckHost.Integrations.System.Metrics;

internal sealed record WindowsGpuAdapter(
	string Name,
	int LuidHigh,
	uint LuidLow,
	ulong DedicatedVideoMemory,
	ulong SharedSystemMemory,
	bool IsSoftware);
