using MacroDeckHost.Integrations.Spotify;
using SpotifyAPI.Web;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

[TestFixture]
internal sealed class SpotifyScopesTests
{
	private const string OldSixScopeGrant =
		"user-read-playback-state user-modify-playback-state user-read-currently-playing " +
		"user-read-playback-position playlist-read-private user-library-read";

	[Test]
	public void Missing_NullGrant_ReturnsEveryRequiredScope()
	{
		Assert.That(SpotifyScopes.Missing(null), Is.EquivalentTo(SpotifyScopes.Required));
	}

	[Test]
	public void Missing_EmptyGrant_ReturnsEveryRequiredScope()
	{
		Assert.That(SpotifyScopes.Missing(""), Is.EquivalentTo(SpotifyScopes.Required));
	}

	[Test]
	public void Missing_WhitespaceGrant_ReturnsEveryRequiredScope()
	{
		Assert.That(SpotifyScopes.Missing("   "), Is.EquivalentTo(SpotifyScopes.Required));
	}

	[Test]
	public void Missing_OldSixScopeGrant_ReturnsExactlyTheFourNewScopes()
	{
		var missing = SpotifyScopes.Missing(OldSixScopeGrant);

		Assert.That(missing,
			Is.EquivalentTo(new[]
			{
				Scopes.UserLibraryModify, Scopes.PlaylistModifyPrivate, Scopes.PlaylistModifyPublic, Scopes.UserTopRead
			}));
	}

	[Test]
	public void Missing_FullGrant_ReturnsNothing()
	{
		Assert.That(SpotifyScopes.Missing(string.Join(' ', SpotifyScopes.Required)), Is.Empty);
	}

	[Test]
	public void Missing_UnknownExtraScopes_AreIgnored()
	{
		var grant = string.Join(' ', SpotifyScopes.Required) + " some-unknown-scope another-one";

		Assert.That(SpotifyScopes.Missing(grant), Is.Empty);
	}

	[Test]
	public void Missing_OddSpacing_IsStillParsedCorrectly()
	{
		var grant = "  " + string.Join("   ", SpotifyScopes.Required) + "  ";

		Assert.That(SpotifyScopes.Missing(grant), Is.Empty);
	}

	[Test]
	public void Required_HasNoDuplicates()
	{
		Assert.That(SpotifyScopes.Required.Distinct(StringComparer.Ordinal).Count(),
			Is.EqualTo(SpotifyScopes.Required.Count));
	}
}
