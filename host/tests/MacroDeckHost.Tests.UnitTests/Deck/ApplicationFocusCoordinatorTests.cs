using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Deck;

[TestFixture]
public class ApplicationFocusCoordinatorTests
{
	private InMemoryProfileStore _store = null!;
	private ProfileCache _profileCache = null!;
	private FolderCache _folderCache = null!;
	private ManualTimeProvider _time = null!;
	private DeviceConnectionTracker _tracker = null!;
	private RecordingDeviceDeckNavigator _navigator = null!;
	private RecordingLogSink _logSink = null!;
	private ProviderDevicePresenceTracker _providerPresence = null!;
	private ApplicationFocusCoordinator _coordinator = null!;
	private Guid _profileId;
	private int _connectionCounter;

	[SetUp]
	public async Task SetUp()
	{
		_store = new InMemoryProfileStore();
		_profileCache = new ProfileCache(_store, new LoggerConfiguration().CreateLogger());
		await _profileCache.InitializeCache();
		_folderCache = new FolderCache(_profileCache);
		_time = new ManualTimeProvider();
		_tracker = new DeviceConnectionTracker(new RecordingEventBus(), _time);
		_navigator = new RecordingDeviceDeckNavigator();
		_logSink = new RecordingLogSink();
		_providerPresence = new ProviderDevicePresenceTracker();
		_coordinator = new ApplicationFocusCoordinator(_folderCache,
			_tracker,
			_navigator,
			new LoggerConfiguration().WriteTo.Sink(_logSink).MinimumLevel.Verbose().CreateLogger(),
			_providerPresence);

		_profileId = Guid.NewGuid();
		await _profileCache.AddOrUpdate(new ProfileEntity { Id = _profileId, Name = "P" });
	}

	[TearDown]
	public void TearDown() => _profileCache.Dispose();

	[Test]
	public async Task OnFocusChanged_MatchingRule_NavigatesOnlyTheTargetDeviceWithTheRuleFolder()
	{
		var deviceA = Guid.NewGuid();
		MarkOnline(deviceA);
		var folder = await AddFolderWithRule("F", deviceA, "notepad");

		await _coordinator.OnFocusChanged(App("notepad"), CancellationToken.None);

		Assert.That(_navigator.Calls, Has.Count.EqualTo(1));
		var call = _navigator.Calls[0];
		Assert.Multiple(() =>
		{
			Assert.That(call.DeviceId, Is.EqualTo(deviceA));
			Assert.That(call.FolderId, Is.EqualTo(folder.Id));
			Assert.That(call.Token, Is.Not.EqualTo(Guid.Empty));
		});
	}

