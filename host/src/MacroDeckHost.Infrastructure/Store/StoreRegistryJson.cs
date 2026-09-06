using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeckHost.Infrastructure.Store;

internal static class StoreRegistryJson
{
	public static readonly JsonSerializerOptions Options = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};
}
