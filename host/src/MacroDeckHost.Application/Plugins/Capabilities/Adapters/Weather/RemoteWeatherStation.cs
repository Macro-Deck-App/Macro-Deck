using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Weather;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Plugins.Capabilities.Mapping;
using MacroDeck.Sdk.Weather;

namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters.Weather;

public sealed class RemoteWeatherStation(
	string pluginId,
	string instanceId,
	IPluginCapabilityInvoker invoker) : IWeatherStation
{
	public async Task<WeatherSnapshot> GetSnapshotAsync(CancellationToken ct)
	{
		try
		{
			var data = await invoker.InvokeAsync(pluginId,
					new CapabilityInvokeRequest
					{
						Kind = CapabilityKinds.Weather,
						LocalId = ProviderCapabilityId.LocalId,
						Operation = CapabilityOperations.Weather.Snapshot,
						Arguments = new WeatherInstanceArguments { InstanceId = instanceId }
					},
					ct)
				.ConfigureAwait(false);

			var dto = data?.Deserialize<WeatherSnapshotDto>(PluginProtocolJson.Options);
			return dto is null ? WeatherSnapshot.Unavailable() : WeatherSnapshotMapper.ToDomain(dto);
		}
		catch (RemoteCapabilityException)
		{
			return WeatherSnapshot.Unavailable();
		}
	}
}
