using System.Text.Json;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Integrations.Imap;
using MacroDeckHost.Integrations.Imap.Configuration;
using MacroDeckHost.Integrations.Imap.Models;

namespace MacroDeckHost.Tests.UnitTests.Imap;

[TestFixture]
internal sealed class ImapConfigFlowTests
{
	private FakeImapServer _server = null!;
	private ImapIntegration _integration = null!;

	[SetUp]
	public void SetUp()
	{
		_server = new FakeImapServer();
		_integration = new ImapIntegration(_server, ImapTestTimings.Fast);
	}

	[TearDown]
	public void TearDown()
	{
		_integration.Dispose();
	}

	[Test]
	public void The_inbox_id_is_derived_from_the_account_and_ignores_the_case_of_server_and_username()
	{
		Assert.Multiple(() =>
		{
			Assert.That(ImapInboxId.For("imap.example.com", 993, "alex@example.com", "INBOX"),
				Is.EqualTo("iaf5455549422874f"));
			Assert.That(ImapInboxId.For("IMAP.Example.com", 993, "Alex@Example.com", "INBOX"),
				Is.EqualTo("iaf5455549422874f"));
			Assert.That(ImapInboxId.For("imap.example.com", 993, "alex@example.com", "Archive"),
				Is.Not.EqualTo("iaf5455549422874f"));
		});
	}

	[Test]
	public async Task Setting_the_same_account_up_again_keeps_its_inbox_id()
	{
		var context = new ImapTestContext();
		var first = context.AddAccount("Work");
		await _integration.InitializeAsync(context);
		var before = _integration.GetInboxes().Single().Id;

		context.ConfigStore.RemoveEntry(first);
		await _integration.ReloadEntriesAsync();
		context.AddAccount("Work again", "IMAP.example.com");
		await _integration.ReloadEntriesAsync();

		Assert.That(_integration.GetInboxes().Single().Id, Is.EqualTo(before));
		await _integration.ShutdownAsync();
	}

