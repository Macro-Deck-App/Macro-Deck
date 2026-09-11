namespace MacroDeckHost.Integrations.System.Metrics;

internal static class WindowsGpuSnapshotBuilder
{
	public enum LuidJoin
	{
		Matched,
		NoMatch
	}

	// Adapters a user would call a GPU, discrete first, so that GPU 0 is the card a hybrid laptop's
	// user means. Plain DXGI order would normally put the integrated adapter there.
	public static IReadOnlyList<WindowsGpuAdapter> Surviving(IReadOnlyList<WindowsGpuAdapter> adapters)
		=> adapters
			.Where(a => !a.IsSoftware && (a.DedicatedVideoMemory > 0 || a.SharedSystemMemory > 0))
			// On multi-GPU machines DXGI can list the default adapter again under another LUID. Two
			// physically identical cards are indistinguishable here and collapse too.
			.GroupBy(a => (a.Name, a.VendorId, a.DeviceId, a.SubSysId, a.Revision, a.DedicatedVideoMemory))
			.Select(g => g.First() with { Luids = g.SelectMany(a => a.Luids).ToArray() })
			.OrderByDescending(a => a.DedicatedVideoMemory)
			.ToArray();

	public static bool NeedsNvidiaFallback(
		IReadOnlyList<WindowsGpuAdapter> survivingAdapters,
		CounterReadResult counters,
		bool hasNvidiaSmi)
	{
		if (!hasNvidiaSmi || counters is CounterReadResult.FirstCollect)
		{
			return false;
		}

		return survivingAdapters.Count == 0 || counters is CounterReadResult.Failed or CounterReadResult.Instances([]);
	}

	public static IReadOnlyList<GpuSample> Build(
		IReadOnlyList<WindowsGpuAdapter> survivingAdapters,
		CounterReadResult counters,
		string? nvidiaSmiOutput,
		out LuidJoin join)
	{
		join = LuidJoin.Matched;

		if (counters is CounterReadResult.Instances { Entries.Count: > 0 } instances && survivingAdapters.Count > 0)
		{
			return Join(survivingAdapters, instances.Entries, out join);
		}

		var nvidia = nvidiaSmiOutput is null ? [] : NvidiaSmiParser.ParseGpus(nvidiaSmiOutput);
		if (survivingAdapters.Count == 0)
		{
			return nvidia;
		}

		return MergeNvidiaUsage(survivingAdapters, nvidia);
	}

	private static GpuSample[] Join(
		IReadOnlyList<WindowsGpuAdapter> adapters,
		IReadOnlyList<CounterEntry> entries,
		out LuidJoin join)
	{
		var usage = WindowsGpuCounterParser.Aggregate(entries);

		if (adapters.Any(a => a.Luids.Any(usage.ContainsKey)))
		{
			join = LuidJoin.Matched;
			return adapters.Select(a => Sample(a, a.Luids.Max(luid => usage.GetValueOrDefault(luid)))).ToArray();
		}

		// Which adapter an instance belongs to is unknown here. A single GPU still gets a correct
		// total; several would only get guesses, so they get none.
		join = LuidJoin.NoMatch;
		return adapters is [var only] && usage.Count > 0
			? [Sample(only, usage.Values.Max())]
			: adapters.Select(a => new GpuSample(a.Name, null)).ToArray();
	}

	// The adapter list stays authoritative: a fallback must not change the GPU count or move a name
	// onto another card.
	private static GpuSample[] MergeNvidiaUsage(
		IReadOnlyList<WindowsGpuAdapter> adapters,
		IReadOnlyList<GpuSample> nvidia)
	{
		var next = 0;
		return adapters
			.Select(a => a.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) && next < nvidia.Count
				? new GpuSample(a.Name, nvidia[next++].UsagePercent)
				: new GpuSample(a.Name, null))
			.ToArray();
	}

	private static GpuSample Sample(WindowsGpuAdapter adapter, double usage)
		=> new(adapter.Name, Math.Clamp(usage, 0d, 100d));
}
