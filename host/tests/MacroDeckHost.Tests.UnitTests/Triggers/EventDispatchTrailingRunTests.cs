using System.Collections.Concurrent;
using System.Threading.Channels;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Tests.UnitTests.Triggers;

[TestFixture]
internal sealed class EventDispatchTrailingRunTests
{
	private const string EventId = "obs::scene-changed";

	private static string Flows(string triggerId = "t1")
	{
		return $$"""
				 [{"triggerId":"{{triggerId}}","triggerType":"onEvent","event":{"providerId":"obs","eventId":"scene-changed"},"children":[]}]
				 """;
	}

	private static EventOccurrence Scene(string name, EventTarget? target = null, int depth = 0)
	{
		return new EventOccurrence(EventId,
			new Dictionary<string, object?>(StringComparer.Ordinal) { ["sceneName"] = name },
			target,
			null,
			depth);
	}

	[Test]
	public async Task Occurrences_during_a_run_leave_one_trailing_run_with_the_newest()
	{
		await using var world = await World.Start();
		world.AddAutomation();

		world.Publish(Scene("A"));
		await world.Runner.WaitForRuns(1);
		world.Publish(Scene("B"));
		world.Publish(Scene("C"));
		world.Publish(Scene("D"));
		await world.Drained();
		world.Runner.Release();
		await world.Runner.WaitForRuns(2);
		world.Runner.Release();
		await world.Idle();

		Assert.That(world.Runner.Scenes, Is.EqualTo(new[] { "A", "D" }));
	}

	[Test]
	public async Task A_failed_run_still_takes_the_trailing_occurrence()
	{
		await using var world = await World.Start();
		world.AddAutomation();
		world.Runner.FailNext = true;

		world.Publish(Scene("A"));
		await world.Runner.WaitForRuns(1);
		world.Publish(Scene("B"));
		await world.Drained();
		world.Runner.Release();
		await world.Runner.WaitForRuns(2);
		world.Runner.Release();
		await world.Idle();

		Assert.That(world.Runner.Scenes, Is.EqualTo(new[] { "A", "B" }));
	}

	[Test]
	public async Task A_new_occurrence_cannot_reset_the_depth_of_a_waiting_chain()
	{
		await using var world = await World.Start();
		world.AddAutomation();

		world.Publish(Scene("A"));
		await world.Runner.WaitForRuns(1);
		world.Publish(Scene("deep", depth: 9));
		world.Publish(Scene("shallow"));
		await world.Drained();
		world.Runner.Release();
		await world.Runner.WaitForRuns(2);
		world.Runner.Release();
		await world.Idle();

		Assert.That(world.Runner.Depths[^1], Is.EqualTo(9));
		Assert.That(world.Runner.Scenes[^1], Is.EqualTo("shallow"));
	}

	[Test]
	public async Task A_trigger_removed_during_its_run_is_skipped_and_works_again_when_it_comes_back()
	{
		await using var world = await World.Start();
		var automation = world.AddAutomation();

		world.Publish(Scene("A"));
		await world.Runner.WaitForRuns(1);
		world.Publish(Scene("waiting"));
		await world.Drained();
		world.Remove(automation);
		world.Runner.Release();
		await world.Idle();

		world.Restore(automation);
		world.Publish(Scene("back"));
		await world.Runner.WaitForRuns(2);
		world.Runner.Release();
		await world.Idle();

		Assert.That(world.Runner.Scenes, Is.EqualTo(new[] { "A", "back" }));
	}

	[Test]
	public async Task A_targeted_occurrence_during_a_run_trails_it_too()
	{
		await using var world = await World.Start();
		var automation = world.AddAutomation();
		var target = new EventTarget(EventTriggerOwner.ForAutomation(automation), "t1");

		world.Publish(Scene("tick 1", target));
		await world.Runner.WaitForRuns(1);
		world.Publish(Scene("tick 2", target));
		world.Publish(Scene("tick 3", target));
		await world.Drained();
		world.Runner.Release();
		await world.Runner.WaitForRuns(2);
		world.Runner.Release();
		await world.Idle();

		Assert.That(world.Runner.Scenes, Is.EqualTo(new[] { "tick 1", "tick 3" }));
	}

