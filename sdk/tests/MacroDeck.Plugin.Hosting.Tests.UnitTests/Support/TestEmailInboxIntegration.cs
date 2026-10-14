using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Email;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;

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

	public IReadOnlyList<EmailInbox> Inboxes { get; set; }
		= [.. inboxIds.Select(id => new EmailInbox { Id = id, DisplayName = id, Address = $"{id}@example.com" })];

	public Dictionary<string, EmailInboxSnapshot> Snapshots { get; } = new(StringComparer.Ordinal);

	public EmailMessageQuery? LastQuery { get; private set; }

	public event EventHandler<EmailInboxChangedEventArgs>? Changed;

	public bool HasChangedSubscribers => Changed is not null;

	public IReadOnlyList<EmailInbox> GetInboxes()
	{
		return Inboxes;
	}

	public Task<EmailInboxSnapshot> GetSnapshotAsync(
		string inboxId,
		EmailMessageQuery query,
		CancellationToken cancellationToken)
	{
		LastQuery = query;
		return Task.FromResult(Snapshots[inboxId]);
	}

	public void RaiseChanged(string? inboxId)
	{
		Changed?.Invoke(this, new EmailInboxChangedEventArgs(inboxId));
	}
}
