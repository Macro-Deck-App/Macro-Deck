using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Weather;
using Serilog;
using MacroDeck.Localization;

namespace MacroDeck.Plugin.Testing.Tests.MisbehavingPlugin;

/// <summary>
/// The one integration this fixture declares. Every bad behaviour standing rule 3 (in the testing
/// package's own acceptance scenarios) and the conformance suite's counterexamples need is gated behind a
/// <see cref="MisbehaviorFlags" /> token, so the same built executable is the well-behaved-by-default
/// subject unless a test asks otherwise.
/// </summary>
internal sealed class MisbehavingIntegration : IPluginIntegration, IWeatherProvider, IVariableProvider
{
	private const string LiarVariableId = "liar";

	private readonly IReadOnlySet<string> _behaviors;
	private int _initializeCount;

	public MisbehavingIntegration(IReadOnlySet<string> behaviors, ILogger logger)
	{
		_behaviors = behaviors;
		Actions =
		[
			new StallAction(behaviors), new LogFloodAction(behaviors, logger.ForContext<MisbehavingIntegration>()),
			new StateProviderAction(behaviors), new IconProviderAction(behaviors)
		];
	}

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public Task InitializeAsync(IIntegrationContext context)
	{
		var count = Interlocked.Increment(ref _initializeCount);

		if (_behaviors.Contains(MisbehaviorFlags.NonIdempotentInit) && count > 1)
		{
			throw new InvalidOperationException(
				"This integration's InitializeAsync only tolerates being called once (non-idempotent-init).");
		}

		return Task.CompletedTask;
	}

	public async Task ShutdownAsync()
	{
		if (_behaviors.Contains(MisbehaviorFlags.HangInStop))
		{
			await Task.Delay(Timeout.Infinite);
		}
	}

	// ----- IWeatherProvider: exercises duplicate and invalid instance ids. -----

	public string ProviderName => "Misbehaving";

	public IReadOnlyList<WeatherStationInstance> GetInstances()
	{
		if (_behaviors.Contains(MisbehaviorFlags.DuplicateInstanceIds))
		{
			return
			[
				new WeatherStationInstance("primary", "Duplicate A"),
				new WeatherStationInstance("primary", "Duplicate B")
			];
		}

		if (_behaviors.Contains(MisbehaviorFlags.InvalidInstanceId))
		{
			// "::" is QualifiedId.Separator - a resource local id must never contain it.
			return [new WeatherStationInstance("a::b", "Invalid")];
		}

		return [new WeatherStationInstance("primary", "Single Station")];
	}

	public IWeatherStation? GetStation(string instanceId)
		=> instanceId is "primary" or "a::b" ? new Station() : null;

	private sealed class Station : IWeatherStation
	{
		public Task<WeatherSnapshot> GetSnapshotAsync(CancellationToken ct) =>
			Task.FromResult(WeatherSnapshot.Unavailable());
	}

	// ----- IVariableProvider: exercises duplicate ids and a lying write capability. The well-behaved
	// default deliberately declares its variable under the same id ("stall") as the action below - a legal
	// collision across two different capability kinds that a correct duplicate-id check must never flag.
	// SupportsCatalog stays at its default: this fixture has no catalog half. -----

	public IReadOnlyList<VariableDefinition> Variables
	{
		get
		{
			if (_behaviors.Contains(MisbehaviorFlags.DuplicateVariableDefinitionIds))
			{
				return
				[
					VariableDefinition.Eager("misbehaving_shared_a", VariableType.Text) with { Id = "shared" },
					VariableDefinition.Eager("misbehaving_shared_b", VariableType.Text) with { Id = "shared" }
				];
			}

			if (_behaviors.Contains(MisbehaviorFlags.WritableVariableRefusesWrite))
			{
				return
				[
					VariableDefinition.Eager("misbehaving_marker", VariableType.Text) with { Id = "stall" },
					VariableDefinition.Eager("misbehaving_liar", VariableType.Text) with
					{
						Id = LiarVariableId, Write = new VariableWriteCapability()
					}
				];
			}

			return [VariableDefinition.Eager("misbehaving_marker", VariableType.Text) with { Id = "stall" }];
		}
	}

