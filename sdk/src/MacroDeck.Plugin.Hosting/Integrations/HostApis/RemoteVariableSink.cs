using MacroDeck.Plugin.Hosting.Capabilities.Variables;
using MacroDeck.Plugin.Hosting.Logging;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Capabilities.Variables;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

/// <summary>
/// Implements <see cref="IVariableSink" /> over <c>host.invoke</c> against
/// <see cref="HostApis.VariableValues" />, handed to a push-capable variable catalog through
/// <see cref="IVariableProvider.OnAttachedAsync" />.
///
/// <para>
/// <see cref="PublishAsync" /> filters against <see cref="VariableSubscriptions.Current" /> before
/// sending anything - the plugin-side half of "push only what you were told to watch"; the host applies
/// the same rule again on arrival, but a provider that ignores the working set should never even reach
/// the wire with values nobody asked for.
/// </para>
///
/// <para>
/// Both members are fire-and-forget by <see cref="IVariableSink" />'s own documented contract: a failed
/// delivery is logged and swallowed, never thrown, so a transient transport problem cannot take down a
/// provider's event loop. Mirrors <see cref="RemoteEventPublisher" /> and
/// <see cref="PluginCatalogNotifier" />'s identical shape.
/// </para>
/// </summary>
internal sealed class RemoteVariableSink(
	IHostInvoker invoker,
	VariableSubscriptions subscriptions,
	ILogger logger) : IVariableSink
{
	private readonly ILogger _logger = logger.ForContext<RemoteVariableSink>();

	public async Task PublishAsync(
		IReadOnlyCollection<VariableValue> values,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(values);

		var working = subscriptions.Current;
		var filtered = values.Where(value => working.Contains(value.Id)).ToList();

		if (filtered.Count == 0)
		{
			return;
		}

		for (var offset = 0; offset < filtered.Count; offset += ProtocolLimits.MaxVariableValuesPerBatch)
		{
			var chunk = filtered
				.Skip(offset)
				.Take(ProtocolLimits.MaxVariableValuesPerBatch)
				.Select(value => new VariableIdValueDto
				{
					Id = value.Id, Reading = VariableValueMapper.ToDto(value.Reading)
				})
				.ToList();

			try
			{
				await invoker.InvokeAsync(Protocol.Callbacks.HostApis.VariableValues,
						HostOperations.VariableValues.Value,
						new VariableValuesValueArguments { Values = chunk },
						cancellationToken)
					.ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				_logger.HostCallbackFailed(Protocol.Callbacks.HostApis.VariableValues,
					HostOperations.VariableValues.Value,
					exception);
			}
		}
	}

	public async Task InvalidateCatalogAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			await invoker.InvokeAsync(Protocol.Callbacks.HostApis.VariableValues,
					HostOperations.VariableValues.Invalidate,
					arguments: null,
					cancellationToken)
				.ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.HostCallbackFailed(Protocol.Callbacks.HostApis.VariableValues,
				HostOperations.VariableValues.Invalidate,
				exception);
		}
	}
}
