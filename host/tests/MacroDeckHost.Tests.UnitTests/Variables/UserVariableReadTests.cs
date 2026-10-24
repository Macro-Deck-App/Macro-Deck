using MacroDeck.Sdk.Variables;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;
using SdkVariableType = MacroDeck.Sdk.Variables.VariableType;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class UserVariableReadTests
{
	private const string WidgetId = "11111111-1111-1111-1111-111111111111";
	private const string ScriptId = "22222222-2222-2222-2222-222222222222";
	private const string AutomationId = "33333333-3333-3333-3333-333333333333";

	private VariableRegistry _registry = null!;
	private ServiceProvider _provider = null!;
	private UserVariableWriter _writer = null!;

	[SetUp]
	public void SetUp()
	{
		_registry = new VariableRegistry();
		var service = TestVariableServices.Create(_registry, new NullUserVariableStore(), new RecordingMediator());
		_provider = new ServiceCollection()
			.AddSingleton(_registry)
			.AddScoped<IVariableService>(_ => service)
			.BuildServiceProvider();
		_writer = new UserVariableWriter(_provider.GetRequiredService<IServiceScopeFactory>());
	}

	[TearDown]
	public void TearDown()
	{
		_writer.Dispose();
		_provider.Dispose();
	}

	[Test]
	public async Task A_read_resolves_script_then_automation_then_widget_then_global()
	{
		Add("level", VariableScope.Global, null, "1");
		Add("level", VariableScope.Widget, WidgetId, "2");
		Add("level", VariableScope.Automation, AutomationId, "3");
		Add("level", VariableScope.Script, ScriptId, "4");

		var script = await Read(new UserVariableContext(WidgetId, ScriptId, AutomationId));
		var automation = await Read(new UserVariableContext(WidgetId, null, AutomationId));
		var widget = await Read(new UserVariableContext(WidgetId));
		var global = await Read(new UserVariableContext());

		Assert.That(new[] { script.Text, automation.Text, widget.Text, global.Text },
			Is.EqualTo(new[] { "4", "3", "2", "1" }));
	}

	[Test]
	public async Task A_numeric_variable_reads_as_a_number_and_as_the_text_a_template_shows()
	{
		Add("level", VariableScope.Global, null, "2.5", DomainVariableType.Numeric, entity => entity.DecimalPlaces = 2);

		var result = await Read(new UserVariableContext());

		Assert.That(result, Is.EqualTo(UserVariableReadResult.Found(SdkVariableType.Numeric, 2.5d, "2.50")));
	}

	[Test]
	public async Task An_integration_variable_is_found_by_its_name()
	{
		Add("autohotkey_installed",
			VariableScope.Global,
			null,
			"true",
			DomainVariableType.Boolean,
			entity =>
			{
				entity.Classification = VariableClassification.Integration;
				entity.OwnerIntegrationId = "app.macro-deck.autohotkey";
				entity.UpdatedAt = DateTime.UtcNow;
			});

		var result = await _writer.ReadInContextAsync("autohotkey_installed", new UserVariableContext(WidgetId));

		Assert.That(result, Is.EqualTo(UserVariableReadResult.Found(SdkVariableType.Boolean, true, "true")));
	}

	[Test]
	public async Task A_name_nothing_declares_is_not_found()
	{
		Add("level", VariableScope.Widget, WidgetId, "2");

		var result = await Read(new UserVariableContext());

		Assert.That(result.Status, Is.EqualTo(UserVariableReadStatus.NotFound));
	}

	[TestCase("")]
	[TestCase("has space")]
	public async Task A_name_that_cannot_be_a_variable_name_is_reported_as_such(string name)
	{
		var result = await _writer.ReadInContextAsync(name, new UserVariableContext());

		Assert.That(result.Status, Is.EqualTo(UserVariableReadStatus.InvalidName));
	}

	[Test]
	public async Task A_variable_whose_owner_has_no_value_right_now_is_unavailable_with_its_type()
	{
		var entity = Add("level", VariableScope.Global, null, "7", DomainVariableType.Numeric);
		_registry.SetAvailable(entity.Id, false);

		var result = await Read(new UserVariableContext());

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(UserVariableReadStatus.Unavailable));
			Assert.That(result.Type, Is.EqualTo(SdkVariableType.Numeric));
			Assert.That(result.Value, Is.Null);
		});
	}

	private Task<UserVariableReadResult> Read(UserVariableContext context)
	{
		return _writer.ReadInContextAsync("level", context);
	}

	private VariableEntity Add(
		string name,
		VariableScope scope,
		string? scopeRefId,
		string value,
		DomainVariableType type = DomainVariableType.Text,
		Action<VariableEntity>? configure = null)
	{
		var entity = new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = name,
			Scope = scope,
			ScopeRefId = scopeRefId,
			Type = type,
			Classification = VariableClassification.User,
			Value = value
		};
		configure?.Invoke(entity);
		_registry.Upsert(entity);
		return entity;
	}
}
