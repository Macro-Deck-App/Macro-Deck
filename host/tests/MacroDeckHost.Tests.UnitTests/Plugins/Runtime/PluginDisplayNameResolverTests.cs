using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Runtime;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Runtime;

[TestFixture]
internal sealed class PluginDisplayNameResolverTests
{
	private const string PluginId = "com.example.plugin";

	private static PluginSessionSnapshot Session(string displayName, Guid? accessTokenId, string? declaredName = null)
		=> new()
		{
			SessionId = "session-1",
			PluginId = PluginId,
			DisplayName = displayName,
			AccessTokenId = accessTokenId,
			Origin = accessTokenId is null ? PluginSessionOrigin.Managed : PluginSessionOrigin.SelfRegistered,
			State = PluginSessionState.Connected,
			NegotiatedVersion = 1,
			DeclaredName = declaredName,
			CreatedAt = DateTimeOffset.UnixEpoch
		};

	[Test]
	public void A_launch_backed_sessions_declared_name_is_ignored()
	{
		var session = Session("Weather Widget", accessTokenId: null, declaredName: "Totally Legit Bank");

		var name = PluginDisplayNameResolver.Resolve(session, manifestName: "Sample", PluginId);

		Assert.That(name, Is.EqualTo("Weather Widget"));
	}

	[Test]
	public void An_enrolled_sessions_declared_name_wins()
	{
		var session = Session("Enrolled Label", accessTokenId: Guid.NewGuid(), declaredName: "Weather Widget");

		var name = PluginDisplayNameResolver.Resolve(session, manifestName: "Sample", PluginId);

		Assert.That(name, Is.EqualTo("Weather Widget"));
	}

	[Test]
	public void An_enrolled_session_with_no_declared_name_falls_back_to_its_enrollment_label()
	{
		var session = Session("Enrolled Label", accessTokenId: Guid.NewGuid(), declaredName: null);

		var name = PluginDisplayNameResolver.Resolve(session, manifestName: "Sample", PluginId);

		Assert.That(name, Is.EqualTo("Enrolled Label"));
	}

	[Test]
	public void An_enrolled_session_with_a_whitespace_only_declared_name_falls_back_to_its_enrollment_label()
	{
		var session = Session("Enrolled Label", accessTokenId: Guid.NewGuid(), declaredName: "   ");

		var name = PluginDisplayNameResolver.Resolve(session, manifestName: "Sample", PluginId);

		Assert.That(name, Is.EqualTo("Enrolled Label"));
	}

	[Test]
	public void With_no_session_the_manifest_name_wins()
	{
		var name = PluginDisplayNameResolver.Resolve(session: null, manifestName: "Sample", PluginId);

		Assert.That(name, Is.EqualTo("Sample"));
	}

	[Test]
	public void With_neither_a_session_nor_a_manifest_name_the_plugin_id_is_the_last_resort()
	{
		var name = PluginDisplayNameResolver.Resolve(session: null, manifestName: null, PluginId);

		Assert.That(name, Is.EqualTo(PluginId));
	}


	private static PluginSessionIdentity Identity(string displayName, Guid? accessTokenId)
		=> new()
		{
			PluginId = PluginId,
			DisplayName = displayName,
			AccessTokenId = accessTokenId,
			Origin = accessTokenId is null ? PluginSessionOrigin.Managed : PluginSessionOrigin.SelfRegistered
		};

	[Test]
	public void The_identity_overload_ignores_a_declared_name_for_a_launch_backed_identity()
	{
		var identity = Identity("Weather Widget", accessTokenId: null);

		var name = PluginDisplayNameResolver.Resolve(identity, declaredName: "Totally Legit Bank");

		Assert.That(name, Is.EqualTo("Weather Widget"));
	}

	[Test]
	public void The_identity_overload_prefers_a_declared_name_for_an_enrolled_identity()
	{
		var identity = Identity("Enrolled Label", accessTokenId: Guid.NewGuid());

		var name = PluginDisplayNameResolver.Resolve(identity, declaredName: "Weather Widget");

		Assert.That(name, Is.EqualTo("Weather Widget"));
	}

	[Test]
	public void The_identity_overload_falls_back_to_the_enrollment_label_when_nothing_was_declared()
	{
		var identity = Identity("Enrolled Label", accessTokenId: Guid.NewGuid());

		var name = PluginDisplayNameResolver.Resolve(identity, declaredName: null);

		Assert.That(name, Is.EqualTo("Enrolled Label"));
	}

	[Test]
	public void The_identity_overload_falls_back_to_the_enrollment_label_for_a_whitespace_only_declared_name()
	{
		var identity = Identity("Enrolled Label", accessTokenId: Guid.NewGuid());

		var name = PluginDisplayNameResolver.Resolve(identity, declaredName: "   ");

		Assert.That(name, Is.EqualTo("Enrolled Label"));
	}
}
