using System.Collections.Concurrent;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Email;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Integrations.Imap.Configuration;
using MacroDeckHost.Integrations.Imap.Connections;
using MacroDeckHost.Integrations.Imap.Models;
using MacroDeckHost.Integrations.Imap.Protocol;
using MacroDeckHost.Localization;
using Serilog;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Imap;

namespace MacroDeckHost.Integrations.Imap;

[MacroDeckIntegration]
public sealed class ImapIntegration
	: IIntegration,
		IConfigFlowProvider,
		IConfigEntryLifecycle,
		IEmailInboxProvider,
		IIntegrationIconProvider,
		IIntegrationIssueProvider,
		IDisposable
{
	public const string IntegrationId = "app.macro-deck.imap";

	internal const string SignInIssuePrefix = "sign-in-failed:";

	internal const string CertificateIssuePrefix = "certificate-failed:";

	internal const string FolderIssuePrefix = "folder-not-found:";

	private static readonly string[] _issuePrefixes = [SignInIssuePrefix, CertificateIssuePrefix, FolderIssuePrefix];

	private static readonly ILogger _logger = IntegrationLog.For<ImapIntegration>(IntegrationId);
	private static readonly byte[] _icon = LoadIcon();

	private readonly IImapSessionFactory _factory;
	private readonly ImapAccountManager _accounts;
	private readonly ConcurrentDictionary<Guid, string> _pendingInboxIds = new();

	private IIntegrationContext? _context;

	public ImapIntegration()
		: this(new MailKitImapSessionFactory())
	{
	}

	internal ImapIntegration(
		IImapSessionFactory factory,
		ImapConnectionTimings? timings = null,
		TimeProvider? time = null)
	{
		_factory = factory;
		_accounts = new ImapAccountManager(factory,
			timings ?? ImapConnectionTimings.Default,
			time ?? TimeProvider.System,
			_logger);
		_accounts.Changed += (_, args) => Changed?.Invoke(this, args);
	}

	public event EventHandler<EmailInboxChangedEventArgs>? Changed;

	public string Id => IntegrationId;

	public LocalizedText Name => Strings.Name();

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions => [];

	public bool AllowsMultipleConfigurations => true;

	public string ProviderName => "IMAP";

	public string IconMimeType => "image/svg+xml";

	public byte[] GetIcon()
	{
		return _icon;
	}

	public IConfigFlow CreateConfigFlow()
	{
		return new ImapConfigFlow(_factory);
	}

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_context = context;
		await _accounts.ReloadAsync(context.Config).ConfigureAwait(false);
		IsInitialized = true;
	}

	public async Task ShutdownAsync()
	{
		IsInitialized = false;
		await _accounts.StopAsync().ConfigureAwait(false);
	}

	public IReadOnlyList<EmailInbox> GetInboxes()
	{
		return _accounts.Inboxes;
	}

	public Task<EmailInboxSnapshot> GetSnapshotAsync(
		string inboxId,
		EmailMessageQuery query,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(query);
		var connection = _accounts.Resolve(inboxId)
			?? throw new InvalidOperationException($"IMAP has no inbox '{inboxId}'.");
		return connection.GetSnapshotAsync(query, cancellationToken);
	}

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult<IReadOnlyList<IntegrationIssue>>([
			.. _accounts.Connections.Select(Issue).OfType<IntegrationIssue>()
		]);
	}

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_issuePrefixes.Any(prefix => issueId.StartsWith(prefix, StringComparison.Ordinal))
			? IssueResolution.Ok()
			: IssueResolution.Failed(AppStrings.Integrations.Issues.UnknownIssue()));
	}

	public Task<ConfigEntryPreparationResult> PrepareEntryAsync(
		ConfigEntryPreparation preparation,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(preparation);

		if (ImapInboxId.FromValues(preparation.Values) is not { } inboxId)
		{
			return Task.FromResult(ConfigEntryPreparationResult.Accept());
		}

		var taken = preparation.Siblings
			.Where(sibling => sibling.Id != preparation.EntryId)
			.Select(sibling => ImapInboxId.FromValues(sibling.Values))
			.Concat(_pendingInboxIds.Where(pair => pair.Key != preparation.EntryId).Select(pair => pair.Value))
			.OfType<string>();
		if (taken.Contains(inboxId, StringComparer.Ordinal))
		{
			return Task.FromResult(ConfigEntryPreparationResult.Refuse(Strings.Config.AlreadyConfigured()));
		}

		_pendingInboxIds[preparation.EntryId] = inboxId;
		return Task.FromResult(ConfigEntryPreparationResult.Accept());
	}

	public Task<bool> IsEntryUsableAsync(StoredConfigEntry entry, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entry);
		return Task.FromResult(ImapInboxId.FromValues(entry.Values) is not null);
	}

	public async Task<ConfigEntryStatus> GetEntryStatusAsync(
		StoredConfigEntry entry,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entry);

		if (!await IsEntryUsableAsync(entry, cancellationToken).ConfigureAwait(false))
		{
			return ConfigEntryStatus.NeedsReconfiguration;
		}

		var connection = _accounts.Connections.FirstOrDefault(candidate => candidate.EntryId == entry.Id);
		if (connection?.Failure is ImapFailure.Certificate or ImapFailure.FolderNotFound)
		{
			return ConfigEntryStatus.NeedsReconfiguration;
		}

		return connection?.Status switch
		{
			null => ConfigEntryStatus.Disconnected,
			EmailInboxStatus.Connecting => ConfigEntryStatus.Connecting,
			EmailInboxStatus.Connected => ConfigEntryStatus.Connected,
			EmailInboxStatus.Error => ConfigEntryStatus.NeedsReconfiguration,
			_ => ConfigEntryStatus.Reconnecting
		};
	}

	public async Task<bool> ReloadEntriesAsync(CancellationToken cancellationToken = default)
	{
		if (IsInitialized && _context is { } context)
		{
			await _accounts.ReloadAsync(context.Config, cancellationToken).ConfigureAwait(false);
		}

		return true;
	}

	public Task ReleasePreparationAsync(Guid entryId, CancellationToken cancellationToken = default)
	{
		_pendingInboxIds.TryRemove(entryId, out _);
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		_accounts.Dispose();
	}

	private static IntegrationIssue? Issue(ImapAccountConnection connection)
	{
		var entryId = connection.EntryId.ToString("D");
		if (connection.Status == EmailInboxStatus.Error)
		{
			return new IntegrationIssue
			{
				Id = SignInIssuePrefix + entryId,
				Title = Strings.Issues.SignInFailedTitle(connection.Title),
				Description = Strings.Issues.SignInFailedDescription(),
				Severity = IntegrationIssueSeverity.Error
			};
		}

		return connection.Failure switch
		{
			ImapFailure.Certificate => new IntegrationIssue
			{
				Id = CertificateIssuePrefix + entryId,
				Title = Strings.Issues.CertificateFailedTitle(connection.Title),
				Description = Strings.Issues.CertificateFailedDescription(connection.Settings.Host),
				Severity = IntegrationIssueSeverity.Error
			},
			ImapFailure.FolderNotFound => new IntegrationIssue
			{
				Id = FolderIssuePrefix + entryId,
				Title = Strings.Issues.FolderNotFoundTitle(connection.Title),
				Description = Strings.Issues.FolderNotFoundDescription(connection.Settings.Folder),
				Severity = IntegrationIssueSeverity.Warning
			},
			_ => null
		};
	}

	private static byte[] LoadIcon()
	{
		var assembly = typeof(ImapIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(resource => resource.EndsWith("imap-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
