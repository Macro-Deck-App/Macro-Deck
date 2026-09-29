using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.MusicPlayer;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.MusicPlayer;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class MusicPlayerOptionsCapabilityHandlerTests
{
	private static ServiceProvider _services = null!;

	[OneTimeSetUp]
	public static void OneTimeSetUp() => _services = new ServiceCollection().BuildServiceProvider();

	[OneTimeTearDown]
	public static void OneTimeTearDown() => _services.Dispose();

	[Test]
	public async Task Describe_carries_the_supported_options_and_leaves_out_the_rest()
	{
		var handler = Handler(new CyclingIntegration(
		[
			ActionParameter.Number("cycleSeconds", "Cycle every", min: 5, max: 60, defaultValue: 10),
			ActionParameter.Text("bad.name", "Dotted"),
			ActionParameter.Color("tint", "Tint"),
			ActionParameter.Toggle("cycleSeconds", "Duplicate"),
		]));

		var result = await handler.InvokeAsync(Invocation(CapabilityOperations.MusicPlayer.Describe),
			CancellationToken.None);

		var payload = result.Data!.Value.Deserialize<MusicPlayerDescribePayload>(PluginProtocolJson.Options)!;
		var options = payload.Instances.Single().Options;
		Assert.Multiple(() =>
		{
			Assert.That(options.Select(option => option.Name), Is.EqualTo(new[] { "cycleSeconds" }));
			Assert.That(options[0].Type, Is.EqualTo(nameof(ActionParameterType.Number)));
		});
	}

	[Test]
	public async Task A_state_read_with_options_resolves_the_player_with_every_declared_option_typed_and_bounded()
	{
		var integration = new CyclingIntegration(
		[
			ActionParameter.Number("cycleSeconds", "Cycle every", min: 5, max: 60, defaultValue: 10),
			ActionParameter.Toggle("skipPaused", "Skip paused apps", defaultValue: true),
			ActionParameter.Choice("order", [Option("newest"), Option("oldest")], "Order", defaultValue: "oldest"),
			ActionParameter.Text("label", "Label"),
		]);
		var handler = Handler(integration);

		var result = await handler.InvokeAsync(Invocation(CapabilityOperations.MusicPlayer.State,
				new
				{
					instanceId = CyclingIntegration.InstanceId,
					options = new Dictionary<string, object> { ["cycleSeconds"] = 500, ["order"] = "sideways", ["unknown"] = 1 }
				}),
			CancellationToken.None);

		Assert.That(result.IsFailure, Is.False);
		var request = integration.Requests.Single();
		Assert.Multiple(() =>
		{
			Assert.That(request.InstanceId, Is.EqualTo(CyclingIntegration.InstanceId));
			Assert.That(request.Options, Is.EquivalentTo(new Dictionary<string, object>
			{
				["cycleSeconds"] = 60d, ["skipPaused"] = true, ["order"] = "oldest", ["label"] = string.Empty,
			}));
			Assert.That(State(result).TrackName, Is.EqualTo("cycling every 60"));
		});
	}

	[Test]
	public async Task A_state_read_without_options_resolves_the_plain_instance()
	{
		var integration = new CyclingIntegration([ActionParameter.Number("cycleSeconds", defaultValue: 10)]);
		var handler = Handler(integration);

		var result = await handler.InvokeAsync(Invocation(CapabilityOperations.MusicPlayer.State,
				new { instanceId = CyclingIntegration.InstanceId }),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(integration.Requests, Is.Empty);
			Assert.That(State(result).TrackName, Is.EqualTo("plain"));
		});
	}

	[Test]
	public async Task Artwork_is_read_from_the_player_resolved_with_the_options()
	{
		var integration = new CyclingIntegration([ActionParameter.Number("cycleSeconds", defaultValue: 10)]);
		var handler = Handler(integration);

		var result = await handler.InvokeAsync(Invocation(CapabilityOperations.MusicPlayer.Artwork,
				new
				{
					instanceId = CyclingIntegration.InstanceId,
					artworkId = "cover",
					options = new Dictionary<string, object> { ["cycleSeconds"] = 20 }
				}),
			CancellationToken.None);

		var artwork = result.Data!.Value.Deserialize<MusicPlayerArtworkResult>(PluginProtocolJson.Options)!;
		Assert.That(Convert.FromBase64String(artwork.Data!), Is.EqualTo(new byte[] { 20 }));
	}

	private static MusicPlayerCapabilityHandler Handler(CyclingIntegration integration)
		=> new([integration], TestMetadata.Default, new FakeAssetUploader());

	private static ActionParameterOption Option(string value) => new() { Value = value, Label = value };

	private static MusicPlayerStateDto State(CapabilityInvocationResult result)
		=> result.Data!.Value.Deserialize<MusicPlayerStateDto>(PluginProtocolJson.Options)!;

	private static CapabilityInvocation Invocation(string operation, object? arguments = null)
		=> new()
		{
			Kind = CapabilityKinds.MusicPlayer,
			LocalId = ProviderCapabilityId.LocalId,
			Operation = operation,
			Arguments = arguments is null
				? null
				: JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options),
			CorrelationId = "correlation",
			Services = _services
		};

	private sealed class CyclingIntegration(IReadOnlyList<ActionParameter> options)
		: IPluginIntegration, IMusicPlayerProvider
	{
		public const string InstanceId = "any-app";

		public List<MusicPlayerOptionsRequest> Requests { get; } = [];

		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public IReadOnlyList<MusicPlayerInstance> GetInstances()
			=> [new MusicPlayerInstance(InstanceId, "Any app") { Options = options }];

		public IMusicPlayer? GetPlayer(string instanceId)
			=> instanceId == InstanceId ? Player("plain", 0) : null;

		public IMusicPlayer? GetPlayerWithOptions(MusicPlayerOptionsRequest request)
		{
			Requests.Add(request);
			var seconds = Convert.ToDouble(request.Options["cycleSeconds"], System.Globalization.CultureInfo.InvariantCulture);

			return Player($"cycling every {seconds}", (byte)seconds);
		}

		private static TestMusicPlayer Player(string track, byte cover)
			=> new()
			{
				StateToReturn = new MusicPlayerState { IsConnected = true, TrackName = track },
				ArtworkToReturn = new MusicPlayerArtwork([cover], "image/png"),
			};
	}
}
