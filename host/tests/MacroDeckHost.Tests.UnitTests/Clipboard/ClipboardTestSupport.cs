using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Integrations.Clipboard.Configuration;
using MacroDeckHost.Integrations.Clipboard.Native;

namespace MacroDeckHost.Tests.UnitTests.Clipboard;

internal sealed class FakeClipboardBackend : IClipboardBackend
{
	private long _changeCount = 1;

	public FakeClipboardBackend(bool exactTokens = true)
	{
		ExactTokens = exactTokens;
	}

	public bool ExactTokens { get; }

	public ClipboardAvailability Availability { get; set; } = ClipboardAvailability.Available;

	public ClipboardContentKinds Kinds { get; private set; }

	public string? Text { get; private set; }

	public IReadOnlyList<string> Files { get; private set; } = [];

	public string? Image { get; private set; }

	public int TextReads { get; private set; }

	public FakeClipboardChangeSource Source { get; } = new();

	public Task<ClipboardContentKinds> ReadKindsAsync(CancellationToken cancellationToken)
	{
		return Task.FromResult(Kinds);
	}

	public Task<ClipboardTextReading> ReadTextAsync(int maxChars, CancellationToken cancellationToken)
	{
		TextReads++;
		if (Text is null)
		{
			return Task.FromResult(ClipboardTextReading.None);
		}

		return Task.FromResult(Text.Length > maxChars ? ClipboardTextReading.TooLarge : ClipboardTextReading.Of(Text));
	}

	public Task<ClipboardWriteReceipt> SetTextAsync(string text, CancellationToken cancellationToken)
	{
		Put(ClipboardContentKinds.Text, text);
		return Receipt();
	}

	public Task<ClipboardWriteReceipt> SetImageAsync(string path, CancellationToken cancellationToken)
	{
		Put(ClipboardContentKinds.Image, null);
		Image = path;
		return Receipt();
	}

	public Task<ClipboardWriteReceipt> SetFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
	{
		Put(ClipboardContentKinds.Files, null);
		Files = paths;
		return Receipt();
	}

	public Task<ClipboardWriteReceipt> ClearAsync(CancellationToken cancellationToken)
	{
		Put(ClipboardContentKinds.None, null);
		return Receipt();
	}

	public IClipboardChangeSource CreateChangeSource()
	{
		return Source;
	}

	public void Seed(ClipboardContentKinds kinds, string? text = null)
	{
		Put(kinds, text);
	}

	public void CopyElsewhere(ClipboardContentKinds kinds, string? text = null)
	{
		Put(kinds, text);
		NotifyChange();
	}

	public void NotifyChange()
	{
		Source.Raise(ExactTokens ? _changeCount : null);
	}

	private void Put(ClipboardContentKinds kinds, string? text)
	{
		Kinds = kinds;
		Text = text;
		Image = null;
		Files = [];
		_changeCount++;
	}

	private Task<ClipboardWriteReceipt> Receipt()
	{
		return Task.FromResult(new ClipboardWriteReceipt(ExactTokens ? _changeCount : null));
	}
}

internal sealed class FakeClipboardChangeSource : IClipboardChangeSource
{
	public event Action<long?>? Changed;

	public event Action? WatchUnsupported;

	public bool IsWatchUnsupported { get; private set; }

	public bool IsStarted { get; private set; }

	public void Start()
	{
		IsStarted = true;
	}

	public void Dispose()
	{
		IsStarted = false;
	}

	public void Raise(long? token)
	{
		if (IsStarted)
		{
			Changed?.Invoke(token);
		}
	}

	public void ReportWatchUnsupported()
	{
		IsWatchUnsupported = true;
		WatchUnsupported?.Invoke();
	}
}

internal sealed class FakeClipboardContext : IIntegrationContext, IIntegrationConfig, IEventPublisher
{
	private readonly List<ConfigEntrySnapshot> _entries = [];
	private readonly Dictionary<(Guid EntryId, string Key), string?> _strings = [];
	private IReadOnlyList<EventBinding> _bindings = [];

	public IIntegrationConfig Config => this;

	public FakeClipboardVariables OwnVariables { get; } = new();

	public IVariableApi Variables => OwnVariables;

	public FakeUserVariables Written { get; } = new();

