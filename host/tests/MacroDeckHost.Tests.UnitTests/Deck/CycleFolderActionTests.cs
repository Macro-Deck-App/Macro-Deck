using System.Text.Json;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Integrations.Deck;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Deck;

[TestFixture]
internal sealed class CycleFolderActionTests
{
	private const string Streaming = "p-streaming";
	private const string Gaming = "p-gaming";
	private const string DeviceId = "0f8fad5b-d9cb-469f-a165-70867728950e";

	private static List<DeckFolder> Folders()
	{
		return
		[
			Folder("chat", "Streaming / Chat", Streaming, null, 2),
			Folder("audio", "Streaming / Audio", Streaming, "home", 5),
			Folder("home", "Streaming / Home", Streaming, null, 0),
			Folder("mics", "Streaming / Mics", Streaming, "audio", 0),
			Folder("scenes", "Streaming / Scenes", Streaming, "home", 1),
			Folder("games", "Gaming / Games", Gaming, null, 0)
		];
	}

	private static DeckFolder Folder(string id, string label, string profileId, string? parentId, int order)
	{
		return new DeckFolder
		{
			Id = id,
			Label = label,
			ProfileId = profileId,
			ParentId = parentId,
			Order = order
		};
	}

	private static DeckClient Client(
		string clientId,
		string folderId,
		string profileId = Streaming,
		string? deviceId = null)
	{
		return new DeckClient { ClientId = clientId, DeviceId = deviceId, ProfileId = profileId, FolderId = folderId };
	}

	private static RecordingDeckNavigator Navigator(params DeckClient[] clients)
	{
		return new RecordingDeckNavigator { Folders = Folders(), Clients = [.. clients] };
	}