	[Test]
	public async Task OnFocusChanged_RepeatedForTheSameOwningIdentity_IsIdempotent()
	{
		var deviceA = Guid.NewGuid();
		MarkOnline(deviceA);
		await AddFolderWithRule("F", deviceA, "notepad");

		await _coordinator.OnFocusChanged(App("notepad"), CancellationToken.None);
		await _coordinator.OnFocusChanged(App("notepad"), CancellationToken.None);

		Assert.That(_navigator.Calls, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task OnFocusChanged_FocusLoss_WithReturnOnFocusLoss_RestoresTheBaseline()
	{
		var deviceA = Guid.NewGuid();
		MarkOnline(deviceA);
		var baseline = await AddFolder("Baseline", 0);
		await AddFolderWithRule("Rule", deviceA, "notepad", returnOnFocusLoss: true, order: 1);
		await _coordinator.OnFolderReported(deviceA, baseline.Id, null, false, CancellationToken.None);

		await _coordinator.OnFocusChanged(App("notepad"), CancellationToken.None);
		await _coordinator.OnFocusChanged(App("wordpad"), CancellationToken.None);

		Assert.That(_navigator.Calls, Has.Count.EqualTo(2));
		Assert.That(_navigator.Calls[1].FolderId, Is.EqualTo(baseline.Id));
	}

	[Test]
	public async Task OnFocusChanged_FocusLoss_WithoutReturnOnFocusLoss_DoesNotRestoreAndDropsTheChain()
	{
		var deviceA = Guid.NewGuid();
		MarkOnline(deviceA);
		var baseline = await AddFolder("Baseline", 0);
		await AddFolderWithRule("Rule", deviceA, "notepad", returnOnFocusLoss: false, order: 1);
		await _coordinator.OnFolderReported(deviceA, baseline.Id, null, false, CancellationToken.None);

		await _coordinator.OnFocusChanged(App("notepad"), CancellationToken.None);
		await _coordinator.OnFocusChanged(App("wordpad"), CancellationToken.None);

		// Only the original acquire call - no restore, and the (already-gone) chain cannot fire again.
		Assert.That(_navigator.Calls, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task
		OnFocusChanged_AppToAppChainOnOneDevice_SkipsIntermediateRestoreAndRestoresTheFirstBaselineAtChainEnd()
	{
		var deviceA = Guid.NewGuid();
		MarkOnline(deviceA);
		var baseline = await AddFolder("Baseline", 0);
		var folderX = await AddFolderWithRule("X", deviceA, "appX", returnOnFocusLoss: true, order: 1);
		var folderY = await AddFolderWithRule("Y", deviceA, "appY", returnOnFocusLoss: true, order: 2);
		await _coordinator.OnFolderReported(deviceA, baseline.Id, null, false, CancellationToken.None);

		await _coordinator.OnFocusChanged(App("appX"), CancellationToken.None);
		Assert.That(_navigator.Calls, Has.Count.EqualTo(1));
		await Confirm(deviceA, folderX.Id);

		await _coordinator.OnFocusChanged(App("appY"), CancellationToken.None);
		Assert.That(_navigator.Calls, Has.Count.EqualTo(2), "app->app must not restore the baseline in between");
		Assert.That(_navigator.Calls[1].FolderId, Is.EqualTo(folderY.Id));
		await Confirm(deviceA, folderY.Id);

		await _coordinator.OnFocusChanged(App("somethingElse"), CancellationToken.None);

		Assert.That(_navigator.Calls, Has.Count.EqualTo(3));
		Assert.That(_navigator.Calls[2].FolderId, Is.EqualTo(baseline.Id), "chain end restores the FIRST baseline");
	}

	[Test]
	public async Task OnFocusChanged_TwoDevices_AreTrackedIndependently()
	{
		var deviceA = Guid.NewGuid();
		var deviceB = Guid.NewGuid();
		MarkOnline(deviceA);
		MarkOnline(deviceB);
		await AddFolderWithRule("A", deviceA, "appA", order: 0);
		var folderB = await AddFolderWithRule("B", deviceB, "appB", order: 1);

		await _coordinator.OnFocusChanged(App("appA"), CancellationToken.None);
		Assert.That(_navigator.Calls, Has.Count.EqualTo(1));
		Assert.That(_navigator.Calls[0].DeviceId, Is.EqualTo(deviceA));

		await _coordinator.OnFocusChanged(App("appB"), CancellationToken.None);

		Assert.That(_navigator.Calls, Has.Count.EqualTo(2));
		Assert.Multiple(() =>
		{
			Assert.That(_navigator.Calls[1].DeviceId, Is.EqualTo(deviceB));
			Assert.That(_navigator.Calls[1].FolderId, Is.EqualTo(folderB.Id));
		});
	}

	[Test]
	public async Task OnFolderReported_ManualNavigation_DropsTheChainSoALaterFocusLossRestoresNothing()
	{
		var deviceA = Guid.NewGuid();
		MarkOnline(deviceA);
		var baseline = await AddFolder("Baseline", 0);
		await AddFolderWithRule("Rule", deviceA, "notepad", returnOnFocusLoss: true, order: 1);
		var elsewhere = await AddFolder("Elsewhere", 2);
		await _coordinator.OnFolderReported(deviceA, baseline.Id, null, false, CancellationToken.None);

		await _coordinator.OnFocusChanged(App("notepad"), CancellationToken.None);
		Assert.That(_navigator.Calls, Has.Count.EqualTo(1));

		await _coordinator.OnFolderReported(deviceA, elsewhere.Id, null, false, CancellationToken.None);

		await _coordinator.OnFocusChanged(App("wordpad"), CancellationToken.None);

		Assert.That(_navigator.Calls, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task OnFolderReported_ManualNavigationAwayFromTheAlreadyShownRuleFolder_DropsTheChain()
	{
		var deviceA = Guid.NewGuid();
		MarkOnline(deviceA);
		var ruleFolder = await AddFolderWithRule("Rule", deviceA, "notepad", returnOnFocusLoss: true);
		var elsewhere = await AddFolder("Elsewhere", 1);

		await _coordinator.OnFocusChanged(App("notepad"), CancellationToken.None); // rule folder now shown
		Assert.That(_navigator.Calls, Has.Count.EqualTo(1));
		Assert.That(_navigator.Calls[0].FolderId, Is.EqualTo(ruleFolder.Id));

		await _coordinator.OnFolderReported(deviceA, elsewhere.Id, null, false, CancellationToken.None);
		await _coordinator.OnFocusChanged(App("wordpad"), CancellationToken.None);

		Assert.That(_navigator.Calls, Has.Count.EqualTo(1), "the manual nav must have dropped the chain");
	}

	[Test]
	public async Task OnFolderReported_MatchingToken_KeepsTheChainSoALaterFocusLossRestores()
	{
		var deviceA = Guid.NewGuid();
		MarkOnline(deviceA);
		var baseline = await AddFolder("Baseline", 0);
		var ruleFolder = await AddFolderWithRule("Rule", deviceA, "notepad", returnOnFocusLoss: true, order: 1);
		await _coordinator.OnFolderReported(deviceA, baseline.Id, null, false, CancellationToken.None);

		await _coordinator.OnFocusChanged(App("notepad"), CancellationToken.None);
		await Confirm(deviceA, ruleFolder.Id);

		await _coordinator.OnFocusChanged(App("wordpad"), CancellationToken.None);

		Assert.That(_navigator.Calls, Has.Count.EqualTo(2));
		Assert.That(_navigator.Calls[1].FolderId, Is.EqualTo(baseline.Id));
	}

	[Test]
	public async Task OnFocusChanged_DeviceOffline_AcquiresNothing_ThenResyncAfterReconnectReEvaluatesThatDeviceOnly()
	{
		var deviceA = Guid.NewGuid();
		var folder = await AddFolderWithRule("F", deviceA, "notepad");
		var priorFolder = await AddFolder("Prior", 1);

		await _coordinator.OnFocusChanged(App("notepad"), CancellationToken.None);
		Assert.That(_navigator.Calls, Is.Empty, "an offline device must store and queue nothing");

		MarkOnline(deviceA);
		await _coordinator.OnFolderReported(deviceA,
			priorFolder.Id,
			null,
			isResync: true,
			CancellationToken.None);

		Assert.That(_navigator.Calls, Has.Count.EqualTo(1));
		Assert.That(_navigator.Calls[0].FolderId, Is.EqualTo(folder.Id));
	}

	[Test]
	public async Task
		OnFolderReported_ResyncStillShowingTheOwnedFolder_KeepsTheChainWithoutNavigatingAndLaterRestoresTheOriginalBaseline()
	{
		var deviceA = Guid.NewGuid();
		MarkOnline(deviceA);
		var baseline = await AddFolder("Baseline", 0);
		var ruleFolder = await AddFolderWithRule("Rule", deviceA, "notepad", returnOnFocusLoss: true, order: 1);
		await _coordinator.OnFolderReported(deviceA, baseline.Id, null, false, CancellationToken.None);

		await _coordinator.OnFocusChanged(App("notepad"), CancellationToken.None);
		Assert.That(_navigator.Calls, Has.Count.EqualTo(1));

		// A reconnect resync with no token, reporting the folder the chain already owns - the
		// client genuinely still shows it, so this must not be mistaken for manual navigation.
		await _coordinator.OnFolderReported(deviceA, ruleFolder.Id, null, isResync: true, CancellationToken.None);

		Assert.That(_navigator.Calls, Has.Count.EqualTo(1), "no redundant push when already showing the rule folder");

		await _coordinator.OnFocusChanged(App("wordpad"), CancellationToken.None);

		Assert.That(_navigator.Calls, Has.Count.EqualTo(2));
		Assert.That(_navigator.Calls[1].FolderId,
			Is.EqualTo(baseline.Id),
			"the resync must not have wiped the original baseline");
	}

	[Test]
	public async Task
		OnFolderReported_ResyncShowingADifferentFolderWhileStillFocused_ReNavigatesToTheRuleFolderKeepingTheOriginalBaseline()
	{
		var deviceA = Guid.NewGuid();
		MarkOnline(deviceA);
		var baseline = await AddFolder("Baseline", 0);
		var ruleFolder = await AddFolderWithRule("Rule", deviceA, "notepad", returnOnFocusLoss: true, order: 1);
		var elsewhere = await AddFolder("Elsewhere", 2);
		await _coordinator.OnFolderReported(deviceA, baseline.Id, null, false, CancellationToken.None);

		await _coordinator.OnFocusChanged(App("notepad"), CancellationToken.None);
		Assert.That(_navigator.Calls, Has.Count.EqualTo(1));

		await _coordinator.OnFolderReported(deviceA, elsewhere.Id, null, isResync: true, CancellationToken.None);

		Assert.That(_navigator.Calls, Has.Count.EqualTo(2));
		Assert.That(_navigator.Calls[1].FolderId, Is.EqualTo(ruleFolder.Id));

		await _coordinator.OnFocusChanged(App("wordpad"), CancellationToken.None);

		Assert.That(_navigator.Calls, Has.Count.EqualTo(3));
		Assert.That(_navigator.Calls[2].FolderId,
			Is.EqualTo(baseline.Id),
			"the resync must not have wiped the original baseline");
	}

	[Test]
	public async Task OnDevicePresenceChanged_Online_WithMatchingCurrentApp_NavigatesAndUnknownBaselineRestoresNothing()
	{
		var deviceA = Guid.NewGuid();
		var folder = await AddFolderWithRule("F", deviceA, "notepad", returnOnFocusLoss: true);

		await _coordinator.OnFocusChanged(App("notepad"), CancellationToken.None);
		Assert.That(_navigator.Calls, Is.Empty);

		MarkOnline(deviceA);
		await _coordinator.OnDevicePresenceChanged(deviceA, online: true, CancellationToken.None);

		Assert.That(_navigator.Calls, Has.Count.EqualTo(1));
		Assert.That(_navigator.Calls[0].FolderId, Is.EqualTo(folder.Id));

		await _coordinator.OnFocusChanged(App("wordpad"), CancellationToken.None);

		Assert.That(_navigator.Calls, Has.Count.EqualTo(1), "an unknown baseline must restore nothing");
	}

	[Test]
	public async Task OnFocusChanged_RuleForADeviceThatNeverConnected_NeverNavigates()
	{
		var deviceA = Guid.NewGuid();
		await AddFolderWithRule("F", deviceA, "notepad");

		await _coordinator.OnFocusChanged(App("notepad"), CancellationToken.None);

		Assert.That(_navigator.Calls, Is.Empty);
	}

	[Test]
	public async Task OnRulesChanged_FolderDeleted_DropsTheChainWithoutNavigating()
	{
		var deviceA = Guid.NewGuid();
		MarkOnline(deviceA);
		var baseline = await AddFolder("Baseline", 0);
		var ruleFolder = await AddFolderWithRule("Rule", deviceA, "notepad", returnOnFocusLoss: true, order: 1);
		await _coordinator.OnFolderReported(deviceA, baseline.Id, null, false, CancellationToken.None);
		await _coordinator.OnFocusChanged(App("notepad"), CancellationToken.None);
		Assert.That(_navigator.Calls, Has.Count.EqualTo(1));

		await _folderCache.RemoveSubtree(ruleFolder.Id);
		await _coordinator.OnRulesChanged(CancellationToken.None);

		Assert.That(_navigator.Calls, Has.Count.EqualTo(1), "OnRulesChanged must never navigate");

		await _coordinator.OnFocusChanged(App("wordpad"), CancellationToken.None);

		Assert.That(_navigator.Calls, Has.Count.EqualTo(1), "the dropped chain must not restore");
	}

	[Test]
	public async Task
		OnFocusChanged_TwoEnabledDuplicateRules_ResolveDeterministicallyToTheLowestFolderIdAndLogAWarning()
	{
		var deviceA = Guid.NewGuid();
		MarkOnline(deviceA);

		var lowerFolderId = new Guid("00000000-0000-0000-0000-000000000001");
		var higherFolderId = new Guid("00000000-0000-0000-0000-000000000002");
		Assert.That(lowerFolderId.CompareTo(higherFolderId), Is.LessThan(0), "test fixture assumption");

		await _folderCache.AddOrUpdate(RuleFolder(_profileId, higherFolderId, "Higher", 0, deviceA, "notepad"));
		await _folderCache.AddOrUpdate(RuleFolder(_profileId, lowerFolderId, "Lower", 1, deviceA, "notepad"));

		await _coordinator.OnFocusChanged(App("notepad"), CancellationToken.None);

		Assert.That(_navigator.Calls, Has.Count.EqualTo(1), "no double navigation");
		Assert.That(_navigator.Calls[0].FolderId, Is.EqualTo(lowerFolderId));
		Assert.That(_logSink.Events.Any(e => e.Level == LogEventLevel.Warning), Is.True);
	}

	[Test]
	public async Task OnFocusChanged_ProviderDeviceWithNoConnection_IsStillNavigated()
	{
		var deviceA = Guid.NewGuid();

		// Deliberately no MarkOnline: a provider device holds no WebSocket connection of its own, and
		// counting connections alone would leave every one of them permanently unfocusable.
		_providerPresence.Set(deviceA, online: true);
		var folder = await AddFolderWithRule("F", deviceA, "notepad");

		await _coordinator.OnFocusChanged(App("notepad"), CancellationToken.None);

		Assert.That(_navigator.Calls, Has.Count.EqualTo(1));
		Assert.That(_navigator.Calls[0].FolderId, Is.EqualTo(folder.Id));
	}

	[Test]
	public async Task OnFocusChanged_DeviceThatIsNeitherConnectedNorReportedOnline_IsNotNavigated()
	{
		var deviceA = Guid.NewGuid();
		await AddFolderWithRule("F", deviceA, "notepad");

		await _coordinator.OnFocusChanged(App("notepad"), CancellationToken.None);

		Assert.That(_navigator.Calls, Is.Empty);
	}

	private void MarkOnline(Guid deviceId)
	{
		var connectionId = $"conn-{++_connectionCounter}";
		_tracker.Attach(connectionId, deviceId, () => { });
		_tracker.Register(connectionId, connectionId);
	}

	private Task Confirm(Guid deviceId, Guid folderId)
	{
		var token = _navigator.Calls.Last(c => c.DeviceId == deviceId).Token;
		return _coordinator.OnFolderReported(deviceId, folderId, token.ToString("D"), false, CancellationToken.None);
	}

	private async Task<FolderEntity> AddFolder(string name, int order)
	{
		var folder = new FolderEntity
		{
			Id = Guid.NewGuid(), ProfileId = _profileId, Name = name, Order = order, Rows = 3, Columns = 3
		};
		await _folderCache.AddOrUpdate(folder);
		return folder;
	}

	private async Task<FolderEntity> AddFolderWithRule(string name,
		Guid deviceId,
		string identity,
		bool returnOnFocusLoss = false,
		int order = 0)
	{
		var folder = RuleFolder(_profileId, Guid.NewGuid(), name, order, deviceId, identity, returnOnFocusLoss);
		await _folderCache.AddOrUpdate(folder);
		return folder;
	}

	private static FolderEntity RuleFolder(Guid profileId,
		Guid folderId,
		string name,
		int order,
		Guid deviceId,
		string identity,
		bool returnOnFocusLoss = false)
		=> new()
		{
			Id = folderId,
			ProfileId = profileId,
			Name = name,
			Order = order,
			Rows = 3,
			Columns = 3,
			FocusRules =
			[
				new FolderFocusRule
				{
					Id = Guid.NewGuid(),
					Enabled = true,
					ApplicationIdentity = identity,
					IdentityKind = ApplicationIdentityKind.ProcessName,
					DeviceId = deviceId,
					ReturnOnFocusLoss = returnOnFocusLoss
				}
			]
		};

	private static int _pidCounter = 1000;

	private static FocusedApplication App(string processName) => new(++_pidCounter, null, processName, null);

	private sealed class RecordingDeviceDeckNavigator : IDeviceDeckNavigator
	{
		public List<(Guid DeviceId, Guid FolderId, Guid Token)> Calls { get; } = [];

		public Task<bool> ChangeFolderOnDeviceAsync(Guid deviceId,
			Guid folderId,
			Guid navigationToken,
			CancellationToken cancellationToken)
		{
			Calls.Add((deviceId, folderId, navigationToken));
			return Task.FromResult(true);
		}

		public Task<bool> ChangeProfileOnDeviceAsync(Guid deviceId,
			string profileId,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException();
	}

	private sealed class RecordingLogSink : ILogEventSink
	{
		public List<LogEvent> Events { get; } = [];

		public void Emit(LogEvent logEvent) => Events.Add(logEvent);
	}
}
