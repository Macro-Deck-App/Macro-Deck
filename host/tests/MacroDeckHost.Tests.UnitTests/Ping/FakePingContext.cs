using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
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
using MacroDeckHost.Integrations;
using MacroDeckHost.Integrations.Ping;
using MacroDeckHost.Integrations.Ping.Configuration;

namespace MacroDeckHost.Tests.UnitTests.Ping;

internal sealed class FakePingContext : IIntegrationContext, IIntegrationConfig, IEventPublisher
{
	private readonly List<ConfigEntrySnapshot> _entries = [];
	private readonly Dictionary<(Guid EntryId, string Key), string?> _strings = [];

	public IIntegrationConfig Config => this;
	public FakePingVariables OwnVariables { get; } = new();
	public IVariableApi Variables => OwnVariables;
	public ConcurrentQueue<(string EventId, IReadOnlyDictionary<string, object?> Payload)> Published { get; } = new();

	public IUiResourceRegistry UiResources { get; } =
		new BuiltInUiResources(PingIntegration.IntegrationId, new UiResourceStore(), typeof(PingIntegration));

	public IUserVariableApi UserVariables => throw new NotSupportedException();
	public IDeckNavigator Deck => throw new NotSupportedException();
	public IScriptApi Scripts => throw new NotSupportedException();
	public IWidgetApi Widgets => throw new NotSupportedException();
	public IEventPublisher Events => this;
	public IUserNotifier Notifications => throw new NotSupportedException();

	public Guid AddTarget(string title, string host, string key, int threshold = 3, int intervalSeconds = 10)
	{
		var entryId = Guid.NewGuid();
		_entries.Add(new ConfigEntrySnapshot(entryId, title));
		SetTarget(entryId, host, key, threshold, intervalSeconds);
		return entryId;
	}

	public void Rename(Guid entryId, string title)
	{
		var index = _entries.FindIndex(entry => entry.Id == entryId);
		_entries[index] = new ConfigEntrySnapshot(entryId, title);
	}

	public void SetTarget(Guid entryId, string host, string key, int threshold = 3, int intervalSeconds = 10)
	{
		_strings[(entryId, PingConfigKeys.Host)] = host;
		_strings[(entryId, PingConfigKeys.IntervalSeconds)] = intervalSeconds.ToString(CultureInfo.InvariantCulture);
		_strings[(entryId, PingConfigKeys.TimeoutMilliseconds)] = "500";
		_strings[(entryId, PingConfigKeys.FailureThreshold)] = threshold.ToString(CultureInfo.InvariantCulture);
		_strings[(entryId, PingConfigKeys.VariableIdentityKey)]
			= ConfigurationIdentityJson.Serialize(new ConfigurationIdentity(key, key));
	}

	public static StoredConfigEntry Stored(Guid entryId, string title, string key)
	{
		return new StoredConfigEntry
		{
			Id = entryId,
			Title = title,
			Values = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[PingConfigKeys.VariableIdentityKey] = JsonSerializer.SerializeToElement(
					ConfigurationIdentityJson.Serialize(new ConfigurationIdentity(title, key)))
			}
		};
	}

	public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
	{
		Published.Enqueue((eventId, parameters ?? new Dictionary<string, object?>()));
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
		return Task.FromResult<string?>(null);
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

internal sealed class FakePingVariables : IVariableApi
{
	private readonly Lock _gate = new();
	private readonly List<VariableHandle> _variables = [];
	private readonly Dictionary<string, List<double?>> _samples = new(StringComparer.Ordinal);
	private readonly List<HistoryWindow> _windows = [];

	public IReadOnlyList<(string Name, int Capacity)> OpenWindows
	{
		get
		{
			lock (_gate)
			{
				return [.. _windows.Select(window => (window.Name, window.Capacity))];
			}
		}
	}

	public IReadOnlyList<VariableHandle> All
	{
		get
		{
			lock (_gate)
			{
				return [.. _variables];
			}
		}
	}

	public void Add(VariableHandle variable)
	{
		lock (_gate)
		{
			_variables.Add(variable);
		}
	}

	public Task<IReadOnlyList<VariableHandle>> GetAllAsync()
	{
		return Task.FromResult(All);
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
		var handle = new VariableHandle(Guid.NewGuid(), name, type, initialValue, decimalPlaces)
		{
			DefinitionId = definitionId
		};
		Add(handle);
		return Task.FromResult(handle);
	}

	public Task SetValueAsync(Guid variableId, object? value)
	{
		throw new NotSupportedException();
	}

	public Task<IVariableHistoryWindow> OpenHistoryAsync(
		string name,
		int capacity,
		CancellationToken cancellationToken = default)
	{
		var window = new HistoryWindow(this, name, capacity);
		lock (_gate)
		{
			_windows.Add(window);
		}

		return Task.FromResult<IVariableHistoryWindow>(window);
	}

	public void RecordSample(string name, double? value)
	{
		List<HistoryWindow> windows;
		lock (_gate)
		{
			if (!_samples.TryGetValue(name, out var samples))
			{
				_samples[name] = samples = [];
			}

			samples.Add(value);
			windows = [.. _windows.Where(window => window.Name == name)];
		}

		foreach (var window in windows)
		{
			window.Raise();
		}
	}

	private IReadOnlyList<double?> Values(string name)
	{
		lock (_gate)
		{
			return _samples.TryGetValue(name, out var samples) ? [.. samples] : [];
		}
	}

	private void Close(HistoryWindow window)
	{
		lock (_gate)
		{
			_windows.Remove(window);
		}
	}

	private sealed class HistoryWindow(FakePingVariables owner, string name, int capacity) : IVariableHistoryWindow
	{
		public string Name => name;

		public int Capacity => capacity;

		public IReadOnlyList<double?> Values => owner.Values(name);

		public event EventHandler? Changed;

		public void Raise()
		{
			Changed?.Invoke(this, EventArgs.Empty);
		}

		public ValueTask DisposeAsync()
		{
			owner.Close(this);
			return ValueTask.CompletedTask;
		}
	}
}
