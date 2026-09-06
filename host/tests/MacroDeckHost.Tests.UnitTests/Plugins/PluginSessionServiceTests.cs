using MacroDeck.Plugin.Protocol.Compatibility;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Compatibility;
using MacroDeckHost.Tests.UnitTests.Auth;
using Microsoft.Extensions.Logging.Abstractions;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class PluginSessionServiceTests
{
	private static readonly string[] _noDeprecatedApis = [];

	private ManualTimeProvider _time = null!;
	private PluginSessionRegistry _registry = null!;
	private FakePluginSessionTokenIssuer _tokenIssuer = null!;
	private PluginCompatibilityService _compatibility = null!;
	private PluginSessionService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new ManualTimeProvider();
		_registry = new PluginSessionRegistry(_time, Serilog.Core.Logger.None);
		_tokenIssuer = new FakePluginSessionTokenIssuer();
		_compatibility = new PluginCompatibilityService(NullLogger<PluginCompatibilityService>.Instance);
		_service = new PluginSessionService(_registry, _tokenIssuer, _time, _compatibility);
	}

	private static PluginSessionIdentity Identity(string pluginId = "com.example.plugin")
		=> new() { PluginId = pluginId, DisplayName = "Example", Origin = PluginSessionOrigin.Managed };

	private static PluginSessionRequest Request(IReadOnlyList<DeclaredCapability>? capabilities = null)
		=> new()
		{
			RequestedVersion = new ProtocolVersionRange
			{
				Minimum = ProtocolVersions.Minimum,
				Maximum = ProtocolVersions.Current
			},
			Capabilities = capabilities ?? []
		};

	private static DeclaredCapability Declared(string kind, string localId, int minimum = 1, int maximum = 1)
		=> new()
		{
			Kind = kind,
			LocalId = localId,
			VersionRange = new CapabilityVersionRange { Minimum = minimum, Maximum = maximum }
		};

	[Test]
	public async Task Several_Capabilities_Of_One_Kind_Negotiate_Once()
	{
		var request = Request([
			Declared(CapabilityKinds.Actions, "refresh-weather"),
			Declared(CapabilityKinds.Actions, "set-alert-threshold"),
			Declared(CapabilityKinds.Actions, "set-condition"),
			Declared(CapabilityKinds.Variables, "temperature")
		]);

		var result = await _service.Create(Identity(), request);

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.True);
			Assert.That(result.Response!.Capabilities.Select(c => c.Kind),
				Is.EquivalentTo(new[] { CapabilityKinds.Actions, CapabilityKinds.Variables }));
			Assert.That(result.Response.Capabilities.All(c => c.Accepted), Is.True);
		});
	}

	[Test]
	public async Task A_Kind_Is_Negotiated_Against_The_Intersection_Of_Its_Ranges()
	{
		var request = Request([
			Declared(CapabilityKinds.Actions, "old", minimum: 1, maximum: 1),
			Declared(CapabilityKinds.Actions, "new", minimum: 2, maximum: 2)
		]);

		var result = await _service.Create(Identity(), request);

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.True);
			Assert.That(result.Response!.Capabilities, Has.Count.EqualTo(1));
			Assert.That(result.Response.Capabilities[0].Accepted, Is.False);
		});
	}

	[Test]
	public async Task Version_Negotiation_Succeeds()
	{
		var result = await _service.Create(Identity(), Request());

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.True);
			Assert.That(result.Response!.NegotiatedVersion, Is.EqualTo(ProtocolVersions.Current));
		});
	}

	// The maintainer's explicit guarantee: a plugin that only ever speaks v1 (has never seen v2) keeps
	// working. {1,1} intersected against the host's supported range must still negotiate 1, not fail
	// just because the host's own range has moved on.
	[Test]
	public async Task A_v1_only_request_still_negotiates_version_1()
	{
		var request = Request() with { RequestedVersion = new ProtocolVersionRange { Minimum = 1, Maximum = 1 } };

		var result = await _service.Create(Identity(), request);

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.True);
			Assert.That(result.Response!.NegotiatedVersion, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Version_Negotiation_Failure_Is_Reported()
	{
		var request = Request() with
		{
			RequestedVersion = new ProtocolVersionRange { Minimum = 99, Maximum = 100 }
		};

		var result = await _service.Create(Identity(), request);

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginSessionCreationError.VersionUnsupported));
			Assert.That(result.HostVersionRange, Is.Not.Null);
		});
	}

	[Test]
	public async Task A_Rejected_Capability_Is_Non_Fatal()
	{
		var capabilities = new List<DeclaredCapability>
		{
			new()
			{
				Kind = "not-a-real-kind",
				LocalId = "thing",
				VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
			}
		};

		var result = await _service.Create(Identity(), Request(capabilities));

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.True);
			Assert.That(result.Response!.Capabilities.Single().Accepted, Is.False);
		});
	}

	[Test]
	public async Task The_Session_Id_Satisfies_PluginSessionId()
	{
		var result = await _service.Create(Identity(), Request());

		Assert.That(PluginSessionId.IsValid(result.Response!.SessionId), Is.True);
	}

	[Test]
	public async Task A_Second_Session_For_The_Same_Plugin_Replaces_The_First()
	{
		var first = await _service.Create(Identity(), Request());
		var connection = new FakePluginConnection();
		_registry.TryAttach(first.Response!.SessionId, connection, "instance-1");

		var second = await _service.Create(Identity(), Request());

		Assert.Multiple(() =>
		{
			Assert.That(second.Response!.SessionId, Is.Not.EqualTo(first.Response.SessionId));
			Assert.That(connection.Closes, Has.Count.EqualTo(1));
			Assert.That(connection.Closes[0].CloseCode, Is.EqualTo(ProtocolCloseCodes.SessionReplaced));
		});
	}

	[Test]
	public async Task A_declared_version_is_stored_on_the_session()
	{
		var request = Request() with { DeclaredVersion = "1.0.0" };

		var result = await _service.Create(Identity(), request);

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.True);
			Assert.That(_registry.Snapshot().Single().DeclaredVersion, Is.EqualTo("1.0.0"));
		});
	}

	[Test]
	public async Task No_declared_version_does_not_fail_the_session()
	{
		var result = await _service.Create(Identity(), Request());

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.True);
			Assert.That(_registry.Snapshot().Single().DeclaredVersion, Is.Null);
		});
	}

	[Test]
	public async Task An_absurdly_long_declared_version_is_rejected()
	{
		var request = Request() with { DeclaredVersion = new string('9', ProtocolLimits.MaxDeclaredVersionLength + 1) };

		var result = await _service.Create(Identity(), request);

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginSessionCreationError.DeclaredVersionTooLong));
		});
	}

	[Test]
	public async Task A_declared_name_names_the_enrolled_plugins_session()
	{
		var identity = Identity() with
		{
			DisplayName = "Enrolled Label",
			AccessTokenId = Guid.NewGuid(),
			Origin = PluginSessionOrigin.SelfRegistered
		};
		var request = Request() with { DeclaredName = "Weather Widget" };

		var result = await _service.Create(identity, request);

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.True);
			Assert.That(_registry.Snapshot().Single().DisplayName, Is.EqualTo("Weather Widget"));
		});
	}

	[Test]
	public async Task A_launch_backed_session_ignores_a_declared_name()
	{
		var identity = Identity() with
		{
			DisplayName = "Weather Widget",
			AccessTokenId = null,
			Origin = PluginSessionOrigin.Managed
		};
		var request = Request() with { DeclaredName = "Totally Legit Bank" };

		var result = await _service.Create(identity, request);

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.True);
			Assert.That(_registry.Snapshot().Single().DisplayName, Is.EqualTo("Weather Widget"));
		});
	}

	[Test]
	public async Task A_session_without_a_declared_name_keeps_the_enrollment_label()
	{
		var identity = Identity() with
		{
			DisplayName = "Enrolled Label",
			AccessTokenId = Guid.NewGuid(),
			Origin = PluginSessionOrigin.SelfRegistered
		};

		var result = await _service.Create(identity, Request());

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.True);
			Assert.That(_registry.Snapshot().Single().DisplayName, Is.EqualTo("Enrolled Label"));
		});
	}

	[Test]
	public async Task An_absurdly_long_declared_name_is_rejected()
	{
		var identity = Identity() with { AccessTokenId = Guid.NewGuid(), Origin = PluginSessionOrigin.SelfRegistered };
		var request = Request() with { DeclaredName = new string('9', 129) };

		var result = await _service.Create(identity, request);

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginSessionCreationError.InvalidDeclaredName));
		});
	}

	[Test]
	public async Task A_declared_name_containing_a_control_character_is_rejected()
	{
		var identity = Identity() with { AccessTokenId = Guid.NewGuid(), Origin = PluginSessionOrigin.SelfRegistered };
		var request = Request() with { DeclaredName = "Weather\nWidget" };

		var result = await _service.Create(identity, request);

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginSessionCreationError.InvalidDeclaredName));
		});
	}

	[Test]
	public async Task A_whitespace_only_declared_name_falls_back_to_the_enrollment_label()
	{
		var identity = Identity() with
		{
			DisplayName = "Enrolled Label",
			AccessTokenId = Guid.NewGuid(),
			Origin = PluginSessionOrigin.SelfRegistered
		};
		var request = Request() with { DeclaredName = "   " };

		var result = await _service.Create(identity, request);

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.True);
			Assert.That(_registry.Snapshot().Single().DisplayName, Is.EqualTo("Enrolled Label"));
		});
	}

	[Test]
	public async Task A_declared_name_of_exactly_128_characters_is_accepted()
	{
		var identity = Identity() with { AccessTokenId = Guid.NewGuid(), Origin = PluginSessionOrigin.SelfRegistered };
		var name = new string('9', 128);
		var request = Request() with { DeclaredName = name };

		var result = await _service.Create(identity, request);

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.True);
			Assert.That(_registry.Snapshot().Single().DisplayName, Is.EqualTo(name));
		});
	}

	private static PluginSdkUsage Sdk(
		string version = "1.0.0",
		IReadOnlyList<string>? deprecatedApis = null)
		=> new() { SdkVersion = version, DeprecatedApis = deprecatedApis };

	[Test]
	public async Task The_session_response_carries_a_compatibility_report()
	{
		var result = await _service.Create(Identity(), Request() with { Sdk = Sdk() });

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.True);
			Assert.That(result.Response!.Compatibility, Is.Not.Null);
			Assert.That(result.Response.Compatibility!.NegotiatedProtocolVersion,
				Is.EqualTo(result.Response.NegotiatedVersion));
			Assert.That(result.Response.Compatibility.SdkVersion, Is.EqualTo("1.0.0"));
			Assert.That(PluginCompatibilityStates.IsKnown(result.Response.Compatibility.State), Is.True);
		});
	}

	[Test]
	public async Task A_plugin_that_reports_no_sdk_block_still_negotiates_normally()
	{
		// The SDK block is additive: every plugin built before it exists must keep working, and its
		// compatibility is reported as unknown rather than guessed at.
		var result = await _service.Create(Identity(), Request());

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.True);
			Assert.That(result.Response!.Compatibility, Is.Not.Null);
			Assert.That(result.Response.Compatibility!.UsageSource,
				Is.EqualTo(CompatibilityFindingSources.Unknown));
			Assert.That(result.Response.Compatibility.SdkVersion, Is.Null);
		});
	}

	[Test]
	public async Task A_plugin_that_cannot_negotiate_a_protocol_version_is_recorded_as_incompatible()
	{
		var request = Request() with
		{
			RequestedVersion = new ProtocolVersionRange { Minimum = 99, Maximum = 100 },
			Sdk = Sdk()
		};

		var result = await _service.Create(Identity(), request);

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.False);
			Assert.That(_compatibility.Find("com.example.plugin")!.Report.State,
				Is.EqualTo(PluginCompatibilityStates.Incompatible));
		});
	}

	[Test]
	public async Task An_absurdly_long_sdk_version_is_rejected()
	{
		var request = Request() with
		{
			Sdk = Sdk(version: new string('9', ProtocolLimits.MaxSdkVersionLength + 1))
		};

		var result = await _service.Create(Identity(), request);

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginSessionCreationError.SdkVersionTooLong));
		});
	}

	[Test]
	public async Task Too_many_reported_deprecated_apis_are_rejected()
	{
		var apis = Enumerable.Range(0, ProtocolLimits.MaxReportedDeprecatedApis + 1)
			.Select(i => $"M:MacroDeck.Sdk.Example.Api{i}")
			.ToList();

		var result = await _service.Create(Identity(), Request() with { Sdk = Sdk(deprecatedApis: apis) });

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginSessionCreationError.TooManyReportedDeprecatedApis));
		});
	}

	[Test]
	public async Task An_absurdly_long_deprecated_api_id_is_rejected()
	{
		var apis = new[] { new string('M', ProtocolLimits.MaxDeprecatedApiIdLength + 1) };

		var result = await _service.Create(Identity(), Request() with { Sdk = Sdk(deprecatedApis: apis) });

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginSessionCreationError.DeprecatedApiIdTooLong));
		});
	}

	[Test]
	public async Task An_sdk_block_exactly_at_the_limits_is_accepted()
	{
		var apis = Enumerable.Range(0, ProtocolLimits.MaxReportedDeprecatedApis)
			.Select(_ => new string('M', ProtocolLimits.MaxDeprecatedApiIdLength))
			.ToList();
		var request = Request() with
		{
			Sdk = Sdk(new string('9', ProtocolLimits.MaxSdkVersionLength), apis)
		};

		var result = await _service.Create(Identity(), request);

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.True);
			Assert.That(result.Response!.Compatibility, Is.Not.Null);
		});
	}

	[Test]
	public async Task An_empty_reported_usage_manifest_is_accepted_and_confirmed()
	{
		var result = await _service.Create(Identity(),
			Request() with { Sdk = Sdk(deprecatedApis: _noDeprecatedApis) });

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.True);
			Assert.That(result.Response!.Compatibility!.UsageSource,
				Is.EqualTo(CompatibilityFindingSources.Confirmed));
		});
	}

	[Test]
	public async Task Too_Many_Declared_Capabilities_Is_Rejected()
	{
		var capabilities = Enumerable.Range(0, ProtocolLimits.MaxDeclaredCapabilities + 1)
			.Select(i => new DeclaredCapability
			{
				Kind = CapabilityKinds.Actions,
				LocalId = $"cap-{i}",
				VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
			})
			.ToList();

		var result = await _service.Create(Identity(), Request(capabilities));

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginSessionCreationError.TooManyDeclaredCapabilities));
		});
	}
}