	private static Task<ActionResult> Run(
		string actionId,
		RecordingDeckNavigator navigator,
		Dictionary<string, object> parameters,
		string? originClientId = null)
	{
		var integration = new DeckNavigationIntegration();
		integration.InitializeAsync(new DeckOnlyContext(navigator)).GetAwaiter().GetResult();
		return integration.Actions.Single(action => action.Id == actionId)
			.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext { Parameters = parameters, OriginClientId = originClientId });
	}

	private static async Task<List<string>> Walk(string actionId, string start, Dictionary<string, object> parameters)
	{
		var visited = new List<string>();
		var current = start;
		for (var press = 0; press < 6; press++)
		{
			var navigator = Navigator(Client("web-1", current));
			await Run(actionId, navigator, parameters, "web-1");
			current = navigator.FolderChanges.Single().FolderId;
			visited.Add(current);
		}

		return visited;
	}

	[Test]
	public async Task NextFolder_InFolderOrder_WalksTheProfileTreeAsShownAndWrapsToTheStart()
	{
		var visited = await Walk("next-folder", "home", []);

		Assert.That(visited, Is.EqualTo(new[] { "scenes", "audio", "mics", "chat", "home", "scenes" }));
	}

	[Test]
	public async Task PreviousFolder_InFolderOrder_WalksTheTreeBackwardsAndWrapsToTheEnd()
	{
		var parameters = new Dictionary<string, object> { ["order"] = "folder-order" };

		var visited = await Walk("previous-folder", "home", parameters);

		Assert.That(visited, Is.EqualTo(new[] { "chat", "mics", "audio", "scenes", "home", "chat" }));
	}

	[Test]
	public async Task NextFolder_Alphabetical_SortsByNameWithinTheOpenProfile()
	{
		var visited = await Walk("next-folder", "audio", new Dictionary<string, object> { ["order"] = "alphabetical" });

		Assert.That(visited, Is.EqualTo(new[] { "chat", "home", "mics", "scenes", "audio", "chat" }));
	}

	[Test]
	public async Task Alphabetical_PlacesAccentedNamesWhereTheyBelongInTheAlphabet()
	{
		var navigator = new RecordingDeckNavigator
		{
			Folders =
			[
				Folder("zeit", "Zeit", Streaming, null, 0),
				Folder("uebersicht", "Übersicht", Streaming, null, 1),
				Folder("anfang", "Anfang", Streaming, null, 2)
			],
			Clients = [Client("web-1", "anfang")]
		};

		await Run("next-folder", navigator, new Dictionary<string, object> { ["order"] = "alphabetical" }, "web-1");

		Assert.That(navigator.FolderChanges.Single().FolderId, Is.EqualTo("uebersicht"));
	}

	[Test]
	public async Task Custom_FollowsTheTickedOrderAcrossProfiles()
	{
		var parameters = new Dictionary<string, object>
		{
			["order"] = "custom",
			["folders"] = JsonSerializer.Deserialize<JsonElement>("""["chat","games","home"]""")
		};

		var visited = await Walk("next-folder", "chat", parameters);

		Assert.That(visited, Is.EqualTo(new[] { "games", "home", "chat", "games", "home", "chat" }));
	}

	[Test]
	public async Task Custom_FromAFolderOutsideTheList_NextStartsAtTheFirstAndPreviousAtTheLast()
	{
		var parameters = new Dictionary<string, object>
		{
			["order"] = "custom",
			["folders"] = new List<string> { "chat", "scenes" }
		};
		var next = Navigator(Client("web-1", "mics"));
		var previous = Navigator(Client("web-1", "mics"));

		await Run("next-folder", next, parameters, "web-1");
		await Run("previous-folder", previous, parameters, "web-1");

		Assert.Multiple(() =>
		{
			Assert.That(next.FolderChanges.Single().FolderId, Is.EqualTo("chat"));
			Assert.That(previous.FolderChanges.Single().FolderId, Is.EqualTo("scenes"));
		});
	}

	[Test]
	public async Task Custom_WithNoFoldersPicked_FailsWithoutMoving()
	{
		var navigator = Navigator(Client("web-1", "home"));

		var parameters = new Dictionary<string, object> { ["order"] = "custom" };

		var result = await Run("next-folder", navigator, parameters, "web-1");

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(navigator.FolderChanges, Is.Empty);
		});
	}

	[TestCase("next-folder", "chat")]
	[TestCase("previous-folder", "home")]
	public async Task WrapOff_AtTheEnd_StaysAndSucceeds(string actionId, string end)
	{
		var navigator = Navigator(Client("web-1", end));

		var result = await Run(actionId, navigator, new Dictionary<string, object> { ["wrap"] = false }, "web-1");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(navigator.FolderChanges, Is.Empty);
		});
	}

	[Test]
	public async Task WrapOff_BeforeTheEnd_StillMoves()
	{
		var navigator = Navigator(Client("web-1", "mics"));

		await Run("next-folder", navigator, new Dictionary<string, object> { ["wrap"] = "False" }, "web-1");

		Assert.That(navigator.FolderChanges.Single().FolderId, Is.EqualTo("chat"));
	}

	[Test]
	public async Task WithoutDeviceOrOrigin_MovesEveryClientOneStepFromItsOwnFolder()
	{
		var navigator = Navigator(Client("web-1", "home"),
			Client("device:phone", "audio", deviceId: DeviceId),
			Client("web-2", "games", Gaming));

		var result = await Run("next-folder", navigator, []);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(navigator.FolderChanges,
				Is.EquivalentTo(new (string, string?)[] { ("scenes", "web-1"), ("mics", "device:phone") }));
		});
	}

	[Test]
	public async Task APress_MovesOnlyThePressingClient()
	{
		var navigator = Navigator(Client("web-1", "home"), Client("web-2", "chat"));

		await Run("next-folder", navigator, [], "web-2");

		Assert.That(navigator.FolderChanges, Is.EqualTo(new (string, string?)[] { ("home", "web-2") }));
	}

	[Test]
	public async Task AChosenDevice_MovesOnlyThatDeviceEvenWhenAnotherClientPressed()
	{
		var navigator = Navigator(Client("web-1", "home"), Client("web-2", "scenes", deviceId: DeviceId));

		await Run("next-folder", navigator, new Dictionary<string, object> { ["deviceId"] = DeviceId }, "web-1");

		Assert.That(navigator.FolderChanges, Is.EqualTo(new (string, string?)[] { ("audio", "web-2") }));
	}

	[Test]
	public async Task AChosenDeviceThatIsNotConnected_FailsAndMovesNobody()
	{
		var navigator = Navigator(Client("web-1", "home"));

		var result = await Run("next-folder", navigator, new Dictionary<string, object> { ["deviceId"] = DeviceId });

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(navigator.FolderChanges, Is.Empty);
		});
	}

	[Test]
	public async Task APressFromAClientWhoseFolderIsUnknown_FailsVisibly()
	{
		var navigator = Navigator(Client("web-1", "home"));

		var result = await Run("next-folder", navigator, [], "web-unknown");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(navigator.FolderChanges, Is.Empty);
		});
	}

	[Test]
	public async Task AClientOnAProfileWithoutListedFolders_FailsVisiblyInsteadOfReportingSuccess()
	{
		var navigator = Navigator(Client("device:phone", "virtual-home", "virtual-profile", DeviceId));

		var result = await Run("next-folder", navigator, [], "device:phone");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(navigator.FolderChanges, Is.Empty);
		});
	}

	[Test]
	public void Parameters_DefaultToFolderOrderWithWrapOn()
	{
		var integration = new DeckNavigationIntegration();
		var parameters = integration.Actions.Single(action => action.Id == "next-folder").Parameters;

		Assert.Multiple(() =>
		{
			Assert.That(parameters.Single(p => p.Name == "order").DefaultValue, Is.EqualTo("folder-order"));
			Assert.That(parameters.Single(p => p.Name == "wrap").DefaultValue, Is.EqualTo(true));
			Assert.That(parameters.Single(p => p.Name == "folders").VisibleWhen?.Values,
				Is.EqualTo(new[] { "custom" }));
			Assert.That(parameters.Any(p => p.Name == "deviceId"), Is.True);
		});
	}

	private sealed class DeckOnlyContext(IDeckNavigator deck) : IIntegrationContext
	{
		public IIntegrationConfig Config => throw new NotSupportedException();
		public IVariableApi Variables => throw new NotSupportedException();
		public IUserVariableApi UserVariables => throw new NotSupportedException();
		public IDeckNavigator Deck { get; } = deck;
		public IScriptApi Scripts => throw new NotSupportedException();
		public IWidgetApi Widgets => throw new NotSupportedException();
		public IEventPublisher Events => throw new NotSupportedException();
		public IUserNotifier Notifications => throw new NotSupportedException();
	}
}
