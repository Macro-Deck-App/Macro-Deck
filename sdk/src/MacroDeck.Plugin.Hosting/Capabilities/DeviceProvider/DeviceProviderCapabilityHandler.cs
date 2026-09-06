using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.DeviceProvider;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Devices;

namespace MacroDeck.Plugin.Hosting.Capabilities.DeviceProvider;

/// <summary>
/// Exposes every registered integration's <c>IDeviceProvider</c> as the <c>device-provider</c>
/// capability. Provider-shaped like <c>virtual-profiles</c>: one <c>provider</c> local id. Registration
/// is driven by the provider itself over the <c>devices</c> host api, so the host-to-plugin direction
/// only describes the provider and re-reads its catalogue - plus, from version 2, the three session
/// operations that hand the provider a deck surface to render.
/// </summary>
internal sealed class DeviceProviderCapabilityHandler(
	IEnumerable<IPluginIntegration> integrations,
	PluginMetadata metadata,
	IHostInvoker? invoker = null,
	IPluginHostAssetReceiver? assets = null) : ICapabilityHandler
{
	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 2 };

	private readonly IReadOnlyList<IDeviceProvider> _providers = [.. integrations.OfType<IDeviceProvider>()];

	private readonly ConcurrentDictionary<string, RemoteDeviceSession> _sessions = new(StringComparer.Ordinal);

	public string Kind => CapabilityKinds.DeviceProvider;

	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		=> _providers.Count == 0
			? []
			:
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.DeviceProvider, LocalId = ProviderCapabilityId.LocalId,
					VersionRange = _version
				}
			];

	public async Task<CapabilityInvocationResult> InvokeAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(invocation);

		// describe ignores the local id entirely - see EventsCapabilityHandler's identical remark.
		if (string.Equals(invocation.Operation,
			CapabilityOperations.DeviceProvider.Describe,
			StringComparison.Ordinal))
		{
			return CapabilityInvocationResult.Ok(new DeviceProviderDescribePayload
			{
				ProviderName = _providers.Select(provider => provider.ProviderName)
						.FirstOrDefault(name => !string.IsNullOrEmpty(name)) ??
					metadata.Name,
				Devices = Devices()
			});
		}

		if (!string.Equals(invocation.LocalId, ProviderCapabilityId.LocalId, StringComparison.Ordinal))
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"No device provider '{invocation.LocalId}' is registered in this plugin.");
		}

		switch (invocation.Operation)
		{
			case CapabilityOperations.DeviceProvider.Devices:
				return CapabilityInvocationResult.Ok(new DeviceProviderDevicesResult { Devices = Devices() });

			case CapabilityOperations.DeviceProvider.SessionOpen:
				return await OpenAsync(Arguments<DeviceSessionOpenArguments>(invocation), cancellationToken);

			// Both session operations below are no-ops for a session this plugin no longer holds, rather
			// than failures. The host pushes only to sessions it opened, so an unknown id means the two
			// ends have already diverged - closing what is closed is what the caller wanted, and a surface
			// for a session that has ended is stale by definition.
			case CapabilityOperations.DeviceProvider.SessionSurface:
			{
				if (Arguments<DeviceSessionSurfaceArguments>(invocation) is { } arguments &&
					_sessions.TryGetValue(arguments.SessionId, out var session))
				{
					session.Apply(arguments.Surface);
				}

				return CapabilityInvocationResult.Ok();
			}

			case CapabilityOperations.DeviceProvider.SessionClose:
			{
				if (Arguments<DeviceSessionCloseArguments>(invocation) is { } arguments &&
					_sessions.TryRemove(arguments.SessionId, out var session))
				{
					session.RaiseClosed(arguments.Reason);
				}

				return CapabilityInvocationResult.Ok();
			}

			default:
				return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
					$"The device-provider capability has no operation '{invocation.Operation}'.");
		}
	}

	/// <summary>
	/// Offers the session to the provider that registered the device, matched by the provider-local id
	/// the host quotes back. A plugin with exactly one provider needs no match: a provider that keeps no
	/// catalogue of its own reports no devices, and it is still the only one that could serve this
	/// session.
	/// </summary>
	private async Task<CapabilityInvocationResult> OpenAsync(
		DeviceSessionOpenArguments? arguments,
		CancellationToken cancellationToken)
	{
		if (arguments is null)
		{
			// A rejected open, not a protocol error: the host's answer to either is the same - no session -
			// and a provider must never see a capability failure it cannot act on.
			return CapabilityInvocationResult.Ok(new DeviceSessionOpenResult
			{
				Accepted = false, RejectionReason = "session.open requires arguments."
			});
		}

		if (invoker is null || assets is null)
		{
			return CapabilityInvocationResult.Ok(new DeviceSessionOpenResult
			{
				Accepted = false, RejectionReason = "This plugin host cannot serve device sessions."
			});
		}

		var provider = _providers.FirstOrDefault(candidate => candidate.GetDevices()
				.Any(device => string.Equals(device.Id, arguments.ProviderDeviceId, StringComparison.Ordinal))) ??
			(_providers.Count == 1 ? _providers[0] : null);

		if (provider is null)
		{
			return CapabilityInvocationResult.Ok(new DeviceSessionOpenResult
			{
				Accepted = false, RejectionReason = "No provider in this plugin offers this device."
			});
		}

		var session = new RemoteDeviceSession(arguments.SessionId,
			arguments.DeviceId,
			arguments.ProviderDeviceId,
			invoker,
			assets);
		_sessions[arguments.SessionId] = session;

		try
		{
			await provider.OnSessionOpenedAsync(session, cancellationToken);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_sessions.TryRemove(arguments.SessionId, out _);
			return CapabilityInvocationResult.Ok(new DeviceSessionOpenResult
			{
				Accepted = false, RejectionReason = exception.Message
			});
		}

		return CapabilityInvocationResult.Ok(new DeviceSessionOpenResult { Accepted = true });
	}

	private static T? Arguments<T>(CapabilityInvocation invocation)
		where T : class
	{
		try
		{
			return invocation.Arguments?.Deserialize<T>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private IReadOnlyList<DeviceDescriptorDto> Devices()
		=> [.. _providers.SelectMany(provider => provider.GetDevices()).Select(DeviceDescriptorMapper.ToDto)];
}
