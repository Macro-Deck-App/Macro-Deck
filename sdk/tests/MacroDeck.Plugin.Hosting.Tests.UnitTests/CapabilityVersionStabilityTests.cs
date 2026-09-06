using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Profiles;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Weather;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

/// <summary>
/// Pins the version range each capability kind declares against hard-coded literals, the way
/// <c>CapabilityOperationStabilityTests</c> pins the operation vocabulary.
///
/// <para>
/// A capability major says what a kind's payloads mean, and a host reads it to decide whether it may send
/// an operation at all. Every kind started at <c>{1,1}</c>, so a range that quietly fell back there would
/// be a silent claim to speak a shape this SDK no longer serves - no build error, no wire error, just a
/// host addressing a plugin in a vocabulary the two no longer share. Changing one of these literals is
/// therefore a decision a reviewer has to make on purpose.
/// </para>
/// </summary>
[TestFixture]
public class CapabilityVersionStabilityTests
{
	private static readonly IReadOnlyDictionary<string, (int Minimum, int Maximum)> _expected
		= new Dictionary<string, (int, int)>(StringComparer.Ordinal)
		{
			// ADR 0081 merged the retired dynamic-variables kind into this one and reshaped every
			// variable payload around attributes and a write capability.
			[CapabilityKinds.Variables] = (2, 2),

			// ADR 0081 removed the slider role from an action descriptor; version 2 was the localized
			// descriptor shape.
			[CapabilityKinds.Actions] = (3, 3),

			// Version 2 introduced session.open - see RemoteDeviceProviderRegistry, which refuses to serve
			// a provider that negotiated 1.
			[CapabilityKinds.DeviceProvider] = (1, 2),

			[CapabilityKinds.Events] = (1, 1),
			[CapabilityKinds.Icons] = (1, 1),
			[CapabilityKinds.ConfigFlow] = (1, 1),
			[CapabilityKinds.MusicPlayer] = (1, 1),
			[CapabilityKinds.Weather] = (1, 1),
			[CapabilityKinds.VirtualProfiles] = (1, 1),
			[CapabilityKinds.Issues] = (1, 1),
			[CapabilityKinds.Ui] = (1, 1),
			[CapabilityKinds.Localization] = (1, 1),
			[CapabilityKinds.LayoutProvider] = (1, 1),
			[CapabilityKinds.FolderViewProvider] = (1, 1),
			[CapabilityKinds.Migration] = (1, 1),
			[CapabilityKinds.WidgetTypeProvider] = (1, 1),
		};

	/// <summary>The kinds the fixture below actually gets a declaration out of, listed so the assertion
	/// cannot pass vacuously if the fixture stops declaring one.</summary>
	private static readonly string[] _declaredByTheFixture =
	[
		CapabilityKinds.Actions,
		CapabilityKinds.Variables,
		CapabilityKinds.Events,
		CapabilityKinds.ConfigFlow,
		CapabilityKinds.MusicPlayer,
		CapabilityKinds.Weather,
		CapabilityKinds.VirtualProfiles,
		CapabilityKinds.Issues,
	];

	[Test]
	public void Every_capability_kind_has_a_pinned_version_range()
		=> Assert.That(_expected.Keys, Is.EquivalentTo(CapabilityKinds.All));

	[Test]
	public void Every_declared_capability_carries_the_pinned_range_for_its_kind()
	{
		var declared = Declare();

		Assert.That(declared.Keys, Is.SupersetOf(_declaredByTheFixture));
		Assert.Multiple(() =>
		{
			foreach (var (kind, ranges) in declared)
			{
				Assert.That(ranges, Has.Count.EqualTo(1), $"'{kind}' declares more than one version range");
				Assert.That((ranges[0].Minimum, ranges[0].Maximum),
					Is.EqualTo(_expected[kind]),
					$"version range for '{kind}'");
			}
		});
	}

	private static Dictionary<string, List<CapabilityVersionRange>> Declare()
	{
		using var plugin = MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration(_ => new TestIntegration(new TestAction("do-something")))
			.RegisterIntegration(_ => new TestVariableIntegration([
				VariableDefinition.Eager("temperature", VariableType.Numeric)
			]))
			.RegisterIntegration(_ => new TestEventIntegration("Events",
				new EventDefinition { Id = "happened", Name = "Happened" }))
			.RegisterIntegration(_ => new TestConfigFlowIntegration(() => new TestConfigFlow()))
			.RegisterIntegration(_ => new TestMusicPlayerIntegration("Player",
				new Dictionary<string, IMusicPlayer>(StringComparer.Ordinal) { ["one"] = new TestMusicPlayer() }))
			.RegisterIntegration(_ => new TestWeatherIntegration("Weather",
				new Dictionary<string, IWeatherStation>(StringComparer.Ordinal)
				{
					["home"] = new TestWeatherStation()
				}))
			.RegisterIntegration(_ => new TestProfileIntegration("Profiles",
				[new VirtualProfileDescriptor("p", "Profile", ProfileLayout.Grid(1, 1), [])]))
			.RegisterIntegration(_ => new TestIssueIntegration(() =>
				[new IntegrationIssue { Id = "i", Title = "Issue" }]))
			.Build();

		var declared = new Dictionary<string, List<CapabilityVersionRange>>(StringComparer.Ordinal);
		foreach (var capability in plugin.Services.GetServices<ICapabilityHandler>()
			.SelectMany(handler => handler.DeclareCapabilities()))
		{
			if (!declared.TryGetValue(capability.Kind, out var ranges))
			{
				ranges = [];
				declared[capability.Kind] = ranges;
			}

			if (!ranges.Contains(capability.VersionRange))
			{
				ranges.Add(capability.VersionRange);
			}
		}

		return declared;
	}
}
