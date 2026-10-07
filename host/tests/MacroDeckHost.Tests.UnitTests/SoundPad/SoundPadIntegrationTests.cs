using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeckHost.Integrations.SoundPad;
using MacroDeckHost.Tests.UnitTests.SinusBot;

namespace MacroDeckHost.Tests.UnitTests.SoundPad;

[TestFixture]
public class SoundPadIntegrationTests
{
	private FakeSoundPadClient _client = null!;
	private SoundPadIntegration _integration = null!;

	[SetUp]
	public void SetUp()
	{
		_client = new FakeSoundPadClient();
		_integration = new SoundPadIntegration(() => _client, TimeSpan.FromMilliseconds(20));
	}

	[TearDown]
	public void TearDown()
	{
		_integration.Dispose();
		_client.Dispose();
	}

	private async Task InitializeConfiguredAsync()
	{
		var context = new FakeSinusBotIntegrationContext();
		context.ConfigStore.AddEntry("SoundPad", new Dictionary<string, string?>());
		await _integration.InitializeAsync(context);
	}

	private async Task<ActionResult> RunAsync(string actionId, Dictionary<string, object>? parameters = null)
	{
		var action = _integration.Actions.Single(definition => definition.Id == actionId);
		return await action.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext { Parameters = parameters ?? [] });
	}

	[Test]
	public async Task WithoutSetup_OffersNoMusicPlayer()
	{
		await _integration.InitializeAsync(new FakeSinusBotIntegrationContext());

		Assert.Multiple(() =>
		{
			Assert.That(_integration.GetInstances(), Is.Empty);
			Assert.That(_client.ConnectAttempts, Is.Zero);
		});
	}

	[Test]
	public async Task AfterSetup_OffersOneSoundPadMusicPlayer()
	{
		await InitializeConfiguredAsync();

		var instance = _integration.GetInstances().Single();

		Assert.Multiple(() =>
		{
			Assert.That(instance.DisplayName, Is.EqualTo("SoundPad"));
			Assert.That(_integration.GetPlayer(instance.Id), Is.Not.Null);
		});
	}

	[Test]
	public async Task Initialize_ReturnsWhileSoundPadNeverAnswers()
	{
		_client.HangConnect = true;

		var initialize = InitializeConfiguredAsync();

		Assert.That(await Task.WhenAny(initialize, Task.Delay(TimeSpan.FromSeconds(2))), Is.SameAs(initialize));
	}

	[Test]
	public async Task Shutdown_ReleasesThePipe()
	{
		await InitializeConfiguredAsync();
		await SoundPadTestSupport.WaitUntilAsync(() => _client.IsConnected);

		await _integration.ShutdownAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_client.Disposed, Is.True);
			Assert.That(_integration.GetInstances(), Is.Empty);
		});
	}

	[Test]
	public void OffersNoShuffleOrRepeat()
	{
		var ids = _integration.Actions.Select(action => action.Id).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(ids, Does.Not.Contain("toggle-shuffle"));
			Assert.That(ids, Does.Not.Contain("set-repeat-mode"));
			Assert.That(ids, Does.Contain("play-sound"));
			Assert.That(ids, Does.Contain("pause"));
		});
	}

	[TestCase("default", "record Default")]
	[TestCase("microphone", "record Microphone")]
	[TestCase("speakers", "record Speakers")]
	public async Task StartRecording_UsesTheChosenSource(string source, string expected)
	{
		await InitializeConfiguredAsync();
		await SoundPadTestSupport.WaitUntilAsync(() => _client.IsConnected);

		var result = await RunAsync(SoundPadActions.StartRecordingId,
			new Dictionary<string, object> { [SoundPadActions.SourceParameter] = source });

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_client.Commands, Is.EqualTo(new[] { expected }));
		});
	}

	[Test]
	public async Task StopPlaybackAndRecording_ReachSoundPad()
	{
		await InitializeConfiguredAsync();
		await SoundPadTestSupport.WaitUntilAsync(() => _client.IsConnected);

		await RunAsync(SoundPadActions.StopPlaybackId);
		await RunAsync(SoundPadActions.StopRecordingId);
		await RunAsync(SoundPadActions.ToggleMuteId);

		Assert.That(_client.Commands, Is.EqualTo(new[] { "stop", "stop recording", "toggle mute" }));
	}

	[Test]
	public async Task PlayRandomSound_FromAllSoundsOrOneCategory()
	{
		await InitializeConfiguredAsync();
		await SoundPadTestSupport.WaitUntilAsync(() => _client.IsConnected);

		await RunAsync(SoundPadActions.PlayRandomSoundId);
		await RunAsync(SoundPadActions.PlayRandomSoundId,
			new Dictionary<string, object>
			{
				[SoundPadActions.CategoryParameter] = "2", [SoundPadActions.MicrophoneParameter] = false
			});

		Assert.That(_client.Commands,
			Is.EqualTo(new[]
			{
				"random all speakers=True microphone=True", "random 2 speakers=True microphone=False"
			}));
	}

	[Test]
	public async Task Actions_WhileSoundPadIsClosed_FailWithoutThrowing()
	{
		_client.Reachable = false;
		await InitializeConfiguredAsync();

		var result = await RunAsync(SoundPadActions.StopPlaybackId);

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
	}

	[Test]
	public async Task RecordingVariable_KeepsTheMacroDeck2Name()
	{
		await InitializeConfiguredAsync();
		await SoundPadTestSupport.WaitUntilAsync(() => _client.IsConnected);
		_client.RecordingPositionMs = 1200;
		await _integration.GetPlayer(SoundPadIntegration.InstanceId)!.GetStateAsync();

		var reading = await _integration.ReadAsync("soundpad-recording");

		Assert.Multiple(() =>
		{
			Assert.That(_integration.Variables.Select(variable => variable.Name), Does.Contain("soundpad_recording"));
			Assert.That(reading.Value, Is.EqualTo(true));
		});
	}

	[Test]
	public async Task ConnectedVariable_FollowsSoundPad()
	{
		_client.Reachable = false;
		await InitializeConfiguredAsync();

		var before = await _integration.ReadAsync("soundpad-is-connected");
		_client.Reachable = true;
		await SoundPadTestSupport.WaitUntilAsync(() => _client.IsConnected);
		var after = await _integration.ReadAsync("soundpad-is-connected");

		Assert.Multiple(() =>
		{
			Assert.That(before.Value, Is.EqualTo(false));
			Assert.That(after.Value, Is.EqualTo(true));
		});
	}

	[Test]
	public async Task Setup_WhenSoundPadAnswers_Completes()
	{
		var flow = _integration.CreateConfigFlow();

		var result = await flow.SubmitAsync("connect", new Dictionary<string, object?>(), null!, CancellationToken.None);

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
	}

	[Test]
	public async Task Setup_WhenSoundPadIsClosed_AsksToStartIt()
	{
		_client.Reachable = false;
		var flow = _integration.CreateConfigFlow();

		var result = await flow.SubmitAsync("connect", new Dictionary<string, object?>(), null!, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.NextStep?.StepId, Is.EqualTo("connect"));
		});
	}

	[Test]
	public async Task Setup_WhileAlreadyConnected_ReusesTheLiveConnection()
	{
		await InitializeConfiguredAsync();
		await SoundPadTestSupport.WaitUntilAsync(() => _client.IsConnected);
		var attempts = _client.ConnectAttempts;

		var result = await _integration.CreateConfigFlow()
			.SubmitAsync("connect", new Dictionary<string, object?>(), null!, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(_client.ConnectAttempts, Is.EqualTo(attempts));
			Assert.That(_client.Disposed, Is.False);
		});
	}
}
