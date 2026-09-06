using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Weather;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Weather;

namespace MacroDeck.Plugin.Hosting.Capabilities.Weather;

/// <summary>
/// Exposes every registered integration's <c>IWeatherProvider</c> as the <c>weather</c> capability.
/// Provider-shaped like <c>music-player</c>: one <c>provider</c> local id, with each station addressed
/// by an <c>instanceId</c> carried in the operation arguments, because weather station instance ids
/// are config-entry GUIDs that do not exist at declaration time.
/// </summary>
internal sealed class WeatherCapabilityHandler(IEnumerable<IPluginIntegration> integrations, PluginMetadata metadata)
	: ICapabilityHandler
{
	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 1 };

	private readonly IReadOnlyList<IWeatherProvider> _providers = [.. integrations.OfType<IWeatherProvider>()];

	public string Kind => CapabilityKinds.Weather;

	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		=> _providers.Count == 0
			? []
			:
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.Weather, LocalId = ProviderCapabilityId.LocalId, VersionRange = _version
				}
			];

	public Task<CapabilityInvocationResult> InvokeAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(invocation);

		// describe ignores the local id entirely - see EventsCapabilityHandler's identical remark.
		if (string.Equals(invocation.Operation, CapabilityOperations.Weather.Describe, StringComparison.Ordinal))
		{
			return Task.FromResult(Describe());
		}

		if (!string.Equals(invocation.LocalId, ProviderCapabilityId.LocalId, StringComparison.Ordinal))
		{
			return Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"No weather provider '{invocation.LocalId}' is registered in this plugin."));
		}

		return invocation.Operation switch
		{
			CapabilityOperations.Weather.Instances => Task.FromResult(Instances()),
			CapabilityOperations.Weather.Snapshot => SnapshotAsync(invocation, cancellationToken),
			_ => Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
				$"The weather capability has no operation '{invocation.Operation}'."))
		};
	}

	private CapabilityInvocationResult Describe()
		=> CapabilityInvocationResult.Ok(new WeatherDescribePayload
		{
			ProviderName = _providers.Select(p => p.ProviderName).FirstOrDefault(name => !string.IsNullOrEmpty(name)) ??
				metadata.Name,
			Instances = BuildInstances()
		});

	/// <summary>The <c>instances</c> operation - see <c>MusicPlayerInstancesResult</c>'s identical remarks.</summary>
	private CapabilityInvocationResult Instances()
		=> CapabilityInvocationResult.Ok(new WeatherInstancesResult { Instances = BuildInstances() });

	/// <summary>Never re-sorted - see <c>MusicPlayerCapabilityHandler.BuildInstances</c>'s identical remarks.</summary>
	private IReadOnlyList<WeatherStationInstanceDto> BuildInstances()
		=> [.. _providers.SelectMany(provider => provider.GetInstances()).Select(WeatherDescriptorMapper.ToDto)];

	private async Task<CapabilityInvocationResult> SnapshotAsync(CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var arguments = invocation.Arguments?.Deserialize<WeatherInstanceArguments>(PluginProtocolJson.Options);
		if (arguments is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"The snapshot operation requires arguments.");
		}

		IWeatherStation? station = null;
		foreach (var provider in _providers)
		{
			if (provider.GetInstances().Any(instance =>
				string.Equals(instance.Id, arguments.InstanceId, StringComparison.Ordinal)))
			{
				station = provider.GetStation(arguments.InstanceId);
				break;
			}
		}

		if (station is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"No weather station '{arguments.InstanceId}' is currently available.");
		}

		var snapshot = await station.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
		return CapabilityInvocationResult.Ok(WeatherDescriptorMapper.ToDto(snapshot));
	}
}
