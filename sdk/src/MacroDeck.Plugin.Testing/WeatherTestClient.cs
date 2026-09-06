using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Weather;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing.Internal;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// The <c>weather</c> capability. A weather provider may serve several station instances;
/// <see cref="GetSnapshotAsync" />'s <see cref="WeatherInstanceArguments" /> is where the instance id
/// being asked about lives, not the wire local id.
/// </summary>
public sealed class WeatherTestClient
{
	private readonly ICapabilityInvoker _invoker;

	internal WeatherTestClient(ICapabilityInvoker invoker) => _invoker = invoker;

	/// <summary>What this provider declares about itself.</summary>
	public Task<CapabilityInvocationOutcome> DescribeAsync(CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Weather,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.Weather.Describe,
			null,
			options);

	/// <summary>The station instances currently available.</summary>
	public Task<CapabilityInvocationOutcome> GetInstancesAsync(CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Weather,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.Weather.Instances,
			null,
			options);

	/// <summary>A station's current reading.</summary>
	public Task<CapabilityInvocationOutcome> GetSnapshotAsync(WeatherInstanceArguments arguments,
		CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.Weather,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.Weather.Snapshot,
			arguments,
			options);
}
