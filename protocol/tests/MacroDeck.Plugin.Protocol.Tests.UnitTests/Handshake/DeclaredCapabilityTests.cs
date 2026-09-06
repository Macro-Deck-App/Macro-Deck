using System.Reflection;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Handshake;

[TestFixture]
public class DeclaredCapabilityTests
{
	/// <summary>
	/// ADR 0004's declared/registered/available split means availability is the host's to decide,
	/// never the plugin's to assert. Checked by reflection over every public member so a future edit
	/// cannot smuggle an availability-shaped field back in under a different name.
	/// </summary>
	[Test]
	public void Declared_capability_has_no_availability_field()
	{
		var propertyNames = typeof(DeclaredCapability)
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Select(property => property.Name)
			.Concat(typeof(DeclaredCapability).GetFields(BindingFlags.Public | BindingFlags.Instance)
				.Select(field => field.Name))
			.ToList();

		Assert.That(propertyNames, Is.Not.Empty);
		Assert.Multiple(() =>
		{
			foreach (var name in propertyNames)
			{
				Assert.That(name, Does.Not.Contain("Available"), $"'{name}' looks like an availability field.");
				Assert.That(name, Does.Not.Contain("Availability"), $"'{name}' looks like an availability field.");
			}
		});
	}

	[Test]
	public void Kind_local_id_and_version_range_are_required_display_name_is_optional()
	{
		var capability = new DeclaredCapability
		{
			Kind = CapabilityKinds.Actions,
			LocalId = "set-volume",
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 },
		};

		Assert.Multiple(() =>
		{
			Assert.That(capability.Kind, Is.EqualTo(CapabilityKinds.Actions));
			Assert.That(capability.LocalId, Is.EqualTo("set-volume"));
			Assert.That(capability.DisplayName, Is.Null);
		});
	}

	[Test]
	public void Round_trips_through_the_wire_serializer()
	{
		var capability = new DeclaredCapability
		{
			Kind = CapabilityKinds.Weather,
			LocalId = "current-conditions",
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 2 },
			DisplayName = "Current Conditions",
		};

		var json = JsonSerializer.Serialize(capability, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<DeclaredCapability>(json, PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.Kind, Is.EqualTo(capability.Kind));
			Assert.That(actual.LocalId, Is.EqualTo(capability.LocalId));
			Assert.That(actual.VersionRange.Minimum, Is.EqualTo(capability.VersionRange.Minimum));
			Assert.That(actual.VersionRange.Maximum, Is.EqualTo(capability.VersionRange.Maximum));
			Assert.That(actual.DisplayName, Is.EqualTo(capability.DisplayName));
		});
	}
}
