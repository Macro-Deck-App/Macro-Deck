using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Steam.Actions;
using MacroDeckHost.Integrations.Steam.Native;
using MacroDeckHost.Integrations.Steam.State;
using MacroDeckHost.Platform.Applications;
using MacroDeckHost.Tests.UnitTests.System;

namespace MacroDeckHost.Tests.UnitTests.Steam;

[TestFixture]
internal sealed class SteamLaunchTests
{
	[TestCase("steam://rungameid/730")]
	[TestCase("steam://open/main")]
	[TestCase("steam://store/730")]
	[TestCase("steam://nav/games/details/730")]
	public void Steam_uris_are_allowed(string uri)
	{
		Assert.That(ApplicationServiceBase.IsSteamUri(uri), Is.True);
	}

	[TestCase("http://example.com")]
	[TestCase("https://store.steampowered.com/app/730")]
	[TestCase("file:///C:/Windows/System32/calc.exe")]
	[TestCase("javascript:alert(1)")]
	[TestCase("calc.exe")]
	[TestCase("steam://rungameid/730 --evil")]
	[TestCase("steam://rungameid/730\" & calc")]
	[TestCase("steam://rungameid/730\nsteam://open/main")]
	[TestCase("STEAM://rungameid/730x y")]
	[TestCase("")]
	[TestCase(null)]
	public void Anything_else_is_refused(string? uri)
	{
		Assert.That(ApplicationServiceBase.IsSteamUri(uri), Is.False);
	}

	[TestCase("http://example.com")]
	[TestCase("file:///C:/Windows/System32/calc.exe")]
	[TestCase("javascript:alert(1)")]
	public void The_real_application_service_refuses_anything_but_steam_uris(string uri)
	{
		Assert.Throws<NotSupportedException>(()
			=> ApplicationServiceFactory.Create(Serilog.Core.Logger.None).OpenUri(uri));
	}

	[Test]
	public void A_platform_that_cannot_open_uris_throws_instead_of_pretending_to_succeed()
	{
		Assert.Throws<NotSupportedException>(() => new NullApplicationService().OpenUri("steam://open/main"));
	}

