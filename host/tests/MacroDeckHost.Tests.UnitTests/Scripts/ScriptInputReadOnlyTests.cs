using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Scripts;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Integrations.Variables;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;

namespace MacroDeckHost.Tests.UnitTests.Scripts;

/// <summary>
/// A Set Variable block running inside a script run, against the real variable writer - the read-only rule
/// is only observable from that combination.
/// </summary>
[TestFixture]
public class ScriptInputReadOnlyTests
{
	private const string OnRunFlow = "[{\"triggerId\":\"onRun\",\"triggerType\":\"onRun\",\"children\":[]}]";

	private VariableService _variables = null!;
	private CountingUserVariableStore _store = null!;
	private ServiceProvider _provider = null!;
	private UserVariableWriter _writer = null!;
	private ScriptCache _scripts = null!;
	private SetVariableFlowExecutor _executor = null!;
	private ScriptRunner _runner = null!;

	[SetUp]
	public async Task SetUp()
	{
		_store = new CountingUserVariableStore();
		_variables = TestVariableServices.Create(new VariableRegistry(), _store, new RecordingMediator());
		_provider = new ServiceCollection()
			.AddScoped<IVariableService>(_ => _variables)
			.AddScoped<IWidgetAppearanceService>(_ => new NoWidgets())
			.BuildServiceProvider();
		_writer = new UserVariableWriter(_provider.GetRequiredService<IServiceScopeFactory>());

		_scripts = new ScriptCache(new InMemoryScriptStore(), new LoggerConfiguration().CreateLogger());
		await _scripts.InitializeCache();
		_executor = new SetVariableFlowExecutor(_writer);
		_runner = new ScriptRunner(_scripts, _executor, new NoWidgets(), new LoggerConfiguration().CreateLogger());
	}

	[TearDown]
	public void TearDown()
	{
		_writer.Dispose();
		_provider.Dispose();
		_scripts.Dispose();
	}

