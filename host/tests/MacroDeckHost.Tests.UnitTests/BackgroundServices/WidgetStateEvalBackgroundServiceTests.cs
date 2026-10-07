using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Tests.UnitTests.BackgroundServices;

[TestFixture]
internal sealed class WidgetStateEvalBackgroundServiceTests
{
	private readonly Guid _widgetId = Guid.NewGuid();
	private FakeTimeProvider _time = null!;
	private WidgetStateEvalChannel _queue = null!;
	private WidgetStateSubscriptionTracker _subscriptions = null!;
	private FakeStateService _stateService = null!;
	private WidgetStateEvalBackgroundService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new FakeTimeProvider();
		_queue = new WidgetStateEvalChannel();
		_subscriptions = new WidgetStateSubscriptionTracker();
		_stateService = new FakeStateService();
		var folderCache = new FakeFolderCache();
		folderCache.AddWidget(new WidgetEntity
		{
			Id = _widgetId,
			FolderId = Guid.NewGuid(),
			Type = WidgetTypeIds.ActionButton,
			Data = """{"stateProvider":{"blockId":"blk-1"}}"""
		});
		var services = new ServiceCollection()
			.AddScoped<IWidgetStateService>(_ => _stateService)
			.BuildServiceProvider();
		var readiness = new Application.Services.StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		_service = new WidgetStateEvalBackgroundService(new StartedHostLifetime(),
			_queue,
			services.GetRequiredService<IServiceScopeFactory>(),
			_subscriptions,
			folderCache,
			new NoOpWidgetVariableIndex(),
			readiness,
			Serilog.Log.Logger,
			timeProvider: _time);
	}

	[TearDown]
	public void TearDown() => _service.Dispose();

	[Test]
	public void ButtonPolledWhileHidden_IsPolledOnTheNextTickOnceSomethingDisplaysIt()
	{
		_stateService.Interval = TimeSpan.FromSeconds(2);
		Tick();
		_time.Advance(TimeSpan.FromSeconds(3));
		Assert.That(Tick(), Is.False, "hidden buttons are polled at the idle cadence");

		_subscriptions.Add("deck", _widgetId.ToString());
		_time.Advance(TimeSpan.FromSeconds(1));

		Assert.That(Tick(), Is.True);
	}

	[Test]
	public void DisplayedButton_IsPolledAtItsDeclaredInterval_EvenWhenTheTickLandsSlightlyEarly()
	{
		_stateService.Interval = TimeSpan.FromSeconds(2);
		_subscriptions.Add("deck", _widgetId.ToString());
		Tick();

		_time.Advance(TimeSpan.FromMilliseconds(999));
		var firstTick = Tick();
		_time.Advance(TimeSpan.FromMilliseconds(999));
		var secondTick = Tick();

		Assert.Multiple(() =>
		{
			Assert.That(firstTick, Is.False);
			Assert.That(secondTick, Is.True);
		});
	}

	[Test]
	public void DisplayedButton_IsNeverPolledFasterThanDeclared()
	{
		_stateService.Interval = TimeSpan.FromMilliseconds(1500);
		_subscriptions.Add("deck", _widgetId.ToString());
		Tick();

		_time.Advance(TimeSpan.FromSeconds(1));
		var afterOneSecond = Tick();
		_time.Advance(TimeSpan.FromSeconds(1));
		var afterTwoSeconds = Tick();

		Assert.Multiple(() =>
		{
			Assert.That(afterOneSecond, Is.False);
			Assert.That(afterTwoSeconds, Is.True);
		});
	}

	[Test]
	public void DeclaredIntervalBelowTheFloor_IsClampedToOneSecond()
	{
		_stateService.Interval = TimeSpan.FromMilliseconds(1);
		_subscriptions.Add("deck", _widgetId.ToString());
		Tick();

		_time.Advance(TimeSpan.FromMilliseconds(500));
		var halfSecond = Tick();
		_time.Advance(TimeSpan.FromMilliseconds(500));
		var oneSecond = Tick();

		Assert.Multiple(() =>
		{
			Assert.That(halfSecond, Is.False);
			Assert.That(oneSecond, Is.True);
		});
	}

	[Test]
	public void HiddenButton_IsPolledAtTheIdleCadenceRatherThanItsDeclaredInterval()
	{
		_stateService.Interval = TimeSpan.FromSeconds(2);
		Tick();

		_time.Advance(TimeSpan.FromSeconds(29));
		var beforeIdleFloor = Tick();
		_time.Advance(TimeSpan.FromSeconds(1));
		var atIdleFloor = Tick();

		Assert.Multiple(() =>
		{
			Assert.That(beforeIdleFloor, Is.False);
			Assert.That(atIdleFloor, Is.True);
		});
	}

	private bool Tick()
	{
		_service.EnqueueDueProviders();
		var enqueued = false;
		while (_queue.Reader.TryRead(out var id))
		{
			enqueued |= id == _widgetId;
		}

		return enqueued;
	}

	private sealed class FakeStateService : IWidgetStateService
	{
		public TimeSpan? Interval { get; set; }

		public Task<WidgetStateResolution?> Resolve(Guid widgetId, CancellationToken cancellationToken = default)
			=> Task.FromResult<WidgetStateResolution?>(null);

		public TimeSpan? GetProviderPollInterval(Guid widgetId) => Interval;
	}

	private sealed class NoOpWidgetVariableIndex : IWidgetVariableIndex
	{
		public IReadOnlyList<Guid> FindLabelReferences(string variableName) => [];
		public IReadOnlyList<Guid> FindStateMappingReferences(string variableName) => [];
		public IReadOnlyList<Guid> FindProviderReferences(string integrationId, string? actionId = null) => [];
		public IReadOnlyList<Guid> FindIconProviderReferences(string integrationId, string? actionId = null) => [];
		public bool LabelReferences(Guid widgetId, string variableName) => false;
		public bool StateMappingReferences(Guid widgetId, string variableName) => false;

		public void Rebuild()
		{
		}

		public void ReindexWidget(Guid widgetId, string type, string? data)
		{
		}

		public void Remove(Guid widgetId)
		{
		}
	}

	private sealed class StartedHostLifetime : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted { get; } = new(canceled: true);
		public CancellationToken ApplicationStopping => CancellationToken.None;
		public CancellationToken ApplicationStopped => CancellationToken.None;

		public void StopApplication()
		{
		}
	}
}
