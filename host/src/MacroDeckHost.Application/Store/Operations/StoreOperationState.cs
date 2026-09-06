using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Store.Operations;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StoreOperationState
{
	Queued,
	Downloading,
	Validating,
	Installing,
	Completed,
	Failed,
	Cancelled
}
