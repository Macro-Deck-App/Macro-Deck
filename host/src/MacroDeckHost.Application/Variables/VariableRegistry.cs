using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeck.Sdk.Identity;

namespace MacroDeckHost.Application.Variables;

public sealed class VariableRegistry
{
	public static readonly TimeSpan IntegrationFreshness = TimeSpan.FromMinutes(15);

	private readonly object _lock = new();
	private readonly Dictionary<Guid, Entry> _byId = new();
	private readonly Dictionary<(VariableScope Scope, string? ScopeRefId, string Name), Guid> _byKey = new();

	private readonly Dictionary<string, Guid> _byDefinition = new(StringComparer.Ordinal);

	private readonly Dictionary<(VariableScope Scope, string? ScopeRefId, string Name), Reservation> _reservations
		= new();

	private sealed class Entry
	{
		public required VariableEntity Variable { get; init; }
		public TimeSpan? Freshness { get; init; }
		public bool ProviderAvailable { get; set; } = true;
	}

	private sealed record Reservation(
		Guid Token,
		string IntegrationId,
		string DefinitionId,
		VariableType Type);

	internal sealed record IntegrationVariableReservation(
		string Name,
		string DefinitionId,
		VariableType Type);

	public void Upsert(VariableEntity variable)
	{
		var freshness = FreshnessFor(variable);

		lock (_lock)
		{
			if (_byId.TryGetValue(variable.Id, out var previous))
			{
				_byKey.Remove(KeyOf(previous.Variable));
				RemoveDefinitionKey(previous.Variable);
			}

			_byId[variable.Id] = new Entry { Variable = variable, Freshness = freshness };
			_byKey[KeyOf(variable)] = variable.Id;
			if (DefinitionKeyOf(variable) is { } definitionKey)
			{
				_byDefinition[definitionKey] = variable.Id;
			}
		}
	}

	internal bool TryAdd(VariableEntity variable)
	{
		var freshness = FreshnessFor(variable);

		lock (_lock)
		{
			var key = KeyOf(variable);
			if (_byKey.ContainsKey(key) || !ReservationAllows(key, variable))
			{
				return false;
			}

			if (DefinitionKeyOf(variable) is { } definitionKey && _byDefinition.ContainsKey(definitionKey))
			{
				return false;
			}

			_byId[variable.Id] = new Entry { Variable = variable, Freshness = freshness };
			_byKey[key] = variable.Id;
			if (DefinitionKeyOf(variable) is { } createdDefinitionKey)
			{
				_byDefinition[createdDefinitionKey] = variable.Id;
			}

			return true;
		}
	}

	internal bool TryReserveGlobalIntegrationVariables(
		Guid token,
		string integrationId,
		IReadOnlyList<IntegrationVariableReservation> variables)
	{
		lock (_lock)
		{
			foreach (var variable in variables)
			{
				var key = (VariableScope.Global, (string?)null, variable.Name);
				if (_reservations.TryGetValue(key, out var reserved) && reserved.Token != token)
				{
					return false;
				}

				if (_byKey.TryGetValue(key, out var existingId) &&
					(!_byId.TryGetValue(existingId, out var existing) ||
						!Matches(existing.Variable, integrationId, variable.DefinitionId, variable.Type)))
				{
					return false;
				}

				if (QualifiedId.TryCreate(integrationId,
						variable.DefinitionId,
						LocalIdKind.Resource,
						out var qualified) &&
					_byDefinition.TryGetValue(qualified.ToString(), out var definitionId) &&
					(!_byId.TryGetValue(definitionId, out var definition) ||
						!Matches(definition.Variable, integrationId, variable.DefinitionId, variable.Type)))
				{
					return false;
				}
			}

			foreach (var variable in variables)
			{
				_reservations[(VariableScope.Global, null, variable.Name)] = new Reservation(token,
					integrationId,
					variable.DefinitionId,
					variable.Type);
			}

			return true;
		}
	}

	internal void ReleaseIntegrationVariableReservation(Guid token)
	{
		lock (_lock)
		{
			foreach (var key in _reservations
				.Where(pair => pair.Value.Token == token)
				.Select(pair => pair.Key)
				.ToList())
			{
				_reservations.Remove(key);
			}
		}
	}

	public void SetAvailable(Guid id, bool available)
	{
		lock (_lock)
		{
			if (_byId.TryGetValue(id, out var entry))
			{
				entry.ProviderAvailable = available;
			}
		}
	}

