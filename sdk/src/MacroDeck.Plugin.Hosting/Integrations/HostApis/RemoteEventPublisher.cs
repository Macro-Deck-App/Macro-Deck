using System.Text.Json;
using MacroDeck.Plugin.Hosting.Logging;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Events;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Events;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

/// <summary>
/// Proxies <see cref="IEventPublisher"/> over <c>event.publish</c> - the dedicated message type
/// <see cref="MessageTypes.EventPublish"/> already carries, not a <c>host.invoke</c>/<c>host.result</c>
/// round trip. <see cref="Publish"/> must never throw into the caller by contract, so a missing
/// connection or a failed send is logged and swallowed, exactly like a dropped occurrence nobody
/// subscribed to.
/// </summary>
internal sealed class RemoteEventPublisher : IEventPublisher
{
	private readonly PluginConnectionState _state;
	private readonly HostStateCache _stateCache;
	private readonly ILogger _logger;

	public RemoteEventPublisher(PluginConnectionState state, HostStateCache stateCache, ILogger logger)
	{
		_state = state;
		_stateCache = stateCache;
		_logger = logger.ForContext<RemoteEventPublisher>();
		// The cache raises inline on the receive loop; a plugin handler that blocks there would stall
		// its own connection, so the plugin-facing event is raised from the pool instead.
		_stateCache.EventBindingsChanged += () => Task.Run(RaiseBindingsChanged);
	}

	public event Action? BindingsChanged;

	private void RaiseBindingsChanged()
	{
		try
		{
			BindingsChanged?.Invoke();
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Warning(exception, "A BindingsChanged handler threw");
		}
	}

	public IReadOnlyList<EventBinding> GetBindings()
		=> [.. _stateCache.GetList<EventBindingDto>(Protocol.Callbacks.HostApis.EventBindings).Select(dto => dto.ToBinding())];

	public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
	{
		if (string.IsNullOrWhiteSpace(eventId))
		{
			return;
		}

		var connection = _state.ActiveConnection;
		if (connection is null)
		{
			return;
		}

		var envelope = new ProtocolEnvelope
		{
			Type = MessageTypes.EventPublish,
			Id = Guid.CreateVersion7().ToString(),
			Payload = JsonSerializer.SerializeToElement(new EventPublishPayload
				{
					EventId = eventId,
					Parameters = parameters is null
						? null
						: JsonSerializer.SerializeToElement(parameters, PluginProtocolJson.Options)
				},
				PluginProtocolJson.Options)
		};

		_ = PublishBestEffortAsync(connection, envelope, eventId);
	}

	private async Task PublishBestEffortAsync(PluginSessionConnection connection,
		ProtocolEnvelope envelope,
		string eventId)
	{
		try
		{
			await connection.SendAsync(envelope, CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.EventPublishFailed(eventId, exception);
		}
	}
}
