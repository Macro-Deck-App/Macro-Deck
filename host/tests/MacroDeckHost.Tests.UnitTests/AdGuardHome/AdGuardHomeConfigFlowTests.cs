using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Application.AdGuardHome;
using MacroDeckHost.Integrations.AdGuardHome;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.AdGuardHome;

[TestFixture]
internal sealed class AdGuardHomeConfigFlowTests
{
	private FakeAdGuardHomeClient _client = null!;
	private AdGuardHomeConnectionSettings? _settings;

	[SetUp]
	public void SetUp()
	{
		_client = new FakeAdGuardHomeClient();
		_settings = null;
	}

	[Test]
	public async Task An_invalid_address_is_reported_on_the_address_field()
	{
		var result = await Submit(Input(url: "ftp://nope"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors!.Keys, Does.Contain(AdGuardHomeConfigKeys.BaseUrl));
			Assert.That(_client.StatusCalls, Is.Zero, "nothing is contacted before the address is valid");
		});
	}

	[Test]
	public async Task Rejected_credentials_are_reported_on_the_password_field()
	{
		_client.Failure = AdGuardHomeConnection.Unauthorized;

		var result = await Submit(Input());

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors!.Keys, Does.Contain(AdGuardHomeConfigKeys.Password));
		});
	}

	[TestCase(AdGuardHomeConnection.Unreachable)]
	[TestCase(AdGuardHomeConnection.Timeout)]
	[TestCase(AdGuardHomeConnection.Incompatible)]
	[TestCase(AdGuardHomeConnection.Redirected)]
	public async Task A_failed_connection_test_keeps_the_user_on_the_step(AdGuardHomeConnection failure)
	{
		_client.Failure = failure;

		var result = await Submit(Input());

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Is.Not.Empty);
		});
	}

	[Test]
	public async Task A_successful_test_completes_with_the_chosen_name_and_tests_with_the_given_credentials()
	{
		var result = await Submit(Input());

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.EntryTitle, Is.EqualTo("Home"));
			Assert.That(result.Values!.ContainsKey(AdGuardHomeConfigKeys.Password), Is.False,
				"the host already keeps the encrypted password");
			Assert.That(_settings!.Username, Is.EqualTo("admin"));
			Assert.That(_settings.Password, Is.EqualTo("secret"));
			Assert.That(_settings.ControlUrl.ToString(), Is.EqualTo("http://192.168.1.2:3000/control/"));
		});
	}

	[Test]
	public async Task An_instance_without_sign_in_can_be_added()
	{
		var result = await Submit(Input(user: "", password: ""));

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(_settings!.Username, Is.Null);
		});
	}

	[Test]
	public async Task Reconfiguring_keeps_the_existing_name()
	{
		var result = await Submit(Input(name: null), new EntryContext("Office"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.EntryTitle, Is.EqualTo("Office"));
		});
	}

	private Task<ConfigFlowResult> Submit(Dictionary<string, object?> input, IConfigFlowContext? context = null)
		=> new AdGuardHomeConfigFlow(settings =>
			{
				_settings = settings;
				return _client;
			})
			.SubmitAsync("connection", input, context ?? new EntryContext(null), CancellationToken.None);

	private static Dictionary<string, object?> Input(
		string? name = "Home",
		string url = "192.168.1.2:3000",
		string user = "admin",
		string password = "secret")
		=> new()
		{
			[AdGuardHomeConfigKeys.Name] = name,
			[AdGuardHomeConfigKeys.BaseUrl] = url,
			[AdGuardHomeConfigKeys.Username] = user,
			[AdGuardHomeConfigKeys.Password] = password,
		};

	private sealed class EntryContext(string? entryTitle) : IConfigFlowEntryContext
	{
		public IOAuthSession OAuth => throw new NotSupportedException();

		public string? EntryTitle { get; } = entryTitle;
	}
}
