using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Variables.Templates;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.OptionsSources;
using MacroDeckHost.Infrastructure.SystemIntegrations.Variables;
using MacroDeckHost.Localization;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class RefreshTemplateVariableActionTests
{
	private const string WidgetId = "22222222-2222-2222-2222-222222222222";
	private const string SecondsTemplate = "{{ date.now | date.to_string '%S' }}";

	private VariableRegistry _registry = null!;
	private ManualTimeProvider _clock = null!;
	private RecordingMediator _mediator = null!;
	private TemplateVariableSynchronizer _templates = null!;
	private VariableService _service = null!;
	private IActionExecutor _executor = null!;

	[SetUp]
	public void SetUp()
	{
		_registry = new VariableRegistry();
		_clock = new ManualTimeProvider
		{
			Now = new DateTimeOffset(2026, 3, 1, 12, 0, 10, TimeSpan.Zero),
			Zone = TimeZoneInfo.Utc
		};
		_mediator = new RecordingMediator();
		_templates = new TemplateVariableSynchronizer(_registry, _mediator, _clock);
		_mediator.BeforePublish = Route;
		_service = TestVariableServices.Create(_registry,
			new NullUserVariableStore(),
			_mediator,
			templates: _templates);
		_executor = new RefreshTemplateVariableActionDefinition(() => (name, context, _)
				=> HostTemplateVariableRefresher.RefreshAsync(_service, _registry, _templates, name, context))
			.CreateExecutor();
	}

	[TearDown]
	public void TearDown()
	{
		_templates.Dispose();
	}

	[Test]
	public async Task Refreshing_renders_the_template_again_and_announces_the_new_value()
	{
		var seconds = await Template("seconds", VariableScope.Global, null, SecondsTemplate);
		_clock.Advance(TimeSpan.FromSeconds(5));

		var result = await Run("seconds");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(Value(seconds), Is.EqualTo("15"));
			Assert.That(ChangesOf(seconds), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Refreshing_to_the_same_value_does_not_announce_a_change()
	{
		var seconds = await Template("seconds", VariableScope.Global, null, SecondsTemplate);

		var result = await Run("seconds");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(Value(seconds), Is.EqualTo("10"));
			Assert.That(ChangesOf(seconds), Is.Zero);
		});
	}

	[Test]
	public async Task A_variable_that_reads_the_refreshed_one_follows_it()
	{
		await Template("seconds", VariableScope.Global, null, SecondsTemplate);
		var label = await Template("label", VariableScope.Global, null, "at {{ vars.seconds }}");
		_clock.Advance(TimeSpan.FromSeconds(7));

		await Run("seconds");
		await Settle();

		Assert.That(Value(label), Is.EqualTo("at 17"));
	}

	[Test]
	public async Task A_widget_refreshes_its_own_template_variable_before_a_global_one_of_the_same_name()
	{
		var global = await Template("seconds", VariableScope.Global, null, SecondsTemplate);
		var local = await Template("seconds", VariableScope.Widget, WidgetId, SecondsTemplate);
		_clock.Advance(TimeSpan.FromSeconds(3));

		await Run("seconds", WidgetId);

		Assert.Multiple(() =>
		{
			Assert.That(Value(local), Is.EqualTo("13"));
			Assert.That(Value(global), Is.EqualTo("10"));
		});
	}

	[Test]
	public async Task A_variable_that_is_not_a_template_variable_fails_the_action()
	{
		await _service.CreateUserVariable("count", VariableScope.Global, null, VariableType.Numeric, 1m, null);

		var result = await Run("count");

		LocalizedText expected = AppStrings.Integrations.Variables.Errors.NotATemplateVariable();
		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(result.ErrorMessage, Is.EqualTo(expected));
		});
	}

	[Test]
	public async Task A_variable_that_does_not_exist_fails_the_action()
	{
		var result = await Run("missing");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
		});
	}

	[Test]
	public async Task A_template_that_gives_no_value_fails_the_action()
	{
		await _service.CreateUserVariable("text", VariableScope.Global, null, VariableType.Text, "abc", null);
		await Template("number", VariableScope.Global, null, "{{ vars.text }}", VariableType.Numeric);

		var result = await Run("number");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
		});
	}

	[Test]
	public async Task The_picker_offers_template_variables_only()
	{
		await _service.CreateUserVariable("count", VariableScope.Global, null, VariableType.Numeric, 1m, null);
		await Template("seconds", VariableScope.Global, null, SecondsTemplate);
		await Template("label", VariableScope.Widget, WidgetId, "{{ vars.count }}");

		var options = await new TemplateVariablesOptionsSource(_registry).GetOptionsAsync(null, CancellationToken.None);

		Assert.That(options.Options.Select(option => option.Value), Is.EqualTo(new[] { "label", "seconds" }));
	}

	private Task<ActionResult> Run(string name, string? widgetId = null)
	{
		return _executor.ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object> { ["variable"] = name },
			OwnerWidgetId = widgetId
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

	private async Task<VariableEntity> Template(
		string name,
		VariableScope scope,
		string? scopeRefId,
		string template,
		VariableType type = VariableType.Text)
	{
		var created = await _service.CreateUserVariable(name,
			scope,
			scopeRefId,
			type,
			null,
			null,
			templateSource: new VariableTemplateSource(template));
		await Settle();
		return created.Data!;
	}

	private string Value(VariableEntity variable)
	{
		return _registry.GetById(variable.Id)!.Value;
	}

	private int ChangesOf(VariableEntity variable)
	{
		return _mediator.Published.OfType<VariableValueChangedNotification>()
			.Count(change => change.Variable.Id == variable.Id);
	}
}
