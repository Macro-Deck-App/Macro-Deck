using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Domain.Entities;
using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Tests.UnitTests.HomeAssistant;

/// <summary>
/// Minimal <see cref="IIntegrationContext"/> for driving <see cref="HomeAssistantIntegration.InitializeAsync"/>
/// end to end. Only <see cref="Config"/>, <see cref="Variables"/> and <see cref="Events"/> are touched by
/// that path, so everything else is a deliberate <see cref="NotSupportedException"/> rather than a mock -
/// a test that reaches one of those has grown outside what this double was built for.
/// </summary>
internal sealed class FakeHomeAssistantIntegrationContext : IIntegrationContext
{
	public FakeHomeAssistantIntegrationConfig ConfigStore { get; } = new();

	public IIntegrationConfig Config => ConfigStore;

	public IVariableApi Variables { get; } = new NoOpVariableApi();

	public IUserVariableApi UserVariables => throw new NotSupportedException();

	public IDeckNavigator Deck => throw new NotSupportedException();

	public IScriptApi Scripts => throw new NotSupportedException();

	public IWidgetApi Widgets => throw new NotSupportedException();

	public IEventPublisher Events { get; } = new NoOpEventPublisher();

	public IUserNotifier Notifications => throw new NotSupportedException();
}

internal sealed class NoOpEventPublisher : IEventPublisher
{
	public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
	{
	}
}

internal sealed class NoOpVariableApi : IVariableApi
{
	public Task<IReadOnlyList<VariableHandle>> GetAllAsync() => throw new NotSupportedException();

	public Task<VariableHandle?> GetByNameAsync(string name) => throw new NotSupportedException();

	public Task<VariableHandle> CreateAsync(
		string name,
		VariableType type,
		object? initialValue = null,
		int? decimalPlaces = null,
		string? definitionId = null)
		=> throw new NotSupportedException();

	public Task SetValueAsync(Guid variableId, object? value) => throw new NotSupportedException();

	public Task DeleteAsync(Guid variableId) => throw new NotSupportedException();
}

internal sealed class FakeHomeAssistantIntegrationConfig : IIntegrationConfig
{
	private readonly List<ConfigEntrySnapshot> _entries = [];
	private readonly Dictionary<(Guid EntryId, string Key), string?> _strings = [];

	public Guid AddEntry(string title, IReadOnlyDictionary<string, string?> values)
	{
		var entryId = Guid.NewGuid();
		_entries.Add(new ConfigEntrySnapshot(entryId, title));

		foreach (var (key, value) in values)
		{
			_strings[(entryId, key)] = value;
		}

		return entryId;
	}

	public Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<ConfigEntrySnapshot>>(_entries.ToList());

	public Task<string?> GetStringAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
		=> Task.FromResult(_strings.GetValueOrDefault((entryId, key)));

	public Task<string?> GetSecretAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
		=> Task.FromResult<string?>(null);

	public Task SetStringAsync(Guid entryId, string key, string? value, CancellationToken cancellationToken = default)
	{
		_strings[(entryId, key)] = value;
		return Task.CompletedTask;
	}

	public Task SetSecretAsync(Guid entryId, string key, string value, CancellationToken cancellationToken = default)
		=> Task.CompletedTask;
}

internal sealed class FakeVariableBindingStore : IVariableBindingStore
{
	private List<VariableBinding> _bindings = [];

	public int SaveCalls { get; private set; }

	public bool FailNextSave { get; set; }

	public IReadOnlyList<VariableBinding> Load() => _bindings;

	public bool TryLoad(out IReadOnlyList<VariableBinding> bindings)
	{
		bindings = _bindings;
		return true;
	}

	public bool Save(IEnumerable<VariableBinding> bindings)
	{
		if (FailNextSave)
		{
			FailNextSave = false;
			return false;
		}

		_bindings = bindings.ToList();
		SaveCalls++;
		return true;
	}
}
