using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeck.Sdk.Widgets;
using Serilog;

namespace MacroDeckHost.Tests.PluginContractTests;

/// <summary>
/// The old-consumer regression the compatibility policy requires (scenarios F1-F5, F8): a plugin built
/// against protocol v1 has only ever spoken the fixed <c>WidgetStateSelector</c> (Current/On/Off/Both)
/// over the wire. Every payload here is written as literal wire JSON rather than by constructing
/// <c>MacroDeck.Sdk.Widgets.WidgetStateSelector</c> or <c>WidgetAppearanceRequest.State</c> directly, so
/// these tests keep proving the compatibility guarantee even after both obsolete members are deleted in
/// 4.0.0 (F7's deprecation lifecycle, covered generically by SdkDeprecationLifecycleTests, is what makes
/// that eventual removal safe).
/// </summary>
[TestFixture]
internal sealed class WidgetStateCompatibilityContractTests
{
	private const string PluginId = "com.example.legacy-plugin";

	// WidgetStateSelector's wire ordinal (State is a plain int on the wire - see WidgetsApplyArgumentsV1).
	private const int SelectorCurrent = 0;
	private const int SelectorOn = 1;
	private const int SelectorOff = 2;
	private const int SelectorBoth = 3;

	[Test]
	public async Task ObsoleteCurrentSelector_TargetsTheActiveState()
	{
		var fixture = new Fixture(ThreeStateData("warn", "crit", "ok", active: "crit"));

		var applied = await fixture.InvokeV1(
			$$"""{"widgetId":"{{Fixture.WidgetId}}","patch":{"label":"changed"},"state":{{SelectorCurrent}},"clearProperties":[]}""");

		Assert.Multiple(() =>
		{
			Assert.That(applied, Is.True);
			Assert.That(fixture.StateLabel("crit"), Is.EqualTo("changed"));
			Assert.That(fixture.StateLabel("warn"), Is.Null, "other states are untouched");
			Assert.That(fixture.StateLabel("ok"), Is.Null, "other states are untouched");
		});
	}

	[Test]
	public async Task ObsoleteOnOffSelectors_PreferLiteralIdsThenFallBackToFirstAndSecondState()
	{
		// (a) literal ids win, even with a third state present.
		var literal = new Fixture(ThreeStateData("off", "on", "away", active: "off"));
		var literalApplied = await literal.InvokeV1(
			$$"""{"widgetId":"{{Fixture.WidgetId}}","patch":{"label":"changed"},"state":{{SelectorOn}},"clearProperties":[]}""");
		Assert.Multiple(() =>
		{
			Assert.That(literalApplied, Is.True);
			Assert.That(literal.StateLabel("on"), Is.EqualTo("changed"));
			Assert.That(literal.StateLabel("away"), Is.Null, "the third state is untouched");
		});

		// (b) no literal "on"/"off" ids - falls back positionally: off -> first, on -> second, mirroring
		// the order the two selectors had when every button was exactly [off, on].
		var positional = new Fixture(ThreeStateData("warn", "crit", "ok", active: "warn"));
		var positionalApplied = await positional.InvokeV1(
			$$"""{"widgetId":"{{Fixture.WidgetId}}","patch":{"label":"changed"},"state":{{SelectorOn}},"clearProperties":[]}""");
		Assert.Multiple(() =>
		{
			Assert.That(positionalApplied, Is.True);
			Assert.That(positional.StateLabel("crit"), Is.EqualTo("changed"), "On falls back to the second state");
			Assert.That(positional.StateLabel("warn"), Is.Null, "On must not land on the first state");
		});

		var positionalOff = new Fixture(ThreeStateData("warn", "crit", "ok", active: "warn"));
		var positionalOffApplied = await positionalOff.InvokeV1(
			$$"""{"widgetId":"{{Fixture.WidgetId}}","patch":{"label":"changed"},"state":{{SelectorOff}},"clearProperties":[]}""");
		Assert.Multiple(() =>
		{
			Assert.That(positionalOffApplied, Is.True);
			Assert.That(positionalOff.StateLabel("warn"), Is.EqualTo("changed"), "Off falls back to the first state");
		});

		// (c) a single-state target + On: no positional second state exists, so nothing resolves.
		var single = new Fixture(SingleStateData("only"));
		var before = single.Widget.Data;
		var singleApplied = await single.InvokeV1(
			$$"""{"widgetId":"{{Fixture.WidgetId}}","patch":{"label":"changed"},"state":{{SelectorOn}},"clearProperties":[]}""");
		Assert.Multiple(() =>
		{
			Assert.That(singleApplied, Is.False);
			Assert.That(single.Widget.Data, Is.EqualTo(before), "nothing is created");
		});
	}

