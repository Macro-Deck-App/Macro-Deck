using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.EmailInbox;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Email;
using MacroDeckHost.Application.Plugins.Capabilities.Mapping;
using Serilog;
using ILogger = Serilog.ILogger;
using SdkEmailInbox = MacroDeck.Sdk.Email.EmailInbox;

namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters.EmailInbox;

// Reads are never degraded to an empty answer: IEmailInboxProvider requires a failure to throw, so a
// RemoteCapabilityException reaches the host's email cache as the inbox's read failure.
public sealed class RemoteEmailInboxProvider(
	string pluginId,
	string providerName,
	IReadOnlyList<SdkEmailInbox> inboxes,
	IPluginCapabilityInvoker invoker) : IEmailInboxProvider
{
	private readonly ILogger _logger = Log.ForContext<RemoteEmailInboxProvider>();

	public string ProviderName => providerName;

	public IReadOnlyList<SdkEmailInbox> GetInboxes()
	{
		return inboxes;
	}

	event EventHandler<EmailInboxChangedEventArgs>? IEmailInboxProvider.Changed
	{
		add { }
		remove { }
	}

	public async Task<EmailInboxSnapshot> GetSnapshotAsync(
		string inboxId,
		EmailMessageQuery query,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(query);

		var data = await invoker.InvokeAsync(pluginId,
				new CapabilityInvokeRequest
				{
					Kind = CapabilityKinds.EmailInbox,
					LocalId = ProviderCapabilityId.LocalId,
					Operation = CapabilityOperations.EmailInbox.Snapshot,
					Arguments = new EmailInboxSnapshotArguments
					{
						InboxId = inboxId, MaxMessages = query.MaxMessages, UnreadOnly = query.UnreadOnly
					}
				},
				cancellationToken)
			.ConfigureAwait(false);

		var result = data?.Deserialize<EmailInboxSnapshotResult>(PluginProtocolJson.Options)
			?? throw new InvalidOperationException($"Plugin {pluginId} returned no email inbox snapshot.");

		if (result.Truncated)
		{
			_logger.Warning("Plugin {PluginId} truncated the messages of an email inbox snapshot", pluginId);
		}

		return EmailInboxMapper.ToDomain(result, query.MaxMessages);
	}
}
