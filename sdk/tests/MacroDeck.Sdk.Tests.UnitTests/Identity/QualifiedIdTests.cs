using MacroDeck.Sdk.Identity;

namespace MacroDeck.Sdk.Tests.UnitTests.Identity;

[TestFixture]
public class QualifiedIdTests
{
	[TestCase("app.macro-deck.spotify")]
	[TestCase("com.suchbyte.test-plugin")]
	public void Reverse_domain_owner_ids_are_valid_packages(string ownerId)
		=> Assert.That(MacroDeckId.IsValidOwnerId(ownerId, OwnerIdKind.Package), Is.True);

	[TestCase("time")]
	[TestCase("macro-deck")]
	[TestCase("music-player")]
	public void Single_segment_owner_ids_are_valid_host_providers_but_not_packages(string ownerId)
	{
		Assert.Multiple(() =>
		{
			Assert.That(MacroDeckId.IsValidOwnerId(ownerId, OwnerIdKind.HostProvider), Is.True);
			Assert.That(MacroDeckId.IsValidOwnerId(ownerId, OwnerIdKind.Package), Is.False);
		});
	}

	[TestCase("App.Macro-Deck.Spotify")]
	[TestCase("a b")]
	[TestCase("app..spotify")]
	[TestCase("-leading")]
	[TestCase("1first")]
	[TestCase("")]
	[TestCase("a::b")]
	public void Malformed_owner_ids_are_invalid_for_either_kind(string ownerId)
	{
		Assert.Multiple(() =>
		{
			Assert.That(MacroDeckId.IsValidOwnerId(ownerId, OwnerIdKind.Package), Is.False);
			Assert.That(MacroDeckId.IsValidOwnerId(ownerId, OwnerIdKind.HostProvider), Is.False);
		});
	}

	[TestCase("set-volume")]
	[TestCase("track-changed")]
	[TestCase("play")]
	public void Kebab_case_local_ids_are_valid_declared_ids(string localId)
		=> Assert.That(MacroDeckId.IsValidLocalId(localId, LocalIdKind.Declared), Is.True);

	[Test]
	public void A_guid_is_invalid_as_declared_but_valid_as_resource()
	{
		const string guid = "0f8fad5b-d9cb-469f-a165-70867728950e";

		Assert.Multiple(() =>
		{
			Assert.That(MacroDeckId.IsValidLocalId(guid, LocalIdKind.Declared), Is.False);
			Assert.That(MacroDeckId.IsValidLocalId(guid, LocalIdKind.Resource), Is.True);
		});
	}

	[TestCase("Set-Volume")]
	[TestCase("set_volume")]
	[TestCase("set volume")]
	[TestCase("")]
	public void Malformed_local_ids_are_invalid_as_declared(string localId)
		=> Assert.That(MacroDeckId.IsValidLocalId(localId, LocalIdKind.Declared), Is.False);

	[Test]
	public void A_local_id_containing_the_separator_is_invalid_as_either_kind()
	{
		Assert.Multiple(() =>
		{
			Assert.That(MacroDeckId.IsValidLocalId("a::b", LocalIdKind.Declared), Is.False);
			Assert.That(MacroDeckId.IsValidLocalId("a::b", LocalIdKind.Resource), Is.False);
		});
	}

	[Test]
	public void Create_serializes_as_owner_separator_local()
	{
		var id = QualifiedId.Create("app.macro-deck.obs", "scene-changed");

		Assert.That(id.ToString(), Is.EqualTo("app.macro-deck.obs::scene-changed"));
	}

	[Test]
	public void Create_throws_on_an_invalid_local_id_and_the_exception_names_the_declaration()
	{
		var exception = Assert.Throws<MacroDeckIdException>(() =>
			QualifiedId.Create("app.macro-deck.obs", "Not Kebab", capabilityType: "Action"));

		Assert.Multiple(() =>
		{
			Assert.That(exception.OwnerId, Is.EqualTo("app.macro-deck.obs"));
			Assert.That(exception.CapabilityType, Is.EqualTo("Action"));
			Assert.That(exception.LocalId, Is.EqualTo("Not Kebab"));
		});
	}