	// The headline old-plugin case (also scenario F8): Both applies to every state, including a third
	// the fixed selector never modeled.
	[Test]
	public async Task ObsoleteBothSelector_AppliesToEveryStateIncludingTheThird()
	{
		var fixture = new Fixture(ThreeStateData("warn", "crit", "ok", active: "warn"));

		var applied = await fixture.InvokeV1(
			$$"""{"widgetId":"{{Fixture.WidgetId}}","patch":{"backgroundColor":"#ff00ff"},"state":{{SelectorBoth}},"clearProperties":[]}""");

		Assert.Multiple(() =>
		{
			Assert.That(applied, Is.True);
			Assert.That(fixture.StateColor("warn"), Is.EqualTo("#ff00ff"));
			Assert.That(fixture.StateColor("crit"), Is.EqualTo("#ff00ff"));
			Assert.That(fixture.StateColor("ok"), Is.EqualTo("#ff00ff"));
		});
	}

	[Test]
	public async Task NewStateIdsField_TakesPrecedenceAndHonoursTheSentinels()
	{
		// $current and $all pass straight through the v2 wire shape (WidgetsApplyArgumentsV2 carries no
		// legacy selector at all).
		var current = new Fixture(ThreeStateData("warn", "crit", "ok", active: "crit"), negotiatedVersion: 2);
		await current.InvokeV2(
			$$"""{"widgetId":"{{Fixture.WidgetId}}","patch":{"label":"changed"},"stateIds":["{{WidgetStates.Current}}"],"clearProperties":[]}""");
		Assert.That(current.StateLabel("crit"), Is.EqualTo("changed"));

		var all = new Fixture(ThreeStateData("warn", "crit", "ok", active: "crit"), negotiatedVersion: 2);
		await all.InvokeV2(
			$$"""{"widgetId":"{{Fixture.WidgetId}}","patch":{"backgroundColor":"#00ff00"},"stateIds":["{{WidgetStates.All}}"],"clearProperties":[]}""");
		Assert.Multiple(() =>
		{
			Assert.That(all.StateColor("warn"), Is.EqualTo("#00ff00"));
			Assert.That(all.StateColor("crit"), Is.EqualTo("#00ff00"));
			Assert.That(all.StateColor("ok"), Is.EqualTo("#00ff00"));
		});

		// StateIds set to a real id wins outright over a non-default State - in-process precedence
		// (WidgetAppearanceRequest.ResolveStateIds), independent of the wire.
#pragma warning disable CS0618 // Exercising the precedence rule against the deprecated selector directly.
		var request = new WidgetAppearanceRequest
		{
			WidgetId = "whatever",
			Patch = new WidgetAppearancePatch(),
			State = WidgetStateSelector.On,
			StateIds = ["crit"]
		};
#pragma warning restore CS0618
		Assert.That(request.ResolveStateIds(), Is.EqualTo(new[] { "crit" }));
	}

	[Test]
	public async Task UnknownStateIdInStateIds_IsANoOpRatherThanACreate()
	{
		var fixture = new Fixture(ThreeStateData("warn", "crit", "ok", active: "crit"), negotiatedVersion: 2);
		var before = fixture.Widget.Data;

		var applied = await fixture.InvokeV2(
			$$"""{"widgetId":"{{Fixture.WidgetId}}","patch":{"label":"changed"},"stateIds":["totally-unknown"],"clearProperties":[]}""");

		Assert.Multiple(() =>
		{
			Assert.That(applied, Is.False);
			Assert.That(fixture.Widget.Data, Is.EqualTo(before), "stored data is byte-identical");
		});
	}

	private static string ThreeStateData(string first, string second, string third, string active)
		=> "{\"stateMode\":true,\"states\":[" +
			$"{{\"id\":\"{first}\",\"label\":\"{Capitalize(first)}\",\"appearance\":{{}}}}," +
			$"{{\"id\":\"{second}\",\"label\":\"{Capitalize(second)}\",\"appearance\":{{}}}}," +
			$"{{\"id\":\"{third}\",\"label\":\"{Capitalize(third)}\",\"appearance\":{{}}}}]," +
			$"\"activeStateId\":\"{active}\"}}";

	private static string SingleStateData(string id)
		=>
			$"{{\"stateMode\":true,\"states\":[{{\"id\":\"{id}\",\"label\":\"{Capitalize(id)}\",\"appearance\":{{}}}}]}}";

	private static string Capitalize(string id) => char.ToUpperInvariant(id[0]) + id[1..];

