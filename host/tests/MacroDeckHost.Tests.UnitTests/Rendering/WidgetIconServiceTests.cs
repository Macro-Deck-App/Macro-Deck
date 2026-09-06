using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Plugins.Assets;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Application.Widgets.Icons;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Widgets.ActionButton;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Rendering;

/// <summary>
/// <see cref="IWidgetIconService" /> mirrors <see cref="IWidgetStateService" /> for issue #425's icon
/// provider: at most one authoritative provider per widget, unavailability falls back to the configured
/// icon (the opposite of the state provider's "hold the last known state"), and the provider overrides the
/// rendered icon for every state alike without ever touching the stored per-state/root icon configuration.
/// Scenarios named in comments refer to the issue #425 acceptance-scenarios document.
/// </summary>
[TestFixture]
public class WidgetIconServiceTests
{
	private const string IconA = "0198aaaa-1111-2222-3333-444444444444";
	private const string IconB = "0198bbbb-1111-2222-3333-444444444444";
	private const string IconC = "0198cccc-1111-2222-3333-444444444444";

	private static readonly string[] _onlyEtagOne = ["etag-1"];
	private static readonly byte[] _newTrackBytes = [9, 9, 9, 9];

	private static WidgetEntity Button(Guid id, string? data) => new()
	{
		Id = id,
		FolderId = Guid.NewGuid(),
		Type = WidgetTypeIds.ActionButton,
		Data = data
	};

	private static string TwoStateFlowsWithBlock(string blockId, string integrationId, string actionId)
		=> "[{\"triggerId\":\"t\",\"triggerType\":\"onEvent\",\"children\":[{\"id\":\"" +
			blockId +
			"\",\"type\":\"action\",\"blockType\":\"integration.provide\",\"integrationId\":\"" +
			integrationId +
			"\",\"actionId\":\"" +
			actionId +
			"\",\"parameters\":[]}]}]";

	// Acceptance Group D, scenario 15 (the central counterexample): one provided icon covers every state,
	// and the per-state configuration survives untouched.
	[Test]
	public async Task ActiveProvider_OverridesBothStates_WhileStoredPerStateIconsSurviveUntouched()
	{
		var data = "{\"stateMode\":true,\"states\":[" +
			"{\"id\":\"on\",\"label\":\"On\",\"appearance\":{\"icon\":{\"type\":\"icon-pack\",\"reference\":\"" +
			IconA +
			"\"}}}," +
			"{\"id\":\"off\",\"label\":\"Off\",\"appearance\":{\"icon\":{\"type\":\"icon-pack\",\"reference\":\"" +
			IconB +
			"\"}}}]," +
			"\"iconProvider\":{\"blockId\":\"blk-1\",\"integrationId\":\"spotify\",\"actionId\":\"current-track\"}," +
			"\"flows\":" +
			TwoStateFlowsWithBlock("blk-1", "spotify", "current-track") +
			"}";
		var widget = Button(Guid.NewGuid(), data);
		var action = new FakeIconProviderAction
			{ Id = "current-track", SnapshotToReturn = ReferenceSnapshot("v1", IconC) };
		var fixture = new Fixture(widget);
		fixture.Integrations.Add(new FakeIntegration { Id = "spotify", Actions = [action] });
		fixture.IconResources.Register(WidgetIconReference.IconPack(IconC), Resource("res-c"));

		var whileActive = await fixture.Service.Resolve(widget.Id);

		var config = ActionButtonWidgetData.Parse(JsonDocument.Parse(widget.Data!).RootElement);
		Assert.Multiple(() =>
		{
			Assert.That(whileActive.IsActive, Is.True);
			Assert.That(whileActive.Resource?.ResourceId,
				Is.EqualTo("res-c"),
				"the provider's icon renders for the button as a whole");
			Assert.That(config.ResolveIcon("on")!.Icon,
				Is.EqualTo(WidgetIconReference.IconPack(IconA)),
				"state on's stored icon is untouched");
			Assert.That(config.ResolveIcon("off")!.Icon,
				Is.EqualTo(WidgetIconReference.IconPack(IconB)),
				"state off's stored icon is untouched");
		});

		// Removing the provider (simulating disconnection) immediately renders the configured icon again -
		// Resolve falls back to Inactive and the stored per-state icons, already proven intact above, are
		// what a caller then applies.
		var withoutProvider = (JsonObject)JsonNode.Parse(widget.Data!)!;
		withoutProvider.Remove("iconProvider");
		widget.Data = withoutProvider.ToJsonString();

		var afterRemoval = await fixture.Service.Resolve(widget.Id);
		Assert.That(afterRemoval.IsActive, Is.False, "removing the provider falls back to the configured icon");
	}

