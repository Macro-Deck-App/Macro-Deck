using System.Text.Json;

namespace MacroDeck.Sdk.Actions;

/// <summary>Parameter values an <see cref="IActionConfigurationSession" /> reports, keyed by parameter name.</summary>
public sealed class ActionParameterValuesReportedEventArgs : EventArgs
{
	public ActionParameterValuesReportedEventArgs(IReadOnlyDictionary<string, JsonElement> values)
	{
		ArgumentNullException.ThrowIfNull(values);
		Values = values;
	}

	/// <summary>The reported values in their stored JSON shape, keyed by declared parameter name.</summary>
	public IReadOnlyDictionary<string, JsonElement> Values { get; }
}
