using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Plugins.Compatibility;
using MacroDeckHost.Application.Plugins.Runtime;

namespace MacroDeckHost.Application.Plugins;

public enum PluginSessionOrigin
{
	// Launched and owned by the supervisor - it manages the plugin's process lifecycle.
	Managed,

	// Authenticated itself against a registration (Developer token or pairing) and runs its own process.
	SelfRegistered
}

public sealed record PluginSessionIdentity
{
	public required string PluginId { get; init; }

	public required string DisplayName { get; init; }

	public Guid? AccessTokenId { get; init; }

	public required PluginSessionOrigin Origin { get; init; }
}

public enum PluginSessionCreationError
{
	VersionUnsupported,
	TooManyDeclaredCapabilities,
	DeclaredVersionTooLong,
	SdkVersionTooLong,
	TooManyReportedDeprecatedApis,
	DeprecatedApiIdTooLong,
	InvalidDeclaredName
}

public sealed record PluginSessionCreationResult
{
	public required bool Succeeded { get; init; }

	public PluginSessionCreationError? Error { get; init; }

	public ProtocolVersionRange? HostVersionRange { get; init; }

	public PluginSessionResponse? Response { get; init; }

	public static PluginSessionCreationResult Success(PluginSessionResponse response)
		=> new() { Succeeded = true, Response = response };

	public static PluginSessionCreationResult Fail(PluginSessionCreationError error,
		ProtocolVersionRange? hostVersionRange = null)
		=> new() { Succeeded = false, Error = error, HostVersionRange = hostVersionRange };
}

public interface IPluginSessionService
{
	Task<PluginSessionCreationResult> Create(PluginSessionIdentity identity, PluginSessionRequest request);

	Task<bool> Close(string sessionId, int closeCode, string reason);
}

public class PluginSessionService : IPluginSessionService
{
	private readonly IPluginSessionRegistry _registry;
	private readonly IPluginSessionTokenIssuer _tokenIssuer;
	private readonly TimeProvider _timeProvider;
	private readonly IPluginCompatibilityService _compatibility;

	public PluginSessionService(
		IPluginSessionRegistry registry,
		IPluginSessionTokenIssuer tokenIssuer,
		TimeProvider timeProvider,
		IPluginCompatibilityService compatibility)
	{
		_registry = registry;
		_tokenIssuer = tokenIssuer;
		_timeProvider = timeProvider;
		_compatibility = compatibility;
	}