	[Test]
	public async Task Missing_fields_are_reported_on_their_fields_without_contacting_the_server()
	{
		var flow = _integration.CreateConfigFlow();

		var result = await flow.SubmitAsync(ImapConfigFlow.StepId,
			new Dictionary<string, object?>
			{
				[ImapConfigKeys.Provider] = ImapConfigKeys.ProviderCustom,
				[ImapConfigKeys.Port] = 70000d
			},
			new TestFlowContext(),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors!.Keys,
				Is.EquivalentTo(new[]
				{
					ImapConfigKeys.ConfigurationName, ImapConfigKeys.Host, ImapConfigKeys.Port, ImapConfigKeys.Username,
					ImapConfigKeys.Password
				}));
			Assert.That(_server.SessionsCreated, Is.Zero);
		});
	}

	[TestCase(nameof(FakeImapServer.RejectSignIn), ImapConfigKeys.Password)]
	[TestCase(nameof(FakeImapServer.MissingFolder), ImapConfigKeys.Folder)]
	public async Task A_refused_sign_in_or_missing_folder_keeps_the_form_with_an_error_on_that_field(
		string failure,
		string field)
	{
		_server.RejectSignIn = failure == nameof(FakeImapServer.RejectSignIn);
		_server.MissingFolder = failure == nameof(FakeImapServer.MissingFolder);

		var result = await _integration.CreateConfigFlow()
			.SubmitAsync(ImapConfigFlow.StepId, ValidInput(), new TestFlowContext(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.NextStep?.StepId, Is.EqualTo(ImapConfigFlow.StepId));
			Assert.That(result.FieldErrors!.Keys, Is.EqualTo(new[] { field }));
		});
	}

	[Test]
	public async Task An_unreachable_server_keeps_the_form_with_a_general_error()
	{
		_server.Unreachable = true;

		var result = await _integration.CreateConfigFlow()
			.SubmitAsync(ImapConfigFlow.StepId, ValidInput(), new TestFlowContext(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.ErrorMessage.ToString(), Is.Not.Empty);
			Assert.That(result.FieldErrors, Is.Null.Or.Empty);
		});
	}

	[Test]
	public async Task A_working_account_completes_with_the_entered_name_and_defaults_filled_in()
	{
		var input = ValidInput();
		input.Remove(ImapConfigKeys.Folder);
		input.Remove(ImapConfigKeys.Port);

		var result = await _integration.CreateConfigFlow()
			.SubmitAsync(ImapConfigFlow.StepId, input, new TestFlowContext(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.EntryTitle, Is.EqualTo("Work"));
			Assert.That(result.Values![ImapConfigKeys.Port].Value, Is.EqualTo("993"));
			Assert.That(result.Values[ImapConfigKeys.Folder].Value, Is.EqualTo("INBOX"));
			Assert.That(result.Values[ImapConfigKeys.Security].Value, Is.EqualTo(ImapConfigKeys.SecurityStartTls));
			Assert.That(_server.LastOpened!.Security, Is.EqualTo(ImapSecurity.StartTls));
			Assert.That(_server.Active!.Disposed, Is.True);
		});
	}

	[Test]
	public async Task An_account_folder_that_is_already_set_up_is_refused_but_its_own_entry_can_be_saved_again()
	{
		var existing = StoredEntry(Guid.NewGuid(), "alex@example.com");

		var duplicate = await _integration.PrepareEntryAsync(new ConfigEntryPreparation
		{
			EntryId = Guid.NewGuid(),
			Title = "Work again",
			Values = StoredEntry(Guid.NewGuid(), "ALEX@example.com").Values,
			Siblings = [existing]
		});
		var sameEntry = await _integration.PrepareEntryAsync(new ConfigEntryPreparation
		{
			EntryId = existing.Id,
			Title = "Work",
			Values = existing.Values,
			Existing = existing,
			Siblings = [existing]
		});
		var otherAccount = await _integration.PrepareEntryAsync(new ConfigEntryPreparation
		{
			EntryId = Guid.NewGuid(),
			Title = "Private",
			Values = StoredEntry(Guid.NewGuid(), "alex@private.example").Values,
			Siblings = [existing]
		});

		Assert.Multiple(() =>
		{
			Assert.That(duplicate.Success, Is.False);
			Assert.That(duplicate.Error.ToString(), Is.Not.Empty);
			Assert.That(sameEntry.Success, Is.True);
			Assert.That(otherAccount.Success, Is.True);
		});
	}

	[TestCase("gmail", "imap.gmail.com")]
	[TestCase("icloud", "imap.mail.me.com")]
	[TestCase("yahoo", "imap.mail.yahoo.com")]
	[TestCase("aol", "imap.aol.com")]
	[TestCase("fastmail", "imap.fastmail.com")]
	[TestCase("zoho", "imap.zoho.com")]
	[TestCase("zoho-eu", "imap.zoho.eu")]
	[TestCase("gmx", "imap.gmx.net")]
	[TestCase("web-de", "imap.web.de")]
	[TestCase("t-online", "secureimap.t-online.de")]
	[TestCase("ionos", "imap.ionos.de")]
	[TestCase("mailbox-org", "imap.mailbox.org")]
	[TestCase("posteo", "posteo.de")]
	public async Task A_provider_preset_connects_to_and_stores_its_own_server_settings(string provider, string host)
	{
		var input = ValidInput();
		input[ImapConfigKeys.Provider] = provider;

		var result = await _integration.CreateConfigFlow()
			.SubmitAsync(ImapConfigFlow.StepId, input, new TestFlowContext(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(_server.LastOpened!.Host, Is.EqualTo(host));
			Assert.That(_server.LastOpened.Port, Is.EqualTo(993));
			Assert.That(_server.LastOpened.Security, Is.EqualTo(ImapSecurity.SslTls));
			Assert.That(result.Values![ImapConfigKeys.Provider].Value, Is.EqualTo(provider));
			Assert.That(result.Values[ImapConfigKeys.Host].Value, Is.EqualTo(host));
			Assert.That(result.Values[ImapConfigKeys.Port].Value, Is.EqualTo("993"));
			Assert.That(result.Values[ImapConfigKeys.Security].Value, Is.EqualTo(ImapConfigKeys.SecuritySslTls));
		});
	}

	[Test]
	public async Task A_preset_account_gets_the_same_inbox_id_as_the_same_account_entered_as_a_custom_server()
	{
		var preset = ValidInput();
		preset[ImapConfigKeys.Provider] = "gmail";
		var custom = ValidInput();
		custom[ImapConfigKeys.Host] = "imap.gmail.com";
		custom[ImapConfigKeys.Port] = 993d;
		custom[ImapConfigKeys.Security] = ImapConfigKeys.SecuritySslTls;

		var fromPreset = await _integration.CreateConfigFlow()
			.SubmitAsync(ImapConfigFlow.StepId, preset, new TestFlowContext(), CancellationToken.None);
		var fromCustom = await _integration.CreateConfigFlow()
			.SubmitAsync(ImapConfigFlow.StepId, custom, new TestFlowContext(), CancellationToken.None);

		Assert.That(ImapInboxId.FromValues(Stored(fromPreset)), Is.EqualTo(ImapInboxId.FromValues(Stored(fromCustom))));
	}

	[Test]
	public async Task A_preset_needs_no_server_fields_but_still_asks_for_username_and_password()
	{
		var result = await _integration.CreateConfigFlow()
			.SubmitAsync(ImapConfigFlow.StepId,
				new Dictionary<string, object?>
				{
					[ImapConfigKeys.ConfigurationName] = "Private",
					[ImapConfigKeys.Provider] = "posteo",
					[ImapConfigKeys.Port] = 70000d
				},
				new TestFlowContext(),
				CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors!.Keys,
				Is.EquivalentTo(new[] { ImapConfigKeys.Username, ImapConfigKeys.Password }));
			Assert.That(_server.SessionsCreated, Is.Zero);
		});
	}

	[TestCase("IMAP.Gmail.com", 993d, ImapConfigKeys.SecuritySslTls, "gmail")]
	[TestCase("imap.gmail.com", 143d, ImapConfigKeys.SecurityStartTls, ImapConfigKeys.ProviderCustom)]
	[TestCase("imap.example.com", 993d, ImapConfigKeys.SecuritySslTls, ImapConfigKeys.ProviderCustom)]
	public async Task A_saved_account_reopens_on_the_preset_its_server_settings_match(
		string host,
		double port,
		string security,
		string expectedProvider)
	{
		var input = ValidInput();
		input[ImapConfigKeys.Host] = host;
		input[ImapConfigKeys.Port] = port;
		input[ImapConfigKeys.Security] = security;

		var result = await _integration.CreateConfigFlow()
			.SubmitAsync(ImapConfigFlow.StepId, input, new TestFlowContext(), CancellationToken.None);

		Assert.That(result.Values![ImapConfigKeys.Provider].Value, Is.EqualTo(expectedProvider));
	}

	[Test]
	public async Task The_server_fields_are_shown_only_for_a_custom_server()
	{
		var result = await _integration.CreateConfigFlow().StartAsync(new TestFlowContext(), CancellationToken.None);
		var fields = result.NextStep!.Fields.ToDictionary(field => field.Name);
		var provider = fields[ImapConfigKeys.Provider];

		Assert.Multiple(() =>
		{
			Assert.That(result.NextStep.Fields[1].Name, Is.EqualTo(ImapConfigKeys.Provider));
			Assert.That(provider.DefaultValue, Is.EqualTo(ImapConfigKeys.ProviderCustom));
			Assert.That(provider.Options!.Select(option => option.Value),
				Is.EqualTo(new[]
				{
					ImapConfigKeys.ProviderCustom, "gmail", "icloud", "yahoo", "aol", "fastmail", "zoho", "zoho-eu",
					"gmx",
					"web-de", "t-online", "ionos", "mailbox-org", "posteo"
				}));
			foreach (var name in new[] { ImapConfigKeys.Host, ImapConfigKeys.Port, ImapConfigKeys.Security })
			{
				Assert.That(fields[name].VisibleWhen?.ParameterName, Is.EqualTo(ImapConfigKeys.Provider), name);
				Assert.That(fields[name].VisibleWhen?.Values,
					Is.EqualTo(new[] { ImapConfigKeys.ProviderCustom }),
					name);
			}

			foreach (var name in new[]
				{
					ImapConfigKeys.Provider, ImapConfigKeys.Username, ImapConfigKeys.Password, ImapConfigKeys.Folder
				})
			{
				Assert.That(fields[name].VisibleWhen, Is.Null, name);
			}
		});
	}

	private static Dictionary<string, JsonElement> Stored(ConfigFlowResult result)
	{
		return result.Values!.ToDictionary(value => value.Key,
			value => JsonSerializer.SerializeToElement(value.Value.Value));
	}

	private static Dictionary<string, object?> ValidInput()
	{
		return new Dictionary<string, object?>
		{
			[ImapConfigKeys.ConfigurationName] = "Work",
			[ImapConfigKeys.Provider] = ImapConfigKeys.ProviderCustom,
			[ImapConfigKeys.Host] = "imap.example.com",
			[ImapConfigKeys.Port] = 143d,
			[ImapConfigKeys.Security] = ImapConfigKeys.SecurityStartTls,
			[ImapConfigKeys.Username] = "alex@example.com",
			[ImapConfigKeys.Password] = "app-password",
			[ImapConfigKeys.Folder] = "INBOX"
		};
	}

	private static StoredConfigEntry StoredEntry(Guid id, string username)
	{
		return new StoredConfigEntry
		{
			Id = id,
			Title = "Work",
			Values = new Dictionary<string, JsonElement>
			{
				[ImapConfigKeys.Host] = JsonSerializer.SerializeToElement("imap.example.com"),
				[ImapConfigKeys.Port] = JsonSerializer.SerializeToElement("993"),
				[ImapConfigKeys.Username] = JsonSerializer.SerializeToElement(username),
				[ImapConfigKeys.Folder] = JsonSerializer.SerializeToElement("INBOX")
			}
		};
	}
}