	[Test]
	public void Parse_and_TryParse_round_trip_the_two_halves()
	{
		var parsed = QualifiedId.Parse("app.macro-deck.obs::scene-changed");

		Assert.Multiple(() =>
		{
			Assert.That(parsed.OwnerId, Is.EqualTo("app.macro-deck.obs"));
			Assert.That(parsed.LocalId, Is.EqualTo("scene-changed"));
		});

		Assert.That(QualifiedId.TryParse("app.macro-deck.obs::scene-changed", out var tryParsed), Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(tryParsed.OwnerId, Is.EqualTo("app.macro-deck.obs"));
			Assert.That(tryParsed.LocalId, Is.EqualTo("scene-changed"));
		});
	}

	[TestCase("a.b::c::d")]
	[TestCase("::x")]
	[TestCase("x::")]
	[TestCase("no-separator")]
	[TestCase(null)]
	[TestCase("")]
	public void TryParse_rejects_a_nested_missing_or_bare_id(string? value)
		=> Assert.That(QualifiedId.TryParse(value, out _), Is.False);

	[TestCase("a.b::c::d")]
	[TestCase("::x")]
	[TestCase("x::")]
	[TestCase("no-separator")]
	[TestCase(null)]
	[TestCase("")]
	[TestCase("app.macro-deck.obs::scene-changed")]
	public void IsQualified_agrees_with_TryParse(string? value)
		=> Assert.That(QualifiedId.IsQualified(value), Is.EqualTo(QualifiedId.TryParse(value, out _)));

	/// <summary>
	/// Regression: the patterns were anchored with <c>^</c>/<c>$</c>, and in .NET <c>$</c> also matches
	/// before a trailing newline. That let "play\n" validate as a declared id while every lookup for
	/// "play" missed it - the integration would register and then resolve nothing.
	/// </summary>
	[TestCase("play\n")]
	[TestCase("play\r\n")]
	public void A_declared_id_with_a_trailing_newline_is_rejected(string localId)
		=> Assert.That(MacroDeckId.IsValidLocalId(localId, LocalIdKind.Declared), Is.False);

	[TestCase("app.macro-deck.spotify\n")]
	[TestCase("time\n")]
	public void An_owner_id_with_a_trailing_newline_is_rejected(string ownerId)
		=> Assert.That(MacroDeckId.IsValidOwnerId(ownerId), Is.False);

	[Test]
	public void Ids_built_from_the_same_parts_are_equal_and_share_a_hash_code()
	{
		var first = QualifiedId.Create("app.macro-deck.obs", "scene-changed");
		var second = QualifiedId.Create("app.macro-deck.obs", "scene-changed");

		Assert.Multiple(() =>
		{
			Assert.That(first, Is.EqualTo(second));
			Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
		});
	}

	[Test]
	public void Ids_differing_in_either_half_are_not_equal()
	{
		var baseline = QualifiedId.Create("app.macro-deck.obs", "scene-changed");
		var differentOwner = QualifiedId.Create("app.macro-deck.spotify", "scene-changed");
		var differentLocal = QualifiedId.Create("app.macro-deck.obs", "stream-started");

		Assert.Multiple(() =>
		{
			Assert.That(baseline, Is.Not.EqualTo(differentOwner));
			Assert.That(baseline, Is.Not.EqualTo(differentLocal));
		});
	}

	[Test]
	public void Default_is_empty_and_stringifies_to_empty_rather_than_throwing()
	{
		var id = default(QualifiedId);

		Assert.Multiple(() =>
		{
			Assert.That(id.IsEmpty, Is.True);
			Assert.That(id.ToString(), Is.Empty);
		});
	}
}
