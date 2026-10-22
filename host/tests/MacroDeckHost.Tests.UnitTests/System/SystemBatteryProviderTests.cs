using System.Reflection;
using MacroDeck.Sdk.Batteries;
using MacroDeckHost.Integrations.System;
using MacroDeckHost.Integrations.System.Providers;
using MacroDeckHost.Tests.UnitTests.Adb;
using MacroDeckHost.Tests.UnitTests.Delegation;

namespace MacroDeckHost.Tests.UnitTests.System;

[TestFixture]
public class SystemBatteryProviderTests
{
	private const string MachineName = "Studio-MacBook";
	private const string InternalId = "internal-0";

	private ScriptedBatteryReader _reader = null!;
	private FakeTimeProvider _time = null!;
	private RecordingBatteryProviderContext _context = null!;
	private SystemBatteryProvider _provider = null!;

	[SetUp]
	public void SetUp()
	{
		_reader = new ScriptedBatteryReader();
		_time = new FakeTimeProvider();
		_context = new RecordingBatteryProviderContext();
		_provider = new SystemBatteryProvider(_reader, _time, MachineName);
	}

	[TearDown]
	public async Task TearDown()
	{
		await _provider.ShutdownAsync(CancellationToken.None);
		_provider.Dispose();
	}

	private static HostBatteryReading Battery(
		int? level,
		BatteryChargeState state = BatteryChargeState.Discharging,
		string key = "0")
	{
		return new HostBatteryReading(key, level, state, null, null);
	}

	private static async Task WaitUntil(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition() && DateTime.UtcNow < deadline)
		{
			await Task.Delay(10);
		}

