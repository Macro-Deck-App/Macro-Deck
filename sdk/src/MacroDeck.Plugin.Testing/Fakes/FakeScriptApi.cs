using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Scripts;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// In-memory <see cref="IScriptApi" />. Seed a script with <see cref="Seed" /> before the plugin under
/// test can see or run it; an id nobody seeded is a documented <see cref="ActionResultStatus.Failed" />
/// from <see cref="RunAsync" />, never a throw and never a silent success.
/// </summary>
public sealed class FakeScriptApi : IScriptApi
{
	private readonly Lock _gate = new();
	private readonly Dictionary<string, Script> _scripts = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Func<ActionResult>> _behaviors = new(StringComparer.Ordinal);
	private readonly List<FakeScriptRun> _runs = [];

	/// <summary>
	/// Every script id passed to <see cref="RunAsync" /> so far, in call order - including unknown ones.
	/// </summary>
	public IReadOnlyList<string> Ran
	{
		get
		{
			lock (_gate)
			{
				return [.. _runs.Select(run => run.ScriptId)];
			}
		}
	}

	/// <summary>
	/// Every run so far with the input values it was given, in call order - so a test can assert what the
	/// plugin passed down, not just which script it picked.
	/// </summary>
	public IReadOnlyList<FakeScriptRun> Runs
	{
		get
		{
			lock (_gate)
			{
				return [.. _runs];
			}
		}
	}

	/// <summary>
	/// Adds (or replaces) a script <see cref="GetScripts" /> will list and <see cref="RunAsync" /> will
	/// accept. <paramref name="behavior" /> decides what a run reports; omit it for a run that always
	/// succeeds.
	/// </summary>
	public void Seed(Script script, Func<ActionResult>? behavior = null)
	{
		ArgumentNullException.ThrowIfNull(script);

		lock (_gate)
		{
			_scripts[script.Id] = script;
			_behaviors[script.Id] = behavior ?? (() => ActionResult.Success());
		}
	}

	/// <inheritdoc />
	public IReadOnlyList<Script> GetScripts()
	{
		lock (_gate)
		{
			return [.. _scripts.Values];
		}
	}

	/// <summary>
	/// Runs <paramref name="scriptId" />'s seeded behaviour, or reports
	/// <see cref="ActionResultStatus.Failed" /> when nothing was seeded under that id - matching the
	/// interface's own contract that an unknown id is a reported failure, not a silent no-op.
	/// </summary>
	public Task<ActionResult> RunAsync(string scriptId,
		IReadOnlyDictionary<string, object?>? inputs = null,
		string? originClientId = null,
		string? ownerWidgetId = null,
		CancellationToken cancellationToken = default)
	{
		Func<ActionResult>? behavior;

		lock (_gate)
		{
			_runs.Add(new FakeScriptRun(scriptId,
				inputs is null
					? new Dictionary<string, object?>(StringComparer.Ordinal)
					: new Dictionary<string, object?>(inputs, StringComparer.Ordinal),
				ownerWidgetId,
				originClientId));
			_behaviors.TryGetValue(scriptId, out behavior);
		}

		var result = behavior?.Invoke() ??
			ActionResult.Failed(ActionErrorCodes.NotFound, $"No script with id '{scriptId}' has been seeded.");

		return Task.FromResult(result);
	}
}

/// <summary>One recorded <see cref="FakeScriptApi.RunAsync" /> call.</summary>
public sealed record FakeScriptRun(
	string ScriptId,
	IReadOnlyDictionary<string, object?> Inputs,
	string? OwnerWidgetId,
	string? OriginClientId);
