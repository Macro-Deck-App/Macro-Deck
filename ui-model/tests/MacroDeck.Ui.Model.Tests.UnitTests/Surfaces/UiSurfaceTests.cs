using System.Reflection;
using System.Text.Json;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeck.Ui.Model.Tests.UnitTests.Surfaces;

/// <summary>The acceptance criterion: shared and exclusive are both expressible, and the surface
/// vocabulary stays open.</summary>
[TestFixture]
public class UiSurfaceTests
{
	[Test]
	public void Session_mode_constants_carry_their_frozen_wire_values()
	{
		Assert.Multiple(() =>
		{
			Assert.That(UiSessionModes.Shared, Is.EqualTo("shared"));
			Assert.That(UiSessionModes.Exclusive, Is.EqualTo("exclusive"));
		});
	}

	[Test]
	public void A_shared_and_an_exclusive_surface_both_serialize_canonically()
	{
		var shared = new UiSurface { Kind = "config", SessionMode = "shared" };
		var exclusive = new UiSurface { Kind = "config", SessionMode = "exclusive" };

		Assert.Multiple(() =>
		{
			Assert.That(UiCanonicalJson.Serialize(shared),
				Is.EqualTo("""{"kind":"config","sessionMode":"shared","attributes":{}}"""));
			Assert.That(UiCanonicalJson.Serialize(exclusive),
				Is.EqualTo("""{"kind":"config","sessionMode":"exclusive","attributes":{}}"""));
		});
	}

	[Test]
	public void Unknown_surface_kinds_and_session_modes_are_non_fatal_and_verbatim()
	{
		const string json = """{"kind":"hologram","sessionMode":"cooperative","attributes":{"a":1}}""";

		UiSurface? surface = null;
		Assert.DoesNotThrow(() => surface = JsonSerializer.Deserialize<UiSurface>(json, UiCanonicalJson.Options));

		Assert.Multiple(() =>
		{
			Assert.That(surface!.Kind, Is.EqualTo("hologram"));
			Assert.That(surface.SessionMode, Is.EqualTo("cooperative"));
		});
	}

	[Test]
	public void Surface_vocabulary_types_expose_no_closed_world_predicate()
	{
		string[] closedWorldNames = ["IsKnown", "All", "Parse", "TryParse", "Validate"];

		Assert.Multiple(() =>
		{
			AssertNoClosedWorldMembers(typeof(UiSurfaceKinds), closedWorldNames);
			AssertNoClosedWorldMembers(typeof(UiSessionModes), closedWorldNames);
			Assert.That(HasMember(typeof(UiPatchOperations), "IsKnown"),
				Is.True,
				"UiPatchOperations is a closed, frozen operation set and must expose IsKnown.");
			Assert.That(HasMember(typeof(UiPatchOperations), "All"), Is.True);
		});
	}

	private static void AssertNoClosedWorldMembers(Type type, IEnumerable<string> names)
	{
		foreach (var name in names)
		{
			Assert.That(HasMember(type, name), Is.False, $"{type.Name} must not expose {name}.");
		}
	}

	private static bool HasMember(Type type, string name)
		=> type.GetMember(name, BindingFlags.Public | BindingFlags.Static).Length > 0;
}