		Assert.That(condition(), Is.True, "timed out waiting for the provider");
	}

	private async Task PollAndSettleAsync()
	{
		var reads = _reader.ReadCount;
		_time.Advance(SystemBatteryProvider.PollInterval);
		await WaitUntil(() => _reader.ReadCount > reads);
		await Task.Delay(50);
	}

	[Test]
	public async Task The_computers_battery_is_published_on_start_as_a_laptop_named_after_the_computer()
	{
		_reader.Next = [new HostBatteryReading("0", 80, BatteryChargeState.Discharging, TimeSpan.FromHours(3), null)];

		await _provider.InitializeAsync(_context, CancellationToken.None);

		var published = _context.Publishes.Single().Single();
		Assert.That(published,
			Is.EqualTo(new BatteryDevice
			{
				Id = InternalId,
				Name = MachineName,
				Kind = BatteryDeviceKinds.Laptop,
				LevelPercent = 80,
				ChargeState = BatteryChargeState.Discharging,
				TimeRemaining = TimeSpan.FromHours(3),
				IsConnected = true
			}));
	}

	[Test]
	public async Task A_changed_level_is_published_on_the_next_poll_and_an_unchanged_one_is_not()
	{
		_reader.Next = [Battery(80)];
		await _provider.InitializeAsync(_context, CancellationToken.None);

		await PollAndSettleAsync();
		Assert.That(_context.Publishes, Has.Count.EqualTo(1), "an unchanged reading is not republished");

		_reader.Next = [Battery(79)];
		_time.Advance(SystemBatteryProvider.PollInterval);

		await WaitUntil(() => _context.Latest(InternalId)?.LevelPercent == 79);
		Assert.That(_context.Publishes, Has.Count.EqualTo(2));
	}

	[Test]
	public async Task A_failed_read_keeps_the_last_published_values()
	{
		_reader.Next = [Battery(80)];
		await _provider.InitializeAsync(_context, CancellationToken.None);

		_reader.Next = null;
		await PollAndSettleAsync();
		_reader.Failure = new IOException("pmset went away");
		await PollAndSettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_context.Publishes, Has.Count.EqualTo(1));
			Assert.That(_context.Latest(InternalId)!.IsConnected, Is.True);
		});
	}

	[Test]
	public async Task A_battery_that_disappears_stays_published_as_not_connected()
	{
		_reader.Next = [Battery(64)];
		await _provider.InitializeAsync(_context, CancellationToken.None);

		_reader.Next = [];
		_time.Advance(SystemBatteryProvider.PollInterval);

		await WaitUntil(() => _context.Latest(InternalId)?.IsConnected == false);
		Assert.Multiple(() =>
		{
			Assert.That(_context.Latest(InternalId)!.LevelPercent, Is.EqualTo(64), "the last known level is kept");
			Assert.That(_context.Removed, Is.Empty, "a vanished battery is not removed");
		});
	}

	[Test]
	public async Task A_computer_without_a_battery_publishes_nothing_and_stops_reading_after_the_first_read()
	{
		_reader.Next = [];

		await _provider.InitializeAsync(_context, CancellationToken.None);
		_reader.Next = [Battery(80)];
		_time.Advance(SystemBatteryProvider.PollInterval * 4);
		await Task.Delay(100);

		Assert.Multiple(() =>
		{
			Assert.That(_reader.ReadCount, Is.EqualTo(1));
			Assert.That(_context.Publishes, Is.Empty);
		});
	}

	[Test]
	public async Task A_computer_without_a_battery_is_read_again_after_a_new_start()
	{
		_reader.Next = [];
		await _provider.InitializeAsync(_context, CancellationToken.None);

		_reader.Next = [Battery(80)];
		await _provider.InitializeAsync(_context, CancellationToken.None);

		Assert.That(_context.Latest(InternalId)?.LevelPercent, Is.EqualTo(80));
	}

	[Test]
	public async Task A_laptop_whose_battery_reads_empty_keeps_polling_and_shows_it_as_not_connected()
	{
		_reader.Next = [Battery(64)];
		await _provider.InitializeAsync(_context, CancellationToken.None);

		_reader.Next = [];
		await PollAndSettleAsync();
		_reader.Next = [Battery(63)];
		_time.Advance(SystemBatteryProvider.PollInterval);

		await WaitUntil(() => _context.Latest(InternalId) is { IsConnected: true, LevelPercent: 63 });
		Assert.That(_context.Publishes.SelectMany(batch => batch).Select(device => device.IsConnected),
			Does.Contain(false),
			"the empty read showed the battery as not connected");
	}

	[Test]
	public async Task Several_batteries_are_told_apart_by_their_key()
	{
		_reader.Next = [Battery(70, key: "BAT0"), Battery(30, key: "BAT1")];

		await _provider.InitializeAsync(_context, CancellationToken.None);

		Assert.That(_context.Publishes.Single().Select(device => (device.Id, device.Name)),
			Is.EquivalentTo(new[]
			{
				("internal-BAT0", $"{MachineName} (BAT0)"),
				("internal-BAT1", $"{MachineName} (BAT1)")
			}));
	}

	[Test]
	public async Task Shutdown_stops_polling()
	{
		_reader.Next = [Battery(80)];
		await _provider.InitializeAsync(_context, CancellationToken.None);

		await _provider.ShutdownAsync(CancellationToken.None);
		_reader.Next = [Battery(10)];
		_time.Advance(SystemBatteryProvider.PollInterval * 3);
		await Task.Delay(100);

		Assert.Multiple(() =>
		{
			Assert.That(_time.ActiveTimerCount, Is.Zero);
			Assert.That(_context.Publishes, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task The_system_integration_publishes_the_computers_battery_under_its_own_name()
	{
		_reader.Next = [Battery(55)];
		using var system = new SystemIntegration(new FakeApplicationService { IsSupported = true },
			new FakeVolumeService(),
			new FakeSystemMetricsService(),
			new FakePowerService(true),
			null,
			null,
			_reader);
		IBatteryProvider integration = system;

		await integration.InitializeAsync(_context);
		await integration.ShutdownAsync();

		Assert.Multiple(() =>
		{
			Assert.That(integration.ProviderName, Is.Empty);
			Assert.That(_context.Latest(InternalId)?.LevelPercent, Is.EqualTo(55));
		});
	}

	[Test]
	public void The_provider_reaches_the_host_only_through_sdk_types_and_its_own_integration()
	{
		var hostTypes = TypesReachedBy(typeof(SystemBatteryProvider))
			.Where(type => type.Namespace?.StartsWith("MacroDeckHost", StringComparison.Ordinal) == true)
			.Where(type => type.Namespace != typeof(SystemBatteryProvider).Namespace)
			.ToList();

		Assert.That(hostTypes, Is.Empty);
	}

	private static HashSet<Type> TypesReachedBy(Type root)
	{
		const BindingFlags all = BindingFlags.Public
			| BindingFlags.NonPublic
			| BindingFlags.Instance
			| BindingFlags.Static
			| BindingFlags.DeclaredOnly;
		var seen = new HashSet<Type>();
		var pending = new Stack<Type>([root]);
		while (pending.TryPop(out var type))
		{
			foreach (var nested in type.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
			{
				pending.Push(nested);
			}

			var referenced = type.GetFields(all)
				.Select(field => field.FieldType)
				.Concat(type.GetMethods(all)
					.SelectMany(method => method.GetParameters()
						.Select(parameter => parameter.ParameterType)
						.Append(method.ReturnType)))
				.Concat(type.GetConstructors(all)
					.SelectMany(constructor => constructor.GetParameters()
						.Select(parameter => parameter.ParameterType)));
			foreach (var reference in referenced.SelectMany(Flatten))
			{
				seen.Add(reference);
			}
		}

		return seen;
	}

	private static IEnumerable<Type> Flatten(Type type)
	{
		var element = type.HasElementType ? type.GetElementType()! : type;
		yield return element.IsGenericType ? element.GetGenericTypeDefinition() : element;
		foreach (var argument in element.IsGenericType ? element.GetGenericArguments() : [])
		{
			foreach (var inner in Flatten(argument))
			{
				yield return inner;
			}
		}
	}

	private sealed class ScriptedBatteryReader : IHostBatteryReader
	{
		private int _readCount;

		public IReadOnlyList<HostBatteryReading>? Next { get; set; } = [];

		public Exception? Failure { get; set; }

		public int ReadCount => Volatile.Read(ref _readCount);

		public Task<IReadOnlyList<HostBatteryReading>?> ReadAsync(CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref _readCount);
			if (Failure is { } failure)
			{
				return Task.FromException<IReadOnlyList<HostBatteryReading>?>(failure);
			}

			return Task.FromResult(Next);
		}
	}
}
