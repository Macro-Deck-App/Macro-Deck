using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Email;

namespace MacroDeckHost.Tests.PluginContractTests.Harness;

internal sealed class TestEmailInboxIntegration(string providerName, params string[] inboxIds)
	: IPluginIntegration, IEmailInboxProvider
{
	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public Task InitializeAsync(IIntegrationContext context)
	{
		return Task.CompletedTask;
	}

	public Task ShutdownAsync()
	{
		return Task.CompletedTask;
	}

	public string ProviderName { get; } = providerName;

	public List<string> InboxIds { get; } = [.. inboxIds];

	public Dictionary<string, EmailInboxSnapshot> Snapshots { get; } = new(StringComparer.Ordinal);

	public Exception? ReadFailure { get; set; }

	public event EventHandler<EmailInboxChangedEventArgs>? Changed;

	public IReadOnlyList<EmailInbox> GetInboxes()
	{
		return
		[
			.. InboxIds.Select(id => new EmailInbox
				{ Id = id, DisplayName = "Inbox " + id, Address = id + "@example.com" })
		];
	}

	public Task<EmailInboxSnapshot> GetSnapshotAsync(
		string inboxId,
		EmailMessageQuery query,
		CancellationToken cancellationToken)
	{
		if (ReadFailure is { } failure)
		{
			return Task.FromException<EmailInboxSnapshot>(failure);
		}

		var snapshot = Snapshots[inboxId];
		var messages = query.UnreadOnly ? snapshot.Messages.Where(message => !message.IsRead) : snapshot.Messages;
		return Task.FromResult(snapshot with { Messages = [.. messages.Take(query.MaxMessages)] });
	}

	public void RaiseChanged(string? inboxId)
	{
		Changed?.Invoke(this, new EmailInboxChangedEventArgs(inboxId));
	}
}
