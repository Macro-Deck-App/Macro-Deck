using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Scripts;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Scripts;

[TestFixture]
public class ScriptRunnerTests
{
	private const string OnRunFlow = "[{\"triggerId\":\"onRun\",\"triggerType\":\"onRun\",\"children\":[]}]";

	private static readonly string[] _sceneOnly = ["scene"];

	private ScriptCache _cache = null!;
	private RecordingFlowExecutor _executor = null!;
	private ScriptRunner _runner = null!;

	[SetUp]
	public async Task SetUp()
	{
		_cache = new ScriptCache(new InMemoryScriptStore(), new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		_executor = new RecordingFlowExecutor();
		_runner = new ScriptRunner(_cache,
			_executor,
			new FakeWidgetAppearanceService("w1", "w2"),
			new LoggerConfiguration().CreateLogger());
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task RunAsync_SelectsTheOnRunFlowInGlobalScope()
	{
		var script = await Store("[{\"triggerId\":\"onRun\",\"triggerType\":\"onRun\",\"children\":[]}]");

		await _runner.RunAsync(script.Id, "client-1", CancellationToken.None);

		var request = _executor.Requests.Single();
		Assert.Multiple(() =>
		{
			Assert.That(request.Trigger.ByTriggerId, Is.False);
			Assert.That(request.Trigger.Value, Is.EqualTo("onRun"));
			Assert.That(request.Scope, Is.EqualTo(VariableScope.Global));
			Assert.That(request.ScopeRefId, Is.Null);
			Assert.That(request.OriginClientId, Is.EqualTo("client-1"));
		});
	}

	[Test]
	public async Task RunAsync_WrapsTheStoredFlowSoTheExecutorCanReadIt()
	{
		const string flows = "[{\"triggerId\":\"onRun\",\"triggerType\":\"onRun\",\"children\":[]}]";
		var script = await Store(flows);

		await _runner.RunAsync(script.Id, null, CancellationToken.None);

		Assert.That(WidgetFlowsJson.TryExtract(_executor.Requests.Single().FlowsSource, out var extracted), Is.True);
		Assert.That(extracted, Is.EqualTo(flows));
	}

	[Test]
	public async Task RunAsync_IsANoOpForAnUnknownScript()
	{
		var result = await _runner.RunAsync(Guid.NewGuid(), null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_executor.Requests, Is.Empty);
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionExecutionErrorCodes.ScriptNotFound));
		});
	}

	[Test]
	public async Task RunAsync_StopsAScriptThatKeepsRunningItself()
	{
		var script = await Store("[{\"triggerId\":\"onRun\",\"triggerType\":\"onRun\",\"children\":[]}]");
		_executor.OnExecute = () => _runner.RunAsync(script.Id, null, CancellationToken.None);

		var result = await _runner.RunAsync(script.Id, null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_executor.Requests, Has.Count.EqualTo(10));
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionExecutionErrorCodes.ScriptDepthExceeded));
		});
	}

	[Test]
	public async Task RunAsync_FailsWithoutRunningWhenTheInheritedDepthIsAtTheCap()
	{
		var script = await Store("[{\"triggerId\":\"onRun\",\"triggerType\":\"onRun\",\"children\":[]}]");

		var result = await _runner.RunAsync(script.Id, null, CancellationToken.None, inheritedDepth: 10);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionExecutionErrorCodes.ScriptDepthExceeded));
			Assert.That(_executor.Requests, Is.Empty);
		});
	}

	[Test]
	public async Task RunAsync_WithNoInheritedDepthRunsNormally()
	{
		var script = await Store("[{\"triggerId\":\"onRun\",\"triggerType\":\"onRun\",\"children\":[]}]");

		var result = await _runner.RunAsync(script.Id, null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
			Assert.That(_executor.Requests, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task RunAsync_AnInheritedDepthBelowTheCapStillRunsAndANestedCallKeepsCounting()
	{
		var script = await Store("[{\"triggerId\":\"onRun\",\"triggerType\":\"onRun\",\"children\":[]}]");
		var observedDepths = new List<int>();
		var ranNestedCall = false;

		_executor.OnExecute = async () =>
		{
			observedDepths.Add(ScriptCallDepth.Current);
			if (!ranNestedCall)
			{
				ranNestedCall = true;
				await _runner.RunAsync(script.Id, null, CancellationToken.None);
			}

			return new FlowExecutionResult
			{
				ExecutionId = Guid.NewGuid(),
				Status = FlowExecutionStatus.Succeeded,
				MatchedFlows = 1
			};
		};

		var result = await _runner.RunAsync(script.Id, null, CancellationToken.None, inheritedDepth: 3);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
			Assert.That(observedDepths, Has.Count.EqualTo(2));
			Assert.That(observedDepths[0], Is.EqualTo(4), "inheritedDepth (3) + 1");
			Assert.That(observedDepths[1], Is.GreaterThan(observedDepths[0]));
		});
	}

	[Test]
	public async Task RunAsync_leavesTheFlowUntouchedForAScriptWithNoDeclarations()
	{
		var script = await Store(OnRunFlow);

		var result = await _runner.RunAsync(script.Id, "client-1", CancellationToken.None);

		var request = _executor.Requests.Single();
		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
			Assert.That(request.Trigger.Value, Is.EqualTo("onRun"));
			Assert.That(request.Scope, Is.EqualTo(VariableScope.Global));
			Assert.That(request.ScopeRefId, Is.Null);
			Assert.That(request.ScriptInputs, Is.Empty);
			Assert.That(result.AppliedInputs, Is.Empty);
		});
	}

	[Test]
	public async Task RunAsync_doesNotCarryAnInputFromOneRunIntoTheNext()
	{
		var script = await Store(OnRunFlow, Input("scene"));

		await _runner.RunAsync(script.Id, null, CancellationToken.None, 0, Supplied(("scene", "Live")));
		await _runner.RunAsync(script.Id, null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_executor.Requests[0].ScriptInputs!["scene"], Is.EqualTo("Live"));
			Assert.That(_executor.Requests[1].ScriptInputs, Is.Empty);
		});
	}

	[Test]
	public async Task RunAsync_givesTwoConcurrentRunsTheirOwnInputValues()
	{
		var script = await Store(OnRunFlow, Input("scene"));
		var bothRecorded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var recorded = 0;

		_executor.OnExecute = async () =>
		{
			// Neither run may finish before both overlays exist, so a shared or ambient overlay is caught
			// rather than being masked by the two runs happening to be sequential.
			if (Interlocked.Increment(ref recorded) == 2)
			{
				bothRecorded.SetResult();
			}

			await bothRecorded.Task;
			return new FlowExecutionResult
			{
				ExecutionId = Guid.NewGuid(),
				Status = FlowExecutionStatus.Succeeded,
				MatchedFlows = 1
			};
		};

		await Task.WhenAll(_runner.RunAsync(script.Id, null, CancellationToken.None, 0, Supplied(("scene", "Live"))),
			_runner.RunAsync(script.Id, null, CancellationToken.None, 0, Supplied(("scene", "Break"))));

		Assert.That(_executor.Requests.Select(request => request.ScriptInputs!["scene"]),
			Is.EquivalentTo(new object?[] { "Live", "Break" }));
	}

	[Test]
	public async Task RunAsync_failsBeforeAnyBlockWhenARequiredInputHasNoValue()
	{
		var script = await Store(OnRunFlow, Input("scene", required: true));

		var result = await _runner.RunAsync(script.Id, null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionExecutionErrorCodes.ScriptInputMissing));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("scene"));
			Assert.That(_executor.Requests, Is.Empty);
		});
	}

	[Test]
	public async Task RunAsync_failsWhenASuppliedValueDoesNotFitTheDeclaredType()
	{
		var script = await Store(OnRunFlow, Input("volume", type: ScriptInputType.Numeric));

		var result = await _runner.RunAsync(script.Id, null, CancellationToken.None, 0, Supplied(("volume", "abc")));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionExecutionErrorCodes.ScriptInputInvalid));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("volume").And.Contain("abc"));
			Assert.That(_executor.Requests, Is.Empty);
		});
	}

	[Test]
	public async Task RunAsync_failsWhenASuppliedValueIsNotABoolean()
	{
		var script = await Store(OnRunFlow, Input("muted", type: ScriptInputType.Boolean));

		var result = await _runner.RunAsync(script.Id, null, CancellationToken.None, 0, Supplied(("muted", "maybe")));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionExecutionErrorCodes.ScriptInputInvalid));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("muted").And.Contain("maybe"));
			Assert.That(_executor.Requests, Is.Empty);
		});
	}

	[Test]
	public async Task RunAsync_doesNotLeakAnOuterScriptsInputsIntoANestedScriptThatDeclaresNone()
	{
		var inner = await Store(OnRunFlow);
		var outer = await Store(OnRunFlow, Input("scene"));
		var ranInner = false;

		_executor.OnExecute = async () =>
		{
			if (!ranInner)
			{
				ranInner = true;
				await _runner.RunAsync(inner.Id, null, CancellationToken.None);
			}

			return new FlowExecutionResult
			{
				ExecutionId = Guid.NewGuid(),
				Status = FlowExecutionStatus.Succeeded,
				MatchedFlows = 1
			};
		};

		await _runner.RunAsync(outer.Id, null, CancellationToken.None, 0, Supplied(("scene", "Live")));

		Assert.Multiple(() =>
		{
			Assert.That(_executor.Requests[0].ScriptInputs!["scene"], Is.EqualTo("Live"));
			Assert.That(_executor.Requests[1].ScriptInputs, Is.Empty);
		});
	}

	[Test]
	public async Task RunAsync_reportsOnlyTheInputNamesTheCallerSupplied()
	{
		// Narrowed meaning: "applied" means the caller's value was used, not merely that the run ended up
		// with a value. `mood` falls back to its own declared default, so it is not reported.
		var script = await Store(OnRunFlow,
			Input("scene"),
			Input("mood", defaultValue: "calm"),
			Input("unset"),
			Input("volume", ScriptInputType.Numeric, defaultValue: "5"),
			Input("muted", ScriptInputType.Boolean, defaultValue: "false"));

		var result = await _runner.RunAsync(script.Id,
			null,
			CancellationToken.None,
			0,
			Supplied(("scene", "Live")));

		var request = _executor.Requests.Single();
		Assert.Multiple(() =>
		{
			Assert.That(result.AppliedInputs, Is.EqualTo(_sceneOnly));
			Assert.That(request.ScriptInputs!["volume"], Is.EqualTo(5d));
			Assert.That(request.ScriptInputs!["muted"], Is.EqualTo(false));
		});
	}

	[TestCase("scene", "Starting Soon")]
	[TestCase("volume", 0d)]
	[TestCase("muted", false)]
	[TestCase("scene", "")]
	public async Task RunAsync_reportsAnInputAppliedEvenWhenItsValueMatchesOrLooksLikeTheDefault(
		string suppliedName,
		object suppliedValue)
	{
		var script = await Store(OnRunFlow,
			Input("scene", defaultValue: "Starting Soon"),
			Input("volume", ScriptInputType.Numeric, defaultValue: "5"),
			Input("muted", ScriptInputType.Boolean, defaultValue: "false"));

		var result = await _runner.RunAsync(script.Id,
			null,
			CancellationToken.None,
			0,
			Supplied((suppliedName, suppliedValue)));

		Assert.That(result.AppliedInputs, Is.EqualTo(new List<string> { suppliedName }));
	}

	[Test]
	public async Task RunAsync_doesNotReportAnUndeclaredSuppliedNameAsApplied()
	{
		var script = await Store(OnRunFlow, Input("scene", defaultValue: "Starting Soon"));

		var result = await _runner.RunAsync(script.Id,
			null,
			CancellationToken.None,
			0,
			Supplied(("scene", "BRB"), ("typo", "x")));

		var request = _executor.Requests.Single();
		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(FlowExecutionStatus.Succeeded));
			Assert.That(result.AppliedInputs, Is.EqualTo(_sceneOnly));
			Assert.That(request.ScriptInputs!.ContainsKey("typo"), Is.False);
		});
	}

	private static ScriptInput Input(
		string name,
		ScriptInputType type = ScriptInputType.Text,
		bool required = false,
		string? defaultValue = null)
		=> new() { Name = name, Type = type, Required = required, DefaultValue = defaultValue };

	private static Dictionary<string, object?> Supplied(params (string Name, object? Value)[] values)
		=> values.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);

	private async Task<ScriptEntity> Store(string flows, params ScriptInput[] inputs)
	{
		var script = new ScriptEntity
		{
			Id = Guid.NewGuid(),
			Name = "Script",
			Flows = flows,
			Inputs = [.. inputs],
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow
		};
		await _cache.AddOrUpdate(script);
		return script;
	}

	private sealed class RecordingFlowExecutor : IFlowExecutor
	{
		private readonly Lock _gate = new();

		public List<FlowExecutionRequest> Requests { get; } = [];

		public Func<Task<FlowExecutionResult>>? OnExecute { get; set; }

		public async Task<FlowExecutionResult> ExecuteAsync(FlowExecutionRequest request,
			CancellationToken cancellationToken)
		{
			lock (_gate)
			{
				Requests.Add(request);
			}

			if (OnExecute is not null)
			{
				return await OnExecute();
			}

			return new FlowExecutionResult
			{
				ExecutionId = request.ExecutionId,
				Status = FlowExecutionStatus.Succeeded,
				MatchedFlows = 1
			};
		}
	}
}
