using System.Collections.Concurrent;

namespace MacroDeckHost.Application.Variables;

public enum VariableRefreshLane
{
	Eager,
	Catalog
}

/// <summary>
/// Asks whichever loop polls an integration's variables to read the named ones again on its next tick.
/// A write is only ever "applied by the owner", so the value the host shows still comes from a read - this
/// is what stops a control from sitting on a stale reading until the ordinary cadence comes round.
/// </summary>
public interface IVariableRefreshSignal
{
	void RequestRefresh(string integrationId, Guid variableId);

	/// <summary>Removes and returns every variable id pending for <paramref name="integrationId"/>.</summary>
	IReadOnlyList<Guid> DrainFor(string integrationId, VariableRefreshLane lane);

	void RequestDefinitionRefresh(string integrationId, string definitionId);

	IReadOnlyList<string> DrainDefinitionsFor(string integrationId, VariableRefreshLane lane);

	IReadOnlyCollection<string> PendingIntegrations(VariableRefreshLane lane);

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
	private readonly Lane _eager = new();
	private readonly Lane _catalog = new();

	private readonly ConcurrentDictionary<string, byte> _pendingEager = new(StringComparer.Ordinal);

	public void RequestRefresh(string integrationId, Guid variableId)
	{
		if (string.IsNullOrEmpty(integrationId))
		{
			return;
		}

		_eager.Add(integrationId, variableId);
		_catalog.Add(integrationId, variableId);
	}

	public IReadOnlyList<Guid> DrainFor(string integrationId, VariableRefreshLane lane)
	{
		return Select(lane).DrainVariables(integrationId);
	}

	public void RequestDefinitionRefresh(string integrationId, string definitionId)
	{
		if (string.IsNullOrEmpty(integrationId) || string.IsNullOrEmpty(definitionId))
		{
			return;
		}

		_eager.Add(integrationId, definitionId);
		_catalog.Add(integrationId, definitionId);
	}

	public IReadOnlyList<string> DrainDefinitionsFor(string integrationId, VariableRefreshLane lane)
	{
		return Select(lane).DrainDefinitions(integrationId);
	}

	public IReadOnlyCollection<string> PendingIntegrations(VariableRefreshLane lane)
	{
		return Select(lane).PendingIntegrations();
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
	{
		return _pendingEager.TryRemove(integrationId, out _);
	}

	private Lane Select(VariableRefreshLane lane)
	{
		return lane == VariableRefreshLane.Catalog ? _catalog : _eager;
	}

	private sealed class Lane
	{
		private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, byte>> _variables =
			new(StringComparer.Ordinal);

		private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _definitions =
			new(StringComparer.Ordinal);

		public void Add(string integrationId, Guid variableId)
		{
			_variables.GetOrAdd(integrationId, static _ => new ConcurrentDictionary<Guid, byte>())
				.TryAdd(variableId, 0);
		}

		public void Add(string integrationId, string definitionId)
		{
			_definitions.GetOrAdd(integrationId,
					static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal))
				.TryAdd(definitionId, 0);
		}

		public List<Guid> DrainVariables(string integrationId)
		{
			return _variables.TryGetValue(integrationId, out var ids) ? Drain(ids) : [];
		}

		public List<string> DrainDefinitions(string integrationId)
		{
			return _definitions.TryGetValue(integrationId, out var ids) ? Drain(ids) : [];
		}

		public HashSet<string> PendingIntegrations()
		{
			var pending = new HashSet<string>(StringComparer.Ordinal);
			foreach (var (integrationId, ids) in _variables)
			{
				if (!ids.IsEmpty)
				{
					pending.Add(integrationId);
				}
			}

			foreach (var (integrationId, ids) in _definitions)
			{
				if (!ids.IsEmpty)
				{
					pending.Add(integrationId);
				}
			}

			return pending;
		}

		private static List<T> Drain<T>(ConcurrentDictionary<T, byte> ids)
			where T : notnull
		{
			var drained = new List<T>();
			foreach (var id in ids.Keys)
			{
				if (ids.TryRemove(id, out _))
				{
					drained.Add(id);
				}
			}

			return drained;
		}
	}
}
