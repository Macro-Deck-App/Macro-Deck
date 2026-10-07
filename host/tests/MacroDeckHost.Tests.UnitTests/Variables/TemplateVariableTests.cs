using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Variables.Templates;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class TemplateVariableTests
{
	private const string WidgetId = "22222222-2222-2222-2222-222222222222";

	private VariableRegistry _registry = null!;
	private RecordingMediator _mediator = null!;
	private TemplateVariableSynchronizer _templates = null!;
	private VariableService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_registry = new VariableRegistry();
		_mediator = new RecordingMediator();
		_templates = new TemplateVariableSynchronizer(_registry, _mediator);
		_mediator.BeforePublish = Route;
		_service = TestVariableServices.Create(_registry,
			new NullUserVariableStore(),
			_mediator,
			templates: _templates);
	}

	[TearDown]
	public void TearDown() => _templates.Dispose();

	[Test]
	public async Task A_template_variable_combines_static_text_variables_and_filters()
	{
		await Plain("weather_temperature", VariableType.Numeric, 21.5m);
		await Plain("weather_condition", VariableType.Text, "sunny");

		var created = await Template("status",
			VariableType.Text,
			"{{ vars.weather_temperature }} °C - {{ vars.weather_condition | upcase }}");

		Assert.Multiple(() =>
		{
			Assert.That(created.Success, Is.True);
			Assert.That(created.Data!.Value, Is.EqualTo("21.5 °C - SUNNY"));
			Assert.That(_registry.IsAvailable(created.Data.Id), Is.True);
			Assert.That(created.Data.CanWrite, Is.False);
		});
	}

	[Test]
	public async Task A_change_to_any_referenced_variable_renders_the_template_again()
	{
		var cpu = await Plain("cpu", VariableType.Numeric, 10m);
		var memory = await Plain("memory", VariableType.Numeric, 40m);
		var created = await Template("load", VariableType.Text, "CPU {{ vars.cpu }}% RAM {{ vars.memory }}%");

		await _service.SetValue(cpu.Id, 55m);
		await Settle();
		var afterCpu = Value(created.Data!);
		await _service.SetValue(memory.Id, 70m);
		await Settle();

		Assert.Multiple(() =>
		{
			Assert.That(afterCpu, Is.EqualTo("CPU 55% RAM 40%"));
			Assert.That(Value(created.Data!), Is.EqualTo("CPU 55% RAM 70%"));
		});
	}

	[Test]
	public async Task A_change_propagates_through_a_template_that_reads_another_template()
	{
		var temperature = await Plain("weather_temperature", VariableType.Numeric, 20m);
		await Plain("weather_condition", VariableType.Text, "cloudy");
		var text = await Template("template_temperature_text",
			VariableType.Text,
			"Temperature: {{ vars.weather_temperature }} °C");
		var status = await Template("template_status",
			VariableType.Text,
			"{{ vars.template_temperature_text }} - {{ vars.weather_condition }}");

		await _service.SetValue(temperature.Id, 23m);
		await Settle();

		Assert.Multiple(() =>
		{
			Assert.That(Value(text.Data!), Is.EqualTo("Temperature: 23 °C"));
			Assert.That(Value(status.Data!), Is.EqualTo("Temperature: 23 °C - cloudy"));
		});
	}

	[Test]
	public async Task A_template_whose_output_does_not_change_announces_nothing()
	{
		var cpu = await Plain("cpu", VariableType.Numeric, 10m);
		var created = await Template("cpu_state",
			VariableType.Text,
			"{% if vars.cpu > 50 %}high{% else %}low{% endif %}");
		_mediator.Published.Clear();

		await _service.SetValue(cpu.Id, 20m);
		await Settle();

		Assert.That(UpdatesOf(created.Data!), Is.Zero);
	}

	[TestCase("42", "42")]
	[TestCase(" 3.25 ", "3.25")]
	[TestCase("1e3", "1000")]
	public async Task A_numeric_template_converts_its_text_to_a_number(string rendered, string expected)
	{
		var created = await Template("number", VariableType.Numeric, rendered);

		Assert.That(created.Data!.Value, Is.EqualTo(expected));
	}

	[TestCase("abc")]
	[TestCase("1,5")]
	[TestCase("")]
	public async Task A_numeric_template_that_renders_no_number_leaves_the_variable_unavailable(string rendered)
	{
		var created = await Template("number", VariableType.Numeric, rendered);

		Assert.Multiple(() =>
		{
			Assert.That(created.Success, Is.True);
			Assert.That(_registry.IsAvailable(created.Data!.Id), Is.False);
			Assert.That(created.Data.TemplateError!.Code, Is.EqualTo(VariableTemplateError.NotNumeric));
		});
	}

	[TestCase("TRUE", "true")]
	[TestCase("0", "false")]
	public async Task A_boolean_template_converts_true_false_one_and_zero(string rendered, string expected)
	{
		var created = await Template("flag", VariableType.Boolean, rendered);

		Assert.That(created.Data!.Value, Is.EqualTo(expected));
	}

	[Test]
	public async Task A_boolean_template_that_renders_something_else_is_unavailable()
	{
		var created = await Template("flag", VariableType.Boolean, "maybe");

		Assert.That(created.Data!.TemplateError!.Code, Is.EqualTo(VariableTemplateError.NotBoolean));
	}

	[Test]
	public async Task An_invalid_result_does_not_stop_other_template_variables_from_updating()
	{
		var source = await Plain("source", VariableType.Text, "1");
		var number = await Template("as_number", VariableType.Numeric, "{{ vars.source }}");
		var text = await Template("as_text", VariableType.Text, "value {{ vars.source }}");

		await _service.SetValue(source.Id, "not a number");
		await Settle();
		var brokenAvailable = _registry.IsAvailable(number.Data!.Id);
		await _service.SetValue(source.Id, "7");
		await Settle();

		Assert.Multiple(() =>
		{
			Assert.That(brokenAvailable, Is.False);
			Assert.That(Value(text.Data!), Is.EqualTo("value 7"));
			Assert.That(Value(number.Data!), Is.EqualTo("7"));
			Assert.That(_registry.IsAvailable(number.Data!.Id), Is.True);
			Assert.That(number.Data!.TemplateError, Is.Null);
		});
	}

	[Test]
	public async Task A_template_that_cannot_be_parsed_is_refused()
	{
		var created = await Template("broken", VariableType.Text, "{{ vars.x ");

		Assert.That(created.Error, Is.EqualTo(VariableError.InvalidTemplate));
	}

	[Test]
	public async Task A_template_that_reads_itself_through_another_template_is_refused()
	{
		await Template("first", VariableType.Text, "a {{ vars.second }}");

		var created = await Template("second", VariableType.Text, "b {{ vars.first }}");

		Assert.Multiple(() =>
		{
			Assert.That(created.Error, Is.EqualTo(VariableError.CircularTemplate));
			Assert.That(_registry.FindByName(VariableScope.Global, null, "second"), Is.Null);
		});
	}

	[Test]
	public async Task Editing_a_template_into_a_cycle_is_refused_and_keeps_the_old_template()
	{
		var first = await Template("first", VariableType.Text, "static");
		await Template("second", VariableType.Text, "b {{ vars.first }}");

		var updated = await _service.UpdateUserVariable(first.Data!.Id,
			null,
			null,
			templateSource: new VariableTemplateSource("a {{ vars.second }}"));

		Assert.Multiple(() =>
		{
			Assert.That(updated.Error, Is.EqualTo(VariableError.CircularTemplate));
			Assert.That(first.Data.TemplateSource!.Template, Is.EqualTo("static"));
		});
	}

	[Test]
	public async Task Editing_a_template_renders_the_new_one()
	{
		await Plain("name", VariableType.Text, "Deck");
		var created = await Template("greeting", VariableType.Text, "Hi");

		var updated = await _service.UpdateUserVariable(created.Data!.Id,
			null,
			null,
			templateSource: new VariableTemplateSource("Hello {{ vars.name }}"));
		await Settle();

		Assert.Multiple(() =>
		{
			Assert.That(updated.Success, Is.True);
			Assert.That(Value(created.Data), Is.EqualTo("Hello Deck"));
		});
	}

	[Test]
	public async Task A_template_edit_whose_rename_is_refused_changes_nothing()
	{
		await Plain("taken", VariableType.Text, "x");
		var created = await Template("first", VariableType.Text, "a");
		_mediator.Published.Clear();

		var updated = await _service.UpdateUserVariable(created.Data!.Id,
			"taken",
			null,
			templateSource: new VariableTemplateSource("b"));
		await Settle();

		Assert.Multiple(() =>
		{
			Assert.That(updated.Error, Is.EqualTo(VariableError.AlreadyExists));
			Assert.That(created.Data.TemplateSource!.Template, Is.EqualTo("a"));
			Assert.That(Value(created.Data), Is.EqualTo("a"));
			Assert.That(UpdatesOf(created.Data), Is.Zero);
		});
	}

	[Test]
	public async Task Editing_the_template_and_its_precision_together_announces_only_the_final_value()
	{
		await Plain("cpu", VariableType.Numeric, 1.25m);
		var created = await _service.CreateUserVariable("cpu_rounded",
			VariableScope.Global,
			null,
			VariableType.Numeric,
			null,
			1,
			templateSource: new VariableTemplateSource("{{ vars.cpu }}"));
		await Settle();
		_mediator.Published.Clear();

		await _service.UpdateUserVariable(created.Data!.Id,
			null,
			3,
			templateSource: new VariableTemplateSource("{{ vars.cpu | times: 2 }}"));
		await Settle();

		var announced = _mediator.Published.OfType<VariableValueChangedNotification>()
			.Where(change => change.Variable.Id == created.Data.Id)
			.ToList();
		Assert.Multiple(() =>
		{
			Assert.That(announced, Has.Count.EqualTo(1));
			Assert.That(Value(created.Data), Is.EqualTo("2.500"));
		});
	}

	[Test]
	public async Task A_new_precision_is_announced_even_while_the_template_is_in_error()
	{
		var created = await _service.CreateUserVariable("broken",
			VariableScope.Global,
			null,
			VariableType.Numeric,
			null,
			1,
			templateSource: new VariableTemplateSource("abc"));
		_mediator.Published.Clear();

		await _service.UpdateUserVariable(created.Data!.Id,
			null,
			3,
			templateSource: new VariableTemplateSource("abc"));

		Assert.Multiple(() =>
		{
			Assert.That(created.Data.DecimalPlaces, Is.EqualTo(3));
			Assert.That(UpdatesOf(created.Data), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Renaming_a_template_variable_to_a_name_its_template_reads_is_refused()
	{
		await Plain("temp", VariableType.Text, "x");
		var created = await _service.CreateUserVariable("shown",
			VariableScope.Widget,
			WidgetId,
			VariableType.Text,
			null,
			null,
			templateSource: new VariableTemplateSource("[{{ vars.temp }}]"));

		var renamed = await _service.UpdateUserVariable(created.Data!.Id, "temp", null);

		Assert.Multiple(() =>
		{
			Assert.That(renamed.Error, Is.EqualTo(VariableError.CircularTemplate));
			Assert.That(created.Data.Name, Is.EqualTo("shown"));
		});
	}

	[Test]
	public async Task A_dependency_change_reaches_the_template_through_the_real_handlers_and_background_service()
	{
		var source = await Plain("source", VariableType.Text, "a");
		var reader = await Template("reader", VariableType.Text, "<{{ vars.source }}>");
		var updatedHandler = new TemplateVariableUpdatedHandler(_templates);
		var createdHandler = new TemplateVariableCreatedHandler(_templates);
		var deletedHandler = new TemplateVariableDeletedHandler(_templates);
		_mediator.BeforePublish = notification =>
		{
			var handled = notification switch
			{
				VariableUpdatedNotification updated => updatedHandler.Handle(updated, CancellationToken.None),
				VariableCreatedNotification added => createdHandler.Handle(added, CancellationToken.None),
				VariableDeletedNotification deleted => deletedHandler.Handle(deleted, CancellationToken.None),
				_ => ValueTask.CompletedTask
			};
			handled.AsTask().GetAwaiter().GetResult();
		};
		using var background = new TemplateVariableBackgroundService(_templates, Serilog.Core.Logger.None);
		await background.StartAsync(CancellationToken.None);

		await _service.SetValue(source.Id, "b");
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (Value(reader.Data!) != "<b>" && DateTime.UtcNow < deadline)
		{
			await Task.Delay(10);
		}

		await background.StopAsync(CancellationToken.None);
		Assert.That(Value(reader.Data!), Is.EqualTo("<b>"));
	}

	[Test]
	public async Task A_cycle_that_only_a_branch_creates_is_contained_and_recovers_when_the_branch_is_left()
	{
		var flag = await Plain("flag", VariableType.Boolean, false);
		var first = await Template("first", VariableType.Text, "{% if vars.flag %}{{ vars.second }}{% endif %}one");
		var second = await Template("second", VariableType.Text, "{{ vars.first }}two");

		await _service.SetValue(flag.Id, true);
		await Settle();
		var whileCyclic = (first.Data!.TemplateError?.Code, second.Data!.TemplateError?.Code);
		await _service.SetValue(flag.Id, false);
		await Settle();

		Assert.Multiple(() =>
		{
			Assert.That(whileCyclic.Item1, Is.EqualTo(VariableTemplateError.CircularReference));
			Assert.That(whileCyclic.Item2, Is.EqualTo(VariableTemplateError.CircularReference));
			Assert.That(Value(first.Data), Is.EqualTo("one"));
			Assert.That(Value(second.Data), Is.EqualTo("onetwo"));
			Assert.That(_registry.IsAvailable(first.Data.Id), Is.True);
			Assert.That(_registry.IsAvailable(second.Data.Id), Is.True);
		});
	}

	[Test]
	public async Task A_template_that_changes_on_every_render_is_only_rendered_when_a_dependency_changes()
	{
		var tick = await Plain("tick", VariableType.Numeric, 1m);
		var created = await Template("stamp", VariableType.Text, "{{ vars.tick }} {{ 'now' | date: '%H:%M:%S.%L' }}");
		_mediator.Published.Clear();

		await _service.SetValue(tick.Id, 2m);
		await Settle();

		Assert.Multiple(() =>
		{
			Assert.That(created.Data!.TemplateError, Is.Null, created.Data.TemplateError?.Detail);
			Assert.That(UpdatesOf(created.Data!), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Renaming_a_dependency_renders_the_templates_that_read_its_old_name()
	{
		var source = await Plain("source", VariableType.Text, "x");
		var created = await Template("reader", VariableType.Text, "[{{ vars.source }}]");

		await _service.UpdateUserVariable(source.Id, "renamed", null);
		await Settle();

		Assert.That(Value(created.Data!), Is.EqualTo("[]"));
	}

	[Test]
	public async Task Creating_a_variable_a_template_already_reads_renders_that_template()
	{
		var created = await Template("reader", VariableType.Text, "[{{ vars.later }}]");

		await Plain("later", VariableType.Text, "here");
		await Settle();

		Assert.That(Value(created.Data!), Is.EqualTo("[here]"));
	}

	[Test]
	public async Task Renaming_an_unavailable_template_variable_keeps_it_unavailable()
	{
		var created = await Template("number", VariableType.Numeric, "abc");

		await _service.UpdateUserVariable(created.Data!.Id, "still_broken", null);

		Assert.That(_registry.IsAvailable(created.Data.Id), Is.False);
	}

	[Test]
	public async Task A_template_variable_refuses_a_written_value()
	{
		var created = await Template("fixed", VariableType.Text, "constant");

		var written = await _service.SetValue(created.Data!.Id, "other");

		Assert.Multiple(() =>
		{
			Assert.That(written.Error, Is.EqualTo(VariableError.TemplateReadOnly));
			Assert.That(Value(created.Data), Is.EqualTo("constant"));
		});
	}

	[Test]
	public async Task A_widget_template_variable_deleted_with_its_widget_is_not_brought_back_by_a_dependency()
	{
		var source = await Plain("source", VariableType.Text, "a");
		var created = await _service.CreateUserVariable("local",
			VariableScope.Widget,
			WidgetId,
			VariableType.Text,
			null,
			null,
			templateSource: new VariableTemplateSource("{{ vars.source }}"));

		await _service.DeleteByScopeInstance(VariableScope.Widget, WidgetId);
		_mediator.Published.Clear();
		await _service.SetValue(source.Id, "b");
		await Settle();

		Assert.Multiple(() =>
		{
			Assert.That(_registry.GetById(created.Data!.Id), Is.Null);
			Assert.That(UpdatesOf(created.Data!), Is.Zero);
		});
	}

	[Test]
	public async Task A_widget_template_variable_reads_its_widgets_variables_before_global_ones()
	{
		await Plain("label", VariableType.Text, "global");
		await _service.CreateUserVariable("label", VariableScope.Widget, WidgetId, VariableType.Text, "local", null);

		var created = await _service.CreateUserVariable("shown",
			VariableScope.Widget,
			WidgetId,
			VariableType.Text,
			null,
			null,
			templateSource: new VariableTemplateSource("{{ vars.label }}"));

		Assert.That(created.Data!.Value, Is.EqualTo("local"));
	}

	[Test]
	public async Task Startup_renders_stored_templates_in_dependency_order_without_announcing_a_change()
	{
		var temperature = Stored("weather_temperature", VariableType.Numeric, "18");
		var status = Stored("template_status", VariableType.Text, string.Empty, "{{ vars.template_text }}!");
		var text = Stored("template_text", VariableType.Text, string.Empty, "{{ vars.weather_temperature }} °C");
		foreach (var variable in new[] { temperature, status, text })
		{
			_registry.Upsert(variable);
		}

		await _templates.InitializeAsync();

		Assert.Multiple(() =>
		{
			Assert.That(status.Value, Is.EqualTo("18 °C!"));
			Assert.That(_mediator.Published, Is.Empty);
		});
	}

	[Test]
	public async Task Renaming_a_dependency_after_a_restart_still_renders_its_readers()
	{
		var source = Stored("source", VariableType.Text, "x");
		var reader = Stored("reader", VariableType.Text, string.Empty, "[{{ vars.source }}]");
		_registry.Upsert(source);
		_registry.Upsert(reader);
		await _templates.InitializeAsync();

		await _service.UpdateUserVariable(source.Id, "renamed", null);
		await Settle();

		Assert.That(reader.Value, Is.EqualTo("[]"));
	}

	[Test]
	public async Task The_preview_reports_the_typed_value_and_a_cycle_for_a_variable_not_created_yet()
	{
		await Plain("count", VariableType.Numeric, 4m);
		await Template("reader", VariableType.Text, "{{ vars.planned }}");

		var typed = _templates.Preview("{{ vars.count | times: 2 }}",
			VariableType.Numeric,
			1,
			VariableScope.Global,
			null,
			null,
			"doubled");
		var cyclic = _templates.Preview("{{ vars.reader }}", VariableType.Text, null, VariableScope.Global, null, null,
			"planned");
		var invalid = _templates.Preview("{{ vars.count }} items",
			VariableType.Numeric,
			null,
			VariableScope.Global,
			null,
			null,
			"items");

		Assert.Multiple(() =>
		{
			Assert.That(typed.Value, Is.EqualTo("8.0"));
			Assert.That(cyclic.Error!.Code, Is.EqualTo(VariableTemplateError.CircularReference));
			Assert.That(invalid.Error!.Code, Is.EqualTo(VariableTemplateError.NotNumeric));
		});
	}

	private void Route(object notification)
	{
		switch (notification)
		{
			case VariableCreatedNotification created:
				_templates.NoteChanged(created.Variable);
				break;
			case VariableUpdatedNotification updated:
				_templates.NoteChanged(updated.Variable);
				break;
			case VariableDeletedNotification deleted:
				_templates.NoteDeleted(deleted.Variable);
				break;
		}
	}

	private async Task Settle()
	{
		for (var pass = 0; pass < 20; pass++)
		{
			if (!_templates.HasPendingChanges)
			{
				return;
			}

			await _templates.DrainAsync();
		}

		Assert.Fail("Template variables kept changing each other and never settled.");
	}

	private async Task<VariableEntity> Plain(string name, VariableType type, object value)
	{
		var created = await _service.CreateUserVariable(name, VariableScope.Global, null, type, value, null);
		await Settle();
		return created.Data!;
	}

	private async Task<MacroDeckHost.Domain.Common.Result<VariableEntity, VariableError>> Template(
		string name,
		VariableType type,
		string template)
	{
		var created = await _service.CreateUserVariable(name,
			VariableScope.Global,
			null,
			type,
			null,
			null,
			templateSource: new VariableTemplateSource(template));
		await Settle();
		return created;
	}

	private static VariableEntity Stored(string name, VariableType type, string value, string? template = null) => new()
	{
		Id = Guid.CreateVersion7(),
		Name = name,
		Scope = VariableScope.Global,
		Type = type,
		Classification = VariableClassification.User,
		Value = value,
		TemplateSource = template is null ? null : new VariableTemplateSource(template)
	};

	private string Value(VariableEntity variable) => _registry.GetById(variable.Id)!.Value;

	private int UpdatesOf(VariableEntity variable)
		=> _mediator.Published.OfType<VariableUpdatedNotification>().Count(update => update.Variable.Id == variable.Id);
}