	public IUserVariableApi UserVariables => Written;

	public ConcurrentQueue<(string EventId, IReadOnlyDictionary<string, object?> Payload)> Published { get; } = new();

	public IDeckNavigator Deck => throw new NotSupportedException();

	public IScriptApi Scripts => throw new NotSupportedException();

	public IWidgetApi Widgets => throw new NotSupportedException();

	public IEventPublisher Events => this;

	public IUserNotifier Notifications => throw new NotSupportedException();

	public IReadOnlyList<string> PublishedIds => [.. Published.Select(published => published.EventId)];

	public void Configure(bool watch, bool exposeText)
	{
		if (_entries.Count == 0)
		{
			_entries.Add(new ConfigEntrySnapshot(Guid.NewGuid(), "Clipboard"));
		}

		var entryId = _entries[0].Id;
		_strings[(entryId, ClipboardConfigKeys.Watch)] = ClipboardSettings.Format(watch);
		_strings[(entryId, ClipboardConfigKeys.ExposeText)] = ClipboardSettings.Format(exposeText);
	}

	public void Bind(params EventBinding[] bindings)
	{
		_bindings = bindings;
	}

	public static EventBinding Binding(string eventId, string pattern, string @operator = "==")
	{
		return new EventBinding
		{
			EventId = eventId,
			Parameters = new Dictionary<string, EventBindingValue>(StringComparer.Ordinal)
			{
				["pattern"] = new(JsonSerializer.SerializeToElement(pattern), @operator)
			}
		};
	}

	public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
	{
		Published.Enqueue((eventId, parameters ?? new Dictionary<string, object?>()));
	}

	public IReadOnlyList<EventBinding> GetBindings()
	{
		return _bindings;
	}

	public Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult<IReadOnlyList<ConfigEntrySnapshot>>([.. _entries]);
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

internal sealed class FakeClipboardVariables : IVariableApi
{
	private readonly List<VariableHandle> _variables = [];

	public int DeclarationChanges { get; private set; }

	public IReadOnlyList<string> Names => [.. _variables.Select(variable => variable.Name)];

	public void Add(string name, VariableType type)
	{
		_variables.Add(new VariableHandle(Guid.NewGuid(), name, type, null, null));
	}

	public Task<IReadOnlyList<VariableHandle>> GetAllAsync()
	{
		return Task.FromResult<IReadOnlyList<VariableHandle>>([.. _variables]);
	}

	public Task<VariableHandle?> GetByNameAsync(string name)
	{
		return Task.FromResult(_variables.FirstOrDefault(variable => variable.Name == name));
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

	public Task DeleteAsync(Guid variableId)
	{
		_variables.RemoveAll(variable => variable.Id == variableId);
		return Task.CompletedTask;
	}

	public Task NotifyDeclarationsChangedAsync(CancellationToken cancellationToken = default)
	{
		DeclarationChanges++;
		return Task.CompletedTask;
	}
}

internal sealed class FakeUserVariables : IUserVariableApi
{
	public Dictionary<string, (VariableType Type, string? Value)> Values { get; } = new(StringComparer.Ordinal);

	public Task<UserVariableWriteResult> ApplyAsync(
		string name,
		string? ownerWidgetId,
		UserVariableOperation operation,
		string? value,
		CancellationToken cancellationToken = default)
	{
		if (!Values.TryGetValue(name, out var existing))
		{
			return Task.FromResult(UserVariableWriteResult.Failed(UserVariableWriteStatus.NotFound, "missing"));
		}

		Values[name] = (existing.Type, value);
		return Task.FromResult(UserVariableWriteResult.Applied());
	}

	public Task<UserVariableCreateResult> CreateAsync(
		string name,
		string? ownerWidgetId,
		VariableType type,
		string? initialValue = null,
		int? decimalPlaces = null,
		CancellationToken cancellationToken = default)
	{
		Values[name] = (type, initialValue);
		return Task.FromResult(UserVariableCreateResult.Created());
	}
}

internal sealed class ClipboardFlowContext(string? entryTitle) : IConfigFlowEntryContext
{
	public string? EntryTitle => entryTitle;

	public IOAuthSession OAuth => throw new NotSupportedException();
}
