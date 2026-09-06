using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Json.Schema;

namespace MacroDeckHost.Application.Widgets;

public interface IWidgetDataSchemaProvider
{
	bool TryGet(string type, [NotNullWhen(true)] out JsonSchema? schema);

	IReadOnlyDictionary<string, JsonElement> All();
}
