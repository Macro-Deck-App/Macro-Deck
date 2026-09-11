using System.Collections.Concurrent;

namespace MacroDeckHost.Application.Variables;

/// <summary>
/// Asks whichever loop polls an integration's variables to read the named ones again on its next tick.
/// A write is only ever "applied by the owner", so the value the host shows still comes from a read - this
/// is what stops a control from sitting on a stale reading until the ordinary cadence comes round.
/// </summary>
public interface IVariableRefreshSignal
{
	void RequestRefresh(string integrationId, Guid variableId);

	/// <summary>Removes and returns every variable id pending for <paramref name="integrationId"/>.</summary>
	IReadOnlyList<Guid> DrainFor(string integrationId);

	void RequestDefinitionRefresh(string integrationId, string definitionId);

	IReadOnlyList<string> DrainDefinitionsFor(string integrationId);

	/// <summary>
	/// Asks for every eager variable of <paramref name="integrationId"/> to be read again. The catalog
	/// half is polled by a different loop and is not covered.
	/// </summary>
	void RequestEagerRefresh(string integrationId);

	/// <summary>Whether an eager refresh was requested, clearing the request.</summary>
	bool DrainEagerRefreshRequested(string integrationId);
}

public sealed class VariableRefreshSignal : IVariableRefreshSignal
{
	private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, byte>> _pending =
		new(StringComparer.Ordinal);

	private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _pendingDefinitions =
		new(StringComparer.Ordinal);

	private readonly ConcurrentDictionary<string, byte> _pendingEager = new(StringComparer.Ordinal);

	public void RequestRefresh(string integrationId, Guid variableId)
	{
		if (string.IsNullOrEmpty(integrationId))
		{
			return;
		}

		var ids = _pending.GetOrAdd(integrationId, static _ => new ConcurrentDictionary<Guid, byte>());
		ids.TryAdd(variableId, 0);
	}

	public IReadOnlyList<Guid> DrainFor(string integrationId)
	{
		if (!_pending.TryGetValue(integrationId, out var ids) || ids.IsEmpty)
		{
			return [];
		}

		var drained = new List<Guid>();
		foreach (var id in ids.Keys)
		{
			if (ids.TryRemove(id, out _))
			{
				drained.Add(id);
			}
		}

		return drained;
	}

	public void RequestDefinitionRefresh(string integrationId, string definitionId)
	{
		if (string.IsNullOrEmpty(integrationId) || string.IsNullOrEmpty(definitionId))
		{
			return;
		}

		var ids = _pendingDefinitions.GetOrAdd(integrationId,
			static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
		ids.TryAdd(definitionId, 0);
	}

	public IReadOnlyList<string> DrainDefinitionsFor(string integrationId)
	{
		if (!_pendingDefinitions.TryGetValue(integrationId, out var ids) || ids.IsEmpty)
		{
			return [];
		}

		var drained = new List<string>();
		foreach (var id in ids.Keys)
		{
			if (ids.TryRemove(id, out _))
			{
				drained.Add(id);
			}
		}

		return drained;
	}

	public void RequestEagerRefresh(string integrationId)
	{
		if (string.IsNullOrEmpty(integrationId))
		{
			return;
		}

		_pendingEager[integrationId] = 0;
	}

	public bool DrainEagerRefreshRequested(string integrationId)
		=> _pendingEager.TryRemove(integrationId, out _);
}
