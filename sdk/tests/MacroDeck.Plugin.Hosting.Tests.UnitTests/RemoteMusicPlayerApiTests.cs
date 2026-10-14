using System.Text.Json;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class RemoteMusicPlayerApiTests
{
	[Test]
	public async Task The_players_follow_each_push_and_a_stale_push_is_ignored()
	{
		var cache = new HostStateCache(new PluginConnectionState());
		using var api = new RemoteMusicPlayerApi(cache, new AnsweringHost(null), Serilog.Core.Logger.None);
		var raised = 0;
		api.Lifecycle.InstancesChanged += (_, _) => Interlocked.Increment(ref raised);
		var before = api.Instances;

		cache.Apply(Push(2, ("spotify::alice", "Spotify (alice)"), ("net.example.jukebox::default", "Jukebox")));
		cache.Apply(Push(1, ("stale::x", "Stale")));
		await WaitForAsync(() => Volatile.Read(ref raised) == 1);

		Assert.Multiple(() =>
		{
			Assert.That(before, Is.Empty);
			Assert.That(api.Instances.Select(instance => (instance.Id, instance.ProviderId, instance.DisplayName)),
				Is.EqualTo(new[]
				{
					("spotify::alice", "spotify", "Spotify (alice)"),
					("net.example.jukebox::default", "net.example.jukebox", "Jukebox")
				}));
			Assert.That(Volatile.Read(ref raised), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Handlers_subscribed_through_the_context_are_released_on_reinitialization()
	{
		var cache = new HostStateCache(new PluginConnectionState());
		using var api = new RemoteMusicPlayerApi(cache, new AnsweringHost(null), Serilog.Core.Logger.None);
		var lifecycle = 0;
		var injected = 0;
		api.Lifecycle.InstancesChanged += (_, _) => Interlocked.Increment(ref lifecycle);
		api.InstancesChanged += (_, _) => Interlocked.Increment(ref injected);

		api.ReleaseLifecycleHandlers();
		cache.Apply(Push(1, ("spotify::alice", "Spotify (alice)")));
		await WaitForAsync(() => Volatile.Read(ref injected) == 1);

		Assert.That(Volatile.Read(ref lifecycle), Is.Zero);
	}

	[Test]
	public async Task The_album_colour_is_what_the_host_answers()
	{
		var host = new AnsweringHost(new MusicPlayerAlbumColorResult { Color = "#102030" });
		using var api = new RemoteMusicPlayerApi(new HostStateCache(new PluginConnectionState()),
			host,
			Serilog.Core.Logger.None);

		var color = await api.GetAlbumColorAsync("spotify::alice");

		Assert.Multiple(() =>
		{
			Assert.That(color, Is.EqualTo("#102030"));
			Assert.That(host.Calls,
				Is.EqualTo(new[] { (HostApis.MusicPlayers, HostOperations.MusicPlayers.AlbumColor) }));
			Assert.That(host.Arguments.Single().GetProperty("instanceId").GetString(), Is.EqualTo("spotify::alice"));
		});
	}

	[Test]
	public async Task A_macro_deck_without_the_api_has_no_players_and_no_album_colour()
	{
		var host = new AnsweringHost(null)
		{
			Failure = HostInvocationException.CreateNonRetryable(ProtocolErrorCodes.CapabilityUnsupported, "no")
		};
		using var api = new RemoteMusicPlayerApi(new HostStateCache(new PluginConnectionState()),
			host,
			Serilog.Core.Logger.None);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(api.Instances, Is.Empty);
			Assert.That(await api.GetAlbumColorAsync("spotify::alice"), Is.Null);
		});
	}

	private static ProtocolEnvelope Push(long revision, params (string Id, string Name)[] players)
	{
		return new ProtocolEnvelope
		{
			Type = MessageTypes.HostState,
			Id = Guid.NewGuid().ToString(),
			Payload = JsonSerializer.SerializeToElement(new HostStatePayload
				{
					Api = HostApis.MusicPlayers,
					Data = JsonSerializer.SerializeToElement(new MusicPlayersStateDto
						{
							Revision = revision,
							Instances =
							[
								.. players.Select(player => new MusicPlayerInstanceStateDto
								{
									Id = player.Id,
									ProviderId = player.Id[..player.Id.IndexOf("::", StringComparison.Ordinal)],
									DisplayName = player.Name
								})
							]
						},
						PluginProtocolJson.Options)
				},
				PluginProtocolJson.Options)
		};
	}

	private static async Task WaitForAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (DateTime.UtcNow < deadline && !condition())
		{
			await Task.Delay(10);
		}

		Assert.That(condition(), Is.True, "the expected state was never reached");
	}

	private sealed class AnsweringHost(object? answer) : IHostInvoker
	{
		public List<(string Api, string Operation)> Calls { get; } = [];

		public List<JsonElement> Arguments { get; } = [];

		public Exception? Failure { get; init; }

		public Task<JsonElement?> InvokeAsync(
			string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
		{
			Calls.Add((api, operation));
			Arguments.Add(JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options));
			if (Failure is { } failure)
			{
				return Task.FromException<JsonElement?>(failure);
			}

			return Task.FromResult<JsonElement?>(answer is null
				? null
				: JsonSerializer.SerializeToElement(answer, PluginProtocolJson.Options));
		}

		public bool TryComplete(ProtocolEnvelope result)
		{
			return false;
		}
	}
}
