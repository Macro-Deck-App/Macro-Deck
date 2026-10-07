using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Tests.UnitTests.AdGuardHome;

internal sealed class FakeAdGuardHomeContext : IIntegrationContext, IIntegrationConfig
{
	private readonly List<ConfigEntrySnapshot> _entries = [];
	private readonly Dictionary<(Guid EntryId, string Key), string?> _strings = [];
	private readonly Dictionary<(Guid EntryId, string Key), string> _secrets = [];

	public IIntegrationConfig Config => this;
	public IVariableApi Variables => throw new NotSupportedException();
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

	public void Clear() => _entries.Clear();

	public Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<ConfigEntrySnapshot>>(_entries.ToList());

	public Task<string?> GetStringAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
		=> Task.FromResult(_strings.GetValueOrDefault((entryId, key)));

	public Task<string?> GetSecretAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
		=> Task.FromResult<string?>(_secrets.GetValueOrDefault((entryId, key)));

	public Task SetStringAsync(Guid entryId, string key, string? value, CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();

	public Task SetSecretAsync(Guid entryId, string key, string value, CancellationToken cancellationToken = default)
		=> throw new NotSupportedException();
}