	/// <summary>
	/// Overridden only for <see cref="MisbehaviorFlags.DuplicateVariableDefinitionIds" />: the wire-level
	/// declared capability local id (<c>VariableDescriptorMapper.LocalIdOf</c>) is a definition's own
	/// <see cref="VariableDefinition.ResolvedId" />, so declaring <see cref="Variables" /> as-is there would
	/// collide two capabilities at the same (kind, localId) - a violation
	/// <c>PluginHostBuilder.Build()</c> itself rejects (MDC0401's domain), which would stop this fixture
	/// from ever starting rather than exercising the runtime-only duplicate-id violation MDC0403 exists
	/// for. Declaring these two without an explicit id falls back to one derived from each variable's own
	/// (distinct) name, so the catalogue builds; <see cref="Variables" /> - what <c>variables/describe</c>
	/// actually reports, and what MDC0403 reads - keeps the collision.
	/// </summary>
	public IReadOnlyList<VariableDefinition> DeclaredVariables
		=> _behaviors.Contains(MisbehaviorFlags.DuplicateVariableDefinitionIds)
			?
			[
				VariableDefinition.Eager("misbehaving_shared_a", VariableType.Text),
				VariableDefinition.Eager("misbehaving_shared_b", VariableType.Text)
			]
			: Variables;

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(VariableReading.Of("marker"));

