using Serilog.Core;
using Serilog.Events;

namespace SuchByte.MacroDeck.Logging;

/// <summary>
/// Adds a <c>Source</c> property to every log event that holds the name of the originating plugin,
/// or <see cref="MacroDeckSource"/> if the event originates from Macro Deck itself.
/// Plugin log events carry their plugin name in the <see cref="PluginPropertyName"/> property
/// (set by <see cref="MacroDeckLogger"/>); everything else is attributed to Macro Deck.
/// </summary>
public class PluginSourceEnricher : ILogEventEnricher
{
    public const string SourcePropertyName = "Source";

    private const string MacroDeckSource = "MacroDeck";
    private const string PluginPropertyName = "Plugin";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var source = MacroDeckSource;
        if (logEvent.Properties.TryGetValue(PluginPropertyName, out var pluginValue)
            && pluginValue is ScalarValue { Value: string pluginName }
            && !string.IsNullOrWhiteSpace(pluginName))
        {
            source = pluginName;
        }

        logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(SourcePropertyName, source));
    }
}
