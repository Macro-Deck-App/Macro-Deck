using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Capabilities.Events;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Events;
using MacroDeck.Plugin.Hosting.Localization;

namespace MacroDeck.Plugin.Hosting.Capabilities.Events;

/// <summary>
/// Exposes every registered integration's <c>IEventProvider</c> as the <c>events</c> capability. Unlike
/// <c>actions</c> and <c>variables</c>, this kind is provider-shaped: the whole plugin declares one
/// <c>provider</c> local id (see <see cref="ProviderCapabilityId" />) whose <c>describe</c> answers with
/// the merged event catalogue across every <c>IEventProvider</c> integration in the process - mirroring
/// how the host's single <c>RemotePluginIntegration</c> adapter is itself the one thing standing in for
/// the whole plugin.
/// </summary>
internal sealed class EventsCapabilityHandler(IEnumerable<IPluginIntegration> integrations, PluginMetadata metadata)
	: ICapabilityHandler
{
	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 1 };

	private readonly IReadOnlyList<IEventProvider> _providers = [.. integrations.OfType<IEventProvider>()];

	public string Kind => CapabilityKinds.Events;

	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		=> _providers.Count == 0
			? []
			:
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.Events, LocalId = ProviderCapabilityId.LocalId, VersionRange = _version
				}
			];

	public Task<CapabilityInvocationResult> InvokeAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(invocation);

		// describe ignores the local id entirely, the same way ActionsCapabilityHandler.Describe does -
		// RemotePluginSnapshotRefresher addresses every kind-wide describe with a placeholder local id
		// (see its DescribeLocalId remarks), never the real "provider" id. Every other operation is
		// addressed to the one provider this kind ever declares, so it is the only place the gate applies.
		if (string.Equals(invocation.Operation, CapabilityOperations.Events.Describe, StringComparison.Ordinal))
		{
			return Task.FromResult(Describe());
		}

		if (!string.Equals(invocation.LocalId, ProviderCapabilityId.LocalId, StringComparison.Ordinal))
		{
			return Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"No events provider '{invocation.LocalId}' is registered in this plugin."));
		}

		return invocation.Operation switch
		{
			CapabilityOperations.Events.Options => GetOptionsAsync(invocation, cancellationToken),
			_ => Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
				$"The events capability has no operation '{invocation.Operation}'."))
		};
	}

	private CapabilityInvocationResult Describe()
	{
		var payload = new EventCatalogPayload
		{
			ProviderName = _providers.Select(p => p.ProviderName).FirstOrDefault(name => !string.IsNullOrEmpty(name)) ??
				metadata.Name,
			Events = [.. _providers.SelectMany(p => p.EventDefinitions).Select(EventDescriptorMapper.ToDto)],
			HasDynamicEventOptions = integrations.OfType<IDynamicEventOptionsProvider>().Any()
		};

		return CapabilityInvocationResult.Ok(payload);
	}

	private async Task<CapabilityInvocationResult> GetOptionsAsync(CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var arguments = invocation.Arguments?.Deserialize<EventOptionsArguments>(PluginProtocolJson.Options);
		if (arguments is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"The options operation requires arguments.");
		}

		var provider = _providers.FirstOrDefault(p
			=> p.EventDefinitions.Any(e => string.Equals(e.Id, arguments.EventId, StringComparison.Ordinal)));

		if (provider is not IDynamicEventOptionsProvider dynamicProvider)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"Event '{arguments.EventId}' does not support dynamic options.");
		}

		var definition = provider.EventDefinitions
			.First(e => string.Equals(e.Id, arguments.EventId, StringComparison.Ordinal));

		var bound = ActionArgumentBinder.Bind(definition.ConfigurationParameters,
			SerializeParameters(arguments.CurrentParameters));
		var parameters = bound.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal);

		var context = new EventOptionsContext
		{
			EventId = arguments.EventId, ParameterName = arguments.ParameterName, Filter = arguments.Filter,
			CurrentParameters = parameters
		};

		var result = await dynamicProvider.GetEventOptionsAsync(context, cancellationToken).ConfigureAwait(false);

		return CapabilityInvocationResult.Ok(new DynamicOptionsResultDto
		{
			Options =
			[
				.. result.Options.Select(option => new ActionParameterOptionDto
				{
					Value = option.Value, Label = PluginText.ToWireOrNull(option.Label), Metadata = option.Metadata
				})
			],
			AllowsCustomValue = result.AllowsCustomValue,
			CacheSeconds = result.CacheSeconds,
			Error = PluginText.ToWireOrNull(result.Error)
		});
	}

	private static JsonElement? SerializeParameters(IReadOnlyDictionary<string, JsonElement> parameters)
		=> JsonSerializer.SerializeToElement(parameters, PluginProtocolJson.Options);
}
