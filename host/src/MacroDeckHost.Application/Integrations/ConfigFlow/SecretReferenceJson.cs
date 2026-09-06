using System.Text.Json;

namespace MacroDeckHost.Application.Integrations.ConfigFlow;

public static class SecretReferenceJson
{
	public const string PropertyName = "$secret";

	public static bool TryGet(JsonElement element, out Guid secretId)
	{
		secretId = Guid.Empty;
		return element.ValueKind == JsonValueKind.Object &&
			element.TryGetProperty(PropertyName, out var reference) &&
			reference.ValueKind == JsonValueKind.String &&
			Guid.TryParse(reference.GetString(), out secretId);
	}
}
