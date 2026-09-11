namespace MacroDeckHost.Integrations.System.Metrics;

internal sealed record WindowsGpuAdapter(
	string Name,
	int LuidHigh,
	uint LuidLow,
	ulong DedicatedVideoMemory,
	ulong SharedSystemMemory,
	bool IsSoftware)
{
	public uint VendorId { get; init; }
	public uint DeviceId { get; init; }
	public uint SubSysId { get; init; }
	public uint Revision { get; init; }
	public IReadOnlyList<(int High, uint Low)> Luids { get; init; } = [(LuidHigh, LuidLow)];
}