	// Acceptance Group D, scenario 16: at most one authoritative icon provider per widget - reassigning
	// moves authority outright, the previous block's action is never consulted again.
	[Test]
	public async Task ReassigningTheProvider_MakesOnlyTheNewBlocksActionAuthoritative()
	{
		const string flows =
			"[{\"triggerId\":\"t\",\"triggerType\":\"onEvent\",\"children\":[" +
			"{\"id\":\"blk-1\",\"type\":\"action\",\"blockType\":\"integration.provide\",\"integrationId\":\"spotify\",\"actionId\":\"track-one\",\"parameters\":[]}," +
			"{\"id\":\"blk-2\",\"type\":\"action\",\"blockType\":\"integration.provide\",\"integrationId\":\"spotify\",\"actionId\":\"track-two\",\"parameters\":[]}]}]";
		var data = "{\"stateMode\":false,\"icon\":{\"type\":\"icon-pack\",\"reference\":\"" +
			IconA +
			"\"}," +
			"\"iconProvider\":{\"blockId\":\"blk-2\",\"integrationId\":\"spotify\",\"actionId\":\"track-two\"}," +
			"\"flows\":" +
			flows +
			"}";
		var widget = Button(Guid.NewGuid(), data);
		var actionOne = new FakeIconProviderAction
			{ Id = "track-one", SnapshotToReturn = ReferenceSnapshot("v1", IconB) };
		var actionTwo = new FakeIconProviderAction
			{ Id = "track-two", SnapshotToReturn = ReferenceSnapshot("v1", IconC) };
		var fixture = new Fixture(widget);
		fixture.Integrations.Add(new FakeIntegration { Id = "spotify", Actions = [actionOne, actionTwo] });
		fixture.IconResources.Register(WidgetIconReference.IconPack(IconC), Resource("res-c"));

		var result = await fixture.Service.Resolve(widget.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.Resource?.ResourceId, Is.EqualTo("res-c"), "blk-2's action is authoritative");
			Assert.That(actionTwo.GetActionIconCallCount, Is.EqualTo(1));
			Assert.That(actionOne.GetActionIconCallCount,
				Is.EqualTo(0),
				"the reassigned-away block's action is never consulted");
		});
	}

	// Acceptance Group D, scenario 17: deleting the owning block clears only the icon-provider capability -
	// a state provider on a different block is unaffected, and rendering falls back to the configured icon.
	[Test]
	public async Task
		DeletingTheIconProvidersBlock_FallsBackToTheConfiguredIcon_LeavingAStateProviderOnAnotherBlockUnaffected()
	{
		// blk-1 (the icon provider) is gone from flows; blk-2 (the state provider) is still present.
		const string flowsWithOnlyBlk2 =
			"[{\"triggerId\":\"t\",\"triggerType\":\"onEvent\",\"children\":[" +
			"{\"id\":\"blk-2\",\"type\":\"action\",\"blockType\":\"integration.provide\",\"integrationId\":\"spotify\",\"actionId\":\"now-playing\",\"parameters\":[]}]}]";
		var data
			= "{\"stateMode\":true,\"states\":[{\"id\":\"on\",\"label\":\"On\",\"appearance\":{\"icon\":{\"type\":\"icon-pack\",\"reference\":\"" +
			IconA +
			"\"}}}]," +
			"\"iconProvider\":{\"blockId\":\"blk-1\",\"integrationId\":\"spotify\",\"actionId\":\"current-track\"}," +
			"\"stateProvider\":{\"blockId\":\"blk-2\",\"integrationId\":\"spotify\",\"actionId\":\"now-playing\",\"states\":[{\"id\":\"on\",\"label\":\"On\"}]}," +
			"\"flows\":" +
			flowsWithOnlyBlk2 +
			"}";
		var widget = Button(Guid.NewGuid(), data);
		var iconAction = new FakeIconProviderAction
			{ Id = "current-track", SnapshotToReturn = ReferenceSnapshot("v1", IconC) };
		var fixture = new Fixture(widget);
		fixture.Integrations.Add(new FakeIntegration { Id = "spotify", Actions = [iconAction] });

		var result = await fixture.Service.Resolve(widget.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.IsActive, Is.False, "the missing block means the icon provider cannot be resolved");
			Assert.That(iconAction.GetActionIconCallCount, Is.EqualTo(0), "a missing block is never even asked");
			Assert.That(ActionButtonStateModel.Read(widget.Data).StateProvider?.BlockId,
				Is.EqualTo("blk-2"),
				"the state provider, on a different block, is unaffected by the icon provider's missing block");
		});

		// The host-side self-heal (a sibling of the state provider's own editor-side repair) drops the
		// dangling iconProvider key on the next host-controlled write, while the state provider - whose
		// block genuinely still exists - is left completely alone.
		var bag = ActionButtonStateJson.ParseDataBag(widget.Data);
		ActionButtonStateJson.Normalize(bag);
		Assert.Multiple(() =>
		{
			Assert.That(bag.ContainsKey("iconProvider"), Is.False, "the dangling assignment is dropped");
			Assert.That(bag["stateProvider"]?["blockId"]?.GetValue<string>(),
				Is.EqualTo("blk-2"),
				"the state provider survives normalization untouched");
		});
	}

	// Acceptance Group D, scenario 18 (counterexample): a Slider never acquires an icon provider, even if
	// an iconProvider key is injected by hand and saved.
	[Test]
	public async Task ASliderWithAHandInjectedIconProvider_NeverBecomesProviderControlled()
	{
		var data = "{\"icon\":{\"type\":\"icon-pack\",\"reference\":\"" +
			IconA +
			"\"}," +
			"\"iconProvider\":{\"blockId\":\"blk-1\",\"integrationId\":\"spotify\",\"actionId\":\"current-track\"}}";
		var widget = new WidgetEntity { Id = Guid.NewGuid(), Type = WidgetTypeIds.Slider, Data = data };
		var action = new FakeIconProviderAction
			{ Id = "current-track", SnapshotToReturn = ReferenceSnapshot("v1", IconC) };
		var fixture = new Fixture(widget);
		fixture.Integrations.Add(new FakeIntegration { Id = "spotify", Actions = [action] });

		var result = await fixture.Service.Resolve(widget.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.IsActive, Is.False);
			Assert.That(action.GetActionIconCallCount,
				Is.EqualTo(0),
				"a Slider is never even considered for a provider");
		});
	}

	// Acceptance Group E, scenario 19: NoIcon renders blank while the provider stays authoritative - the
	// content endpoint is never consulted for a snapshot that never named bytes or a reference.
	[Test]
	public async Task NoIcon_RendersBlankWithoutEverCallingGetActionIconContentAsync()
	{
		var widget = SinglePlayerWidget(out var action);
		action.SnapshotToReturn = new ActionIconSnapshot { NoIcon = true };
		var fixture = new Fixture(widget);
		fixture.Integrations.Add(new FakeIntegration { Id = "spotify", Actions = [action] });

		var result = await fixture.Service.Resolve(widget.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.IsActive, Is.True, "the provider is still authoritative");
			Assert.That(result.Resource, Is.Null, "deliberately blank");
			Assert.That(action.GetActionIconContentCallCount, Is.EqualTo(0));
		});
	}

	// Acceptance Group E, scenario 20 (counterexample): null and NoIcon:true must not produce the same
	// result - null falls back to the configured icon, NoIcon renders blank.
	[Test]
	public async Task NullSnapshot_AndNoIconSnapshot_ResolveToDifferentOutcomes()
	{
		var noIconWidget = SinglePlayerWidget(out var noIconAction);
		noIconAction.SnapshotToReturn = new ActionIconSnapshot { NoIcon = true };
		var noIconFixture = new Fixture(noIconWidget);
		noIconFixture.Integrations.Add(new FakeIntegration { Id = "spotify", Actions = [noIconAction] });

		var nullWidget = SinglePlayerWidget(out var nullAction);
		nullAction.SnapshotToReturn = null;
		var nullFixture = new Fixture(nullWidget);
		nullFixture.Integrations.Add(new FakeIntegration { Id = "spotify", Actions = [nullAction] });

		var noIconResult = await noIconFixture.Service.Resolve(noIconWidget.Id);
		var nullResult = await nullFixture.Service.Resolve(nullWidget.Id);

		Assert.Multiple(() =>
		{
			Assert.That(noIconResult.IsActive, Is.True, "NoIcon:true stays authoritative and renders blank");
			Assert.That(noIconResult.Resource, Is.Null);
			Assert.That(nullResult.IsActive, Is.False, "null falls back to the configured icon instead");
		});
	}

	// Acceptance Group C, scenario 13: state and icon providers coexist on one instance and move
	// independently - the active state changes while the icon's content is fetched exactly once in total.
	[Test]
	public async Task StateAndIconProviders_OnOneInstance_MoveIndependently()
	{
		const string flows =
			"[{\"triggerId\":\"t\",\"triggerType\":\"onEvent\",\"children\":[{\"id\":\"blk-1\",\"type\":\"action\"," +
			"\"blockType\":\"integration.provide\",\"integrationId\":\"spotify\",\"actionId\":\"now-playing\"," +
			"\"parameters\":[]}]}]";
		var data
			= "{\"stateMode\":true,\"states\":[{\"id\":\"playing\",\"label\":\"Playing\"},{\"id\":\"paused\",\"label\":\"Paused\"}]," +
			"\"stateProvider\":{\"blockId\":\"blk-1\",\"integrationId\":\"spotify\",\"actionId\":\"now-playing\"," +
			"\"states\":[{\"id\":\"playing\",\"label\":\"Playing\"},{\"id\":\"paused\",\"label\":\"Paused\"}]}," +
			"\"iconProvider\":{\"blockId\":\"blk-1\",\"integrationId\":\"spotify\",\"actionId\":\"now-playing\"}," +
			"\"flows\":" +
			flows +
			"}";
		var widget = Button(Guid.NewGuid(), data);
		var action = new FakeStateAndIconProviderAction
		{
			Id = "now-playing",
			StateSnapshotToReturn
				= new ActionStateSnapshot([new ActionStateDefinition("playing", "Playing")], "playing"),
			IconSnapshotToReturn = BytesSnapshot("etag-1")
		};
		var fixture = new Fixture(widget);
		fixture.Integrations.Add(new FakeIntegration { Id = "spotify", Actions = [action] });

		var firstIcon = await fixture.Service.Resolve(widget.Id);

		// The state changes independently (a real WidgetStateService is not needed to prove independence -
		// the fake reports the change directly, exactly as a real provider's own state would move).
		action.StateSnapshotToReturn
			= new ActionStateSnapshot([new ActionStateDefinition("paused", "Paused")], "paused");

		var secondIcon = await fixture.Service.Resolve(widget.Id);

		Assert.Multiple(() =>
		{
			Assert.That(firstIcon.IsActive, Is.True);
			Assert.That(secondIcon.IsActive, Is.True);
			Assert.That(secondIcon.Resource?.ResourceId,
				Is.EqualTo(firstIcon.Resource?.ResourceId),
				"the rendered image is unchanged while only state moved");
			Assert.That(action.GetActionIconContentCallCount,
				Is.EqualTo(1),
				"content was fetched exactly once in total");
		});
	}

	// Acceptance Group F, scenario 22 (counterexample): an unchanged version must not refetch bytes, even
	// though the identity poll genuinely keeps running.
	[Test]
	public async Task UnchangedVersion_IsPolledRepeatedly_ButContentIsFetchedExactlyOnce()
	{
		var widget = SinglePlayerWidget(out var action);
		action.SnapshotToReturn = BytesSnapshot("etag-1");
		var fixture = new Fixture(widget);
		fixture.Integrations.Add(new FakeIntegration { Id = "spotify", Actions = [action] });

		for (var i = 0; i < 5; i++)
		{
			await fixture.Service.Resolve(widget.Id);
		}

		Assert.Multiple(() =>
		{
			Assert.That(action.GetActionIconCallCount, Is.GreaterThanOrEqualTo(3), "polling really ran");
			Assert.That(action.GetActionIconContentCallCount, Is.EqualTo(1));
			Assert.That(action.ContentCallVersions, Is.EqualTo(_onlyEtagOne));
		});
	}

	// Acceptance Group F, scenario 23: a changed version refetches once and reaches the caller as new
	// pixels under a changed cache identity - never the same identity serving stale bytes.
	[Test]
	public async Task ChangedVersion_RefetchesOnce_AndTheNewBytesCarryADifferentContentIdentity()
	{
		var widget = SinglePlayerWidget(out var action);
		action.ContentByVersion = version => version == "etag-2"
			? new ActionIconContent(_newTrackBytes, "image/png")
			: new ActionIconContent([1, 2, 3, 4], "image/png");
		action.SnapshotToReturn = BytesSnapshot("etag-1");
		var fixture = new Fixture(widget);
		fixture.Integrations.Add(new FakeIntegration { Id = "spotify", Actions = [action] });

		var first = await fixture.Service.Resolve(widget.Id);

		action.SnapshotToReturn = BytesSnapshot("etag-2");
		var second = await fixture.Service.Resolve(widget.Id);

		Assert.Multiple(() =>
		{
			Assert.That(action.GetActionIconContentCallCount, Is.EqualTo(2), "exactly one more content call");
			Assert.That(action.ContentCallVersions[^1], Is.EqualTo("etag-2"));
			Assert.That(second.Resource?.ContentHash,
				Is.Not.EqualTo(first.Resource?.ContentHash),
				"the cache identity differs");

			// The resource store holds exactly the bytes just registered - never the previous track's.
			Assert.That(fixture.ResourceStore.TryGet(second.Resource!.ResourceId, out var content), Is.True);
			Assert.That(content.Content.ToArray(), Is.EqualTo(_newTrackBytes));
		});
	}

	// Acceptance Group F, scenario 26 (counterexample): the host never fetches a URL a provider hands it.
	// Uses a real local listener - it must record zero connections - and a widget-icon pipeline with no
	// registered source at all for the made-up "url" type, exactly as production has none.
	[Test]
	public async Task AUrlReferenceFromAProvider_IsNeverFetched()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		var acceptTask = AcceptOnceOrTimeoutAsync(listener);

		var widget = SinglePlayerWidget(out var action);
		action.SnapshotToReturn = new ActionIconSnapshot
		{
			Version = "v1",
			Reference = new ActionIconReference("url", $"http://127.0.0.1:{port}/art.jpg")
		};

		// No source is registered for "url" - exactly as production registers none - so resolution must
		// fall through to nothing without ever dereferencing the reference as a URL.
		var noSources
			= new WidgetIconResources(new WidgetIconSourceRegistry([]), new UiResourceStore(), Serilog.Log.Logger);
		var fixture = new Fixture(widget, iconResources: noSources);
		fixture.Integrations.Add(new FakeIntegration { Id = "spotify", Actions = [action] });

		var result = await fixture.Service.Resolve(widget.Id);
		var connected = await acceptTask;
		listener.Stop();

		Assert.Multiple(() =>
		{
			Assert.That(result.IsActive, Is.True, "the provider did answer, just with an unrenderable reference");
			Assert.That(result.Resource, Is.Null, "renders no image");
			Assert.That(connected, Is.False, "the listener must never see a connection");
		});
	}

	private static async Task<bool> AcceptOnceOrTimeoutAsync(TcpListener listener)
	{
		try
		{
			using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
			using var client = await listener.AcceptTcpClientAsync(cts.Token);
			return true;
		}
		catch (OperationCanceledException)
		{
			return false;
		}
	}

	private static WidgetEntity SinglePlayerWidget(out FakeIconProviderAction action)
	{
		var data = "{\"stateMode\":false,\"icon\":{\"type\":\"icon-pack\",\"reference\":\"" +
			IconA +
			"\"}," +
			"\"iconProvider\":{\"blockId\":\"blk-1\",\"integrationId\":\"spotify\",\"actionId\":\"current-track\"}," +
			"\"flows\":" +
			TwoStateFlowsWithBlock("blk-1", "spotify", "current-track") +
			"}";
		action = new FakeIconProviderAction { Id = "current-track" };
		return Button(Guid.NewGuid(), data);
	}

	private static ActionIconSnapshot ReferenceSnapshot(string version, string iconPackReference)
		=> new() { Version = version, Reference = ActionIconReference.IconPack(iconPackReference) };

	private static ActionIconSnapshot BytesSnapshot(string version)
		=> new() { Version = version, MediaType = "image/png" };

	private static UiResource Resource(string id) => new() { ResourceId = id };

	private sealed class Fixture
	{
		public Fixture(WidgetEntity widget, IWidgetIconResources? iconResources = null)
		{
			var readiness = new StartupReadiness();
			readiness.MarkCachesReady();
			readiness.MarkVariablesReady();

			Integrations = new FakeIntegrationRegistry();
			IconResources = new FakeWidgetIconResources();
			ResourceStore = new UiResourceStore();
			var providerResources = new WidgetIconProviderResources(ResourceStore, Serilog.Log.Logger);

			Service = new WidgetIconService(new FakeFolderCache(widget),
				Integrations,
				new RemoteIconProviderActionRegistry(new EmptySnapshotStore(),
					new UnusedInvoker(),
					new EmptyAssetCache()),
				iconResources ?? IconResources,
				providerResources,
				readiness);
		}

		public WidgetIconService Service { get; }
		public FakeIntegrationRegistry Integrations { get; }

		/// <summary>Only actually wired into <see cref="Service" /> when the constructor was not given a
		/// custom <see cref="IWidgetIconResources" /> - present unconditionally so most tests can register
		/// icon-pack resources without needing to know that.</summary>
		public FakeWidgetIconResources IconResources { get; }

		public UiResourceStore ResourceStore { get; }
	}

	private sealed class FakeFolderCache : IFolderCache
	{
		private readonly FolderEntity _folder;

		public FakeFolderCache(params WidgetEntity[] widgets)
		{
			_folder = new FolderEntity { Name = "f", Order = 0, Widgets = widgets.ToList() };
		}

		public List<FolderEntity> GetAllFolders() => [_folder];

		public Task InitializeCache() => Task.CompletedTask;
		public FolderEntity? GetFolderById(Guid id) => _folder;
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

	private sealed class FakeWidgetIconResources : IWidgetIconResources
	{
		private readonly Dictionary<WidgetIconReference, UiResource> _byReference = new();

		public void Register(WidgetIconReference reference, UiResource resource) => _byReference[reference] = resource;

		public Task<UiResource?> ResolveAsync(WidgetIconReference? reference, CancellationToken cancellationToken)
			=> Task.FromResult(reference is { } value && _byReference.TryGetValue(value, out var resource)
				? resource
				: null);

		public void Evict(Guid iconId)
		{
		}
	}

	private sealed class EmptySnapshotStore : IRemotePluginSnapshotStore
	{
		public RemotePluginCapabilitySnapshot GetSnapshot(string pluginId) =>
			RemotePluginCapabilitySnapshot.Empty(pluginId);

		public bool Has(string pluginId) => false;

		public Task SaveAsync(RemotePluginCapabilitySnapshot snapshot, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;
	}

	private sealed class UnusedInvoker : IPluginCapabilityInvoker
	{
		public Task<JsonElement?> InvokeAsync(string pluginId,
			CapabilityInvokeRequest request,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException("No test in this file resolves through the remote registry.");

		public bool TryComplete(string pluginId, MacroDeck.Plugin.Protocol.Envelope.ProtocolEnvelope result)
			=> throw new NotSupportedException();

		public void AbortAll(string pluginId, MacroDeck.Plugin.Protocol.Errors.ProtocolError reason)
		{
		}

		public bool IsLiveActionExecute(string pluginId, string correlationId) => false;
	}

	private sealed class EmptyAssetCache : IPluginAssetCache
	{
		public void Write(string contentHash, string mimeType, byte[] bytes)
			=> throw new NotSupportedException();

		public bool TryRead(string contentHash, out byte[] bytes, out string mimeType)
			=> throw new NotSupportedException();
	}

	/// <summary>A configured icon-provider action instance. Mirrors <c>FakeStateProviderAction</c>'s own
	/// documented contract - <see cref="ExecuteCount" /> stays zero across every test that only reads
	/// <see cref="GetActionIconAsync" />/<see cref="GetActionIconContentAsync" />, since reporting an icon
	/// must never itself run the action's side effect.</summary>
	private sealed class FakeIconProviderAction : IActionDefinition, IIconProviderActionDefinition
	{
		public string Id { get; init; } = "provide-icon";
		public MacroDeck.Localization.LocalizedText Name { get; init; } = "Provide Icon";
		public MacroDeck.Localization.LocalizedText Description => string.Empty;
		public IReadOnlyList<ActionParameter> Parameters { get; init; } = [];

		public ActionIconSnapshot? SnapshotToReturn { get; set; }
		public Func<string, ActionIconContent?>? ContentByVersion { get; set; }
		public ActionResult Result { get; set; } = ActionResult.Success();

		public int ExecuteCount { get; private set; }
		public int GetActionIconCallCount { get; private set; }
		public int GetActionIconContentCallCount { get; private set; }
		public List<string> ContentCallVersions { get; } = [];

		public TimeSpan IconPollInterval { get; set; } = TimeSpan.FromSeconds(5);

		TimeSpan IIconProviderActionDefinition.IconPollInterval => IconPollInterval;

		public Task<ActionIconSnapshot?> GetActionIconAsync(
			IReadOnlyDictionary<string, object?> parameters,
			CancellationToken cancellationToken)
		{
			GetActionIconCallCount++;
			return Task.FromResult(SnapshotToReturn);
		}

		public Task<ActionIconContent?> GetActionIconContentAsync(
			IReadOnlyDictionary<string, object?> parameters,
			string version,
			CancellationToken cancellationToken)
		{
			GetActionIconContentCallCount++;
			ContentCallVersions.Add(version);
			var content = ContentByVersion?.Invoke(version) ?? new ActionIconContent([1, 2, 3, 4], "image/png");
			return Task.FromResult<ActionIconContent?>(content);
		}

		public IActionExecutor CreateExecutor() => new Executor(this);

		private sealed class Executor : IActionExecutor
		{
			private readonly FakeIconProviderAction _owner;

			public Executor(FakeIconProviderAction owner) => _owner = owner;

			public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
			{
				_owner.ExecuteCount++;
				return Task.FromResult(_owner.Result);
			}
		}
	}

	/// <summary>An action that is both a state and an icon provider - Acceptance Group C, scenario 13.</summary>
	private sealed class FakeStateAndIconProviderAction
		: IActionDefinition, IStateProviderActionDefinition, IIconProviderActionDefinition
	{
		public string Id { get; init; } = "provide-both";
		public MacroDeck.Localization.LocalizedText Name { get; init; } = "Provide Both";
		public MacroDeck.Localization.LocalizedText Description => string.Empty;
		public IReadOnlyList<ActionParameter> Parameters { get; init; } = [];

		public ActionStateSnapshot? StateSnapshotToReturn { get; set; }
		public ActionIconSnapshot? IconSnapshotToReturn { get; set; }

		public int GetActionIconContentCallCount { get; private set; }
		public List<string> ContentCallVersions { get; } = [];

		public TimeSpan StatePollInterval => TimeSpan.FromSeconds(2);
		public TimeSpan IconPollInterval => TimeSpan.FromSeconds(5);

		public Task<ActionStateSnapshot?> GetActionStateAsync(
			IReadOnlyDictionary<string, object?> parameters,
			CancellationToken cancellationToken)
			=> Task.FromResult(StateSnapshotToReturn);

		public Task<ActionIconSnapshot?> GetActionIconAsync(
			IReadOnlyDictionary<string, object?> parameters,
			CancellationToken cancellationToken)
			=> Task.FromResult(IconSnapshotToReturn);

		public Task<ActionIconContent?> GetActionIconContentAsync(
			IReadOnlyDictionary<string, object?> parameters,
			string version,
			CancellationToken cancellationToken)
		{
			GetActionIconContentCallCount++;
			ContentCallVersions.Add(version);
			return Task.FromResult<ActionIconContent?>(new ActionIconContent([5, 6, 7, 8], "image/png"));
		}

		public IActionExecutor CreateExecutor() => throw new NotSupportedException("Not executed by these tests.");
	}
}
