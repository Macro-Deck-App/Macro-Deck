using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeck.Sdk.Identity;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Variables;

public sealed record SharedVariableEntry(string Name, VariableType Type, VariableEntity? Variable);

public sealed class SharedVariables
{
	public const string DelegateIntegrationId = "app.macro-deck.delegate";

	private readonly object _lock = new();
	private readonly VariableRegistry _registry;
	private readonly ISharedVariableStore _store;
	private readonly ILogger _logger;

	private List<SharedVariable>? _entries;
	private bool _unreadable;

	public SharedVariables(VariableRegistry registry, ISharedVariableStore store, ILogger logger)
	{
		_registry = registry;
		_store = store;
		_logger = logger.ForContext<SharedVariables>();
	}

	public bool IsShared(VariableEntity variable)
	{
		lock (_lock)
		{
			return Entries().Any(entry => Matches(entry, variable));
		}
	}

	public Result<VariableEntity, VariableError> SetShared(Guid variableId, bool shared)
	{
		var variable = _registry.GetById(variableId);
		if (variable is null)
		{
			return Result.Fail<VariableEntity, VariableError>(VariableError.NotFound, "Variable not found");
		}

		if (variable.Scope != VariableScope.Global)
		{
			return Result.Fail<VariableEntity, VariableError>(VariableError.ValidationError,
				"Only global variables can be shared");
		}

		if (string.Equals(variable.OwnerIntegrationId, DelegateIntegrationId, StringComparison.Ordinal))
		{
			return Result.Fail<VariableEntity, VariableError>(VariableError.NotEditable,
				"Variables imported from another Macro Deck cannot be shared");
		}

		lock (_lock)
		{
			var entries = Entries();
			var updated = entries.Where(entry => !Matches(entry, variable)).ToList();
			if (shared)
			{
				updated.Add(EntryFor(variable));
			}

			if (!Persist(updated))
			{
				return Result.Fail<VariableEntity, VariableError>(VariableError.InternalError,
					"Shared variables could not be saved");
			}
		}

		return Result.Ok<VariableEntity, VariableError>(variable);
	}

	public IReadOnlyList<SharedVariableEntry> List()
	{
		lock (_lock)
		{
			var entries = Entries();
			var result = new List<SharedVariableEntry>(entries.Count);
			var refreshed = new List<SharedVariable>(entries.Count);
			var changed = false;

			foreach (var entry in entries)
			{
				var variable = Resolve(entry);
				var current = variable is null
					? entry
					: entry with { Name = variable.Name, Type = variable.Type };
				changed |= current != entry;
				refreshed.Add(current);
				result.Add(new SharedVariableEntry(current.Name, current.Type, variable));
			}

			if (changed)
			{
				Persist(refreshed);
			}

			return result;
		}
	}

	public VariableEntity? FindShared(string name)
		=> List().FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.Ordinal))?.Variable;

	public void Forget(VariableEntity variable)
	{
		lock (_lock)
		{
			var entries = Entries();
			var remaining = entries.Where(entry => !Matches(entry, variable)).ToList();
			if (remaining.Count != entries.Count)
			{
				Persist(remaining);
			}
		}
	}

	private List<SharedVariable> Entries()
	{
		if (_entries is { } cached)
		{
			return cached;
		}

		if (_store.TryLoad(out var loaded))
		{
			_entries = loaded.ToList();
			return _entries;
		}

		_logger.Warning("Shared variables could not be read; nothing is shared until the file is readable");
		_unreadable = true;
		_entries = [];
		return _entries;
	}

	private bool Persist(List<SharedVariable> entries)
	{
		if (_unreadable || !_store.Save(entries))
		{
			return false;
		}

		_entries = entries;
		return true;
	}

	private VariableEntity? Resolve(SharedVariable entry)
	{
		if (entry.UserVariableId is { } userId)
		{
			return _registry.GetById(userId) is { Classification: VariableClassification.User } user ? user : null;
		}

		if (entry.OwnerIntegrationId is null)
		{
			return null;
		}

		if (entry.DefinitionId is { } definitionId &&
			QualifiedId.TryCreate(entry.OwnerIntegrationId, definitionId, LocalIdKind.Resource, out var qualified))
		{
			return _registry.FindByDefinition(qualified);
		}

		return _registry.FindByName(VariableScope.Global, null, entry.Name) is { } byName &&
			string.Equals(byName.OwnerIntegrationId, entry.OwnerIntegrationId, StringComparison.Ordinal)
				? byName
				: null;
	}

	private static SharedVariable EntryFor(VariableEntity variable)
		=> variable.Classification == VariableClassification.User
			? new SharedVariable { UserVariableId = variable.Id, Name = variable.Name, Type = variable.Type }
			: new SharedVariable
			{
				OwnerIntegrationId = variable.OwnerIntegrationId,
				DefinitionId = variable.DefinitionId,
				Name = variable.Name,
				Type = variable.Type
			};

	private static bool Matches(SharedVariable entry, VariableEntity variable)
	{
		if (variable.Scope != VariableScope.Global)
		{
			return false;
		}

		if (entry.UserVariableId is { } userId)
		{
			return variable.Classification == VariableClassification.User && variable.Id == userId;
		}

		if (variable.Classification == VariableClassification.User ||
			!string.Equals(entry.OwnerIntegrationId, variable.OwnerIntegrationId, StringComparison.Ordinal))
		{
			return false;
		}

		return entry.DefinitionId is { } definitionId
			? string.Equals(definitionId, variable.DefinitionId, StringComparison.Ordinal)
			: string.Equals(entry.Name, variable.Name, StringComparison.Ordinal);
	}
}