	private sealed class Fixture
	{
		public static readonly Guid WidgetGuid = Guid.Parse("55555555-5555-5555-5555-555555555555");
		public static readonly string WidgetId = WidgetGuid.ToString();

		public WidgetEntity Widget { get; }

		private readonly PluginCallbackRouter _router;
		private readonly RecordingWidgetService _widgetsService;

		public Fixture(string data, int negotiatedVersion = 1)
		{
			var profileId = Guid.NewGuid();
			Widget = new WidgetEntity { Id = WidgetGuid, Type = WidgetTypeIds.ActionButton, Data = data };
			var folder = new FolderEntity
			{
				Id = Guid.NewGuid(), ProfileId = profileId, Name = "Home", Order = 0, Widgets = [Widget]
			};

			_widgetsService = new RecordingWidgetService();
			var appearanceService = new WidgetAppearanceService(new SingleFolderCache(folder),
				new SingleProfileCache(profileId, "Main"),
				_widgetsService,
				new RecordingWriteLock(),
				new Application.Rendering.WidgetDerivedStateStore(),
				new LoggerConfiguration().CreateLogger());

			var sessionRegistry = new PluginSessionRegistry(TimeProvider.System, Log.Logger);
			sessionRegistry.Create(new PluginSessionRecord
				{
					SessionId = "session-1",
					PluginId = PluginId,
					DisplayName = "Legacy Plugin",
					Origin = PluginSessionOrigin.Managed,
					NegotiatedVersion = negotiatedVersion,
					Capabilities
						= new Dictionary<string, MacroDeck.Plugin.Protocol.Versioning.CapabilityNegotiationResult>(
							StringComparer.Ordinal),
					DeclaredCapabilities = [],
					State = PluginSessionState.Awaiting,
					CreatedAt = TimeProvider.System.GetUtcNow()
				})
				.GetAwaiter()
				.GetResult();

			// Every dependency but the session registry and widget api belongs to a route this fixture
			// never exercises - passing null keeps the test honest about which collaborator matters here.
			_router = new PluginCallbackRouter(sessionRegistry,
				invoker: null!,
				scopeFactory: null!,
				notificationStore: null!,
				deckNavigator: null!,
				scriptApi: null!,
				new DirectWidgetApi(appearanceService),
				widgetIconInvalidator: null!,
				userVariableApi: null!,
				actionInteractions: null!,
				uiSessions: null!,
				deviceRegistry: null!,
				layoutRegistry: null!,
				folderViewRegistry: null!,
				widgetTypeRegistry: null!,
				modals: null!,
				transport: null!,
				throttle: new HostCallbackThrottle(TimeProvider.System, capacity: 1000, refillPerSecond: 1000),
				lockState: null!,
				logger: Log.Logger);
		}

		public Task<bool> InvokeV1(string argumentsJson) => InvokeCore(argumentsJson);

		public Task<bool> InvokeV2(string argumentsJson) => InvokeCore(argumentsJson);

		private async Task<bool> InvokeCore(string argumentsJson)
		{
			using var document = JsonDocument.Parse(argumentsJson);
			var payload = new HostInvokePayload
			{
				Api = HostApis.Widgets, Operation = HostOperations.Widgets.Apply,
				Arguments = document.RootElement.Clone()
			};

			var result = await _router.RouteAsync(PluginId,
				Guid.NewGuid().ToString("N"),
				payload,
				CancellationToken.None);
			Assert.That(result.Error, Is.Null, result.Error?.Message);
			return result.Data!.Value.GetBoolean();
		}

		public string? StateLabel(string stateId) => Appearance(stateId)?["label"]?.GetValue<string>();

		public string? StateColor(string stateId) => Appearance(stateId)?["backgroundColor"]?.GetValue<string>();

		private JsonObject? Appearance(string stateId)
			=> ((JsonObject)JsonNode.Parse(Widget.Data!)!)["states"]!.AsArray()
				.OfType<JsonObject>()
				.FirstOrDefault(e => e["id"]!.GetValue<string>() == stateId)?["appearance"]?.AsObject();
	}

	private sealed class DirectWidgetApi : IWidgetApi
	{
		private readonly IWidgetAppearanceService _inner;

		public DirectWidgetApi(IWidgetAppearanceService inner) => _inner = inner;

		public IReadOnlyList<WidgetTargetInfo> GetWidgets() => _inner.GetWidgets();

		public bool Exists(string widgetId) => _inner.Exists(widgetId);

		public Task<bool> ApplyAsync(WidgetAppearanceRequest request, CancellationToken cancellationToken = default)
			=> _inner.ApplyAsync(request, cancellationToken);

		public Task<WidgetStateWriteResult> SetStateAsync(
			string widgetId,
			string stateId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound));

