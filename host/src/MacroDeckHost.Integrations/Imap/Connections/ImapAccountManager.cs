using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Email;
using MacroDeckHost.Integrations.Imap.Configuration;
using MacroDeckHost.Integrations.Imap.Protocol;
using Serilog;

namespace MacroDeckHost.Integrations.Imap.Connections;

internal sealed class ImapAccountManager : IDisposable
{
	private readonly IImapSessionFactory _factory;
	private readonly ImapConnectionTimings _timings;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _reloadGate = new(1, 1);

	private volatile IReadOnlyList<ImapAccountConnection> _connections = [];
	private volatile IReadOnlyList<EmailInbox> _inboxes = [];

	public ImapAccountManager(
		IImapSessionFactory factory,
		ImapConnectionTimings timings,
		TimeProvider time,
		ILogger logger)
	{
		_factory = factory;
		_timings = timings;
		_time = time;
		_logger = logger;
	}

	public event EventHandler<EmailInboxChangedEventArgs>? Changed;

	public IReadOnlyList<ImapAccountConnection> Connections => _connections;

	public IReadOnlyList<EmailInbox> Inboxes => _inboxes;

	public ImapAccountConnection? Resolve(string inboxId)
	{
		return _connections.FirstOrDefault(connection
			=> string.Equals(connection.InboxId, inboxId, StringComparison.Ordinal));
	}

	public async Task ReloadAsync(IIntegrationConfig config, CancellationToken cancellationToken = default)
	{
		await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var desired = await ReadAccountsAsync(config, cancellationToken).ConfigureAwait(false);
			var previous = _connections;
			var next = new List<ImapAccountConnection>(desired.Count);
			var retained = new HashSet<ImapAccountConnection>();

			foreach (var account in desired)
			{
				var existing = previous.FirstOrDefault(connection
					=> connection.EntryId == account.EntryId
					&& connection.Settings == account.Settings
					&& connection.Status != EmailInboxStatus.Error);
				if (existing is not null)
				{
					existing.Title = account.Title;
					retained.Add(existing);
					next.Add(existing);
					continue;
				}

				var connection = new ImapAccountConnection(account.EntryId,
					account.Title,
					account.Settings,
					_factory,
					_timings,
					_time);
				connection.Changed += OnConnectionChanged;
				next.Add(connection);
			}

			var previousInboxes = _inboxes;
			_connections = next;
			_inboxes = [.. next.Select(ToInbox)];

			foreach (var connection in previous.Where(connection => !retained.Contains(connection)))
			{
				await StopAsync(connection).ConfigureAwait(false);
			}

			foreach (var connection in next.Where(connection => !retained.Contains(connection)))
			{
				connection.Start();
			}

			_logger.Information("IMAP configured with {Count} inbox(es)", next.Count);
			if (!previousInboxes.SequenceEqual(_inboxes))
			{
				RaiseChanged(null);
			}
		}
		finally
		{
			_reloadGate.Release();
		}
	}

	public async Task StopAsync()
	{
		await _reloadGate.WaitAsync().ConfigureAwait(false);
		try
		{
			var connections = _connections;
			_connections = [];
			_inboxes = [];
			await Task.WhenAll(connections.Select(StopAsync)).ConfigureAwait(false);
		}
		finally
		{
			_reloadGate.Release();
		}
	}

	public void Dispose()
	{
		_reloadGate.Dispose();
	}

	private async Task StopAsync(ImapAccountConnection connection)
	{
		connection.Changed -= OnConnectionChanged;
		await connection.DisposeAsync().ConfigureAwait(false);
	}

	private void OnConnectionChanged(object? sender, EventArgs e)
	{
		if (sender is ImapAccountConnection connection && _connections.Contains(connection))
		{
			RaiseChanged(connection.InboxId);
		}
	}

	private void RaiseChanged(string? inboxId)
	{
		try
		{
			Changed?.Invoke(this, new EmailInboxChangedEventArgs(inboxId));
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "An email inbox change subscriber failed");
		}
	}

	private static EmailInbox ToInbox(ImapAccountConnection connection)
	{
		return new EmailInbox
			{ Id = connection.InboxId, DisplayName = connection.Title, Address = connection.Settings.Address };
	}

	private async Task<IReadOnlyList<ImapAccount>> ReadAccountsAsync(
		IIntegrationConfig config,
		CancellationToken cancellationToken)
	{
		var accounts = new List<ImapAccount>();
		var inboxIds = new HashSet<string>(StringComparer.Ordinal);
		foreach (var entry in await config.GetEntriesAsync(cancellationToken).ConfigureAwait(false))
		{
			var settings = await ReadSettingsAsync(config, entry.Id, cancellationToken).ConfigureAwait(false);
			if (settings is null)
			{
				_logger.Warning("IMAP config entry {EntryId} is incomplete; skipping it", entry.Id);
				continue;
			}

			if (!inboxIds.Add(settings.InboxId))
			{
				_logger.Warning("IMAP config entry {EntryId} reads the same folder as another entry; skipping it",
					entry.Id);
				continue;
			}

			var title = string.IsNullOrWhiteSpace(entry.Title) ? settings.Username : entry.Title.Trim();
			accounts.Add(new ImapAccount(entry.Id, title, settings));
		}

		return accounts;
	}

	private static async Task<ImapAccountSettings?> ReadSettingsAsync(
		IIntegrationConfig config,
		Guid entryId,
		CancellationToken cancellationToken)
	{
		var host = await config.GetStringAsync(entryId, ImapConfigKeys.Host, cancellationToken).ConfigureAwait(false);
		var username = await config.GetStringAsync(entryId, ImapConfigKeys.Username, cancellationToken)
			.ConfigureAwait(false);
		var password = await config.GetSecretAsync(entryId, ImapConfigKeys.Password, cancellationToken)
			.ConfigureAwait(false);
		var port = ImapAccountSettings.ParsePort(await config
			.GetStringAsync(entryId, ImapConfigKeys.Port, cancellationToken)
			.ConfigureAwait(false));
		if (string.IsNullOrWhiteSpace(host)
			|| string.IsNullOrWhiteSpace(username)
			|| string.IsNullOrEmpty(password)
			|| port is not (>= 1 and <= 65535))
		{
			return null;
		}

		var security = await config.GetStringAsync(entryId, ImapConfigKeys.Security, cancellationToken)
			.ConfigureAwait(false);
		var folder = await config.GetStringAsync(entryId, ImapConfigKeys.Folder, cancellationToken)
			.ConfigureAwait(false);
		return new ImapAccountSettings(host.Trim(),
			port.Value,
			ImapAccountSettings.ParseSecurity(security),
			username.Trim(),
			password,
			ImapAccountSettings.NormalizeFolder(folder));
	}

	private sealed record ImapAccount(Guid EntryId, string Title, ImapAccountSettings Settings);
}
