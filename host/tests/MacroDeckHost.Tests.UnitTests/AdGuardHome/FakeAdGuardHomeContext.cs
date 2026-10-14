using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Integrations.AdGuardHome;

namespace MacroDeckHost.Tests.UnitTests.AdGuardHome;

internal sealed class FakeAdGuardHomeContext : IIntegrationContext, IIntegrationConfig
{
	private readonly List<ConfigEntrySnapshot> _entries = [];
	private readonly Dictionary<(Guid EntryId, string Key), string?> _strings = [];
	private readonly Dictionary<(Guid EntryId, string Key), string> _secrets = [];

	public IIntegrationConfig Config => this;
	public FakeAdGuardHomeVariables OwnVariables { get; } = new();
	public IVariableApi Variables => OwnVariables;

	public IUiResourceRegistry UiResources { get; } =
		new BuiltInUiResources(AdGuardHomeIntegration.IntegrationId,
			new UiResourceStore(),
			typeof(AdGuardHomeIntegration));

	public IUserVariableApi UserVariables => throw new NotSupportedException();
	public IDeckNavigator Deck => throw new NotSupportedException();
	public IScriptApi Scripts => throw new NotSupportedException();
	public IWidgetApi Widgets => throw new NotSupportedException();
	public IEventPublisher Events => throw new NotSupportedException();
	public IUserNotifier Notifications => throw new NotSupportedException();

	public Guid AddEntry(string title, IReadOnlyDictionary<string, string?> values, string? password = null)
	{
		var entryId = Guid.NewGuid();
		_entries.Add(new ConfigEntrySnapshot(entryId, title));

		foreach (var (key, value) in values)
		{
			_strings[(entryId, key)] = value;
		}

		if (password is not null)
		{
			_secrets[(entryId, "password")] = password;
		}

		return entryId;
	}

	public void Clear()
	{
		_entries.Clear();
	}

	public Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult<IReadOnlyList<ConfigEntrySnapshot>>(_entries.ToList());
	}

	public Task<string?> GetStringAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_strings.GetValueOrDefault((entryId, key)));
	}

	public Task<string?> GetSecretAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
	{
		return Task.FromResult<string?>(_secrets.GetValueOrDefault((entryId, key)));
	}

	public Task SetStringAsync(Guid entryId, string key, string? value, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public Task SetSecretAsync(Guid entryId, string key, string value, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}
}

internal sealed class FakeAdGuardHomeVariables : IVariableApi
{
	private readonly Lock _gate = new();
	private readonly List<VariableHandle> _variables = [];
	private int _refreshRequests;

	public int RefreshRequests => Volatile.Read(ref _refreshRequests);

	public void Add(VariableHandle variable)
	{
		lock (_gate)
		{
			_variables.Add(variable);
		}
	}

	public Task<IReadOnlyList<VariableHandle>> GetAllAsync()
	{
		lock (_gate)
		{
			return Task.FromResult<IReadOnlyList<VariableHandle>>([.. _variables]);
		}
	}

	public Task DeleteAsync(Guid variableId)
	{
		lock (_gate)
		{
			_variables.RemoveAll(variable => variable.Id == variableId);
		}

		return Task.CompletedTask;
	}

	public Task RequestRefreshAsync(CancellationToken cancellationToken = default)
	{
		Interlocked.Increment(ref _refreshRequests);
		return Task.CompletedTask;
	}

	public Task<VariableHandle?> GetByNameAsync(string name)
	{
		throw new NotSupportedException();
	}

	public Task<VariableHandle> CreateAsync(
		string name,
		VariableType type,
		object? initialValue = null,
		int? decimalPlaces = null,
		string? definitionId = null)
	{
		throw new NotSupportedException();
	}

	public Task SetValueAsync(Guid variableId, object? value)
	{
		throw new NotSupportedException();
	}
}
