using MacroDeckHost.Integrations.Twitch;
using MacroDeckHost.Integrations.Twitch.Protocol;
using Serilog;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Twitch;

[TestFixture]
internal sealed class TwitchStatePollerTests
{
	private static readonly TwitchAccount _account = new(Guid.NewGuid(),
		"client-id",
		"111",
		"streamer",
		"Streamer",
		"streamer",
		DateTimeOffset.UtcNow);

	private static readonly string[] _hydrateReward = ["Hydrate"];

	private FakeTwitchHelixClient _helix = null!;
	private TwitchAccountState _state = null!;
	private List<TwitchCustomReward> _rewards = null!;
	private TwitchStatePoller _poller = null!;

	[SetUp]
	public void SetUp()
	{
		_helix = new FakeTwitchHelixClient();
		_state = TwitchAccountState.Unknown;
		_rewards = [];
		_poller = new TwitchStatePoller(_helix,
			_account,
			update =>
			{
				_state = update(_state);
				return _state;
			},
			rewards => _rewards = [.. rewards],
			SilentLogger());
	}

	[TearDown]
	public void TearDown()
	{
		_poller.Dispose();
	}

	[Test]
	public async Task One_cycle_fills_the_snapshot_from_five_reads()
	{
		_helix.Stream = new TwitchStreamInfo(true, 120, "Playing Hades", "Hades", DateTimeOffset.UtcNow.AddMinutes(-5));
		_helix.FollowerCount = 4200;
		_helix.Subscribers = new TwitchSubscriberInfo(12, 20);
		_helix.ChatSettings = new TwitchChatSettings(true, false, null, false, null, false, false);

		await _poller.PollOnceAsync(includeRewards: false, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_state.IsLive, Is.True);
			Assert.That(_state.ViewerCount, Is.EqualTo(120));
			Assert.That(_state.StreamTitle, Is.EqualTo("Playing Hades"));
			Assert.That(_state.FollowerCount, Is.EqualTo(4200));
			Assert.That(_state.SubscriberCount, Is.EqualTo(12));
			Assert.That(_state.SubscriberPoints, Is.EqualTo(20));
			Assert.That(_state.ChatSettings!.EmoteOnly, Is.True);
		});
	}

	[Test]
	public async Task A_failed_read_leaves_the_previous_value_standing()
	{
		_helix.FollowerCount = 4200;
		await _poller.PollOnceAsync(includeRewards: false, CancellationToken.None);

		_helix.FailingReads.Add("followers");
		_helix.FailingReads.Add("subscribers");
		await _poller.PollOnceAsync(includeRewards: false, CancellationToken.None);

		Assert.That(_state.FollowerCount, Is.EqualTo(4200));
	}

	[Test]
	public async Task An_offline_channel_is_reported_offline_without_losing_its_title()
	{
		_helix.Stream = new TwitchStreamInfo(true, 120, "Playing Hades", "Hades", DateTimeOffset.UtcNow);
		await _poller.PollOnceAsync(includeRewards: false, CancellationToken.None);

		_helix.Stream = new TwitchStreamInfo(false, 0, null, null, null);
		_helix.Channel = new TwitchChannelInfo("Playing Hades", "1", "Hades", "en");
		await _poller.PollOnceAsync(includeRewards: false, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_state.IsLive, Is.False);
			Assert.That(_state.StreamTitle, Is.EqualTo("Playing Hades"), "the channel keeps its title while offline");
			Assert.That(_state.StreamStartedAt, Is.Null);
		});
	}

	[Test]
	public async Task Rewards_are_only_fetched_when_asked_for()
	{
		_helix.Rewards = [new TwitchCustomReward("r1", "Hydrate")];

		await _poller.PollOnceAsync(includeRewards: false, CancellationToken.None);
		Assert.That(_rewards, Is.Empty);

		await _poller.PollOnceAsync(includeRewards: true, CancellationToken.None);
		Assert.That(_rewards.Select(reward => reward.Title), Is.EqualTo(_hydrateReward));
	}

	[Test]
	public async Task A_failed_reward_fetch_keeps_the_known_list()
	{
		_helix.Rewards = [new TwitchCustomReward("r1", "Hydrate")];
		await _poller.PollOnceAsync(includeRewards: true, CancellationToken.None);

		_helix.FailingReads.Add("rewards");
		await _poller.PollOnceAsync(includeRewards: true, CancellationToken.None);

		Assert.That(_rewards.Select(reward => reward.Title), Is.EqualTo(_hydrateReward));
	}

	[Test]
	public async Task A_scope_refusal_does_not_stop_the_rest_of_the_cycle()
	{
		_helix.FailingReads.Add("subscribers");
		_helix.FollowerCount = 7;

		await _poller.PollOnceAsync(includeRewards: false, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_state.FollowerCount, Is.EqualTo(7));
			Assert.That(_state.SubscriberCount, Is.Null);
		});
	}

	private static Logger SilentLogger() => new LoggerConfiguration().CreateLogger();
}
