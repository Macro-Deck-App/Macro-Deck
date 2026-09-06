using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Ui.Sessions;

namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters.Ui;

// Serves a plugin's UI sessions over capability.invoke. No tree or patch ever returns
// on a result here - the plugin pushes those back through host.invoke ui/*.
public sealed class RemoteUiSessionProvider : IUiSessionProvider
{
	private readonly IPluginCapabilityInvoker _invoker;

	public RemoteUiSessionProvider(string pluginId, IPluginCapabilityInvoker invoker)
	{
		ProviderId = pluginId;
		_invoker = invoker;
	}

	public string ProviderId { get; }

	public async Task<UiSessionOpenOutcome> OpenAsync(UiSessionOpenCommand command,
		CancellationToken cancellationToken)
	{
		var data = await InvokeAsync(CapabilityOperations.Ui.SessionOpen,
				new UiSessionOpenArguments
				{
					SessionId = command.SessionId,
					SurfaceKind = command.Surface.Kind,
					SessionMode = command.Surface.SessionMode,
					// Written even when empty, and carrying whatever keys the surface declared including
					// ones this host build does not recognise: the surface vocabulary is open, and a
					// provider that understands an attribute must receive it verbatim.
					SurfaceAttributes = JsonSerializer.SerializeToElement(command.Surface.Attributes,
						PluginProtocolJson.Options),
					UiModelVersion = command.UiModelVersion
				},
				cancellationToken)
			.ConfigureAwait(false);

		var result = data?.Deserialize<UiSessionOpenResult>(PluginProtocolJson.Options);

		if (result is null || !result.Accepted)
		{
			return UiSessionOpenOutcome.Reject(UiSessionErrorCodes.ProviderRejected, result?.RejectionReason);
		}

		return UiSessionOpenOutcome.Accept(result.NegotiatedUiModelVersion ?? command.UiModelVersion);
	}

	public Task CloseAsync(string sessionId, string reason, CancellationToken cancellationToken)
		=> InvokeAsync(CapabilityOperations.Ui.SessionClose,
			new UiSessionCloseArguments { SessionId = sessionId, Reason = reason },
			cancellationToken);

	public Task RequestSnapshotAsync(string sessionId, CancellationToken cancellationToken)
		=> InvokeAsync(CapabilityOperations.Ui.SessionSnapshot,
			new UiSessionSnapshotArguments { SessionId = sessionId },
			cancellationToken);

	public Task DispatchEventAsync(string sessionId,
		UiSessionEventCommand command,
		CancellationToken cancellationToken)
		=> InvokeAsync(CapabilityOperations.Ui.SessionEvent,
			new UiSessionEventArguments
			{
				SessionId = sessionId,
				NodeId = command.NodeId,
				Name = command.Name,
				Data = command.Data.IsEmpty ? null : command.Data.ToElement(),
				Revision = command.Revision,
				ClientId = command.ClientId
			},
			cancellationToken);

	private async Task<JsonElement?> InvokeAsync(string operation,
		object arguments,
		CancellationToken cancellationToken)
	{
		try
		{
			return await _invoker.InvokeAsync(ProviderId,
					new CapabilityInvokeRequest
					{
						Kind = CapabilityKinds.Ui,
						LocalId = ProviderCapabilityId.LocalId,
						Operation = operation,
						Arguments = arguments
					},
					cancellationToken)
				.ConfigureAwait(false);
		}
		catch (RemoteCapabilityException exception)
			when (string.Equals(exception.Code, ProtocolErrorCodes.CapabilityUnavailable, StringComparison.Ordinal))
		{
			// The link is gone, which the plugin session registry is reporting on its own thread at the
			// same moment. Reported as what it is so the broker does not race two verdicts on one death.
			throw new UiProviderDisconnectedException("The plugin serving this view disconnected.", exception);
		}
	}
}
