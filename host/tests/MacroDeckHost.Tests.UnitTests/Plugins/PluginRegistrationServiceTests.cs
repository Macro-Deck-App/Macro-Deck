using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Compatibility;
using MacroDeckHost.Application.Plugins.Logging;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Tests.UnitTests.Auth;
using Microsoft.Extensions.Logging.Abstractions;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class PluginRegistrationServiceTests
{
	private ManualTimeProvider _time = null!;
	private InMemoryPluginRegistrationRepository _registrations = null!;
	private InMemoryPluginAccessTokenRepository _tokens = null!;
	private PluginSessionRegistry _sessionRegistry = null!;
	private FakePluginInstallationCatalog _catalog = null!;
	private PluginRegistrationService _service = null!;
	private Guid _accessTokenId;

	[SetUp]
	public void SetUp()
	{
		_time = new ManualTimeProvider();
		_registrations = new InMemoryPluginRegistrationRepository();
		_tokens = new InMemoryPluginAccessTokenRepository();
		_sessionRegistry = new PluginSessionRegistry(_time, Serilog.Core.Logger.None);
		_catalog = new FakePluginInstallationCatalog();
		var logRateLimiter = new PluginLogRateLimiter(_time);
		var logIngestor = new PluginLogIngestor(logRateLimiter, _sessionRegistry, new FakePluginSupervisor(), _time);
		_service = new PluginRegistrationService(_registrations,
			_tokens,
			_sessionRegistry,
			new PluginIdentityForgetter(new PluginCompatibilityService(NullLogger<PluginCompatibilityService>.Instance),
				logRateLimiter,
				logIngestor),
			_catalog,
			_time);

		_accessTokenId = Guid.NewGuid();
		_tokens.Tokens.Add(new PluginAccessTokenEntity
		{
			Id = _accessTokenId,
			Name = "Dev Token",
			TokenHash = "irrelevant",
			Scopes = PluginTokenScopes.Enroll,
			CreatedAt = _time.GetUtcNow().UtcDateTime
		});
	}

	[Test]
	public async Task Only_A_Hash_Is_Persisted_And_The_Secret_Comes_Back_Exactly_Once()
	{
		var result = await _service.Register("com.example.plugin",
			"Example",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.True);
			Assert.That(result.PluginSecret, Is.Not.Null.And.Not.Empty);
			Assert.That(result.Registration!.SecretHash, Is.Not.EqualTo(result.PluginSecret));
			Assert.That(TokenHasher.Verify(result.PluginSecret!, result.Registration.SecretHash), Is.True);
			Assert.That(typeof(PluginRegistrationResult).GetProperties().Count(p => p.Name == "PluginSecret"),
				Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Duplicate_Plugin_Id_Is_Rejected()
	{
		await _service.Register("com.example.plugin",
			"Example",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);

		var result = await _service.Register("com.example.plugin",
			"Example again",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginRegistrationError.AlreadyRegistered));
		});
	}

	[Test]
	public async Task Malformed_Plugin_Id_Is_Rejected()
	{
		var result = await _service.Register("not-a-valid-id",
			"Example",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginRegistrationError.InvalidPluginId));
		});
	}

	[Test]
	public async Task Last_Used_Advances_On_Registration()
	{
		_time.Advance(TimeSpan.FromMinutes(5));

		await _service.Register("com.example.plugin",
			"Example",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);

		Assert.That(_tokens.Tokens.Single().LastUsedAt, Is.EqualTo(_time.GetUtcNow().UtcDateTime));
	}

	[Test]
	public async Task A_Revoked_Registration_Cannot_Authenticate()
	{
		var registered = await _service.Register("com.example.plugin",
			"Example",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);
		await _service.Revoke("com.example.plugin");

		var authenticated = await _service.Authenticate("com.example.plugin", registered.PluginSecret!);

		Assert.That(authenticated, Is.Null);
	}

	[Test]
	public async Task A_Valid_Secret_Authenticates()
	{
		var registered = await _service.Register("com.example.plugin",
			"Example",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);

		var authenticated = await _service.Authenticate("com.example.plugin", registered.PluginSecret!);

		Assert.That(authenticated?.PluginId, Is.EqualTo("com.example.plugin"));
	}

	[Test]
	public async Task A_Wrong_Secret_Fails_To_Authenticate()
	{
		await _service.Register("com.example.plugin",
			"Example",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);

		var authenticated = await _service.Authenticate("com.example.plugin", "wrong-secret");

		Assert.That(authenticated, Is.Null);
	}

	[Test]
	public async Task A_Revoked_Developer_Token_Cannot_Enrol()
	{
		_tokens.Tokens.Single().RevokedAt = _time.GetUtcNow().UtcDateTime;

		var registered = await _service.Register("com.example.plugin",
			"Example",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);

		Assert.That(registered.Succeeded, Is.True);

		var authenticated = await _service.Authenticate("com.example.plugin", registered.PluginSecret!);

		Assert.That(authenticated, Is.Null);
	}

	[Test]
	public async Task An_Expired_Developer_Token_Cannot_Back_Authentication()
	{
		_tokens.Tokens.Single().ExpiresAt = _time.GetUtcNow().UtcDateTime.AddMinutes(1);
		var registered = await _service.Register("com.example.plugin",
			"Example",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);

		_time.Advance(TimeSpan.FromMinutes(2));

		var authenticated = await _service.Authenticate("com.example.plugin", registered.PluginSecret!);

		Assert.That(authenticated, Is.Null);
	}

	[Test]
	public async Task Re_Registering_The_Same_Plugin_Id_After_Revoke_Reactivates_It()
	{
		var firstAccessTokenId = _accessTokenId;
		var secondAccessTokenId = Guid.NewGuid();
		_tokens.Tokens.Add(new PluginAccessTokenEntity
		{
			Id = secondAccessTokenId,
			Name = "Second Dev Token",
			TokenHash = "irrelevant-2",
			Scopes = PluginTokenScopes.Enroll,
			CreatedAt = _time.GetUtcNow().UtcDateTime
		});

		var first = await _service.Register("com.example.plugin",
			"Example",
			firstAccessTokenId,
			PluginRegistrationOrigins.DeveloperToken);
		await _service.Revoke("com.example.plugin");

		_time.Advance(TimeSpan.FromMinutes(1));
		var second = await _service.Register("com.example.plugin",
			"Example again",
			secondAccessTokenId,
			PluginRegistrationOrigins.DeveloperToken);

		var authenticatedOnOldSecret = await _service.Authenticate("com.example.plugin", first.PluginSecret!);
		var authenticatedOnNewSecret = await _service.Authenticate("com.example.plugin", second.PluginSecret!);

		Assert.Multiple(() =>
		{
			Assert.That(second.Succeeded, Is.True);
			Assert.That(second.PluginSecret, Is.Not.Null.And.Not.EqualTo(first.PluginSecret));
			Assert.That(second.Registration!.AccessTokenId, Is.EqualTo(secondAccessTokenId));
			Assert.That(second.Registration.RevokedAt, Is.Null);
			Assert.That(authenticatedOnOldSecret, Is.Null);
			Assert.That(authenticatedOnNewSecret, Is.Not.Null);
		});
	}

	[Test]
	public async Task An_Active_Registration_Still_Rejects_Re_Registration()
	{
		await _service.Register("com.example.plugin",
			"Example",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);

		var result = await _service.Register("com.example.plugin",
			"Example again",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);

		Assert.That(result.Error, Is.EqualTo(PluginRegistrationError.AlreadyRegistered));
	}

	[Test]
	public async Task Enrolling_onto_an_installed_id_is_refused()
	{
		_catalog.Plugins.Add(InstalledWith("com.example.plugin", "1.0.0"));

		var result = await _service.Register("com.example.plugin",
			"Example",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginRegistrationError.AlreadyRegistered));
		});
	}

	[Test]
	public async Task An_installed_id_and_an_already_enrolled_id_are_refused_indistinguishably()
	{
		_catalog.Plugins.Add(InstalledWith("com.example.installed", "1.0.0"));
		await _service.Register("com.example.enrolled",
			"Example",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);

		var installedResult = await _service.Register("com.example.installed",
			"Example",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);
		var enrolledResult = await _service.Register("com.example.enrolled",
			"Example again",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);

		Assert.Multiple(() =>
		{
			Assert.That(installedResult.Succeeded, Is.False);
			Assert.That(enrolledResult.Succeeded, Is.False);
			Assert.That(installedResult.Error, Is.EqualTo(enrolledResult.Error));
			Assert.That(installedResult.ErrorDetail, Is.EqualTo(enrolledResult.ErrorDetail));
			Assert.That(installedResult.ErrorDetail, Is.Null);
		});
	}

	[Test]
	public async Task Re_enrolling_after_an_uninstall_that_kept_the_data_directory_succeeds()
	{
		var registered = await _service.Register("com.example.plugin",
			"Example",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);
		await _service.Revoke("com.example.plugin");

		_catalog.Plugins.Add(new InstalledPlugin
		{
			PluginId = "com.example.plugin",
			PluginDirectory = "/plugins/com.example.plugin",
			Versions = [],
			ActiveVersion = null
		});

		_time.Advance(TimeSpan.FromMinutes(1));
		var result = await _service.Register("com.example.plugin",
			"Example again",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.True);
			Assert.That(result.Registration!.RevokedAt, Is.Null);
		});
	}

	[Test]
	public async Task Enrolling_an_id_that_is_not_installed_still_succeeds()
	{
		_catalog.Plugins.Add(InstalledWith("com.example.other", "1.0.0"));

		var result = await _service.Register("com.example.plugin",
			"Example",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);

		Assert.That(result.Succeeded, Is.True);
	}

	[Test]
	public async Task A_Paired_Registration_With_No_Owning_Token_Authenticates()
	{
		var registered = await _service.Register("com.example.plugin",
			"Example",
			accessTokenId: null,
			PluginRegistrationOrigins.Pairing);

		var authenticated = await _service.Authenticate("com.example.plugin", registered.PluginSecret!);

		Assert.Multiple(() =>
		{
			Assert.That(registered.Registration!.AccessTokenId, Is.Null);
			Assert.That(authenticated?.PluginId, Is.EqualTo("com.example.plugin"));
		});
	}

	[Test]
	public async Task A_Paired_Registration_Stops_Authenticating_Once_Revoked()
	{
		var registered = await _service.Register("com.example.plugin",
			"Example",
			accessTokenId: null,
			PluginRegistrationOrigins.Pairing);

		await _service.Revoke("com.example.plugin");

		var authenticated = await _service.Authenticate("com.example.plugin", registered.PluginSecret!);

		Assert.That(authenticated, Is.Null);
	}

	[Test]
	public async Task Replacing_The_Secret_Retires_The_Old_One_And_Activates_The_New_One()
	{
		var registered = await _service.Register("com.example.plugin",
			"Example",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);

		var replaced = await _service.ReplaceSecret("com.example.plugin",
			"Example",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);

		var authenticatedOnOldSecret = await _service.Authenticate("com.example.plugin", registered.PluginSecret!);
		var authenticatedOnNewSecret = await _service.Authenticate("com.example.plugin", replaced.PluginSecret!);

		Assert.Multiple(() =>
		{
			Assert.That(replaced.Succeeded, Is.True);
			Assert.That(replaced.PluginSecret, Is.Not.Null.And.Not.EqualTo(registered.PluginSecret));
			Assert.That(authenticatedOnOldSecret, Is.Null);
			Assert.That(authenticatedOnNewSecret, Is.Not.Null);
			Assert.That(
				_registrations.Registrations.Count(r => r.PluginId == "com.example.plugin" && r.RevokedAt is null),
				Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Replacing_The_Secret_Terminates_The_Plugins_Live_Sessions()
	{
		await _service.Register("com.example.plugin",
			"Example",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);

		var connection = new FakePluginConnection();
		await _sessionRegistry.Create(new PluginSessionRecord
		{
			SessionId = "session-1",
			PluginId = "com.example.plugin",
			DisplayName = "Example",
			AccessTokenId = _accessTokenId,
			Origin = PluginSessionOrigin.SelfRegistered,
			NegotiatedVersion = 1,
			Capabilities = new Dictionary<string, MacroDeck.Plugin.Protocol.Versioning.CapabilityNegotiationResult>(),
			DeclaredCapabilities = [],
			State = PluginSessionState.Connected,
			CreatedAt = _time.GetUtcNow()
		});
		_sessionRegistry.TryAttach("session-1", connection, instanceId: null);

		await _service.ReplaceSecret("com.example.plugin",
			"Example",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);

		Assert.That(connection.Closes, Is.Not.Empty);
	}

	[Test]
	public async Task Replacing_The_Secret_Of_A_Revoked_Registration_Fails_Rather_Than_Resurrecting_It()
	{
		await _service.Register("com.example.plugin",
			"Example",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);
		await _service.Revoke("com.example.plugin");

		var result = await _service.ReplaceSecret("com.example.plugin",
			"Example",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginRegistrationError.AlreadyRegistered));
		});
	}

	[Test]
	public async Task Replacing_The_Secret_Of_An_Absent_Registration_Fails()
	{
		var result = await _service.ReplaceSecret("com.example.never-registered",
			"Example",
			_accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);

		Assert.That(result.Succeeded, Is.False);
	}

	private static InstalledPlugin InstalledWith(string pluginId, string version)
	{
		var installedVersion = new InstalledPluginVersion
		{
			Version = version,
			VersionDirectory = $"/plugins/{pluginId}/versions/{version}",
			ManifestPath = $"/plugins/{pluginId}/versions/{version}/manifest.json"
		};

		return new InstalledPlugin
		{
			PluginId = pluginId,
			PluginDirectory = $"/plugins/{pluginId}",
			Versions = [installedVersion],
			ActiveVersion = installedVersion
		};
	}
}
