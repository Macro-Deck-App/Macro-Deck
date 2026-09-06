using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Tests.UnitTests.YtmDesktop;

internal sealed class FakeYtmDesktopIntegrationContext : IIntegrationContext
{
	public FakeYtmDesktopIntegrationConfig ConfigStore { get; } = new();

	public RecordingEventPublisher Publisher { get; } = new();

	public IIntegrationConfig Config => ConfigStore;

	public IEventPublisher Events => Publisher;

	public IVariableApi Variables => throw new NotSupportedException();
	public IUserVariableApi UserVariables => throw new NotSupportedException();
	public IDeckNavigator Deck => throw new NotSupportedException();
	public IScriptApi Scripts => throw new NotSupportedException();
	public IWidgetApi Widgets => throw new NotSupportedException();
	public IUserNotifier Notifications => throw new NotSupportedException();
}

internal sealed class RecordingEventPublisher : IEventPublisher
{
	public List<(string EventId, IReadOnlyDictionary<string, object?>? Parameters)> Published { get; } = [];

	public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
		=> Published.Add((eventId, parameters));
}

internal sealed class FakeYtmDesktopIntegrationConfig : IIntegrationConfig
{
	private readonly List<ConfigEntrySnapshot> _entries = [];
	private readonly Dictionary<(Guid EntryId, string Key), string?> _strings = [];
	private readonly Dictionary<(Guid EntryId, string Key), string> _secrets = [];

	public Guid AddEntry(
		string title,
		IReadOnlyDictionary<string, string?> values,
		IReadOnlyDictionary<string, string>? secrets = null)
	{
		var entryId = Guid.NewGuid();
		_entries.Add(new ConfigEntrySnapshot(entryId, title));

		foreach (var (key, value) in values)
		{
			_strings[(entryId, key)] = value;
		}

		foreach (var (key, value) in secrets ?? new Dictionary<string, string>(StringComparer.Ordinal))
		{
			_secrets[(entryId, key)] = value;
		}

		return entryId;
	}

	public Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<ConfigEntrySnapshot>>(_entries.ToList());

	public Task<string?> GetStringAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
		=> Task.FromResult(_strings.GetValueOrDefault((entryId, key)));

	public Task<string?> GetSecretAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
		=> Task.FromResult<string?>(_secrets.GetValueOrDefault((entryId, key)));

	public Task SetStringAsync(Guid entryId, string key, string? value, CancellationToken cancellationToken = default)
	{
		_strings[(entryId, key)] = value;
		return Task.CompletedTask;
	}

	public Task SetSecretAsync(Guid entryId, string key, string value, CancellationToken cancellationToken = default)
	{
		_secrets[(entryId, key)] = value;
		return Task.CompletedTask;
	}
}
