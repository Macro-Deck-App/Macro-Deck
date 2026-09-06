using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Store.Operations;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StoreOperationKind
{
	Install,
	Update
}
