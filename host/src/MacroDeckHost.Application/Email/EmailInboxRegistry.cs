using MacroDeck.Sdk;
using MacroDeck.Sdk.Email;
using MacroDeck.Sdk.Identity;
using MacroDeckHost.Application.Integrations;
using Serilog;

namespace MacroDeckHost.Application.Email;

public sealed record ResolvedEmailInbox(EmailInboxDescriptor Inbox, IEmailInboxProvider Provider);

public interface IEmailInboxRegistry
{
	IReadOnlyList<EmailInboxDescriptor> GetInboxes();

	IReadOnlyList<ResolvedEmailInbox> ResolveAll();

	ResolvedEmailInbox? Resolve(string inboxRef);

	bool IsEmailIntegration(string integrationId);

	void ReconcileSubscriptions();
}

public sealed class EmailInboxRegistry : IEmailInboxRegistry, IDisposable
{
	private readonly IIntegrationRegistry _integrations;
	private readonly IEmailSyncSignal _signal;
	private readonly ILogger _logger;
	private readonly Lock _gate = new();
	private readonly Dictionary<string, Subscription> _subscriptions = new(StringComparer.Ordinal);
	private readonly HashSet<(string IntegrationId, string LocalId)> _reportedInvalid = [];

	public EmailInboxRegistry(IIntegrationRegistry integrations, IEmailSyncSignal signal, ILogger logger)
	{
		_integrations = integrations;
		_signal = signal;
		_logger = logger.ForContext<EmailInboxRegistry>();
	}

	public IReadOnlyList<EmailInboxDescriptor> GetInboxes()
	{
		return [.. Enumerate().Select(entry => entry.Inbox)];
	}

	public IReadOnlyList<ResolvedEmailInbox> ResolveAll()
	{
		return [.. Enumerate()];
	}

	public ResolvedEmailInbox? Resolve(string inboxRef)
	{
		return Enumerate().FirstOrDefault(entry => string.Equals(entry.Inbox.Ref, inboxRef, StringComparison.Ordinal));
	}

	public bool IsEmailIntegration(string integrationId)
	{
		return Enumerate()
			.Any(entry => string.Equals(entry.Inbox.IntegrationId, integrationId, StringComparison.Ordinal));
	}

	// Matched by instance, not by id: a plugin adapter or a re-registered integration is a new object
	// under the same id, and an event on the old one would never fire again.
	public void ReconcileSubscriptions()
	{
		var current = EnabledProviders().ToDictionary(integration => integration.Id, StringComparer.Ordinal);

		lock (_gate)
		{
			foreach (var (integrationId, subscription) in _subscriptions.ToList())
			{
				if (current.TryGetValue(integrationId, out var integration)
					&& ReferenceEquals(integration, subscription.Provider))
				{
					continue;
				}

				subscription.Provider.Changed -= subscription.Handler;
				_subscriptions.Remove(integrationId);
			}

			foreach (var (integrationId, integration) in current)
			{
				if (_subscriptions.ContainsKey(integrationId))
				{
					continue;
				}

				var provider = (IEmailInboxProvider)integration;
				EventHandler<EmailInboxChangedEventArgs> handler
					= (_, args) => _signal.Request(integrationId, args?.InboxId);
				provider.Changed += handler;
				_subscriptions[integrationId] = new Subscription(provider, handler);
			}
		}
	}

	public void Dispose()
	{
		lock (_gate)
		{
			foreach (var subscription in _subscriptions.Values)
			{
				subscription.Provider.Changed -= subscription.Handler;
			}

			_subscriptions.Clear();
		}
	}

	private IEnumerable<ResolvedEmailInbox> Enumerate()
	{
		var seen = new HashSet<string>(StringComparer.Ordinal);

		foreach (var integration in EnabledProviders())
		{
			var provider = (IEmailInboxProvider)integration;
			var providerName = ProviderDisplayName.Resolve(provider.ProviderName, integration);

			IReadOnlyList<EmailInbox> inboxes;
			try
			{
				inboxes = provider.GetInboxes();
			}
			catch (Exception exception)
			{
				_logger.Warning(exception,
					"Integration {IntegrationId} failed to list its email inboxes",
					integration.Id);
				continue;
			}

			foreach (var inbox in inboxes)
			{
				if (inbox is null || !QualifiedId.TryCreate(integration.Id, inbox.Id, out var id))
				{
					WarnInvalidOnce(integration.Id, inbox?.Id ?? string.Empty);
					continue;
				}

				var inboxRef = id.ToString();
				if (!seen.Add(inboxRef))
				{
					WarnInvalidOnce(integration.Id, inbox.Id);
					continue;
				}

				yield return new ResolvedEmailInbox(new EmailInboxDescriptor(inboxRef,
						integration.Id,
						inbox.Id,
						providerName,
						string.IsNullOrWhiteSpace(inbox.DisplayName) ? inbox.Id : inbox.DisplayName.Trim(),
						string.IsNullOrWhiteSpace(inbox.Address) ? null : inbox.Address.Trim()),
					provider);
			}
		}
	}

	private void WarnInvalidOnce(string integrationId, string localId)
	{
		lock (_gate)
		{
			if (!_reportedInvalid.Add((integrationId, localId)))
			{
				return;
			}
		}

		_logger.Warning("Integration {IntegrationId} announced an invalid or duplicate email inbox id {LocalId}; "
			+ "skipping it",
			integrationId,
			localId);
	}

	private IEnumerable<IIntegration> EnabledProviders()
	{
		return _integrations.Integrations
			.Where(integration => integration is IEmailInboxProvider && _integrations.IsEnabled(integration.Id));
	}

	private sealed record Subscription(IEmailInboxProvider Provider, EventHandler<EmailInboxChangedEventArgs> Handler);
}
