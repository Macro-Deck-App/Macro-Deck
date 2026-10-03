using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.Actions.Options;
using MacroDeckHost.Integrations.Deck;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Deck;

[TestFixture]
internal sealed class DeckDeviceTargetTests
{
	private const string _deviceId = "0f8fad5b-d9cb-469f-a165-70867728950e";

	private static readonly string[] _actionIds = ["change-folder", "change-profile", "go-to-parent", "go-back"];

	private static RecordingDeckNavigator Navigator(params DeckClient[] clients)
		=> new()
		{
			Folders = [new DeckFolder { Id = "f1", Label = "Home" }],
			Profiles = [new DeckProfile { Id = "p1", Label = "Streaming" }],
			Clients = [.. clients]
		};

	private static DeckClient Client(string clientId, string? deviceId)
		=> new() { ClientId = clientId, DeviceId = deviceId, ProfileId = "p1", FolderId = "f1" };

	private static IActionDefinition Definition(string actionId, IDeckNavigator navigator)
	{
		var integration = new DeckNavigationIntegration();
		integration.InitializeAsync(new DeckOnlyContext(navigator)).GetAwaiter().GetResult();
		return integration.Actions.Single(action => action.Id == actionId);
	}

	private static Task<ActionResult> Run(string actionId,
		RecordingDeckNavigator navigator,
		object? deviceId,
		string? originClientId = null)
	{
		var parameters = new Dictionary<string, object> { ["folderId"] = "f1", ["profileId"] = "p1" };
		if (deviceId is not null)
		{
			parameters["deviceId"] = deviceId;
		}

		return Definition(actionId, navigator)
			.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext { Parameters = parameters, OriginClientId = originClientId });
	}

	[TestCaseSource(nameof(_actionIds))]
	public void Parameters_OfferAnOptionalDeviceChoiceFromTheDevicesSource(string actionId)
	{
		var parameter = Definition(actionId, Navigator()).Parameters.Single(p => p.Name == "deviceId");

		Assert.Multiple(() =>
		{
			Assert.That(parameter.Type, Is.EqualTo(ActionParameterType.DynamicChoice));
			Assert.That(parameter.Required, Is.False);
			Assert.That(parameter.OptionsSourceId, Is.EqualTo(DeckOptionsSourceIds.Devices));
		});
	}

	[TestCaseSource(nameof(_actionIds))]
	public async Task NoDeviceChosen_KeepsThePressOrigin(string actionId)
	{
		var navigator = Navigator(Client("web-1", _deviceId));

		var result = await Run(actionId, navigator, deviceId: null, originClientId: "device:press");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(navigator.Calls.Single(), Is.EqualTo((actionId, "device:press")));
		});
	}

	[TestCaseSource(nameof(_actionIds))]
	public async Task NoDeviceChosenAndNoOrigin_StillTargetsEveryClient(string actionId)
	{
		var navigator = Navigator(Client("web-1", _deviceId));

		await Run(actionId, navigator, deviceId: null);

		Assert.That(navigator.Calls.Single(), Is.EqualTo((actionId, (string?)null)));
	}

	[TestCaseSource(nameof(_actionIds))]
	public async Task BlankDevice_CountsAsNoDeviceChosen(string actionId)
	{
		var navigator = Navigator(Client("web-1", _deviceId));

		await Run(actionId, navigator, deviceId: "  ", originClientId: "client-9");

		Assert.That(navigator.Calls.Single(), Is.EqualTo((actionId, "client-9")));
	}

	[TestCaseSource(nameof(_actionIds))]
	public async Task ChosenDevice_NavigatesOnlyItsClient(string actionId)
	{
		var navigator = Navigator(Client("web-2", Guid.NewGuid().ToString("D")), Client("web-1", _deviceId));

		var result = await Run(actionId, navigator, _deviceId);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(navigator.Calls.Single(), Is.EqualTo((actionId, "web-1")));
		});
	}

	[TestCaseSource(nameof(_actionIds))]
	public async Task ChosenDevice_WinsOverThePressOrigin(string actionId)
	{
		var navigator = Navigator(Client("web-1", _deviceId));

		await Run(actionId, navigator, _deviceId, originClientId: "client-9");

		Assert.That(navigator.Calls.Single(), Is.EqualTo((actionId, "web-1")));
	}

	[TestCaseSource(nameof(_actionIds))]
	public async Task ChosenDevice_IsMatchedIgnoringIdFormatting(string actionId)
	{
		var navigator = Navigator(Client("web-1", _deviceId));

		await Run(actionId, navigator, _deviceId.ToUpperInvariant());

		Assert.That(navigator.Calls.Single(), Is.EqualTo((actionId, "web-1")));
	}

	[TestCaseSource(nameof(_actionIds))]
	public async Task ChosenDeviceNotConnected_FailsAndNeverBroadcasts(string actionId)
	{
		var navigator = Navigator(Client("web-2", Guid.NewGuid().ToString("D")), Client("web-3", null));

		var result = await Run(actionId, navigator, _deviceId, originClientId: "client-9");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(navigator.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task UnparsableDevice_FailsWithoutNavigating()
	{
		var navigator = Navigator(Client("web-1", _deviceId));

		var result = await Run("go-back", navigator, "not-a-guid");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(navigator.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task NavigationUnavailable_StillFailsBeforeLookingAtTheDevice()
	{
		var integration = new DeckNavigationIntegration();

		var result = await integration.Actions.Single(a => a.Id == "go-back")
			.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object> { ["deviceId"] = _deviceId }
			});

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
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