	[TestCase(SteamCommandKind.LaunchGame, 730, "steam://rungameid/730")]
	[TestCase(SteamCommandKind.LaunchSteam, 0, "steam://open/main")]
	[TestCase(SteamCommandKind.OpenStorePage, 730, "steam://store/730")]
	[TestCase(SteamCommandKind.OpenLibraryPage, 730, "steam://nav/games/details/730")]
	public async Task Each_command_opens_the_matching_steam_uri(SteamCommandKind kind, int appId, string expected)
	{
		var applications = new FakeApplicationService();

		var outcome = await new SteamLauncher(applications).ExecuteAsync(new SteamCommand(kind, appId),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(SteamCommandOutcome.Succeeded));
			Assert.That(applications.OpenedUris, Is.EqualTo(new[] { expected }));
		});
	}

	[TestCase(SteamCommandKind.LaunchGame, 0)]
	[TestCase(SteamCommandKind.LaunchGame, -5)]
	[TestCase(SteamCommandKind.OpenStorePage, 0)]
	[TestCase(SteamCommandKind.OpenLibraryPage, -1)]
	public async Task A_game_command_without_a_valid_app_id_opens_nothing(SteamCommandKind kind, int appId)
	{
		var applications = new FakeApplicationService();

		var outcome = await new SteamLauncher(applications).ExecuteAsync(new SteamCommand(kind, appId),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(SteamCommandOutcome.InvalidAppId));
			Assert.That(applications.OpenedUris, Is.Empty);
		});
	}

	[Test]
	public async Task A_failure_to_open_the_uri_is_reported()
	{
		var applications = new FakeApplicationService
			{ OpenUriException = new InvalidOperationException("no handler") };

		var outcome = await new SteamLauncher(applications).ExecuteAsync(new SteamCommand(SteamCommandKind.LaunchSteam),
			CancellationToken.None);

		Assert.That(outcome, Is.EqualTo(SteamCommandOutcome.Failed));
	}

	[TestCase("730", 730)]
	[TestCase("1", 1)]
	[TestCase("2147483647", int.MaxValue)]
	public void App_ids_are_plain_positive_numbers(string text, int expected)
	{
		Assert.That(SteamLauncher.ParseAppId(text), Is.EqualTo(expected));
	}

	[TestCase("")]
	[TestCase("0")]
	[TestCase("-1")]
	[TestCase("73 0")]
	[TestCase("730/../x")]
	[TestCase("730abc")]
	[TestCase("2147483648")]
	[TestCase("١٢٣")]
	[TestCase(null)]
	public void Anything_that_is_not_a_plain_positive_number_is_not_an_app_id(string? text)
	{
		Assert.That(SteamLauncher.ParseAppId(text), Is.Null);
	}

	[Test]
	public async Task The_launch_action_resolves_a_picked_game_or_typed_id_and_rejects_garbage()
	{
		var target = new RecordingTarget();
		var action = SteamActions.Create(target).Single(candidate => candidate.Id == "launch-game");

		var picked = await action.CreateExecutor().ExecuteAsync(Context(("game", "730")));
		var typed = await action.CreateExecutor().ExecuteAsync(Context(("game", " 220 ")));
		var garbage = await action.CreateExecutor().ExecuteAsync(Context(("game", "730; calc")));
		var missing = await action.CreateExecutor().ExecuteAsync(Context());

		Assert.Multiple(() =>
		{
			Assert.That(picked.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(typed.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(garbage.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(missing.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(target.Commands,
				Is.EqualTo(new[]
				{
					new SteamCommand(SteamCommandKind.LaunchGame, 730),
					new SteamCommand(SteamCommandKind.LaunchGame, 220)
				}));
		});
	}

	[Test]
	public async Task Launching_steam_needs_no_game()
	{
		var target = new RecordingTarget();
		var action = SteamActions.Create(target).Single(candidate => candidate.Id == "launch-steam");

		var result = await action.CreateExecutor().ExecuteAsync(Context());

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(target.Commands, Is.EqualTo(new[] { new SteamCommand(SteamCommandKind.LaunchSteam) }));
			Assert.That(action.Parameters, Is.Empty);
		});
	}

	[Test]
	public async Task The_game_picker_offers_installed_games_filtered_by_what_was_typed_and_allows_a_custom_id()
	{
		var target = new RecordingTarget
		{
			Snapshot = new SteamSnapshot
			{
				SteamFound = true,
				Games =
				[
					new SteamGame(730, "Counter-Strike 2", null),
					new SteamGame(220, "Half-Life 2", null)
				]
			}
		};
		var action = (IDynamicOptionsActionDefinition)SteamActions.Create(target).Single(a => a.Id == "launch-game");

		var result = await action.GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = "game",
				Filter = "half",
				CurrentParameters = new Dictionary<string, object?>()
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Options.Select(option => option.Value), Is.EqualTo(new[] { "220" }));
			Assert.That(result.AllowsCustomValue, Is.True);
		});
	}

	[TestCase(SteamCommandOutcome.NotAvailable)]
	[TestCase(SteamCommandOutcome.Failed)]
	[TestCase(SteamCommandOutcome.InvalidAppId)]
	public async Task A_command_that_did_not_succeed_fails_the_action(SteamCommandOutcome outcome)
	{
		var target = new RecordingTarget { Outcome = outcome };
		var action = SteamActions.Create(target).Single(candidate => candidate.Id == "launch-steam");

		Assert.That((await action.CreateExecutor().ExecuteAsync(Context())).Status,
			Is.EqualTo(ActionResultStatus.Failed));
	}

	private static ActionExecutionContext Context(params (string Name, object Value)[] parameters)
	{
		return new ActionExecutionContext
		{
			Parameters = parameters.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal),
			CancellationToken = CancellationToken.None
		};
	}

	private sealed class RecordingTarget : ISteamSource
	{
		public event EventHandler? Changed
		{
			add { }
			remove { }
		}

		public List<SteamCommand> Commands { get; } = [];

		public SteamCommandOutcome Outcome { get; set; } = SteamCommandOutcome.Succeeded;

		public bool IsAvailable => true;

		public SteamSnapshot Snapshot { get; set; } = SteamSnapshot.Empty;

		public Task<SteamCommandOutcome> ExecuteAsync(SteamCommand command, CancellationToken cancellationToken)
		{
			Commands.Add(command);
			return Task.FromResult(Outcome);
		}
	}
}
