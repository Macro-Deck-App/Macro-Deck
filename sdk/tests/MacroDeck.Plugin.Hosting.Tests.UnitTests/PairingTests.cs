using System.Security.Cryptography;
using System.Text;
using MacroDeck.Plugin.Hosting.Credentials;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol;
using MacroDeck.Plugin.Protocol.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MacroDeck.Plugin.Testing;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

/// <summary>
/// Interactive pairing (issue #588): the fallback a self-registering plugin with no stored credential
/// and no enrollment token uses when the host supports it - see <see cref="FakePluginHost" />'s pairing
/// knobs for the shapes of host behaviour exercised here.
/// </summary>
[TestFixture]
public class PairingTests
{
	private FakePluginHost _host = null!;
	private string _stateDirectory = string.Empty;
	private PluginManifestFixture _manifest = null!;

	[SetUp]
	public async Task SetUp()
	{
		_host = await FakePluginHost.StartAsync();
		_stateDirectory = Directory.CreateTempSubdirectory("macro-deck-plugin-pairing-tests").FullName;
		_manifest = new PluginManifestFixture("""
											  {
											    "manifestVersion": 1,
											    "id": "com.example.test",
											    "name": "Test Plugin",
											    "version": "1.0.0"
											  }
											  """);
	}

	[TearDown]
	public async Task TearDown()
	{
		await _host.DisposeAsync();
		Directory.Delete(_stateDirectory, recursive: true);
		_manifest.Dispose();
	}

	private PluginHostBuilder Builder(string? enrollmentToken = null, TimeSpan? pairingTimeout = null)
	{
		var builder = _manifest.CreateBuilder();

		builder.Configuration["MacroDeck:Plugin:HostUrl"] = _host.Url;
		builder.Configuration["MacroDeck:Plugin:StateDirectory"] = _stateDirectory;

		if (enrollmentToken is not null)
		{
			builder.Configuration["MacroDeck:Plugin:EnrollmentToken"] = enrollmentToken;
		}

		if (pairingTimeout is { } timeout)
		{
			builder.Configuration["MacroDeck:Plugin:PairingTimeout"] = timeout.ToString();
		}

		return builder;
	}

	private Task<PluginCredentials?> LoadStoredCredentialsAsync()
		=> new FilePluginCredentialStore(Options.Create(new PluginHostOptions { StateDirectory = _stateDirectory }),
			new PluginMetadata { Id = "com.example.test", Name = "Test Plugin", Version = "1.0.0" },
			Serilog.Core.Logger.None).LoadAsync();

	private static async Task<PluginConnectionStatus> WaitForFaultAsync(PluginApplication plugin)
	{
		var state = plugin.Services.GetRequiredService<PluginConnectionState>();
		await Wait.UntilAsync(() => state.Status == PluginConnectionStatus.Faulted, TimeSpan.FromSeconds(15));
		return state.Status;
	}