	[Test]
	public async Task Setting_a_variable_a_script_input_shadows_fails_the_block_and_writes_nothing()
	{
		await _variables.CreateUserVariable("scene",
			VariableScope.Global,
			null,
			DomainVariableType.Text,
			"Starting Soon",
			null);
		var script = await Store(Input("scene"));
		_executor.Writes = [("scene", "Live")];
		_store.Reset();

		await _runner.RunAsync(script.Id, null, CancellationToken.None, 0, Supplied(("scene", "Live")));

		var outcome = _executor.Results.Single();
		Assert.Multiple(async () =>
		{
			Assert.That(outcome.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(TestLocalization.Resolve(outcome.ErrorMessage), Does.Contain("scene"));
			Assert.That(_store.SaveCount, Is.Zero);
			Assert.That(await ValueOf("scene"), Is.EqualTo("Starting Soon"));
		});
	}

	[Test]
	public async Task Setting_an_unrelated_variable_during_the_same_run_still_succeeds()
	{
		await _variables.CreateUserVariable("scene",
			VariableScope.Global,
			null,
			DomainVariableType.Text,
			"Starting Soon",
			null);
		await _variables.CreateUserVariable("status",
			VariableScope.Global,
			null,
			DomainVariableType.Text,
			"idle",
			null);
		var script = await Store(Input("scene"));
		_executor.Writes = [("scene", "Live"), ("status", "running")];
		_store.Reset();

		await _runner.RunAsync(script.Id, null, CancellationToken.None, 0, Supplied(("scene", "Live")));

		Assert.Multiple(async () =>
		{
			Assert.That(_executor.Results[0].Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(_executor.Results[1].Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_store.SaveCount, Is.EqualTo(1));
			Assert.That(await ValueOf("status"), Is.EqualTo("running"));
			Assert.That(await ValueOf("scene"), Is.EqualTo("Starting Soon"));
		});
	}

	[Test]
	public async Task A_declared_input_nobody_supplied_a_value_for_is_still_read_only()
	{
		await _variables.CreateUserVariable("scene",
			VariableScope.Global,
			null,
			DomainVariableType.Text,
			"Starting Soon",
			null);
		var script = await Store(Input("scene", defaultValue: "Intermission"));
		_executor.Writes = [("scene", "Live")];
		_store.Reset();

		await _runner.RunAsync(script.Id, null, CancellationToken.None);

		Assert.Multiple(async () =>
		{
			Assert.That(_executor.Results.Single().Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(_store.SaveCount, Is.Zero);
			Assert.That(await ValueOf("scene"), Is.EqualTo("Starting Soon"));
		});
	}

	[Test]
	public async Task The_guard_is_lifted_once_the_run_is_over()
	{
		await _variables.CreateUserVariable("scene",
			VariableScope.Global,
			null,
			DomainVariableType.Text,
			"Starting Soon",
			null);
		var script = await Store(Input("scene"));
		_executor.Writes = [];

		await _runner.RunAsync(script.Id, null, CancellationToken.None, 0, Supplied(("scene", "Live")));
		var afterTheRun = await _writer.ApplyAsync("scene", null, UserVariableOperation.Set, "Live");

		Assert.Multiple(async () =>
		{
			Assert.That(afterTheRun.Status, Is.EqualTo(UserVariableWriteStatus.Applied));
			Assert.That(await ValueOf("scene"), Is.EqualTo("Live"));
		});
	}

	private static ScriptInput Input(string name, string? defaultValue = null)
		=> new() { Name = name, Type = ScriptInputType.Text, DefaultValue = defaultValue };

	private static Dictionary<string, object?> Supplied(params (string Name, object? Value)[] values)
		=> values.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);

	private async Task<string> ValueOf(string name)
		=> (await _variables.Resolve(name, VariableScope.Global, null))!.Value;

	private async Task<ScriptEntity> Store(params ScriptInput[] inputs)
	{
		var script = new ScriptEntity
		{
			Id = Guid.NewGuid(),
			Name = "Script",
			Flows = OnRunFlow,
			Inputs = [.. inputs],
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow
		};
		await _scripts.AddOrUpdate(script);
		return script;
	}

	private sealed class SetVariableFlowExecutor : IFlowExecutor
	{
		private readonly IUserVariableApi _variables;

		public SetVariableFlowExecutor(IUserVariableApi variables) => _variables = variables;

		public IReadOnlyList<(string Name, string Value)> Writes { get; set; } = [];

		public List<ActionResult> Results { get; } = [];

		public async Task<FlowExecutionResult> ExecuteAsync(
			FlowExecutionRequest request,
			CancellationToken cancellationToken)
		{
			var executor = new SetVariableActionDefinition(() => _variables).CreateExecutor();
			foreach (var (name, value) in Writes)
			{
				Results.Add(await executor.ExecuteAsync(new ActionExecutionContext
				{
					Parameters = new Dictionary<string, object>(StringComparer.Ordinal)
					{
						["variable"] = name,
						["operation"] = "set",
						["value"] = value
					},
					CancellationToken = cancellationToken
				}));
			}

			return new FlowExecutionResult
			{
				ExecutionId = request.ExecutionId,
				Status = FlowExecutionStatus.Succeeded,
				MatchedFlows = 1
			};
		}
	}

	private sealed class CountingUserVariableStore : IUserVariableStore
	{
		public int SaveCount { get; private set; }

		public void Reset() => SaveCount = 0;

		public IReadOnlyList<VariableEntity> Load() => [];

		public void Save(IEnumerable<VariableEntity> userVariables) => SaveCount++;
	}

	private sealed class NoWidgets : IWidgetAppearanceService
	{
		public IReadOnlyList<WidgetTargetInfo> GetWidgets() => [];

		public bool Exists(string widgetId) => false;

		public Task<bool> ApplyAsync(WidgetAppearanceRequest request, CancellationToken cancellationToken = default)
			=> Task.FromResult(false);
	}
}
