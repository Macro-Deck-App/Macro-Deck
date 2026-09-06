using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Compatibility;
using MacroDeckHost.Application.Plugins.Logging;
using MacroDeckHost.Tests.UnitTests.Auth;
using Microsoft.Extensions.Logging.Abstractions;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class PluginTokenServiceTests
{
	private ManualTimeProvider _time = null!;
	private InMemoryPluginAccessTokenRepository _tokens = null!;
	private InMemoryPluginRegistrationRepository _registrations = null!;
	private PluginSessionRegistry _sessionRegistry = null!;
	private PluginCompatibilityService _compatibility = null!;
	private PluginTokenService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new ManualTimeProvider();
		_tokens = new InMemoryPluginAccessTokenRepository();
		_registrations = new InMemoryPluginRegistrationRepository();
		_sessionRegistry = new PluginSessionRegistry(_time, Serilog.Core.Logger.None);
		_compatibility = new PluginCompatibilityService(NullLogger<PluginCompatibilityService>.Instance);
		var logRateLimiter = new PluginLogRateLimiter(_time);
		_service = new PluginTokenService(_tokens,
			_registrations,
			_sessionRegistry,
			new PluginIdentityForgetter(_compatibility,
				logRateLimiter,
				new PluginLogIngestor(logRateLimiter, _sessionRegistry, new FakePluginSupervisor(), _time)),
			_time);
	}

	[Test]
	public async Task Create_Returns_A_Plaintext_And_Stores_Only_A_Hash()
	{
		var created = await _service.Create("My Token", null);

		var stored = _tokens.Tokens.Single();

		Assert.Multiple(() =>
		{
			Assert.That(created.PlaintextToken, Is.Not.Empty);
			Assert.That(stored.TokenHash, Is.Not.EqualTo(created.PlaintextToken));
			Assert.That(TokenHasher.Verify(created.PlaintextToken, stored.TokenHash), Is.True);
		});
	}

	[Test]
	public async Task Listing_Never_Exposes_A_Plaintext()
	{
		var created = await _service.Create("My Token", null);

		var all = await _service.GetAll();

		Assert.That(all.Single().Id, Is.EqualTo(created.Token.Id));

		Assert.That(typeof(PluginAccessToken).GetProperty("TokenHash"), Is.Null);
		Assert.That(typeof(PluginAccessToken).GetProperty("PlaintextToken"), Is.Null);
	}

	[Test]
	public async Task Revoke_Cascades_Registrations_And_Terminates_Sessions()
	{
		var created = await _service.Create("My Token", null);
		_registrations.Registrations.Add(new Domain.Entities.PluginRegistrationEntity
		{
			Id = Guid.NewGuid(),
			PluginId = "com.example.plugin",
			DisplayName = "Example",
			SecretHash = "hash",
			AccessTokenId = created.Token.Id,
			Origin = PluginRegistrationOrigins.DeveloperToken,
			CreatedAt = _time.GetUtcNow().UtcDateTime
		});

		var connection = new FakePluginConnection();
		await _sessionRegistry.Create(new PluginSessionRecord
		{
			SessionId = Guid.CreateVersion7().ToString("D"),
			PluginId = "com.example.plugin",
			DisplayName = "Example",
			AccessTokenId = created.Token.Id,
			Origin = PluginSessionOrigin.SelfRegistered,
			NegotiatedVersion = 1,
			Capabilities = new Dictionary<string, MacroDeck.Plugin.Protocol.Versioning.CapabilityNegotiationResult>(),
			DeclaredCapabilities = [],
			State = PluginSessionState.Connected,
			CreatedAt = _time.GetUtcNow(),
			Connection = connection
		});

		var terminated = await _service.Revoke(created.Token.Id);

		Assert.Multiple(() =>
		{
			Assert.That(terminated, Is.EqualTo(1));
			Assert.That(_tokens.Tokens.Single().RevokedAt, Is.Not.Null);
			Assert.That(_registrations.Registrations.Single().RevokedAt, Is.Not.Null);
			Assert.That(connection.Closes, Has.Count.EqualTo(1));
			Assert.That(_sessionRegistry.Snapshot(), Is.Empty);
		});
	}

	[Test]
	public async Task Revoking_A_Token_Forgets_Every_Plugin_It_Enrolled_And_No_Other()
	{
		var revoked = await _service.Create("Revoked Token", null);
		var kept = await _service.Create("Kept Token", null);
		SeedRegistration("com.example.connected", revoked.Token.Id);
		SeedRegistration("com.example.offline", revoked.Token.Id);
		SeedRegistration("com.example.other-token", kept.Token.Id);
		RecordCompatibility("com.example.connected");
		RecordCompatibility("com.example.offline");
		RecordCompatibility("com.example.other-token");

		var terminated = await _service.Revoke(revoked.Token.Id);

		Assert.Multiple(() =>
		{
			Assert.That(_compatibility.Find("com.example.connected"), Is.Null);
			Assert.That(_compatibility.Find("com.example.offline"), Is.Null);
			Assert.That(_compatibility.Find("com.example.other-token"), Is.Not.Null);

			Assert.That(terminated, Is.EqualTo(0));
		});
	}

	private void SeedRegistration(string pluginId, Guid accessTokenId)
		=> _registrations.Registrations.Add(new Domain.Entities.PluginRegistrationEntity
		{
			Id = Guid.NewGuid(),
			PluginId = pluginId,
			DisplayName = pluginId,
			SecretHash = "hash",
			AccessTokenId = accessTokenId,
			Origin = PluginRegistrationOrigins.DeveloperToken,
			CreatedAt = _time.GetUtcNow().UtcDateTime
		});

	private void RecordCompatibility(string pluginId) => _compatibility.Record(new PluginCompatibilityEvaluation
	{
		PluginId = pluginId,
		DisplayName = pluginId,
		NegotiatedProtocolVersion = 1
	});

	[Test]
	public async Task A_Revoked_Token_Cannot_Authenticate()
	{
		var created = await _service.Create("My Token", null);
		await _service.Revoke(created.Token.Id);

		var authenticated = await _service.Authenticate(created.PlaintextToken);

		Assert.That(authenticated, Is.Null);
	}

	[Test]
	public async Task An_Expired_Token_Cannot_Authenticate()
	{
		var created = await _service.Create("My Token", 1);

		_time.Advance(TimeSpan.FromDays(2));

		var authenticated = await _service.Authenticate(created.PlaintextToken);

		Assert.That(authenticated, Is.Null);
	}

	[Test]
	public async Task An_Expired_But_Unrevoked_Token_Is_Refused_Removal_And_Survives()
	{
		var created = await _service.Create("My Token", 1);
		_time.Advance(TimeSpan.FromDays(2));

		var result = await _service.Delete(created.Token.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(PluginAccessTokenDeleteResult.NotRevoked));
			Assert.That(_tokens.Tokens.Single().Id, Is.EqualTo(created.Token.Id));
		});
	}

	[Test]
	public async Task A_Valid_Token_Authenticates()
	{
		var created = await _service.Create("My Token", null);

		var authenticated = await _service.Authenticate(created.PlaintextToken);

		Assert.That(authenticated?.Id, Is.EqualTo(created.Token.Id));
	}
}