		public Task<WidgetStateWriteResult> AdvanceStateAsync(string widgetId,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound));
	}

	private sealed class RecordingWriteLock : IWidgetDataWriteLock
	{
		private readonly WidgetDataWriteLock _inner = new();

		public Task<IDisposable> AcquireAsync(Guid widgetId, CancellationToken cancellationToken = default)
			=> _inner.AcquireAsync(widgetId, cancellationToken);
	}

	private sealed class RecordingWidgetService : IWidgetService
	{
		public Task<Result<WidgetEntity, WidgetError>> Update(WidgetEntity widget)
			=> Task.FromResult(Result.Ok<WidgetEntity, WidgetError>(widget));

		public Task<Result<WidgetEntity, WidgetError>> Create(Guid folderId,
			WidgetEntity widget,
			Guid? sourceWidgetId = null)
			=> throw new NotSupportedException();

		public Task<Result<List<WidgetEntity>, WidgetError>> UpdatePositions(
			Guid folderId,
			IReadOnlyList<WidgetPlacement> placements)
			=> throw new NotSupportedException();

		public Task<Result<WidgetError>> Delete(Guid widgetId, Guid folderId) => throw new NotSupportedException();

		public Task<Result<WidgetEntity, WidgetError>> SetPinned(Guid folderId,
			Guid widgetId,
			bool pinned,
			PinScope? scope = null)
			=> throw new NotSupportedException();

		public Task<Result<List<WidgetEntity>, WidgetError>> CreateMany(Guid folderId,
			IReadOnlyList<WidgetEntity> widgets,
			IReadOnlyList<Guid>? replaceIds = null,
			IReadOnlyList<Guid?>? sourceWidgetIds = null)
			=> throw new NotSupportedException();

		public Task<Result<WidgetError>> DeleteMany(Guid folderId, IReadOnlyList<Guid> widgetIds)
			=> throw new NotSupportedException();

		public Task<Result<List<WidgetEntity>, WidgetError>> SetPinnedMany(
			Guid folderId,
			IReadOnlyList<Guid> widgetIds,
			bool pinned,
			PinScope? scope = null)
			=> throw new NotSupportedException();
	}

	private sealed class SingleFolderCache : IFolderCache
	{
		private readonly FolderEntity _folder;

		public SingleFolderCache(FolderEntity folder) => _folder = folder;

		public List<FolderEntity> GetAllFolders() => [_folder];
		public FolderEntity? GetFolderById(Guid id) => id == _folder.Id ? _folder : null;
		public Task InitializeCache() => Task.CompletedTask;
		public List<FolderEntity> GetFoldersByParentId(Guid? parentId) => [_folder];
		public List<FolderEntity> GetFoldersByProfileId(Guid profileId) => [_folder];
		public Task AddOrUpdate(FolderEntity folder) => Task.CompletedTask;

		public Task AddOrUpdateRange(IReadOnlyCollection<FolderEntity> folders) => Task.CompletedTask;

		public Task<FolderSubtreeRemoval> RemoveSubtree(Guid rootId)
			=> Task.FromResult(new FolderSubtreeRemoval(false, [], []));

		public void AddWidget(Guid folderId, WidgetEntity widget)
		{
		}

		public void AddWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
		{
		}

		public void UpdateWidget(Guid folderId, WidgetEntity widget)
		{
		}

		public void UpdateWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
		{
		}

		public void UpdateWidgetPositions(Guid folderId, IReadOnlyList<WidgetPlacement> placements)
		{
		}

		public void RemoveWidget(Guid folderId, Guid widgetId)
		{
		}

		public void RemoveWidgets(Guid folderId, IReadOnlyList<Guid> widgetIds)
		{
		}

		public void ReplaceWidgets(Guid folderId, IReadOnlyList<Guid> removeIds, IReadOnlyList<WidgetEntity> addWidgets)
		{
		}
	}

	private sealed class SingleProfileCache : IProfileCache
	{
		private readonly ProfileEntity _profile;

		public SingleProfileCache(Guid id, string name) => _profile = new ProfileEntity { Id = id, Name = name };

		public bool HadUnreadableProfiles => false;
		public ProfileEntity? GetById(Guid id) => id == _profile.Id ? _profile : null;
		public List<ProfileEntity> GetAll() => [_profile];
		public Task InitializeCache() => Task.CompletedTask;
		public Task AddOrUpdate(ProfileEntity profile) => Task.CompletedTask;

		public Task AddOrUpdateAggregate(ProfileEntity profile, IReadOnlyCollection<FolderEntity> folders)
			=> Task.CompletedTask;

		public Task Remove(Guid id) => Task.CompletedTask;
	}
}
