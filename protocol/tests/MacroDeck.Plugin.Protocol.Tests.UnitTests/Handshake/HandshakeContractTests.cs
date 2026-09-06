using System.Text.Json;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Handshake;

[TestFixture]
public class HandshakeContractTests
{
	private const string SampleSessionId = "0f8fad5b-d9cb-7f5b-9165-70867728950e";

	[Test]
	public void Plugin_protocol_descriptor_round_trips_with_camel_case_keys()
	{
		var descriptor = new PluginProtocolDescriptor
		{
			SupportedVersions = ProtocolVersions.Supported,
			CapabilityKinds = CapabilityKinds.All,
			Limits = SampleLimits(),
			Timeouts = SampleTimeouts(),
		};

		var json = JsonSerializer.Serialize(descriptor, PluginProtocolJson.Options);
		using var document = JsonDocument.Parse(json);

		Assert.Multiple(() =>
		{
			Assert.That(document.RootElement.TryGetProperty("supportedVersions", out _), Is.True);
			Assert.That(document.RootElement.TryGetProperty("capabilityKinds", out _), Is.True);
			Assert.That(document.RootElement.TryGetProperty("limits", out _), Is.True);
			Assert.That(document.RootElement.TryGetProperty("timeouts", out _), Is.True);
		});

		var actual = JsonSerializer.Deserialize<PluginProtocolDescriptor>(json, PluginProtocolJson.Options);
		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.SupportedVersions, Is.EqualTo(descriptor.SupportedVersions));
			Assert.That(actual.CapabilityKinds, Is.EqualTo(descriptor.CapabilityKinds));
			Assert.That(actual.Limits.MaxMessageBytes, Is.EqualTo(descriptor.Limits.MaxMessageBytes));
			Assert.That(actual.Timeouts.Handshake, Is.EqualTo(descriptor.Timeouts.Handshake));
		});
	}

	[Test]
	public void Plugin_registration_response_round_trips_the_one_time_secret()
	{
		var response = new PluginRegistrationResponse
			{ PluginId = "app.macro-deck.example", PluginSecret = new string('a', 43) };

		var json = JsonSerializer.Serialize(response, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<PluginRegistrationResponse>(json, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(actual!.PluginId, Is.EqualTo(response.PluginId));
			Assert.That(actual.PluginSecret, Is.EqualTo(response.PluginSecret));
		});
	}

	[Test]
	public void Plugin_registration_request_serializes_owner_id_and_display_name()
	{
		var request = new PluginRegistrationRequest { PluginId = "app.macro-deck.example", DisplayName = "Example" };

		var json = JsonSerializer.Serialize(request, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("\"pluginId\""));
			Assert.That(json, Does.Contain("\"displayName\""));
		});
	}

	[Test]
	public void Plugin_session_request_carries_the_requested_version_range_and_declared_capabilities()
	{
		var request = new PluginSessionRequest
		{
			RequestedVersion = new ProtocolVersionRange
				{ Minimum = ProtocolVersions.Minimum, Maximum = ProtocolVersions.Current },
			Capabilities =
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.Actions,
					LocalId = "set-volume",
					VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 },
				},
			],
		};

		var json = JsonSerializer.Serialize(request, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<PluginSessionRequest>(json, PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.RequestedVersion.Minimum, Is.EqualTo(ProtocolVersions.Minimum));
			Assert.That(actual.RequestedVersion.Maximum, Is.EqualTo(ProtocolVersions.Current));
			Assert.That(actual.Capabilities, Has.Count.EqualTo(1));
			Assert.That(actual.Capabilities[0].Kind, Is.EqualTo(CapabilityKinds.Actions));
			Assert.That(actual.Capabilities[0].LocalId, Is.EqualTo("set-volume"));
		});
	}

	[Test]
	public void Plugin_session_request_carries_the_declared_version_when_present()
	{
		var request = new PluginSessionRequest
		{
			RequestedVersion = new ProtocolVersionRange
				{ Minimum = ProtocolVersions.Minimum, Maximum = ProtocolVersions.Current },
			Capabilities = [],
			DeclaredVersion = "1.0.0"
		};

		var json = JsonSerializer.Serialize(request, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<PluginSessionRequest>(json, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("\"declaredVersion\""));
			Assert.That(actual!.DeclaredVersion, Is.EqualTo("1.0.0"));
		});
	}

	/// <summary>An older plugin's payload never carried this field - deserializing it must still work,
	/// leaving DeclaredVersion null rather than failing.</summary>
	[Test]
	public void Plugin_session_request_without_a_declared_version_still_deserializes()
	{
		// Shaped like an older plugin's payload: no declaredVersion property at all.
		var json = $$"""
					 {
					 	"requestedVersion": { "minimum": {{ProtocolVersions.Minimum}}, "maximum": {{ProtocolVersions.Current}} },
					 	"capabilities": []
					 }
					 """;

		var actual = JsonSerializer.Deserialize<PluginSessionRequest>(json, PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.That(actual!.DeclaredVersion, Is.Null);
	}

	[Test]
	public void Plugin_session_request_carries_the_declared_name_when_present()
	{
		var request = new PluginSessionRequest
		{
			RequestedVersion = new ProtocolVersionRange
				{ Minimum = ProtocolVersions.Minimum, Maximum = ProtocolVersions.Current },
			Capabilities = [],
			DeclaredName = "Weather Widget"
		};

		var json = JsonSerializer.Serialize(request, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<PluginSessionRequest>(json, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("\"declaredName\""));
			Assert.That(actual!.DeclaredName, Is.EqualTo("Weather Widget"));
		});
	}

	/// <summary>An older plugin's payload never carried this field - deserializing it must still work,
	/// leaving DeclaredName null rather than failing.</summary>
	[Test]
	public void Plugin_session_request_without_a_declared_name_still_deserializes()
	{
		// Shaped like an older plugin's payload: no declaredName property at all.
		var json = $$"""
					 {
					 	"requestedVersion": {
					 		"minimum": {{ProtocolVersions.Minimum}},
					 		"maximum": {{ProtocolVersions.Current}}
					 	},
					 	"capabilities": []
					 }
					 """;

		var actual = JsonSerializer.Deserialize<PluginSessionRequest>(json, PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.That(actual!.DeclaredName, Is.Null);
	}

	[Test]
	public void Plugin_session_response_carries_negotiated_version_capabilities_limits_and_timeouts()
	{
		var response = new PluginSessionResponse
		{
			SessionId = SampleSessionId,
			SessionToken = "opaque-token",
			NegotiatedVersion = ProtocolVersions.Current,
			Capabilities = [CapabilityNegotiationResult.Accept(CapabilityKinds.Actions, 1)],
			Limits = SampleLimits(),
			Timeouts = SampleTimeouts(),
		};

		var json = JsonSerializer.Serialize(response, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<PluginSessionResponse>(json, PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.SessionId, Is.EqualTo(SampleSessionId));
			Assert.That(actual.NegotiatedVersion, Is.EqualTo(ProtocolVersions.Current));
			Assert.That(actual.Capabilities, Has.Count.EqualTo(1));
			Assert.That(actual.Capabilities[0].Accepted, Is.True);
		});
	}

	[Test]
	public void Session_hello_payload_optional_fields_are_omitted_when_absent()
	{
		var payload = new SessionHelloPayload { ProtocolVersion = 1, SessionId = SampleSessionId };

		var json = JsonSerializer.Serialize(payload, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Not.Contain("resumeSessionId"));
			Assert.That(json, Does.Not.Contain("instanceId"));
		});
	}

	[Test]
	public void Session_hello_payload_carries_resume_session_id_and_instance_id_when_present()
	{
		var payload = new SessionHelloPayload
		{
			ProtocolVersion = 1,
			SessionId = SampleSessionId,
			ResumeSessionId = "1f8fad5b-d9cb-7f5b-9165-70867728950e",
			InstanceId = "instance-1",
		};

		var json = JsonSerializer.Serialize(payload, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<SessionHelloPayload>(json, PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.ResumeSessionId, Is.EqualTo(payload.ResumeSessionId));
			Assert.That(actual.InstanceId, Is.EqualTo(payload.InstanceId));
		});
	}

	[Test]
	public void Session_welcome_payload_reports_whether_the_session_was_resumed()
	{
		var payload = new SessionWelcomePayload { SessionId = SampleSessionId, Resumed = true };

		var json = JsonSerializer.Serialize(payload, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<SessionWelcomePayload>(json, PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.That(actual!.Resumed, Is.True);
	}

	[Test]
	public void Session_goodbye_payload_reason_is_optional_and_omitted_when_null()
	{
		var withoutReason = new SessionGoodbyePayload();

		var json = JsonSerializer.Serialize(withoutReason, PluginProtocolJson.Options);

		Assert.That(json, Does.Not.Contain("reason"));
	}

	[Test]
	public void Plugin_pairing_request_round_trips_with_camel_case_keys()
	{
		var request = new PluginPairingRequest
		{
			PluginId = "app.macro-deck.example",
			DisplayName = "Example",
			CodeChallenge = "challenge",
			CodeChallengeMethod = PluginPairingChallengeMethods.S256,
			Client = new PluginPairingClientInfo
			{
				ExecutablePath = "/usr/local/bin/example",
				ProcessId = 4242,
				SdkVersion = "1.0.0",
			},
		};

		var json = JsonSerializer.Serialize(request, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<PluginPairingRequest>(json, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("\"pluginId\""));
			Assert.That(json, Does.Contain("\"codeChallenge\""));
			Assert.That(json, Does.Contain("\"codeChallengeMethod\""));
			Assert.That(actual, Is.Not.Null);
			Assert.That(actual!.PluginId, Is.EqualTo(request.PluginId));
			Assert.That(actual.Client!.ExecutablePath, Is.EqualTo(request.Client.ExecutablePath));
			Assert.That(actual.Client.ProcessId, Is.EqualTo(request.Client.ProcessId));
			Assert.That(actual.Client.SdkVersion, Is.EqualTo(request.Client.SdkVersion));
		});
	}

	[Test]
	public void Plugin_pairing_response_round_trips_with_camel_case_keys()
	{
		var response = new PluginPairingResponse
		{
			RequestId = "req-1",
			ExpiresAt = DateTimeOffset.UnixEpoch,
			PollIntervalSeconds = 2,
		};

		var json = JsonSerializer.Serialize(response, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<PluginPairingResponse>(json, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("\"requestId\""));
			Assert.That(json, Does.Contain("\"pollIntervalSeconds\""));
			Assert.That(actual, Is.Not.Null);
			Assert.That(actual!.RequestId, Is.EqualTo(response.RequestId));
			Assert.That(actual.ExpiresAt, Is.EqualTo(response.ExpiresAt));
			Assert.That(actual.PollIntervalSeconds, Is.EqualTo(response.PollIntervalSeconds));
		});
	}

	[Test]
	public void Plugin_pairing_status_response_round_trips_with_camel_case_keys()
	{
		var response = new PluginPairingStatusResponse
		{
			Status = PluginPairingStatuses.Approved,
			ExpiresAt = DateTimeOffset.UnixEpoch,
		};

		var json = JsonSerializer.Serialize(response, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<PluginPairingStatusResponse>(json, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("\"status\""));
			Assert.That(json, Does.Contain("\"expiresAt\""));
			Assert.That(actual, Is.Not.Null);
			Assert.That(actual!.Status, Is.EqualTo(response.Status));
		});
	}

	[Test]
	public void Plugin_pairing_redemption_request_round_trips_with_camel_case_keys()
	{
		var request = new PluginPairingRedemptionRequest { CodeVerifier = "verifier" };

		var json = JsonSerializer.Serialize(request, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<PluginPairingRedemptionRequest>(json, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("\"codeVerifier\""));
			Assert.That(actual!.CodeVerifier, Is.EqualTo(request.CodeVerifier));
		});
	}

	[Test]
	public void Plugin_pairing_statuses_all_contains_exactly_the_four_documented_statuses()
	{
		Assert.That(PluginPairingStatuses.All,
			Is.EquivalentTo(new[]
			{
				PluginPairingStatuses.Pending,
				PluginPairingStatuses.Approved,
				PluginPairingStatuses.Rejected,
				PluginPairingStatuses.Expired,
			}));
	}

	/// <summary>An older host reports pairing without saying anything about Developer Mode. That must
	/// deserialize to "unknown" rather than to "off", because a plugin that read it as off would stop
	/// pairing against every host that predates the field.</summary>
	[Test]
	public void A_pairing_block_without_developer_mode_deserializes_as_unknown()
	{
		const string json = """
							{
								"supported": true,
								"requestLifetimeSeconds": 300,
								"pollIntervalSeconds": 2
							}
							""";

		var actual = JsonSerializer.Deserialize<PluginPairingDescriptor>(json, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(actual, Is.Not.Null);
			Assert.That(actual!.Supported, Is.True);
			Assert.That(actual.DeveloperModeEnabled, Is.Null);
		});
	}

	[Test]
	public void A_pairing_block_round_trips_developer_mode()
	{
		var descriptor = new PluginPairingDescriptor
		{
			Supported = true,
			RequestLifetimeSeconds = 300,
			PollIntervalSeconds = 2,
			DeveloperModeEnabled = false
		};

		var json = JsonSerializer.Serialize(descriptor, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<PluginPairingDescriptor>(json, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("\"developerModeEnabled\""));
			Assert.That(actual, Is.EqualTo(descriptor));
		});
	}

	/// <summary>An older host's discovery payload never carried this field - deserializing it must
	/// still work, leaving Pairing null so an older host keeps working.</summary>
	[Test]
	public void Plugin_protocol_descriptor_without_a_pairing_block_still_deserializes()
	{
		var json = $$"""
					 {
					 	"supportedVersions": [{{ProtocolVersions.Current}}],
					 	"capabilityKinds": [],
					 	"limits": {{JsonSerializer.Serialize(SampleLimits(), PluginProtocolJson.Options)}},
					 	"timeouts": {{JsonSerializer.Serialize(SampleTimeouts(), PluginProtocolJson.Options)}}
					 }
					 """;

		var actual = JsonSerializer.Deserialize<PluginProtocolDescriptor>(json, PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.That(actual!.Pairing, Is.Null);
	}

	private static PluginProtocolLimitsDescriptor SampleLimits() => new()
	{
		MaxMessageBytes = ProtocolLimits.MaxMessageBytes,
		MaxAssetBytes = ProtocolLimits.MaxAssetBytes,
		MaxAssetChunkBytes = ProtocolLimits.MaxAssetChunkBytes,
		MaxInboundQueueDepth = ProtocolLimits.MaxInboundQueueDepth,
		MaxOutboundQueueDepth = ProtocolLimits.MaxOutboundQueueDepth,
		QueueHighWatermark = ProtocolLimits.QueueHighWatermark,
		QueueLowWatermark = ProtocolLimits.QueueLowWatermark,
		MaxConcurrentInvocations = ProtocolLimits.MaxConcurrentInvocations,
		MaxDeclaredCapabilities = ProtocolLimits.MaxDeclaredCapabilities,
		MaxIdempotencyKeyLength = ProtocolLimits.MaxIdempotencyKeyLength,
		MaxErrorMessageLength = ProtocolLimits.MaxErrorMessageLength,
		MaxErrorDetailEntries = ProtocolLimits.MaxErrorDetailEntries,
		MaxJsonDepth = ProtocolLimits.MaxJsonDepth,
		MaxSessionsPerPlugin = ProtocolLimits.MaxSessionsPerPlugin,
		MaxLogEventsPerBatch = ProtocolLimits.MaxLogEventsPerBatch,
		MaxLogMessageLength = ProtocolLimits.MaxLogMessageLength,
		MaxLogPropertiesPerEvent = ProtocolLimits.MaxLogPropertiesPerEvent,
		MaxLogPropertyNameLength = ProtocolLimits.MaxLogPropertyNameLength,
		MaxLogPropertyValueLength = ProtocolLimits.MaxLogPropertyValueLength,
		MaxLogSourceContextLength = ProtocolLimits.MaxLogSourceContextLength,
		MaxLogExceptionLength = ProtocolLimits.MaxLogExceptionLength,
		MaxLogExceptionDepth = ProtocolLimits.MaxLogExceptionDepth,
		MaxLogInboundQueueDepth = ProtocolLimits.MaxLogInboundQueueDepth,
		MaxLogEventsPerSecond = ProtocolLimits.MaxLogEventsPerSecond,
		MaxLogEventBurst = ProtocolLimits.MaxLogEventBurst,
	};

	private static PluginProtocolTimeoutsDescriptor SampleTimeouts() => new()
	{
		Handshake = ProtocolTimeouts.Handshake,
		DefaultRequest = ProtocolTimeouts.DefaultRequest,
		CapabilityInvoke = ProtocolTimeouts.CapabilityInvoke,
		AssetUpload = ProtocolTimeouts.AssetUpload,
		KeepAliveInterval = ProtocolTimeouts.KeepAliveInterval,
		KeepAliveTimeout = ProtocolTimeouts.KeepAliveTimeout,
		SessionResumeWindow = ProtocolTimeouts.SessionResumeWindow,
		GracefulClose = ProtocolTimeouts.GracefulClose,
	};
}
