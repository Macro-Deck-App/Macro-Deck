using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Variables;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;
using SdkVariableType = MacroDeck.Sdk.Variables.VariableType;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class ScriptAutomationVariableTests
{
	private const string WidgetId = "11111111-1111-1111-1111-111111111111";

	private VariableRegistry _registry = null!;
	private VariableService _variables = null!;
	private ScriptCache _scriptCache = null!;
	private AutomationCache _automationCache = null!;
	private ScriptService _scripts = null!;
	private AutomationService _automations = null!;
	private ServiceProvider _provider = null!;
	private UserVariableWriter _writer = null!;

	[SetUp]
	public async Task SetUp()
	{
		_registry = new VariableRegistry();
		_variables = TestVariableServices.Create(_registry, new NullUserVariableStore(), new RecordingMediator());
		var logger = new LoggerConfiguration().CreateLogger();
		_scriptCache = new ScriptCache(new InMemoryScriptStore(), logger);
		await _scriptCache.InitializeCache();
		_automationCache = new AutomationCache(new InMemoryAutomationStore(), logger);
		await _automationCache.InitializeCache();
		var cloner = new WidgetVariableCloner(_variables, logger);
		_scripts = new ScriptService(_scriptCache, new RecordingMediator(), cloner);
		_automations = new AutomationService(_automationCache, new RecordingMediator(), cloner);

		_provider = new ServiceCollection()
			.AddScoped<IVariableService>(_ => _variables)
			.AddScoped<IScriptService>(_ => _scripts)
			.AddScoped<IAutomationService>(_ => _automations)
			.AddScoped<IWidgetAppearanceService>(_ => new FakeWidgetAppearanceService(WidgetId))
			.BuildServiceProvider();
		_writer = new UserVariableWriter(_provider.GetRequiredService<IServiceScopeFactory>());
	}

	[TearDown]
	public void TearDown()
	{
		_writer.Dispose();
		_provider.Dispose();
		_scriptCache.Dispose();
		_automationCache.Dispose();
	}

	[Test]
	public async Task A_write_in_a_script_lands_on_its_local_and_leaves_the_widget_and_global_alone()
	{
		var script = await CreateScript();
		await Create("count", VariableScope.Global, null, 1);
		await Create("count", VariableScope.Widget, WidgetId, 10);
		await Create("count", VariableScope.Script, script.Id.ToString(), 100);

		var result = await _writer.ApplyInContextAsync("count",
			new UserVariableContext(WidgetId, script.Id.ToString()),
			UserVariableOperation.Add,
			"1");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.Applied));
			Assert.That(ValueOf(VariableScope.Script, script.Id.ToString()), Is.EqualTo("101"));
			Assert.That(ValueOf(VariableScope.Widget, WidgetId), Is.EqualTo("10"));
			Assert.That(ValueOf(VariableScope.Global, null), Is.EqualTo("1"));
		});
	}

	[Test]
	public async Task A_write_in_a_script_without_a_local_falls_through_to_its_widget_then_global()
	{
		var script = await CreateScript();
		await Create("count", VariableScope.Global, null, 1);
		await Create("count", VariableScope.Widget, WidgetId, 10);

		await _writer.ApplyInContextAsync("count",
			new UserVariableContext(WidgetId, script.Id.ToString()),
			UserVariableOperation.Add,
			"1");
		await _writer.ApplyInContextAsync("count",
			new UserVariableContext(ScriptId: script.Id.ToString()),
			UserVariableOperation.Add,
			"1");

		Assert.Multiple(() =>
		{
			Assert.That(ValueOf(VariableScope.Widget, WidgetId), Is.EqualTo("11"));
			Assert.That(ValueOf(VariableScope.Global, null), Is.EqualTo("2"));
		});
	}

	[Test]
	public async Task A_write_in_an_automation_lands_on_its_local()
	{
		var automation = await CreateAutomation();
		await Create("count", VariableScope.Global, null, 1);
		await Create("count", VariableScope.Automation, automation.Id.ToString(), 5);

		await _writer.ApplyInContextAsync("count",
			new UserVariableContext(AutomationId: automation.Id.ToString()),
			UserVariableOperation.Set,
			"7");

		Assert.Multiple(() =>
		{
			Assert.That(ValueOf(VariableScope.Automation, automation.Id.ToString()), Is.EqualTo("7"));
			Assert.That(ValueOf(VariableScope.Global, null), Is.EqualTo("1"));
		});
	}

	[Test]
	public async Task A_create_in_a_script_makes_a_local_of_that_script()
	{
		var script = await CreateScript();

		var result = await _writer.CreateInContextAsync("result",
			new UserVariableContext(WidgetId, script.Id.ToString()),
			SdkVariableType.Text,
			"done");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(UserVariableCreateStatus.Created));
			Assert.That(_registry.FindByName(VariableScope.Script, script.Id.ToString(), "result")?.Value,
				Is.EqualTo("done"));
			Assert.That(_registry.FindByName(VariableScope.Widget, WidgetId, "result"), Is.Null);
			Assert.That(_registry.FindByName(VariableScope.Global, null, "result"), Is.Null);
		});
	}

	[Test]
	public async Task A_create_for_a_script_or_automation_that_does_not_exist_is_refused()
	{
		var script = await _writer.CreateInContextAsync("result",
			new UserVariableContext(ScriptId: Guid.NewGuid().ToString()),
			SdkVariableType.Text);
		var automation = await _writer.CreateInContextAsync("result",
			new UserVariableContext(AutomationId: "not-an-id"),
			SdkVariableType.Text);

		Assert.Multiple(() =>
		{
			Assert.That(script.Status, Is.EqualTo(UserVariableCreateStatus.UnknownScript));
			Assert.That(automation.Status, Is.EqualTo(UserVariableCreateStatus.UnknownAutomation));
			Assert.That(_registry.GetAll(), Is.Empty);
		});
	}

	[Test]
	public async Task Deleting_a_script_or_automation_removes_its_locals_only()
	{
		var script = await CreateScript();
		var automation = await CreateAutomation();
		await Create("count", VariableScope.Global, null, 1);
		await Create("count", VariableScope.Script, script.Id.ToString(), 2);
		await Create("count", VariableScope.Automation, automation.Id.ToString(), 3);

		await new ScriptVariablesScriptDeletedHandler(_provider.GetRequiredService<IServiceScopeFactory>())
			.Handle(new ScriptDeletedNotification(script.Id), CancellationToken.None);
		await new AutomationVariablesAutomationDeletedHandler(_provider.GetRequiredService<IServiceScopeFactory>())
			.Handle(new AutomationDeletedNotification(automation.Id), CancellationToken.None);

		Assert.That(_registry.GetAll().Single().Scope, Is.EqualTo(VariableScope.Global));
	}

	[Test]
	public async Task Duplicating_a_script_or_automation_copies_its_user_locals_to_the_copy()
	{
		var script = await CreateScript();
		var automation = await CreateAutomation();
		await Create("count", VariableScope.Script, script.Id.ToString(), 2);
		await Create("count", VariableScope.Automation, automation.Id.ToString(), 3);

		var scriptCopy = (await _scripts.Duplicate(script.Id)).Data!;
		var automationCopy = (await _automations.Duplicate(automation.Id)).Data!;

		Assert.Multiple(() =>
		{
			Assert.That(ValueOf(VariableScope.Script, scriptCopy.Id.ToString()), Is.EqualTo("2"));
			Assert.That(ValueOf(VariableScope.Automation, automationCopy.Id.ToString()), Is.EqualTo("3"));
			Assert.That(ValueOf(VariableScope.Script, script.Id.ToString()), Is.EqualTo("2"));
		});
	}

	[TestCase("script")]
	[TestCase("automation")]
	[TestCase("widget")]
	public async Task The_ui_cannot_create_a_local_for_an_owner_that_does_not_exist(string scope)
	{
		var response = await CreateHandler()
			.Handle(new CreateVariableRequest
				{
					Name = "orphan",
					Scope = scope,
					ScopeRefId = Guid.NewGuid().ToString(),
					Type = "text"
				},
				CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(_registry.GetAll(), Is.Empty);
		});
	}

	[Test]
	public async Task The_ui_creates_a_local_for_an_existing_script_widget_and_automation()
	{
		var script = await CreateScript();
		var automation = await CreateAutomation();
		var handler = CreateHandler();

		var responses = new[]
		{
			await handler.Handle(Request("script", script.Id.ToString()), CancellationToken.None),
			await handler.Handle(Request("automation", automation.Id.ToString()), CancellationToken.None),
			await handler.Handle(Request("widget", WidgetId), CancellationToken.None)
		};

		Assert.Multiple(() =>
		{
			Assert.That(responses.Select(response => response.Success), Is.All.True);
			Assert.That(responses.Select(response => response.Variable!.Scope),
				Is.EqualTo(new[] { "script", "automation", "widget" }));
		});
	}

	private static CreateVariableRequest Request(string scope, string scopeRefId)
	{
		return new CreateVariableRequest { Name = "local", Scope = scope, ScopeRefId = scopeRefId, Type = "text" };
	}

	private CreateVariableRequestMessageHandler CreateHandler()
	{
		return new CreateVariableRequestMessageHandler(_variables,
			_registry,
			new SharedVariables(_registry, new InMemorySharedVariableStore(), new LoggerConfiguration().CreateLogger()),
			new FakeWidgetAppearanceService(WidgetId),
			_scripts,
			_automations);
	}

	private async Task<ScriptEntity> CreateScript()
	{
		return (await _scripts.Create("Script", null, "[]")).Data!;
	}

	private async Task<AutomationEntity> CreateAutomation()
	{
		return (await _automations.Create("Automation", null, "[]")).Data!;
	}

	private async Task Create(string name, VariableScope scope, string? scopeRefId, decimal value)
	{
		await _variables.CreateUserVariable(name, scope, scopeRefId, DomainVariableType.Numeric, value, null);
	}

	private string? ValueOf(VariableScope scope, string? scopeRefId)
	{
		return _registry.FindByName(scope, scopeRefId, "count")?.Value;
	}
}