	public async Task<PluginSessionCreationResult> Create(PluginSessionIdentity identity,
		PluginSessionRequest request)
	{
		var versionOutcome = ProtocolVersionNegotiator.Negotiate(request.RequestedVersion);
		if (!versionOutcome.Succeeded)
		{
			// Recorded before returning: a plugin that cannot negotiate never reaches the registry, so
			// without this the UI could only show that it is absent, not that it is incompatible.
			_compatibility.Record(new PluginCompatibilityEvaluation
			{
				PluginId = identity.PluginId,
				DisplayName = identity.DisplayName,
				NegotiatedProtocolVersion = null,
				Sdk = request.Sdk
			});

			return PluginSessionCreationResult.Fail(PluginSessionCreationError.VersionUnsupported,
				versionOutcome.HostRange);
		}

		if (request.Capabilities.Count > ProtocolLimits.MaxDeclaredCapabilities)
		{
			return PluginSessionCreationResult.Fail(PluginSessionCreationError.TooManyDeclaredCapabilities);
		}

		// Attacker-controlled text from an untrusted process that the host stores and renders in the UI -
		// bounded the same way PluginId already is, rather than trusted at whatever length a plugin sends.
		if (request.DeclaredVersion is { Length: > ProtocolLimits.MaxDeclaredVersionLength })
		{
			return PluginSessionCreationResult.Fail(PluginSessionCreationError.DeclaredVersionTooLong);
		}

		if (request.DeclaredName is { } declaredName &&
			!string.IsNullOrWhiteSpace(declaredName) &&
			(declaredName.Length > ProtocolLimits.MaxDeclaredNameLength || declaredName.Any(char.IsControl)))
		{
			return PluginSessionCreationResult.Fail(PluginSessionCreationError.InvalidDeclaredName);
		}

		// The SDK block is attacker-controlled text and an attacker-controlled list, bounded the same way
		// everything else from an untrusted process is. Rejected rather than clamped, following
		// DeclaredVersionTooLong: a plugin sending something out of bounds has a bug worth surfacing.
		if (request.Sdk is { } sdk)
		{
			if (sdk.SdkVersion.Length > ProtocolLimits.MaxSdkVersionLength)
			{
				return PluginSessionCreationResult.Fail(PluginSessionCreationError.SdkVersionTooLong);
			}

			if (sdk.DeprecatedApis is { } apis)
			{
				if (apis.Count > ProtocolLimits.MaxReportedDeprecatedApis)
				{
					return PluginSessionCreationResult.Fail(PluginSessionCreationError.TooManyReportedDeprecatedApis);
				}

				if (apis.Any(api => api.Length > ProtocolLimits.MaxDeprecatedApiIdLength))
				{
					return PluginSessionCreationResult.Fail(PluginSessionCreationError.DeprecatedApiIdTooLong);
				}
			}
		}

		var capabilityResults = request.Capabilities
			.GroupBy(capability => capability.Kind, StringComparer.Ordinal)
			.Select(group => CapabilityVersionNegotiator.Negotiate(group.Key, Intersect(group)))
			.ToList();

		var sessionId = Guid.CreateVersion7().ToString("D");
		var sessionToken = _tokenIssuer.Issue(identity.PluginId, sessionId);
		var now = _timeProvider.GetUtcNow();

		var record = new PluginSessionRecord
		{
			SessionId = sessionId,
			PluginId = identity.PluginId,
			DisplayName = PluginDisplayNameResolver.Resolve(identity, request.DeclaredName),
			AccessTokenId = identity.AccessTokenId,
			Origin = identity.Origin,
			NegotiatedVersion = versionOutcome.NegotiatedVersion!.Value,
			DeclaredName = request.DeclaredName,
			DeclaredVersion = request.DeclaredVersion,
			Capabilities = capabilityResults.ToDictionary(c => c.Kind, StringComparer.Ordinal),
			DeclaredCapabilities = request.Capabilities,
			State = PluginSessionState.Awaiting,
			CreatedAt = now
		};

		await _registry.Create(record);

		var compatibility = _compatibility.Record(new PluginCompatibilityEvaluation
		{
			PluginId = identity.PluginId,
			DisplayName = identity.DisplayName,
			NegotiatedProtocolVersion = versionOutcome.NegotiatedVersion.Value,
			Capabilities = capabilityResults,
			Sdk = request.Sdk
		});

		var response = new PluginSessionResponse
		{
			SessionId = sessionId,
			SessionToken = sessionToken,
			NegotiatedVersion = versionOutcome.NegotiatedVersion.Value,
			Capabilities = capabilityResults,
			Limits = ProtocolDescriptorFactory.CreateLimitsDescriptor(),
			Timeouts = ProtocolDescriptorFactory.CreateTimeoutsDescriptor(),
			Compatibility = compatibility
		};

		return PluginSessionCreationResult.Success(response);
	}

	public Task<bool> Close(string sessionId, int closeCode, string reason)
	{
		_registry.MakeNonResumable(sessionId);
		return _registry.Terminate(sessionId, closeCode, reason);
	}

	private static CapabilityVersionRange Intersect(IEnumerable<DeclaredCapability> capabilities)
	{
		var minimum = int.MinValue;
		var maximum = int.MaxValue;

		foreach (var capability in capabilities)
		{
			minimum = Math.Max(minimum, capability.VersionRange.Minimum);
			maximum = Math.Min(maximum, capability.VersionRange.Maximum);
		}

		return new CapabilityVersionRange { Minimum = minimum, Maximum = maximum };
	}
}
