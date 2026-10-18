using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;

namespace MacroDeck.Sdk.Actions;

/// <summary>
/// Wraps any <see cref="IUiSession" /> as an <see cref="IActionConfigurationSession" />: every session
/// member is forwarded to the inner session, and <see cref="Report(string, JsonElement)" /> raises
/// <see cref="ParameterValuesReported" />.
/// </summary>
/// <remarks>
/// Report from the handler that runs inside <see cref="Dispatch(UiEvent, IUiEventOrigin)" /> so a save the
/// user starts right after the edit includes the value. Disposing this session disposes the inner one.
/// </remarks>
public sealed class ActionConfigurationSession : IActionConfigurationSession
{
	private readonly IUiSession _inner;

	public ActionConfigurationSession(IUiSession inner)
	{
		ArgumentNullException.ThrowIfNull(inner);
		_inner = inner;
	}

	public event EventHandler? Changed
	{
		add => _inner.Changed += value;
		remove => _inner.Changed -= value;
	}

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted
	{
		add => _inner.Faulted += value;
		remove => _inner.Faulted -= value;
	}

	public event EventHandler<ActionParameterValuesReportedEventArgs>? ParameterValuesReported;

	public UiTree BuildTree()
	{
		return _inner.BuildTree();
	}

	public IReadOnlyList<UiPatch> DrainPatches()
	{
		return _inner.DrainPatches();
	}

	public void Dispatch(UiEvent uiEvent)
	{
		_inner.Dispatch(uiEvent);
	}

	public void Dispatch(UiEvent uiEvent, IUiEventOrigin origin)
	{
		_inner.Dispatch(uiEvent, origin);
	}

	public ValueTask DisposeAsync()
	{
		return _inner.DisposeAsync();
	}

	/// <summary>Reports the value of one declared parameter, as if the user had edited it.</summary>
	/// <param name="name">The declared parameter name.</param>
	/// <param name="value">The value in the JSON shape Macro Deck stores for the parameter type.</param>
	public void Report(string name, JsonElement value)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);
		Report(new Dictionary<string, JsonElement>(StringComparer.Ordinal) { [name] = value.Clone() });
	}

	/// <summary>Reports the values of several declared parameters at once, as if the user had edited them.</summary>
	/// <param name="values">The values keyed by declared parameter name. An empty map raises nothing.</param>
	public void Report(IReadOnlyDictionary<string, JsonElement> values)
	{
		ArgumentNullException.ThrowIfNull(values);
		if (values.Count == 0)
		{
			return;
		}

		var copy = new Dictionary<string, JsonElement>(values.Count, StringComparer.Ordinal);
		foreach (var (name, value) in values)
		{
			copy[name] = value.Clone();
		}

		ParameterValuesReported?.Invoke(this, new ActionParameterValuesReportedEventArgs(copy));
	}
}
