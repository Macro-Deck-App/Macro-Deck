using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Steam.Actions;
using MacroDeckHost.Integrations.Steam.Native;
using MacroDeckHost.Integrations.Steam.State;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Steam;

[TestFixture]
internal sealed class SteamCloseGameTests
{
	private static readonly TimeSpan _shortGrace = TimeSpan.FromMilliseconds(300);

	private static readonly SteamSnapshot _playing = new()
	{
		SteamFound = true,
		Games =
		[
			new SteamGame(730, "Counter-Strike 2", null, @"D:\Steam\steamapps\common\Counter-Strike Global Offensive")
		],
		CurrentGame = new SteamCurrentGame(730, "Counter-Strike 2", DateTimeOffset.UnixEpoch)
	};

	private FakeSteamPlatform _platform = null!;

	[SetUp]
	public void SetUp()
	{
		_platform = new FakeSteamPlatform { Os = SteamOs.Windows };
	}

	[Test]
	public async Task Without_a_game_running_here_nothing_is_closed()
	{
		var outcome = await Closer().CloseAsync(new SteamSnapshot { SteamFound = true }, false, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(SteamCommandOutcome.NotRunning));
			Assert.That(_platform.ProcessQueries, Is.Empty);
		});
	}

	[Test]
	public async Task The_running_game_is_asked_to_close_from_its_install_folder()
	{
		_platform.GameProcesses.AddRange([
			new SteamGameProcess(10, DateTime.UnixEpoch), new SteamGameProcess(11, DateTime.UnixEpoch)
		]);

		var outcome = await Closer().CloseAsync(_playing, false, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(SteamCommandOutcome.Succeeded));
			Assert.That(_platform.ProcessQueries.Single(),
				Is.EqualTo((730, (string?)@"D:\Steam\steamapps\common\Counter-Strike Global Offensive")));
			Assert.That(_platform.CloseRequests, Is.EquivalentTo(new[] { 10, 11 }));
			Assert.That(_platform.Killed, Is.Empty);
		});
	}

	[Test]
	public async Task A_game_that_ignores_the_request_is_left_running_unless_forced()
	{
		_platform.GameProcesses.Add(new SteamGameProcess(10, DateTime.UnixEpoch));
		_platform.IgnoresCloseRequests.Add(10);

		var outcome = await Closer().CloseAsync(_playing, false, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(SteamCommandOutcome.Failed));
			Assert.That(_platform.Killed, Is.Empty);
		});
	}

	[Test]
	public async Task A_forced_close_ends_a_game_that_ignores_the_request()
	{
		_platform.GameProcesses.Add(new SteamGameProcess(10, DateTime.UnixEpoch));
		_platform.IgnoresCloseRequests.Add(10);

		var outcome = await Closer().CloseAsync(_playing, true, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(SteamCommandOutcome.Succeeded));
			Assert.That(_platform.Killed, Is.EqualTo(new[] { 10 }));
		});
	}

	[Test]
	public async Task A_game_without_a_window_to_close_is_only_ended_when_forced()
	{
		_platform.GameProcesses.Add(new SteamGameProcess(10, DateTime.UnixEpoch));
		_platform.WithoutWindow.Add(10);

		var gentle = await Closer().CloseAsync(_playing, false, CancellationToken.None);
		var forced = await Closer().CloseAsync(_playing, true, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(gentle, Is.EqualTo(SteamCommandOutcome.Failed));
			Assert.That(forced, Is.EqualTo(SteamCommandOutcome.Succeeded));
			Assert.That(_platform.Killed, Is.EqualTo(new[] { 10 }));
		});
	}

	[Test]
	public async Task Where_closing_is_not_supported_the_action_says_so()
	{
		_platform.CanCloseGames = false;

		var outcome = await Closer().CloseAsync(_playing, false, CancellationToken.None);

		Assert.That(outcome, Is.EqualTo(SteamCommandOutcome.NotAvailable));
	}

	[Test]
	public async Task The_close_action_needs_no_game_and_passes_force_on()
	{
		var source = new RecordingSource();
		var action = SteamActions.Create(source).Single(definition => definition.Id == "close-game");

		var result = await action.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters
					= new Dictionary<string, object>(StringComparer.Ordinal) { [SteamActions.ForceParameter] = true },
				CancellationToken = CancellationToken.None
			});

		Assert.Multiple(() =>
		{
			Assert.That(action.Parameters.Select(parameter => parameter.Name),
				Is.EqualTo(new[] { SteamActions.ForceParameter }));
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(source.Commands.Single(), Is.EqualTo(new SteamCommand(SteamCommandKind.CloseGame, 0, true)));
		});
	}

	[TestCase(SteamCommandOutcome.NotRunning, "No Steam game is running on this computer.")]
	[TestCase(SteamCommandOutcome.Failed, "The game did not close.")]
	[TestCase(SteamCommandOutcome.NotAvailable, "Closing games is not supported on this operating system.")]
	public void A_failed_close_explains_itself(SteamCommandOutcome outcome, string message)
	{
		Assert.That(TestLocalization.Resolve(SteamActions.ToResult(outcome, SteamCommandKind.CloseGame).ErrorMessage),
			Is.EqualTo(message));
	}

	[TestCase(@"D:\Games\common\Portal\portal.exe", @"D:\Games\common\Portal", true, true)]
	[TestCase(@"D:\Games\common\Portal 2\portal2.exe", @"D:\Games\common\Portal", true, false)]
	[TestCase(@"d:\games\COMMON\portal\bin\portal.exe", @"D:\Games\common\Portal\", true, true)]
	[TestCase("/games/common/Portal/portal", "/games/common/portal", false, false)]
	[TestCase(@"D:\Games\common\Portal", @"D:\Games\common\Portal", true, false)]
	public void A_process_belongs_to_a_game_only_inside_its_folder(
		string path,
		string folder,
		bool ignoreCase,
		bool expected)
	{
		Assert.That(SteamGameProcesses.IsUnder(path, folder, ignoreCase), Is.EqualTo(expected));
	}

	[TestCase("Counter-Strike Global Offensive", true)]
	[TestCase("", false)]
	[TestCase(" ", false)]
	[TestCase(".", false)]
	[TestCase("..", false)]
	[TestCase(@"..\..\Windows", false)]
	[TestCase("../etc", false)]
	[TestCase(@"C:\Windows", false)]
	[TestCase("C:", false)]
	public void Only_a_plain_folder_name_from_a_manifest_is_used_to_find_a_game(string installDir, bool usable)
	{
		Assert.That(SteamLibrary.InstallDirectory(Path.Combine("lib", "steamapps", "appmanifest_1.acf"), installDir),
			usable ? Is.EqualTo(Path.Combine("lib", "steamapps", "common", installDir)) : Is.Null);
	}

	private SteamGameCloser Closer()
	{
		return new SteamGameCloser(_platform, TimeProvider.System, _shortGrace);
	}

	private sealed class RecordingSource : ISteamSource
	{
		public event EventHandler? Changed
		{
			add { }
			remove { }
		}

		public bool IsAvailable => true;

		public SteamSnapshot Snapshot => SteamSnapshot.Empty;

		public List<SteamCommand> Commands { get; } = [];

		public Task<SteamCommandOutcome> ExecuteAsync(SteamCommand command, CancellationToken cancellationToken)
		{
			Commands.Add(command);
			return Task.FromResult(SteamCommandOutcome.Succeeded);
		}
	}
}