	public bool Rename(Guid id, string name, DateTime updatedAt)
	{
		lock (_lock)
		{
			if (!_byId.TryGetValue(id, out var entry))
			{
				return false;
			}

			var target = (entry.Variable.Scope, entry.Variable.ScopeRefId, name);
			if ((_byKey.TryGetValue(target, out var occupied) && occupied != id) ||
				!ReservationAllows(target, entry.Variable))
			{
				return false;
			}

			_byKey.Remove(KeyOf(entry.Variable));
			entry.Variable.Name = name;
			entry.Variable.UpdatedAt = updatedAt;
			_byKey[KeyOf(entry.Variable)] = id;
			return true;
		}
	}

	public void Remove(Guid id)
	{
		lock (_lock)
		{
			if (_byId.Remove(id, out var entry))
			{
				_byKey.Remove(KeyOf(entry.Variable));
				RemoveDefinitionKey(entry.Variable);
			}
		}
	}

	public VariableEntity? GetById(Guid id)
	{
		lock (_lock)
		{
			return _byId.TryGetValue(id, out var entry) ? entry.Variable : null;
		}
	}

	public VariableEntity? FindByName(VariableScope scope, string? scopeRefId, string name)
	{
		lock (_lock)
		{
			return _byKey.TryGetValue((scope, scopeRefId, name), out var id) ? _byId[id].Variable : null;
		}
	}

	public VariableEntity? FindByDefinition(QualifiedId definitionId)
	{
		if (definitionId.IsEmpty)
		{
			return null;
		}

		lock (_lock)
		{
			return _byDefinition.TryGetValue(definitionId.ToString(), out var id) && _byId.TryGetValue(id, out var e)
				? e.Variable
				: null;
		}
	}

	public IReadOnlyList<VariableEntity> GetByScope(VariableScope scope, string? scopeRefId)
	{
		lock (_lock)
		{
			return _byId.Values
				.Where(e => e.Variable.Scope == scope && e.Variable.ScopeRefId == scopeRefId)
				.Select(e => e.Variable)
				.ToList();
		}
	}

	public IReadOnlyList<VariableEntity> GetByOwnerIntegration(string integrationId)
	{
		lock (_lock)
		{
			return _byId.Values
				.Where(e => e.Variable.OwnerIntegrationId == integrationId)
				.Select(e => e.Variable)
				.ToList();
		}
	}

	public IReadOnlyList<VariableEntity> GetAll()
	{
		lock (_lock)
		{
			return _byId.Values.Select(e => e.Variable).ToList();
		}
	}

	public bool IsAvailable(Guid id)
	{
		lock (_lock)
		{
			return _byId.TryGetValue(id, out var entry) && IsFresh(entry);
		}
	}

	private static bool IsFresh(Entry entry)
		=> entry.ProviderAvailable &&
			(entry.Freshness is null || DateTime.UtcNow - entry.Variable.UpdatedAt <= entry.Freshness);

	private static (VariableScope, string?, string) KeyOf(VariableEntity v) => (v.Scope, v.ScopeRefId, v.Name);

	private bool ReservationAllows(
		(VariableScope Scope, string? ScopeRefId, string Name) key,
		VariableEntity variable)
		=> !_reservations.TryGetValue(key, out var reservation) ||
			Matches(variable,
				reservation.IntegrationId,
				reservation.DefinitionId,
				reservation.Type);

	private static bool Matches(
		VariableEntity variable,
		string integrationId,
		string definitionId,
		VariableType type)
		=> variable.Classification == VariableClassification.Integration &&
			string.Equals(variable.OwnerIntegrationId, integrationId, StringComparison.Ordinal) &&
			variable.Scope == VariableScope.Global &&
			variable.ScopeRefId is null &&
			variable.Type == type &&
			string.Equals(variable.DefinitionId, definitionId, StringComparison.Ordinal);

	// LocalIdKind.Resource, not Declared: an on-demand definition id is a resource id
	// (entity/light.living_room/brightness). Declared ids are a strict subset, so every id that indexed
	// before indexes identically.
	private static string? DefinitionKeyOf(VariableEntity v)
		=> v.OwnerIntegrationId is not null &&
			v.DefinitionId is not null &&
			QualifiedId.TryCreate(v.OwnerIntegrationId, v.DefinitionId, LocalIdKind.Resource, out var id)
				? id.ToString()
				: null;

	private static TimeSpan? FreshnessFor(VariableEntity variable)
		=> variable.Classification == VariableClassification.Integration &&
			variable.UpdateMode == VariableUpdateMode.Polled
				? IntegrationFreshness
				: null;

	private void RemoveDefinitionKey(VariableEntity v)
	{
		if (DefinitionKeyOf(v) is { } key && _byDefinition.TryGetValue(key, out var indexed) && indexed == v.Id)
		{
			_byDefinition.Remove(key);
		}
	}
}
