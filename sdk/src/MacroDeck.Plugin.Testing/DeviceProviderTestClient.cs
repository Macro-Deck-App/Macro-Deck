using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.DeviceProvider;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing.Internal;

namespace MacroDeck.Plugin.Testing;

/// <summary>The <c>device-provider</c> capability.</summary>
public sealed class DeviceProviderTestClient
{
	private readonly ICapabilityInvoker _invoker;

	internal DeviceProviderTestClient(ICapabilityInvoker invoker) => _invoker = invoker;

	/// <summary>What this provider declares about itself.</summary>
	public Task<CapabilityInvocationOutcome> DescribeAsync(CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.DeviceProvider,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.DeviceProvider.Describe,
			null,
			options);

	/// <summary>The devices the provider currently offers.</summary>
	public Task<CapabilityInvocationOutcome> GetDevicesAsync(CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.DeviceProvider,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.DeviceProvider.Devices,
			null,
			options);

	/// <summary>
	/// Offers the provider a device session, the way the host does once a registered device is ready to
	/// render. The result deserializes to <see cref="DeviceSessionOpenResult" />: a provider that
	/// declines answers with <c>Accepted = false</c> rather than with a capability failure.
	/// </summary>
	/// <param name="sessionId">The id every later surface, close and host-api call for this session
	/// quotes. Pick a fresh one per open - reusing one lets a stale call address the session that
	/// replaced it, exactly as it would against a real host.</param>
	public Task<CapabilityInvocationOutcome> OpenSessionAsync(
		string sessionId,
		string deviceId,
		string providerDeviceId,
		CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.DeviceProvider,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.DeviceProvider.SessionOpen,
			new DeviceSessionOpenArguments
			{
				SessionId = sessionId, DeviceId = deviceId, ProviderDeviceId = providerDeviceId
			},
			options);

	/// <summary>Pushes one surface to an open session. A surface for an unknown session is a no-op, not a
	/// failure - the two ends have already diverged and the surface is stale by definition.</summary>
	public Task<CapabilityInvocationOutcome> PushSurfaceAsync(
		string sessionId,
		DeviceSurfaceDto surface,
		CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.DeviceProvider,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.DeviceProvider.SessionSurface,
			new DeviceSessionSurfaceArguments { SessionId = sessionId, Surface = surface },
			options);

	/// <summary>Closes an open session, so the provider sees the same <c>Closed</c> event a real host
	/// would raise on it.</summary>
	public Task<CapabilityInvocationOutcome> CloseSessionAsync(
		string sessionId,
		string? reason = null,
		CapabilityInvokeOptions? options = null)
		=> _invoker.InvokeAsync(CapabilityKinds.DeviceProvider,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.DeviceProvider.SessionClose,
			new DeviceSessionCloseArguments { SessionId = sessionId, Reason = reason },
			options);
}
