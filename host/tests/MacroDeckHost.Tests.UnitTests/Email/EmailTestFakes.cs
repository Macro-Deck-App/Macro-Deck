using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Email;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Email;

internal sealed class FakeEmailIntegration : IIntegration, IEmailInboxProvider
{
	private readonly Dictionary<string, EmailInboxSnapshot> _snapshots = new(StringComparer.Ordinal);

	public FakeEmailIntegration(string id, string providerName = "")
	{
		Id = id;
		ProviderName = providerName;
	}

	public string Id { get; }

	public LocalizedText Name => Id + " integration";

	public string Version => "1.0.0";

	public string ProviderName { get; }

	public bool IsInitialized => true;

	public IReadOnlyList<IActionDefinition> Actions => [];

	public List<EmailInbox> Inboxes { get; } = [];

	public HashSet<string> Failing { get; } = new(StringComparer.Ordinal);

	public List<(string InboxId, EmailMessageQuery Query)> Reads { get; } = [];

	public event EventHandler<EmailInboxChangedEventArgs>? Changed;

	public FakeEmailIntegration WithInbox(string id, string? displayName = null)
	{
		Inboxes.Add(new EmailInbox { Id = id, DisplayName = displayName ?? id, Address = id + "@example.com" });
		_snapshots[id] = EmailTesting.Snapshot(EmailInboxStatus.Connected, 0, []);
		return this;
	}

	public void Set(string inboxId, EmailInboxSnapshot snapshot)
	{
		_snapshots[inboxId] = snapshot;
	}

	public void RaiseChanged(string? inboxId)
	{
		Changed?.Invoke(this, new EmailInboxChangedEventArgs(inboxId));
	}

	public IReadOnlyList<EmailInbox> GetInboxes()
	{
		return Inboxes;
	}

	public Task InitializeAsync(IIntegrationContext context)
	{
		return Task.CompletedTask;
	}

	public Task ShutdownAsync()
	{
		return Task.CompletedTask;
	}

	public Task<EmailInboxSnapshot> GetSnapshotAsync(
		string inboxId,
		EmailMessageQuery query,
		CancellationToken cancellationToken)
	{
		Reads.Add((inboxId, query));
		if (Failing.Contains(inboxId))
		{
			throw new InvalidOperationException("The mail server cannot be reached.");
		}

		var snapshot = _snapshots[inboxId];
		var messages = query.UnreadOnly ? snapshot.Messages.Where(message => !message.IsRead) : snapshot.Messages;
		return Task.FromResult(snapshot with { Messages = [.. messages.Take(query.MaxMessages)] });
	}
}

internal sealed class InMemoryEmailInboxKeyStore : IEmailInboxKeyStore
{
	private EmailInboxKeySet _keys = EmailInboxKeySet.Empty;

	public IReadOnlyList<EmailInboxKey> Keys => _keys.Keys;

	public IReadOnlyDictionary<string, string> Assign(IReadOnlyList<EmailInboxDescriptor> visible)
	{
		_keys = EmailInboxKeys.Assign(_keys, visible);
		return _keys.Keys.ToDictionary(key => key.Ref, key => key.Slug, StringComparer.Ordinal);
	}
}

internal sealed class RecordingEmailEventPublisher : IEmailEventPublisher
{
	public List<EmailInboxChange> Changes { get; } = [];

	public void Publish(IReadOnlyList<EmailInboxChange> changes)
	{
		Changes.AddRange(changes);
	}
}

internal sealed class EmailWorld : IDisposable
{
	public EmailWorld(params IIntegration[] integrations)
		: this(new ConfigurableIntegrationRegistry(integrations))
	{
	}

	public EmailWorld(IIntegrationRegistry integrations)
	{
		Signal = new EmailSyncSignal();
		Registry = new EmailInboxRegistry(integrations, Signal, EmailTesting.Logger());
		Cache = new EmailInboxCache(Registry, Keys, Signal, Time, EmailTesting.Logger(), Events);
	}

	public FakeTimeProvider Time { get; } = new() { Now = EmailTesting.Noon };

	public EmailSyncSignal Signal { get; }

	public EmailInboxRegistry Registry { get; }

	public InMemoryEmailInboxKeyStore Keys { get; } = new();

	public RecordingEmailEventPublisher Events { get; } = new();

	public EmailInboxCache Cache { get; }

	public Task SyncAllAsync()
	{
		return Cache.SyncAsync(EmailSyncRequest.Everything, CancellationToken.None);
	}

	public Task SyncPendingAsync()
	{
		return Cache.SyncAsync(Signal.TakePending(), CancellationToken.None);
	}

	public void Dispose()
	{
		Cache.Dispose();
		Registry.Dispose();
		Signal.Dispose();
	}
}

internal static class EmailTesting
{
	public static readonly DateTimeOffset Noon = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

	public static EmailMessage Message(
		string id,
		int minutesAfterNoon,
		bool isRead = false,
		string? sender = null,
		string? subject = null)
	{
		return new EmailMessage
		{
			Id = id,
			SenderName = sender ?? "Sender " + id,
			SenderAddress = id + "@example.com",
			Subject = subject ?? "Subject " + id,
			Preview = "Preview " + id,
			ReceivedAt = Noon.AddMinutes(minutesAfterNoon),
			IsRead = isRead
		};
	}

	public static EmailInboxSnapshot Snapshot(
		EmailInboxStatus status,
		int unreadCount,
		IReadOnlyList<EmailMessage> messages,
		int? totalCount = null)
	{
		return new EmailInboxSnapshot
		{
			Status = status,
			UnreadCount = unreadCount,
			TotalCount = totalCount ?? messages.Count,
			Messages = [.. messages.OrderByDescending(message => message.ReceivedAt)],
			SyncedAt = Noon
		};
	}

	public static ILogger Logger()
	{
		return new LoggerConfiguration().CreateLogger();
	}
}
