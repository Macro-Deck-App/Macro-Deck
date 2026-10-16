using System.Text.Json;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Integrations.Steam.Configuration;
using MacroDeckHost.Integrations.Steam.Protocol;
using MacroDeckHost.Integrations.Steam.State;

namespace MacroDeckHost.Tests.UnitTests.Steam;

[TestFixture]
internal sealed class SteamConfigFlowTests
{
	private const string SteamId = "76561198000000001";
	private const string Key = "ABCDEF0123456789ABCDEF0123456789";

	private FakeClient _client = null!;
	private SteamConfigFlow _flow = null!;

	[SetUp]
	public void SetUp()
	{
		_client = new FakeClient();
		_flow = new SteamConfigFlow(_ => _client);
	}

	[Test]
	public async Task Everything_is_optional_so_the_flow_can_be_completed_empty()
	{
		var result = await Submit(new Dictionary<string, object?>());

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.Values!.ContainsKey("steamId"), Is.False);
			Assert.That(result.Values!.ContainsKey("apiKey"), Is.False);
			Assert.That(_client.Calls, Is.Zero, "no request to Steam without a key");
		});
	}

	[Test]
	public async Task A_valid_steam_id_and_key_are_stored_with_the_key_as_a_secret()
	{
		var result = await Submit(new Dictionary<string, object?> { ["steamId"] = SteamId, ["apiKey"] = Key });

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.Values!["steamId"].Value, Is.EqualTo(SteamId));
			Assert.That(result.Values!["apiKey"].IsSecret, Is.True);
			Assert.That(result.Values!["apiKey"].Value, Is.EqualTo(Key));
			Assert.That(result.Values!["steamId"].IsSecret, Is.False);
		});
	}

	[Test]
	public async Task A_profile_link_is_reduced_to_the_steam_id()
	{
		var result = await Submit(new Dictionary<string, object?>
		{
			["steamId"] = $"https://steamcommunity.com/profiles/{SteamId}/",
			["apiKey"] = Key
		});

		Assert.That(result.Values!["steamId"].Value, Is.EqualTo(SteamId));
	}

	[TestCase("https://steamcommunity.com/id/ann/")]
	[TestCase("ann")]
	public async Task A_custom_profile_name_is_resolved_with_the_key_before_the_profile_is_checked(string input)
	{
		_client.Vanity["ann"] = SteamId;

		var result = await Submit(new Dictionary<string, object?> { ["steamId"] = input, ["apiKey"] = Key });

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.Values!["steamId"].Value, Is.EqualTo(SteamId));
			Assert.That(_client.Order, Is.EqualTo(new[] { "vanity:ann", "summary:" + SteamId }));
		});
	}

	[Test]
	public async Task An_unknown_custom_name_is_reported_on_the_steam_id_field()
	{
		var result = await Submit(new Dictionary<string, object?> { ["steamId"] = "nobody", ["apiKey"] = Key });

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors!.Keys, Is.EqualTo(new[] { "steamId" }));
		});
	}

	[Test]
	public async Task A_steam_id_that_steam_does_not_know_is_reported()
	{
		_client.KnownIds.Clear();

		var result = await Submit(new Dictionary<string, object?> { ["steamId"] = SteamId, ["apiKey"] = Key });

		Assert.That(result.FieldErrors!.Keys, Is.EqualTo(new[] { "steamId" }));
	}

	[Test]
	public async Task A_rejected_key_is_reported_on_the_key_field_without_echoing_it()
	{
		_client.Failure = SteamWebFailure.InvalidKey;

		var result = await Submit(new Dictionary<string, object?> { ["steamId"] = SteamId, ["apiKey"] = Key });

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors!.Keys, Is.EqualTo(new[] { "apiKey" }));
			Assert.That(result.Values, Is.Null);
		});
	}

	[Test]
	public async Task Steam_being_unreachable_is_a_general_error_not_a_field_error()
	{
		_client.Failure = SteamWebFailure.Unavailable;

		var result = await Submit(new Dictionary<string, object?> { ["steamId"] = SteamId, ["apiKey"] = Key });

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors, Is.Null.Or.Empty);
		});
	}

	[TestCase(SteamId, "", "apiKey")]
	[TestCase("", Key, "steamId")]
	public async Task The_steam_id_and_the_key_are_given_together_or_not_at_all(
		string steamId,
		string key,
		string missingField)
	{
		var result = await Submit(new Dictionary<string, object?> { ["steamId"] = steamId, ["apiKey"] = key });

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.FieldErrors, Has.Count.EqualTo(1));
			Assert.That(result.FieldErrors, Does.ContainKey(missingField));
			Assert.That(_client.Calls, Is.Zero);
		});
	}

	[TestCase(null)]
	[TestCase("Steam")]
	public async Task The_key_is_asked_for_before_the_steam_id_it_belongs_to(string? entryTitle)
	{
		var start = await _flow.StartAsync(new FakeEntryContext(entryTitle), CancellationToken.None);

		var names = start.NextStep!.Fields.Select(field => field.Name).ToList();
		Assert.That(names.IndexOf("apiKey"), Is.GreaterThanOrEqualTo(0).And.LessThan(names.IndexOf("steamId")));
	}

	[TestCase("12345")]
	[TestCase("https://example.com/profiles/76561198000000001")]
	[TestCase("not a name!")]
	public async Task Something_that_cannot_be_a_steam_id_is_rejected_before_asking_steam(string input)
	{
		var result = await Submit(new Dictionary<string, object?> { ["steamId"] = input, ["apiKey"] = Key });

		Assert.Multiple(() =>
		{
			Assert.That(result.FieldErrors!.Keys, Is.EqualTo(new[] { "steamId" }));
			Assert.That(_client.Calls, Is.Zero);
		});
	}

	[Test]
	public async Task Additional_library_folders_must_be_absolute_paths()
	{
		var absolute = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SteamLibrary"));

		var accepted = await Submit(new Dictionary<string, object?>
		{
			["libraryFolders"] = JsonSerializer.Serialize(new[] { absolute })
		});
		var rejected = await Submit(new Dictionary<string, object?>
		{
			["libraryFolders"] = JsonSerializer.Serialize(new[] { "relative/folder" })
		});

		Assert.Multiple(() =>
		{
			Assert.That(accepted.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(rejected.FieldErrors!.Keys, Is.EqualTo(new[] { "libraryFolders" }));
		});
	}

	[Test]
	public void The_folder_list_is_read_from_a_json_array_and_cleaned_up()
	{
		var folders = SteamConfigFlow.ParseFolders("""["C:\\Games", "  ", "C:\\Games", " D:\\Library "]""");

		Assert.That(folders, Is.EqualTo(new[] { @"C:\Games", @"D:\Library" }));
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("[not json")]
	public void No_or_unreadable_folder_list_means_no_folders(string? text)
	{
		Assert.That(SteamConfigFlow.ParseFolders(text), Is.Empty);
	}

	[Test]
	public async Task The_folder_list_is_an_advanced_array_of_folders_with_a_dialog_per_entry()
	{
		var start = await _flow.StartAsync(new FakeEntryContext(null), CancellationToken.None);

		var folders = start.NextStep!.AdvancedFields.Single();
		Assert.Multiple(() =>
		{
			Assert.That(folders.Name, Is.EqualTo("libraryFolders"));
			Assert.That(folders.Type, Is.EqualTo(MacroDeck.Sdk.Actions.ActionParameterType.Array));
			Assert.That(folders.ItemTemplate!.Type, Is.EqualTo(MacroDeck.Sdk.Actions.ActionParameterType.Folder));
			Assert.That(start.NextStep.Fields.Select(field => field.Name), Does.Not.Contain("libraryFolders"));
		});
	}

	[Test]
	public async Task A_new_account_is_named_after_the_configuration_name_and_the_variable_key_follows_it()
	{
		var result = await Submit(new Dictionary<string, object?>
		{
			["name"] = "Alt Account",
			["steamId"] = SteamId,
			["apiKey"] = Key
		});

		Assert.Multiple(() =>
		{
			Assert.That(result.EntryTitle, Is.EqualTo("Alt Account"));
			Assert.That(result.Values!["variableKey"].Value, Is.EqualTo("alt_account"));
		});
	}

	[Test]
	public async Task Without_a_name_the_account_is_named_after_its_steam_profile()
	{
		var result = await Submit(new Dictionary<string, object?> { ["steamId"] = SteamId, ["apiKey"] = Key });

		Assert.Multiple(() =>
		{
			Assert.That(result.EntryTitle, Is.EqualTo("Ann"));
			Assert.That(result.Values!["variableKey"].Value, Is.EqualTo("ann"));
		});
	}

	[Test]
	public async Task Reconfiguring_keeps_the_stored_variable_key_and_the_title()
	{
		var result = await _flow.SubmitAsync("settings",
			new Dictionary<string, object?>
			{
				["steamId"] = SteamId,
				["apiKey"] = Key,
				["variableKey"] = "old_key"
			},
			new FakeEntryContext("Renamed"),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.EntryTitle, Is.EqualTo("Renamed"));
			Assert.That(result.Values!["variableKey"].Value, Is.EqualTo("old_key"));
		});
	}

	private sealed class FakeEntryContext(string? title) : IConfigFlowEntryContext
	{
		public IOAuthSession OAuth => throw new NotSupportedException();

		public string? EntryTitle { get; } = title;
	}

	[Test]
	public async Task A_step_that_does_not_exist_is_an_error()
	{
		var result = await _flow.SubmitAsync("bogus", new Dictionary<string, object?>(), null!, CancellationToken.None);

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
	}

	private Task<ConfigFlowResult> Submit(Dictionary<string, object?> input)
	{
		return _flow.SubmitAsync("settings", input, new FakeEntryContext(null), CancellationToken.None);
	}

	private sealed class FakeClient : ISteamWebApiClient
	{
		public Dictionary<string, string> Vanity { get; } = new(StringComparer.Ordinal);

		public HashSet<string> KnownIds { get; } = [SteamId];

		public SteamWebFailure? Failure { get; set; }

		public int Calls { get; private set; }

		public List<string> Order { get; } = [];

		public Task<SteamPlayerSummary?> GetPlayerSummaryAsync(string steamId, CancellationToken cancellationToken)
		{
			Calls++;
			Order.Add("summary:" + steamId);
			return Failure is { } failure
				? throw new SteamWebApiException(failure)
				: Task.FromResult(KnownIds.Contains(steamId)
					? new SteamPlayerSummary(steamId, "Ann", 1, null, null)
					: null);
		}

		public Task<IReadOnlyList<SteamPlayerSummary>> GetPlayerSummariesAsync(
			IReadOnlyList<string> steamIds,
			CancellationToken cancellationToken)
		{
			throw new NotSupportedException();
		}

		public Task<IReadOnlyList<string>?> GetFriendIdsAsync(string steamId, CancellationToken cancellationToken)
		{
			throw new NotSupportedException();
		}

		public Task<string?> ResolveVanityUrlAsync(string vanityName, CancellationToken cancellationToken)
		{
			Calls++;
			Order.Add("vanity:" + vanityName);
			return Failure is { } failure
				? throw new SteamWebApiException(failure)
				: Task.FromResult(Vanity.GetValueOrDefault(vanityName));
		}

		public Task<IReadOnlyList<SteamOwnedGame>?> GetOwnedGamesAsync(
			string steamId,
			CancellationToken cancellationToken)
		{
			return Task.FromResult<IReadOnlyList<SteamOwnedGame>?>(null);
		}
	}
}
