using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.AutoHotkey.Providers;
using MacroDeckHost.Integrations.AutoHotkey.Variables;

namespace MacroDeckHost.Integrations.AutoHotkey;

public sealed partial class AutoHotkeyIntegration : IVariableProvider
{
	private static readonly int _maxScriptVariables =
		VariableLimits.MaxEagerVariablesPerProvider - AutoHotkeyVariables.Status.Count;

	public IReadOnlyList<VariableDefinition> Variables
		=>
		[
			.. AutoHotkeyVariables.Status,
			.. (_store?.Scripts ?? []).Take(_maxScriptVariables).Select(AutoHotkeyVariables.ForScript)
		];

	public IReadOnlyList<VariableDefinition> DeclaredVariables
		=> _store is { Scripts.Count: > 0 } ? Variables : [.. AutoHotkeyVariables.Status, AutoHotkeyVariables.Template];

	// A definition id is the canonical name with '_' swapped for '-', and a canonical variable name can never
	// contain '-', so swapping the separator back recovers the name exactly.
	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		var name = localId.Replace('-', '_');
		var manager = _manager;
		var reading = name switch
		{
			AutoHotkeyVariables.Installed => VariableReading.Of(_installation.IsValid),
			AutoHotkeyVariables.Version => VariableReading.Of(_installation.IsValid ? _installation.Version : null),
			AutoHotkeyVariables.RunningScripts => VariableReading.Of(manager?.RunningCount ?? 0),
			AutoHotkeyVariables.LastScript => VariableReading.Of(manager?.LastScript),
			AutoHotkeyVariables.LastExitCode => VariableReading.Of(manager?.LastExitCode),
			AutoHotkeyVariables.LastResult => VariableReading.Of(manager?.LastResult),
			AutoHotkeyVariables.LastError => VariableReading.Of(manager?.LastError),
			_ => ReadScriptRunning(name, manager)
		};

		return ValueTask.FromResult(reading);
	}

	private VariableReading ReadScriptRunning(string name, AutoHotkeyProcessManager? manager)
	{
		if (AutoHotkeyVariables.SlugOf(name) is not { } slug
			|| _store?.Scripts.FirstOrDefault(script => script.Slug == slug) is not { } script)
		{
			return VariableReading.Unavailable;
		}

		return VariableReading.Of(manager?.IsScriptRunning(script.Id) ?? false);
	}

	private async Task RemoveStaleScriptVariablesAsync(IVariableApi variables)
	{
		if (_store is not { IsReadOnly: false } store)
		{
			return;
		}

		var live = store.Scripts.Select(script => AutoHotkeyVariables.ScriptRunningName(script.Slug))
			.ToHashSet(StringComparer.Ordinal);
		foreach (var variable in await variables.GetAllAsync().ConfigureAwait(false))
		{
			var name = variable.DefinitionId?.Replace('-', '_') ?? variable.Name;
			if (AutoHotkeyVariables.SlugOf(name) is not null && !live.Contains(name))
			{
				await variables.DeleteAsync(variable.Id).ConfigureAwait(false);
			}
		}
	}

	private async Task RefreshVariablesAsync(Guid? scriptId = null)
	{
		if (_context is not { } context)
		{
			return;
		}

		var ids = new List<string>(AutoHotkeyVariables.StatusIds);
		if (scriptId is { } id
			&& _store?.Find(id) is { } script
			&& AutoHotkeyVariables.ForScript(script).ResolvedId is { } scriptVariable)
		{
			ids.Add(scriptVariable);
		}

		try
		{
			await context.Variables.RequestDefinitionRefreshAsync(ids).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Debug(ex, "AutoHotkey variables could not be refreshed");
		}
	}
}