	[Test]
	public async Task Different_triggers_do_not_wait_for_each_other()
	{
		await using var world = await World.Start();
		world.AddAutomation();
		world.AddAutomation();

		world.Publish(Scene("A"));
		await world.Runner.WaitForRuns(2);
		world.Runner.Release();
		world.Runner.Release();
		await world.Idle();

		Assert.That(world.Runner.Scenes, Is.EqualTo(new[] { "A", "A" }));
	}

	private sealed class GatedRunner : IEventTriggerRunner
	{
		private readonly ConcurrentQueue<EventOccurrence> _runs = new();
		private readonly Channel<bool> _gate = Channel.CreateUnbounded<bool>();
		private int _active;

		public bool FailNext { get; set; }

		public bool Active => Volatile.Read(ref _active) > 0;

		public IReadOnlyList<string> Scenes => [.. _runs.Select(run => (string)run.Parameters["sceneName"]!)];

		public IReadOnlyList<int> Depths => [.. _runs.Select(run => run.CascadeDepth)];

		public async Task<FlowExecutionResult> Run(
			EventSubscription subscription,
			EventOccurrence occurrence,
			CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref _active);
			_runs.Enqueue(occurrence);
			try
			{
				await _gate.Reader.ReadAsync(cancellationToken);
				if (FailNext)
				{
					FailNext = false;
					throw new InvalidOperationException("flow failed");
				}

				return new FlowExecutionResult
				{
					ExecutionId = Guid.NewGuid(),
					Status = FlowExecutionStatus.Succeeded,
					MatchedFlows = 1
				};
			}
			finally
			{
				Interlocked.Decrement(ref _active);
			}
		}

		public void Release()
		{
			_gate.Writer.TryWrite(true);
		}

		public async Task WaitForRuns(int count)
		{
			await Until(() => _runs.Count >= count);
		}
	}

	private sealed class World : IAsyncDisposable
	{
		private readonly IHost _host;
		private readonly StubAutomationCache _automations;
		private readonly EventSubscriptionIndex _index;
		private readonly EventBus _bus;

		private World(
			IHost host,
			StubAutomationCache automations,
			EventSubscriptionIndex index,
			EventBus bus,
			GatedRunner runner)
		{
			_host = host;
			_automations = automations;
			_index = index;
			_bus = bus;
			Runner = runner;
		}

		public GatedRunner Runner { get; }

		public static async Task<World> Start()
		{
			var automations = new StubAutomationCache();
			var index = new EventSubscriptionIndex(new StubFolderCache(), automations);
			var bus = new EventBus(index, new EventSampleStore(), Serilog.Core.Logger.None);
			var readiness = new StartupReadiness();
			readiness.MarkCachesReady();
			readiness.MarkVariablesReady();
			var runner = new GatedRunner();

			var host = new HostBuilder()
				.ConfigureServices(services =>
				{
					services.AddSingleton<IEventSubscriptionIndex>(index);
					services.AddSingleton<IEventBus>(bus);
					services.AddSingleton(readiness);
					services.AddSingleton<Serilog.ILogger>(Serilog.Core.Logger.None);
					services.AddScoped<IEventTriggerRunner>(_ => runner);
					services.AddHostedService<EventDispatchBackgroundService>();
				})
				.Build();
			await host.StartAsync();
			await readiness.WhenEventDispatchReady.WaitAsync(TimeSpan.FromSeconds(5));

			return new World(host, automations, index, bus, runner);
		}

		public Guid AddAutomation()
		{
			var automation = _automations.Add(Flows());
			_index.ReindexAutomation(automation.Id, automation.Flows, true);
			return automation.Id;
		}

		public void Remove(Guid automation)
		{
			_index.ReindexAutomation(automation, null, true);
		}

		public void Restore(Guid automation)
		{
			_index.ReindexAutomation(automation, Flows(), true);
		}

		public void Publish(EventOccurrence occurrence)
		{
			_bus.Publish(occurrence);
		}

		public async Task Drained()
		{
			await Until(() => !_bus.Reader.TryPeek(out _));
			await Task.Delay(50);
		}

		public async Task Idle()
		{
			await Task.Delay(50);
			await Until(() => !Runner.Active);
		}

		public async ValueTask DisposeAsync()
		{
			await _host.StopAsync();
			_host.Dispose();
		}
	}

	private static async Task Until(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail("The condition was not met in time.");
			}

			await Task.Delay(5);
		}
	}
}
