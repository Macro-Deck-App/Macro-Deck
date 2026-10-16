using System.Security.Cryptography;
using System.Text;
using MacroDeck.Localization;
using MacroDeck.Sdk.Batteries;
using MacroDeckHost.Application.Batteries;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;
using MacroDeckHost.Tests.UnitTests.Integrations;
using MacroDeckHost.Tests.UnitTests.Variables;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Batteries;

[TestFixture]
internal sealed class BatteryVariablePublisherTests
{
	private const string Adb = "app.macro-deck.adb";
	private const string Plugin = "com.example.headsets";
	private const string PhoneId = $"{Adb}:3fa9c2d1e0b4a756";
	private const string HeadsetId = $"{Plugin}:hs-1";

	private static readonly string[] _suffixes =
		["level", "charging", "connected", "state", "time_remaining_minutes", "time_to_full_minutes", "health"];

	private VariableCatalogHarness _variables = null!;
	private IntegrationRegistry _integrations = null!;
	private BatteryRegistry _batteries = null!;
	private BatteryVariablePublisher _publisher = null!;

	[SetUp]
	public async Task SetUp()
	{
		_variables = new VariableCatalogHarness();
		_integrations = new IntegrationRegistry(
			new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
			new IntegrationLifecycleTests.FakeIntegrationStateStore(),
			Serilog.Core.Logger.None);
		_batteries = new BatteryRegistry(TimeProvider.System, Serilog.Core.Logger.None, _integrations);
		_publisher = new BatteryVariablePublisher(_batteries, _variables.ScopeFactory, Serilog.Core.Logger.None);
		await _publisher.StartAsync(CancellationToken.None);
	}

	[TearDown]
	public async Task TearDown()
	{
		await _publisher.StopAsync(CancellationToken.None);
		_batteries.Dispose();
	}