	[Test]
	public async Task No_stored_credential_and_no_token_pairs_and_connects_once_approved()
	{
		_host.PairingApproveAfterPolls = 1;

		await using var plugin = Builder().Build();
		await plugin.StartAsync();

		await _host.ConnectedAsync().WaitAsync(TimeSpan.FromSeconds(10));
		await _host.WelcomedAsync();

		var stored = await LoadStoredCredentialsAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_host.PairingCreates, Has.Count.EqualTo(1));
			Assert.That(_host.PairingStatusPolls, Is.Not.Empty);
			Assert.That(_host.PairingRedemptions, Has.Count.EqualTo(1));
			Assert.That(stored, Is.Not.Null);
			Assert.That(_host.Requests.Any(request
					=> request.Headers.ContainsKey(PluginAuthDefaults.EnrollmentTokenHeaderName)),
				Is.False);
		});
	}

	[Test]
	public async Task The_pairing_challenge_is_not_the_verifier_and_matches_its_sha256()
	{
		_host.PairingApproveAfterPolls = 1;

		await using var plugin = Builder().Build();
		await plugin.StartAsync();

		await _host.ConnectedAsync().WaitAsync(TimeSpan.FromSeconds(10));

		var create = _host.PairingCreates.Single();
		var redemption = _host.PairingRedemptions.Single();

		var expectedChallenge = Convert
			.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(redemption.CodeVerifier)))
			.TrimEnd('=')
			.Replace('+', '-')
			.Replace('/', '_');

		Assert.Multiple(() =>
		{
			Assert.That(create.CodeChallenge, Is.Not.EqualTo(redemption.CodeVerifier));
			Assert.That(create.CodeChallenge, Is.EqualTo(expectedChallenge));
			Assert.That(create.CodeChallengeMethod, Is.EqualTo("S256"));
		});
	}

	[Test]
	public async Task No_request_path_or_query_string_ever_carries_the_verifier_or_the_issued_secret()
	{
		_host.PairingApproveAfterPolls = 1;

		await using var plugin = Builder().Build();
		await plugin.StartAsync();

		await _host.ConnectedAsync().WaitAsync(TimeSpan.FromSeconds(10));

		var verifier = _host.PairingRedemptions.Single().CodeVerifier;
		var stored = await LoadStoredCredentialsAsync();

		Assert.Multiple(() =>
		{
			foreach (var request in _host.Requests)
			{
				Assert.That(request.Path, Does.Not.Contain(verifier));
				Assert.That(request.QueryString, Does.Not.Contain(verifier));
				Assert.That(request.Path, Does.Not.Contain(stored!.Secret));
				Assert.That(request.QueryString, Does.Not.Contain(stored.Secret));
			}
		});
	}

	[Test]
	public async Task A_second_launch_over_the_same_state_directory_sends_no_pairing_requests()
	{
		_host.PairingApproveAfterPolls = 1;

		await using (var first = Builder().Build())
		{
			await first.StartAsync();
			await _host.ConnectedAsync().WaitAsync(TimeSpan.FromSeconds(10));
			await first.StopAsync();
		}

		// Hostile to pairing, so a second start that attempted pairing anyway would fail loudly rather
		// than silently succeed - proving the stored credential, not luck, is why nothing was sent.
		_host.PairingApproveAfterPolls = null;
		_host.PairingRejected = true;
		_host.PairingCreateNotFound = true;
		_host.Requests.Clear();

		await using var second = Builder().Build();
		await second.StartAsync();

		await Wait.UntilAsync(() => _host.Sessions.Count == 2);

		Assert.That(_host.Requests.Any(request =>
				request.Path.StartsWith(ProtocolConstants.PairingPath, StringComparison.Ordinal)),
			Is.False);
	}

	[Test]
	public async Task An_enrollment_token_wins_over_pairing_and_sends_no_pairing_requests()
	{
		// Never approved: a plugin that tried pairing before the token would hang here until the test
		// itself times out, which is the point - this fails by timeout, not by assertion, if the
		// precedence is wrong.
		await using var plugin = Builder(enrollmentToken: "enrollment-token").Build();
		await plugin.StartAsync();

		await _host.ConnectedAsync().WaitAsync(TimeSpan.FromSeconds(10));

		Assert.Multiple(() =>
		{
			Assert.That(_host.Registrations, Has.Count.EqualTo(1));
			Assert.That(_host.PairingCreates, Is.Empty);
		});
	}

	[Test]
	public async Task Developer_mode_off_creates_no_request_and_keeps_waiting_instead_of_faulting()
	{
		_host.PairingDeveloperModeEnabled = false;

		await using var plugin = Builder().Build();
		await plugin.StartAsync();

		var state = plugin.Services.GetRequiredService<PluginConnectionState>();
		await Wait.UntilAsync(() => state.ReconnectAttempt >= 2, TimeSpan.FromSeconds(15));

		Assert.Multiple(() =>
		{
			// A prompt the user cannot see must never be created, and refusing to pair is not a reason to
			// give up - the user may still enable Developer Mode.
			Assert.That(_host.PairingCreates, Is.Empty);
			Assert.That(state.Status, Is.Not.EqualTo(PluginConnectionStatus.Faulted));
		});
	}

	[Test]
	public async Task Enabling_developer_mode_pairs_without_restarting_the_plugin()
	{
		_host.PairingDeveloperModeEnabled = false;
		_host.PairingApproveAfterPolls = 1;

		await using var plugin = Builder().Build();
		await plugin.StartAsync();

		var state = plugin.Services.GetRequiredService<PluginConnectionState>();
		await Wait.UntilAsync(() => state.ReconnectAttempt >= 1, TimeSpan.FromSeconds(15));
		Assert.That(_host.PairingCreates, Is.Empty);

		_host.PairingDeveloperModeEnabled = true;

		await _host.ConnectedAsync().WaitAsync(TimeSpan.FromSeconds(60));
		await _host.WelcomedAsync();

		var stored = await LoadStoredCredentialsAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_host.PairingCreates, Has.Count.EqualTo(1));
			Assert.That(stored, Is.Not.Null);
		});
	}

	[Test]
	public async Task A_host_that_does_not_report_developer_mode_still_fails_once_without_a_retry_storm()
	{
		// An older host: the descriptor says nothing, so the create attempt is the only way to find out -
		// and the host counts every refused create against a throttle shared by all plugins, so it must
		// stay a single attempt rather than becoming a retry loop.
		_host.PairingDeveloperModeEnabled = null;
		_host.PairingCreateForbidden = true;

		await using var plugin = Builder().Build();
		await plugin.StartAsync();

		var status = await WaitForFaultAsync(plugin);

		Assert.Multiple(() =>
		{
			Assert.That(status, Is.EqualTo(PluginConnectionStatus.Faulted));
			Assert.That(_host.Requests.Count(request
					=> request.Method == HttpMethod.Post.Method && request.Path == ProtocolConstants.PairingPath),
				Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_rejected_pairing_request_is_created_once_and_faults_without_reprompting()
	{
		_host.PairingRejected = true;

		await using var plugin = Builder().Build();
		await plugin.StartAsync();

		var status = await WaitForFaultAsync(plugin);

		Assert.Multiple(() =>
		{
			Assert.That(status, Is.EqualTo(PluginConnectionStatus.Faulted));
			Assert.That(_host.PairingCreates, Has.Count.EqualTo(1));
			Assert.That(plugin.Services.GetRequiredService<PluginConnectionState>().FaultReason,
				Does.Contain("rejected"));
		});
	}

	[Test]
	public async Task A_pairing_request_that_never_resolves_expires_once_and_faults_without_reprompting()
	{
		// Never approved and never rejected: stays "pending" until the plugin's own short timeout fires.
		await using var plugin = Builder(pairingTimeout: TimeSpan.FromSeconds(2)).Build();
		await plugin.StartAsync();

		var status = await WaitForFaultAsync(plugin);

		Assert.Multiple(() =>
		{
			Assert.That(status, Is.EqualTo(PluginConnectionStatus.Faulted));
			Assert.That(_host.PairingCreates, Has.Count.EqualTo(1));
			Assert.That(plugin.Services.GetRequiredService<PluginConnectionState>().FaultReason,
				Does.Contain("expired"));
		});
	}

	[Test]
	public async Task A_descriptor_without_a_pairing_block_fails_once_without_naming_a_credential()
	{
		_host.PairingDescriptorAbsent = true;

		await using var plugin = Builder().Build();
		await plugin.StartAsync();

		var status = await WaitForFaultAsync(plugin);
		var faultReason = plugin.Services.GetRequiredService<PluginConnectionState>().FaultReason;

		Assert.Multiple(() =>
		{
			Assert.That(status, Is.EqualTo(PluginConnectionStatus.Faulted));
			Assert.That(_host.PairingCreates, Is.Empty);
			Assert.That(faultReason, Does.Contain("does not support interactive pairing"));

			// The fault reason is served verbatim by the diagnostics endpoint, so it must not name a
			// credential. The headless fallback's environment variable is logged instead.
			Assert.That(faultReason!.ToUpperInvariant(), Does.Not.Contain("TOKEN"));
			Assert.That(faultReason.ToUpperInvariant(), Does.Not.Contain("SECRET"));
		});
	}

	[Test]
	public async Task A_404_on_pairing_creation_fails_once_without_naming_a_credential()
	{
		_host.PairingCreateNotFound = true;

		await using var plugin = Builder().Build();
		await plugin.StartAsync();

		var status = await WaitForFaultAsync(plugin);
		var faultReason = plugin.Services.GetRequiredService<PluginConnectionState>().FaultReason;

		Assert.Multiple(() =>
		{
			Assert.That(status, Is.EqualTo(PluginConnectionStatus.Faulted));

			// The create attempt itself was made exactly once - no retry storm - even though it 404s.
			Assert.That(_host.Requests.Count(request
					=> request.Method == HttpMethod.Post.Method && request.Path == ProtocolConstants.PairingPath),
				Is.EqualTo(1));

			Assert.That(faultReason, Does.Contain("does not support interactive pairing"));

			// The fault reason is served verbatim by the diagnostics endpoint, so it must not name a
			// credential. The headless fallback's environment variable is logged instead.
			Assert.That(faultReason!.ToUpperInvariant(), Does.Not.Contain("TOKEN"));
			Assert.That(faultReason.ToUpperInvariant(), Does.Not.Contain("SECRET"));
		});
	}

	[Test]
	public async Task Stopping_while_a_pairing_request_is_pending_leaves_no_usable_credential()
	{
		// Never approved: the request is still "pending" when the plugin is stopped mid-flight.
		var plugin = Builder().Build();

		await plugin.StartAsync();
		await Wait.UntilAsync(() => !_host.PairingStatusPolls.IsEmpty);

		await plugin.StopAsync();
		await plugin.DisposeAsync();

		var stored = await LoadStoredCredentialsAsync();

		Assert.Multiple(() =>
		{
			Assert.That(stored, Is.Null);
			Assert.That(_host.PairingRedemptions, Is.Empty);
		});
	}
}
