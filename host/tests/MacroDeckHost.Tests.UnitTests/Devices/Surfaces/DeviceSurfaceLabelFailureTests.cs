using System.Text.Json;
using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Devices.Surfaces;

[TestFixture]
public class DeviceSurfaceLabelFailureTests
{
	private const string RoundedCpuLabel = "{{ vars.cpu | round: 1 }}";

	private static readonly Guid _roundedId = Guid.NewGuid();
	private static readonly Guid _temperatureId = Guid.NewGuid();
	private static readonly Guid _staticId = Guid.NewGuid();

	private VariableRegistry _registry = null!;
	private VariableEntity _cpu = null!;
	private VariableEntity _temperature = null!;
	private DeviceSurfaceFixture _fixture = null!;
	private List<LogEvent> _warnings = null!;

	[SetUp]
	public void SetUp()
	{
		_registry = new VariableRegistry();
		_cpu = Variable("cpu", VariableType.Numeric, "12.345");
		_temperature = Variable("temperature", VariableType.Text, "20");
		_registry.Upsert(_cpu, false);
		_registry.Upsert(_temperature);

		var folders = new FakeFolderCache();
		folders.AddWidget(Entity(_roundedId, RoundedCpuLabel));
		folders.AddWidget(Entity(_temperatureId, "{{ vars.temperature }} C"));
		folders.AddWidget(Entity(_staticId, "Static"));

		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();

		_warnings = [];
		var logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.Sink(new DelegatingLogSink(logEvent =>
			{
				if (logEvent.Level == LogEventLevel.Warning)
				{
					lock (_warnings)
					{
						_warnings.Add(logEvent);
					}
				}
			}))
			.CreateLogger();

		_fixture = new DeviceSurfaceFixture(null,
			new LabelTextService(folders,
				new VariableTemplateRenderer(_registry),
				readiness,
				new LabelRenderFailureLog(logger)));
		_fixture.Home.Widgets =
		[
			DeviceSurfaceFixture.Button(_roundedId.ToString(), 0, 0, label: RoundedCpuLabel),
			DeviceSurfaceFixture.Button(_temperatureId.ToString(), 1, 0, label: "{{ vars.temperature }} C"),
			DeviceSurfaceFixture.Button(_staticId.ToString(), 2, 0, label: "Static")
		];
	}

	[TearDown]
	public void TearDown()
	{
		_fixture.Dispose();
	}

	[Test]
	public async Task A_label_that_fails_to_render_does_not_stop_the_rest_of_the_page()
	{
		var deviceId = await _fixture.OpenDeviceAsync();
		await _fixture.SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Provider.PushCount(deviceId), Is.GreaterThan(0), "the page never reached the device");
			Assert.That(LabelOf(deviceId, _roundedId), Is.EqualTo(VariableTemplateRenderer.UnavailablePlaceholder));
			Assert.That(LabelOf(deviceId, _temperatureId), Is.EqualTo("20 C"));
			Assert.That(LabelOf(deviceId, _staticId), Is.EqualTo("Static"));
		});

		_temperature.Value = "21";
		_registry.Upsert(_temperature);
		await _fixture.Service.OnWidgetLabelChanged(_temperatureId, "off");
		await _fixture.SettleAsync();

		Assert.That(LabelOf(deviceId, _temperatureId), Is.EqualTo("21 C"));
	}

	[Test]
	public async Task The_failing_label_recovers_once_its_variable_becomes_available()
	{
		var deviceId = await _fixture.OpenDeviceAsync();
		await _fixture.SettleAsync();

		_registry.SetAvailable(_cpu.Id, true);
		await _fixture.Service.OnWidgetLabelChanged(_roundedId, "off");
		await _fixture.SettleAsync();

		Assert.That(LabelOf(deviceId, _roundedId), Is.EqualTo("12.3"));
	}

	[Test]
	public async Task A_failing_label_is_logged_once_however_often_the_page_rebuilds()
	{
		await _fixture.OpenDeviceAsync();
		await _fixture.SettleAsync();

		for (var refresh = 0; refresh < 5; refresh++)
		{
			await _fixture.Service.OnWidgetLabelChanged(_roundedId, "off");
			await _fixture.SettleAsync();
		}

		Assert.That(_warnings, Has.Count.EqualTo(1));

		_registry.SetAvailable(_cpu.Id, true);
		await _fixture.Service.OnWidgetLabelChanged(_roundedId, "off");
		await _fixture.SettleAsync();
		_registry.SetAvailable(_cpu.Id, false);
		await _fixture.Service.OnWidgetLabelChanged(_roundedId, "off");
		await _fixture.SettleAsync();

		Assert.That(_warnings, Has.Count.EqualTo(2), "a failure after a recovery is a new failure");
	}

	[Test]
	public async Task A_widget_whose_state_cannot_be_resolved_stays_on_the_page_and_pressable()
	{
		_fixture.WidgetStates.Fail(_roundedId);

		var deviceId = await _fixture.OpenDeviceAsync();
		await _fixture.SettleAsync();

		var widgets = _fixture.Provider.Latest(deviceId).Widgets;
		Assert.Multiple(() =>
		{
			Assert.That(widgets.Select(widget => widget.Id),
				Is.EquivalentTo(new[] { _roundedId.ToString(), _temperatureId.ToString(), _staticId.ToString() }));
			Assert.That(widgets.Single(widget => widget.Id == _roundedId.ToString()).SupportedInteractions,
				Does.Contain(DeviceInteractionKind.Press));
			Assert.That(LabelOf(deviceId, _staticId), Is.EqualTo("Static"));
		});
	}

	private string? LabelOf(Guid deviceId, Guid widgetId)
	{
		return _fixture.Provider.Latest(deviceId)
			.Widgets.Single(widget => widget.Id == widgetId.ToString())
			.Appearance?.Label;
	}

	private static VariableEntity Variable(string name, VariableType type, string value)
	{
		return new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = name,
			Scope = VariableScope.Global,
			Type = type,
			Classification = VariableClassification.User,
			Value = value
		};
	}

	private static WidgetEntity Entity(Guid id, string label)
	{
		return new WidgetEntity
		{
			Id = id,
			FolderId = Guid.NewGuid(),
			Type = WidgetTypeIds.ActionButton,
			Data = JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
				{ ["label"] = label })
		};
	}
}