	private static string Hash(string qualifiedId)
	{
		return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(qualifiedId)))[..8];
	}

	private static string PhoneVariable(string suffix)
	{
		return $"battery_pixel_8_{Hash(PhoneId)}_{suffix}";
	}

	private static string HeadsetVariable(string suffix)
	{
		return $"battery_headset_{Hash(HeadsetId)}_{suffix}";
	}

	private static BatteryDevice Device(
		string qualifiedId,
		string name,
		int? level,
		BatteryChargeState chargeState = BatteryChargeState.Discharging,
		bool connected = true,
		TimeSpan? timeRemaining = null,
		TimeSpan? timeToFull = null,
		BatteryHealth health = BatteryHealth.Unknown)
	{
		return new BatteryDevice
		{
			Id = qualifiedId[(qualifiedId.IndexOf(':') + 1)..],
			Name = name,
			Kind = BatteryDeviceKinds.Phone,
			LevelPercent = level,
			ChargeState = chargeState,
			IsConnected = connected,
			TimeRemaining = timeRemaining,
			TimeToFull = timeToFull,
			Health = health
		};
	}

	private VariableEntity? Variable(string name)
	{
		return _variables.Registry.FindByName(VariableScope.Global, null, name);
	}

	private object? ValueOf(string name)
	{
		return Variable(name) is { } variable
			? VariableValueSerializer.Deserialize(variable.Type, variable.Value)
			: null;
	}

	private Dictionary<string, object?> ValuesOf(Func<string, string> nameOf)
	{
		return _suffixes.ToDictionary(suffix => suffix, suffix => ValueOf(nameOf(suffix)));
	}

	private List<VariableEntity> BatteryVariables()
	{
		return _variables.Registry.GetAll()
			.Where(variable => variable.Name.StartsWith("battery_", StringComparison.Ordinal))
			.ToList();
	}

	private static async Task WaitUntil(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition() && DateTime.UtcNow < deadline)
		{
			await Task.Delay(10);
		}

		Assert.That(condition(), Is.True, "timed out waiting for the battery variables");
	}

	[Test]
	public async Task Only_the_level_variable_declares_a_percent_unit()
	{
		_batteries.Publish(Adb, null, [Device(PhoneId, "Pixel 8", 81)]);

		await WaitUntil(() => BatteryVariables().Count == 7);

		Assert.That(_suffixes.ToDictionary(suffix => suffix, suffix => Variable(PhoneVariable(suffix))!.Unit),
			Is.EquivalentTo(new Dictionary<string, string?>
			{
				["level"] = "%",
				["charging"] = null,
				["connected"] = null,
				["state"] = null,
				["time_remaining_minutes"] = null,
				["time_to_full_minutes"] = null,
				["health"] = null
			}));
	}

	[Test]
	public async Task A_level_variable_created_without_a_unit_gains_it_and_keeps_its_name()
	{
		var displayName = (LocalizedText)AppStrings.Integrations.Batteries.Variables.Level("Pixel 8");
		await _variables.VariableService.CreateIntegrationVariable(Adb,
			PhoneVariable("level"),
			VariableScope.Global,
			null,
			VariableType.Numeric,
			50,
			0,
			declaration: new VariableDeclaration
			{
				Presentation = new VariablePresentation(displayName, null, default),
				DecimalPlaces = 0
			},
			updateMode: VariableUpdateMode.Pushed,
			publishedByHost: true);
		Assert.That(Variable(PhoneVariable("level"))!.Unit, Is.Null);

		_batteries.Publish(Adb, null, [Device(PhoneId, "Pixel 8", 81)]);

		await WaitUntil(() => Variable(PhoneVariable("level"))!.Unit == "%");
		Assert.Multiple(() =>
		{
			Assert.That(Variable(PhoneVariable("level"))!.Presentation?.DisplayName, Is.EqualTo(displayName));
			Assert.That(ValueOf(PhoneVariable("level")), Is.EqualTo(81m));
		});
	}

	[Test]
	public async Task Each_device_gets_its_seven_variables_under_the_integration_that_provides_it()
	{
		_batteries.Publish(Adb,
			null,
			[
				Device(PhoneId,
					"Pixel 8",
					81,
					BatteryChargeState.Charging,
					timeToFull: TimeSpan.FromMinutes(42.2),
					health: BatteryHealth.Good)
			]);
		_batteries.Publish(Plugin,
			"session-1",
			[
				Device(HeadsetId,
					"Headset",
					30,
					timeRemaining: TimeSpan.FromMinutes(90) + TimeSpan.FromSeconds(1),
					health: BatteryHealth.Overheat)
			]);

		await WaitUntil(() => BatteryVariables().Count == 14);

		Assert.Multiple(() =>
		{
			Assert.That(ValuesOf(PhoneVariable),
				Is.EquivalentTo(new Dictionary<string, object?>
				{
					["level"] = 81m,
					["charging"] = true,
					["connected"] = true,
					["state"] = "charging",
					["time_remaining_minutes"] = 0m,
					["time_to_full_minutes"] = 43m,
					["health"] = "good"
				}));
			Assert.That(ValuesOf(HeadsetVariable),
				Is.EquivalentTo(new Dictionary<string, object?>
				{
					["level"] = 30m,
					["charging"] = false,
					["connected"] = true,
					["state"] = "discharging",
					["time_remaining_minutes"] = 91m,
					["time_to_full_minutes"] = 0m,
					["health"] = "overheat"
				}));
			Assert.That(_suffixes.Select(suffix => Variable(PhoneVariable(suffix))!.OwnerIntegrationId),
				Is.All.EqualTo(Adb));
			Assert.That(_suffixes.Select(suffix => Variable(HeadsetVariable(suffix))!.OwnerIntegrationId),
				Is.All.EqualTo(Plugin));
			Assert.That(_variables.Registry.GetByOwnerIntegration("app.macro-deck.batteries"), Is.Empty);
			Assert.That(_suffixes.Select(suffix => Variable(PhoneVariable(suffix))!.PublishedByHost), Is.All.True);
			Assert.That(_suffixes.ToDictionary(suffix => suffix, suffix => Variable(PhoneVariable(suffix))!.Type),
				Is.EquivalentTo(new Dictionary<string, VariableType>
				{
					["level"] = VariableType.Numeric,
					["charging"] = VariableType.Boolean,
					["connected"] = VariableType.Boolean,
					["state"] = VariableType.Text,
					["time_remaining_minutes"] = VariableType.Numeric,
					["time_to_full_minutes"] = VariableType.Numeric,
					["health"] = VariableType.Text
				}));
			Assert.That(Variable(PhoneVariable("level"))!.Presentation?.DisplayName,
				Is.EqualTo((LocalizedText)AppStrings.Integrations.Batteries.Variables.Level("Pixel 8")));
			Assert.That(Variable(HeadsetVariable("health"))!.Presentation?.DisplayName,
				Is.EqualTo((LocalizedText)AppStrings.Integrations.Batteries.Variables.Health("Headset")));
		});
	}

	[Test]
	public async Task A_variable_the_integration_created_itself_is_never_taken_over()
	{
		var own = await _variables.VariableService.CreateIntegrationVariable(Adb,
			PhoneVariable("level"),
			VariableScope.Global,
			null,
			VariableType.Numeric,
			5,
			0);

		_batteries.Publish(Adb, null, [Device(PhoneId, "Pixel 8", 81)]);

		await WaitUntil(() => BatteryVariables().Count == 7);
		var level = Variable(PhoneVariable("level"))!;
		Assert.Multiple(() =>
		{
			Assert.That(level.Id, Is.EqualTo(own.Data!.Id));
			Assert.That(level.PublishedByHost, Is.False);
			Assert.That(ValueOf(PhoneVariable("level")), Is.EqualTo(5m));
			Assert.That(Variable(PhoneVariable("connected"))!.PublishedByHost, Is.True);
		});
	}

	[Test]
	public async Task Battery_variables_stay_available_long_after_their_last_change()
	{
		_batteries.Publish(Adb, null, [Device(PhoneId, "Pixel 8", 81)]);
		await WaitUntil(() => BatteryVariables().Count == 7);

		foreach (var variable in BatteryVariables())
		{
			variable.UpdatedAt = DateTime.UtcNow - VariableRegistry.IntegrationFreshness - TimeSpan.FromMinutes(5);
		}

		Assert.That(BatteryVariables().Select(variable => _variables.Registry.IsAvailable(variable.Id)), Is.All.True);
	}

	[Test]
	public async Task A_device_reported_as_not_connected_reads_disconnected_with_no_times()
	{
		_batteries.Publish(Adb,
			null,
			[Device(PhoneId, "Pixel 8", 81, BatteryChargeState.Charging, health: BatteryHealth.Good)]);
		await WaitUntil(() => BatteryVariables().Count == 7);

		_batteries.Publish(Adb,
			null,
			[
				Device(PhoneId,
					"Pixel 8",
					64,
					BatteryChargeState.Charging,
					false,
					TimeSpan.FromMinutes(10),
					TimeSpan.FromMinutes(30),
					BatteryHealth.Good)
			]);

		await WaitUntil(() => Equals(ValueOf(PhoneVariable("connected")), false));
		Assert.That(ValuesOf(PhoneVariable),
			Is.EquivalentTo(new Dictionary<string, object?>
			{
				["level"] = 64m,
				["charging"] = false,
				["connected"] = false,
				["state"] = "disconnected",
				["time_remaining_minutes"] = 0m,
				["time_to_full_minutes"] = 0m,
				["health"] = "good"
			}));
	}

	[Test]
	public async Task A_withdrawn_device_reads_disconnected_and_keeps_its_last_level_and_health()
	{
		_batteries.Publish(Adb,
			null,
			[
				Device(PhoneId,
					"Pixel 8",
					81,
					BatteryChargeState.Charging,
					timeToFull: TimeSpan.FromMinutes(20),
					health: BatteryHealth.Good)
			]);
		_batteries.Publish(Plugin,
			"session-1",
			[Device(HeadsetId, "Headset", 30, timeRemaining: TimeSpan.FromMinutes(120))]);
		await WaitUntil(() => BatteryVariables().Count == 14);

		_batteries.RemoveAll(Adb);

		await WaitUntil(() => Equals(ValueOf(PhoneVariable("state")), "disconnected"));
		Assert.Multiple(() =>
		{
			Assert.That(BatteryVariables(), Has.Count.EqualTo(14));
			Assert.That(ValuesOf(PhoneVariable),
				Is.EquivalentTo(new Dictionary<string, object?>
				{
					["level"] = 81m,
					["charging"] = false,
					["connected"] = false,
					["state"] = "disconnected",
					["time_remaining_minutes"] = 0m,
					["time_to_full_minutes"] = 0m,
					["health"] = "good"
				}));
			Assert.That(ValueOf(HeadsetVariable("time_remaining_minutes")), Is.EqualTo(120m));
		});
	}

	[Test]
	public async Task The_devices_of_a_disabled_integration_read_as_not_connected()
	{
		_batteries.Publish(Plugin, "session-1", [Device(HeadsetId, "Headset", 30)]);
		await WaitUntil(() => Equals(ValueOf(HeadsetVariable("connected")), true));

		_integrations.SetEnabled(Plugin, false);

		await WaitUntil(() => Equals(ValueOf(HeadsetVariable("connected")), false));
		Assert.Multiple(() =>
		{
			Assert.That(ValueOf(HeadsetVariable("state")), Is.EqualTo("disconnected"));
			Assert.That(ValueOf(HeadsetVariable("level")), Is.EqualTo(30m));
		});
	}

	[Test]
	public async Task A_device_its_provider_removed_for_good_loses_its_variables()
	{
		_batteries.Publish(Adb, null, [Device(PhoneId, "Pixel 8", 81)]);
		_batteries.Publish(Plugin, "session-1", [Device(HeadsetId, "Headset", 30)]);
		await WaitUntil(() => BatteryVariables().Count == 14);

		_batteries.Remove(Adb, [PhoneId[(Adb.Length + 1)..]]);

		await WaitUntil(() => BatteryVariables().Count == 7);
		Assert.That(BatteryVariables().Select(variable => variable.Name),
			Is.EquivalentTo(_suffixes.Select(HeadsetVariable)));
	}

	[Test]
	public async Task A_renamed_device_keeps_the_variable_names_it_was_created_with()
	{
		_batteries.Publish(Adb, null, [Device(PhoneId, "Pixel 8", 81)]);
		await WaitUntil(() => BatteryVariables().Count == 7);

		_batteries.Publish(Adb, null, [Device(PhoneId, "Work phone", 70)]);

		await WaitUntil(() => Equals(ValueOf(PhoneVariable("level")), 70m));
		Assert.That(BatteryVariables().Select(variable => variable.Name),
			Is.EquivalentTo(_suffixes.Select(PhoneVariable)));
	}

	[Test]
	public async Task An_unknown_level_reads_zero_and_an_unknown_state_and_health_read_unknown()
	{
		_batteries.Publish(Adb, null, [Device(PhoneId, "Pixel 8", null, BatteryChargeState.Unknown)]);

		await WaitUntil(() => BatteryVariables().Count == 7);
		Assert.That(ValuesOf(PhoneVariable),
			Is.EquivalentTo(new Dictionary<string, object?>
			{
				["level"] = 0m,
				["charging"] = false,
				["connected"] = true,
				["state"] = "unknown",
				["time_remaining_minutes"] = 0m,
				["time_to_full_minutes"] = 0m,
				["health"] = "unknown"
			}));
	}

	[Test]
	public async Task Forgetting_a_plugin_removes_its_battery_variables_for_good()
	{
		_batteries.Publish(Plugin, "session-1", [Device(HeadsetId, "Headset", 30)]);
		await WaitUntil(() => BatteryVariables().Count == 7);

		_batteries.RemoveAll(Plugin);
		await _variables.BindingService.RemoveIntegrationAsync(Plugin);
		_batteries.Publish(Adb, null, [Device(PhoneId, "Pixel 8", 81)]);

		await WaitUntil(() => Variable(PhoneVariable("level")) is not null);
		await WaitUntil(() => BatteryVariables().Count == 7);
		Assert.That(_variables.Registry.GetByOwnerIntegration(Plugin), Is.Empty);
	}

	[Test]
	public async Task Nothing_is_written_once_the_publisher_has_stopped()
	{
		_batteries.Publish(Adb, null, [Device(PhoneId, "Pixel 8", 81)]);
		await WaitUntil(() => BatteryVariables().Count == 7);

		await _publisher.StopAsync(CancellationToken.None);
		_batteries.Publish(Adb, null, [Device(PhoneId, "Pixel 8", 20)]);
		_batteries.Publish(Plugin, "session-1", [Device(HeadsetId, "Headset", 30)]);
		await Task.Delay(100);

		Assert.Multiple(() =>
		{
			Assert.That(ValueOf(PhoneVariable("level")), Is.EqualTo(81m));
			Assert.That(BatteryVariables(), Has.Count.EqualTo(7));
		});
	}
}
