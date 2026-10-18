using System.Collections.Concurrent;
using System.Collections.Frozen;

namespace MacroDeckHost.Application.Ui.Transport;

public sealed class UiClientFeatures
{
	public const string OpenUrl = "open-url";

	private const int MaxFeatures = 32;
	private const int MaxFeatureLength = 64;

	private readonly ConcurrentDictionary<string, FrozenSet<string>> _byConnection = new(StringComparer.Ordinal);

	public void Declare(string connectionId, IEnumerable<string?>? features)
	{
		ArgumentException.ThrowIfNullOrEmpty(connectionId);

		var declared = (features ?? [])
			.Where(feature => !string.IsNullOrEmpty(feature) && feature.Length <= MaxFeatureLength)
			.Take(MaxFeatures)
			.Select(feature => feature!)
			.ToFrozenSet(StringComparer.Ordinal);

		_byConnection[connectionId] = declared;
	}

	public bool Supports(string connectionId, string feature)
	{
		return _byConnection.TryGetValue(connectionId, out var declared) && declared.Contains(feature);
	}

	public void Remove(string connectionId)
	{
		_byConnection.TryRemove(connectionId, out _);
	}
}
