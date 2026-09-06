using MacroDeckHost.Integrations.Delegation;
using MacroDeckHost.Integrations.Delegation.Protocol;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Tests.UnitTests.Delegation;

[TestFixture]
internal sealed class DelegateConfigFlowTests
{
	private FakeDelegateClient _client = null!;
	private FakeTimeProvider _time = null!;
	private DelegateConfigFlow _flow = null!;

	[SetUp]
	public void SetUp()
	{
		_client = new FakeDelegateClient();
		_time = new FakeTimeProvider();
		_flow = new DelegateConfigFlow(() => _client, null, _time);
	}

	[TearDown]
	public void TearDown() => _client.Dispose();

	[Test]
	public async Task A_successful_setup_completes_with_the_remotes_machine_name_in_the_title()
	{
		var uri = new Uri("http://10.0.0.5:5000");
		_client.For(uri).InstanceName = "GAMING-PC";

		var result = await Submit(uri.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.EntryTitle, Is.EqualTo("Macro Deck (GAMING-PC)"));
			Assert.That(_client.LoginCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_wrong_password_is_a_field_error_on_the_password_and_saves_nothing()
	{
		var uri = new Uri("http://10.0.0.5:5000");

		var result = await Submit(uri.ToString(), password: "totally-wrong");

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors, Contains.Key(DelegateConfigKeys.Password));
			Assert.That(result.FieldErrors, Does.Not.ContainKey(DelegateConfigKeys.BaseUrl));
			Assert.That(result.Values, Is.Null);
			Assert.That(_client.LoginCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task An_address_that_is_not_a_macro_deck_is_reported_as_a_wrong_address()
	{
		var uri = new Uri("http://10.0.0.5:5000");
		_client.For(uri).ProbeException = new DelegateNotMacroDeckException();

		var result = await Submit(uri.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors, Contains.Key(DelegateConfigKeys.BaseUrl));
			Assert.That(TestLocalization.Resolve(result.FieldErrors![DelegateConfigKeys.BaseUrl]),
				Does.Contain("not a Macro Deck"));
			Assert.That(_client.LoginCount, Is.Zero);
		});
	}

	[Test]
	public async Task An_unreachable_address_is_a_field_error_on_the_address_and_never_attempts_a_login()
	{
		var uri = new Uri("http://10.0.0.5:5000");
		_client.For(uri).Reachable = false;

		var result = await Submit(uri.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors, Contains.Key(DelegateConfigKeys.BaseUrl));
			Assert.That(TestLocalization.Resolve(result.FieldErrors![DelegateConfigKeys.BaseUrl]),
				Does.Contain("Could not reach"));
			Assert.That(_client.LoginCount, Is.Zero);
		});
	}

	[Test]
	public async Task A_remote_that_is_locked_out_is_reported_as_a_lockout_and_is_not_retried()
	{
		var uri = new Uri("http://10.0.0.5:5000");
		_client.For(uri).LoginException = new DelegateRateLimitedException(TimeSpan.FromMinutes(15));

		var result = await Submit(uri.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("locked out"));
			Assert.That(result.FieldErrors, Is.Null.Or.Empty);
			Assert.That(_client.LoginCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task The_password_is_persisted_only_as_a_secret()
	{
		var uri = new Uri("http://10.0.0.5:5000");
		const string password = "correct-password";

		var result = await Submit(uri.ToString(), password: password);

		var values = result.Values!;
		Assert.Multiple(() =>
		{
			Assert.That(values[DelegateConfigKeys.Password].IsSecret, Is.True);
			Assert.That(values.Values.Where(v => !v.IsSecret).Select(v => v.Value), Has.None.EqualTo(password));
		});
	}

	[Test]
	public async Task Pointing_the_setup_at_this_machine_is_refused()
	{
		var uri = new Uri("http://127.0.0.1:5000");
		_client.For(uri).InstanceName = Environment.MachineName;

		var result = await Submit(uri.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("this Macro Deck"));
			Assert.That(result.Values, Is.Null);
		});
	}

	[TestCase("password")]
	[TestCase("address")]
	[TestCase("machine-name")]
	public async Task Reconfiguring_with_only_one_signal_changed_reuses_the_existing_instance_id(string what)
	{
		var originalUri = new Uri("http://10.0.0.5:5000");
		var config = new RecordingIntegrationConfig();
		var existingInstanceId = AddCompletedEntry(config, originalUri, "GAMING-PC");
		var flow = new DelegateConfigFlow(() => _client, config, _time);

		var newUri = what == "address" ? new Uri("http://10.0.0.9:5000") : originalUri;
		var newMachineName = what == "machine-name" ? "GAMING-PC-2" : "GAMING-PC";
		var newPassword = what == "password" ? "new-password" : "correct-password";
		_client.For(newUri).InstanceName = newMachineName;
		_client.For(newUri).Password = newPassword;

		var result = await Submit(flow, newUri.ToString(), password: newPassword);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.Values![DelegateConfigKeys.InstanceId].Value, Is.EqualTo(existingInstanceId));
		});
	}

	[Test]
	public async Task Reconfiguring_with_both_the_address_and_the_machine_name_different_mints_a_new_instance_id()
	{
		var originalUri = new Uri("http://10.0.0.5:5000");
		var config = new RecordingIntegrationConfig();
		var existingInstanceId = AddCompletedEntry(config, originalUri, "GAMING-PC");
		var flow = new DelegateConfigFlow(() => _client, config, _time);

		var newUri = new Uri("http://10.0.0.9:5000");
		_client.For(newUri).InstanceName = "OTHER-PC";

		var result = await Submit(flow, newUri.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.Values![DelegateConfigKeys.InstanceId].Value, Is.Not.EqualTo(existingInstanceId));
		});
	}

	private string AddCompletedEntry(RecordingIntegrationConfig config, Uri baseUrl, string machineName)
	{
		var instanceId = Guid.NewGuid().ToString("N");
		config.AddEntry("Macro Deck (existing)",
			new Dictionary<string, string?>(StringComparer.Ordinal)
			{
				[DelegateConfigKeys.BaseUrl] = baseUrl.ToString(),
				[DelegateConfigKeys.Username] = "admin",
				[DelegateConfigKeys.InstanceId] = instanceId,
				[DelegateConfigKeys.MachineName] = machineName,
				[DelegateConfigKeys.InstanceKey] = DelegateSlug.For(machineName, instanceId),
				[DelegateConfigKeys.ConfiguredAt] = _time.Now.ToString("o")
			},
			new Dictionary<string, string>(StringComparer.Ordinal)
				{ [DelegateConfigKeys.Password] = "correct-password" });
		return instanceId;
	}

	private Task<ConfigFlowResult> Submit(
		string baseUrl,
		string username = "admin",
		string password = "correct-password")
		=> Submit(_flow, baseUrl, username, password);

	private static Task<ConfigFlowResult> Submit(
		DelegateConfigFlow flow,
		string baseUrl,
		string username = "admin",
		string password = "correct-password")
		=> flow.SubmitAsync("connection",
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				[DelegateConfigKeys.BaseUrl] = baseUrl,
				[DelegateConfigKeys.Username] = username,
				[DelegateConfigKeys.Password] = password
			},
			null!,
			CancellationToken.None);
}