	/// <summary>
	/// The violation itself: <see cref="Variables" /> told every client this variable may be written, and
	/// this refuses the write anyway - a control the user can move that silently does nothing.
	/// </summary>
	public ValueTask<VariableWriteResult> SetValueAsync(
		string localId,
		object? value,
		CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(VariableWriteResult.NotWritable());

	/// <summary>
	/// The action this fixture declares unconditionally. Its parameter list balloons past
	/// <c>ProtocolLimits.MaxMessageBytes</c> when <see cref="MisbehaviorFlags.OversizeDescribe" /> is set,
	/// and its executor never observes cancellation when <see cref="MisbehaviorFlags.IgnoreCancellation" />
	/// is set - awaiting forever on a token it never checks, exactly the case a real timeout has to
	/// survive.
	/// </summary>
	private sealed class StallAction(IReadOnlySet<string> behaviors) : IActionDefinition
	{
		public string Id => "stall";

		public LocalizedText Name => "Stall";

		public LocalizedText Description => behaviors.Contains(MisbehaviorFlags.OversizeDescribe)
			? new string('x', 300_000)
			: "Awaits until cancelled - or forever, when misbehaving.";

		public IReadOnlyList<ActionParameter> Parameters { get; } = [];

		public IActionExecutor CreateExecutor() => new Executor(behaviors);

		private sealed class Executor(IReadOnlySet<string> behaviors) : IActionExecutor
		{
			public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
			{
				if (behaviors.Contains(MisbehaviorFlags.IgnoreCancellation))
				{
					await Task.Delay(Timeout.Infinite, CancellationToken.None);
				}
				else
				{
					await Task.Delay(Timeout.Infinite, context.CancellationToken);
				}

				return ActionResult.Success();
			}
		}
	}

	/// <summary>
	/// Logs a burst of informational lines followed by one error, completing immediately itself - unlike
	/// <see cref="StallAction" />, this is what the conformance suite's bounded-queue checks invoke while
	/// draining is paused. Well-behaved by default: the error goes through <c>ILogger</c>, so it reaches
	/// <c>log.publish</c> like everything else. <see cref="MisbehaviorFlags.BypassLoggingPipeline" /> writes
	/// that last line straight to stderr instead, simulating an author who wired up their own sink instead
	/// of <c>UseMacroDeckLogging</c> - the host's log collector then never sees it.
	/// </summary>
	private sealed class LogFloodAction(IReadOnlySet<string> behaviors, ILogger logger)
		: IActionDefinition
	{
		// Comfortably past ProtocolLimits.MaxLogEventsPerBatch (64) so a paused drain has real backlog to bound.
		private const int FloodLineCount = 300;

		public string Id => "log-flood";

		public LocalizedText Name => "Log flood";

		public LocalizedText Description => "Logs a burst of informational lines, then one error.";

		public IReadOnlyList<ActionParameter> Parameters { get; } = [];

		public IActionExecutor CreateExecutor() => new Executor(behaviors, logger);

		private sealed class Executor(IReadOnlySet<string> behaviors, ILogger logger)
			: IActionExecutor
		{
			public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
			{
				for (var i = 0; i < FloodLineCount; i++)
				{
					logger.Information("Flood line {Index}.", i);
				}

				if (behaviors.Contains(MisbehaviorFlags.BypassLoggingPipeline))
				{
					Console.Error.WriteLine("misbehaving-plugin: log-flood error (bypass-logging-pipeline).");
				}
				else
				{
					logger.Error("Flood complete.");
				}

				return Task.FromResult(ActionResult.Success());
			}
		}
	}

	/// <summary>
	/// Well-behaved by default - a real, constructible violator only when
	/// <see cref="MisbehaviorFlags.DuplicateStateIds" /> or <see cref="MisbehaviorFlags.InvalidStateId" /> is
	/// set. What the conformance suite's <c>MDC0309</c> and <c>MDC0310</c> counterexample tests exercise, and
	/// - unflagged - what proves those two checks genuinely pass rather than merely skip, in the well-behaved
	/// executable run this fixture also serves.
	/// </summary>
	private sealed class StateProviderAction(IReadOnlySet<string> behaviors)
		: IActionDefinition, IStateProviderActionDefinition
	{
		public string Id => "report-state";

		public LocalizedText Name => "Report state";

		public LocalizedText Description => "Reports a fixed two-state snapshot, or a malformed one when misbehaving.";

		public IReadOnlyList<ActionParameter> Parameters { get; } = [];

		public IActionExecutor CreateExecutor() => new Executor();

		public Task<ActionStateSnapshot?> GetActionStateAsync(
			IReadOnlyDictionary<string, object?> parameters,
			CancellationToken cancellationToken)
		{
			if (behaviors.Contains(MisbehaviorFlags.DuplicateStateIds))
			{
				return Task.FromResult<ActionStateSnapshot?>(new ActionStateSnapshot([
						new ActionStateDefinition("state-a", "State A"),
						new ActionStateDefinition("state-a", "Also state A")
					],
					"state-a"));
			}

			if (behaviors.Contains(MisbehaviorFlags.InvalidStateId))
			{
				// Uppercase fails LocalIdKind.Declared's lowercase-kebab rule - the same shape
				// MisbehaviorFlags.InvalidInstanceId's "a::b" is illegal for, just via a different rule.
				return Task.FromResult<ActionStateSnapshot?>(new ActionStateSnapshot(
					[new ActionStateDefinition("Invalid-State", "Invalid state")],
					"Invalid-State"));
			}

			return Task.FromResult<ActionStateSnapshot?>(new ActionStateSnapshot(
				[new ActionStateDefinition("state-a", "State A"), new ActionStateDefinition("state-b", "State B")],
				"state-a"));
		}

		private sealed class Executor : IActionExecutor
		{
			public Task<ActionResult> ExecuteAsync(ActionExecutionContext context) => ActionResult.SucceededTask;
		}
	}

	/// <summary>
	/// Well-behaved by default - a real, constructible violator only when
	/// <see cref="MisbehaviorFlags.InconsistentIconSnapshot" /> or
	/// <see cref="MisbehaviorFlags.NonImageIconMediaType" /> is set. What the conformance suite's
	/// <c>MDC0312</c> and <c>MDC0313</c> counterexample tests exercise, and - unflagged - what proves those
	/// two checks genuinely pass rather than merely skip, in the well-behaved executable run this fixture
	/// also serves.
	/// </summary>
	private sealed class IconProviderAction(IReadOnlySet<string> behaviors)
		: IActionDefinition, IIconProviderActionDefinition
	{
		private const string IconVersion = "icon-v1";

		private static readonly byte[] _iconBytes = "misbehaving-plugin-icon"u8.ToArray();

		public string Id => "report-icon";

		public LocalizedText Name => "Report icon";

		public LocalizedText Description => "Reports a fixed icon, or a malformed snapshot when misbehaving.";

		public IReadOnlyList<ActionParameter> Parameters { get; } = [];

		public IActionExecutor CreateExecutor() => new Executor();

		public Task<ActionIconSnapshot?> GetActionIconAsync(
			IReadOnlyDictionary<string, object?> parameters,
			CancellationToken cancellationToken)
		{
			if (behaviors.Contains(MisbehaviorFlags.InconsistentIconSnapshot))
			{
				// NoIcon set alongside a Reference and a non-empty Version - MDC0312 requires NoIcon to
				// imply neither.
				return Task.FromResult<ActionIconSnapshot?>(new ActionIconSnapshot
				{
					NoIcon = true,
					Version = IconVersion,
					Reference = ActionIconReference.IconPack("should-be-absent")
				});
			}

			return Task.FromResult<ActionIconSnapshot?>(new ActionIconSnapshot
				{ Version = IconVersion, MediaType = "image/png" });
		}

		public Task<ActionIconContent?> GetActionIconContentAsync(
			IReadOnlyDictionary<string, object?> parameters,
			string version,
			CancellationToken cancellationToken)
		{
			var mediaType = behaviors.Contains(MisbehaviorFlags.NonImageIconMediaType) ? "text/html" : "image/png";
			return Task.FromResult<ActionIconContent?>(new ActionIconContent(_iconBytes, mediaType));
		}

		private sealed class Executor : IActionExecutor
		{
			public Task<ActionResult> ExecuteAsync(ActionExecutionContext context) => ActionResult.SucceededTask;
		}
	}
}
