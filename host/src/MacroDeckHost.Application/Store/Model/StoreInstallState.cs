using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Store.Model;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StoreInstallState
{
	NotInstalled,
	Installed,
	UpdateAvailable,
	Unsupported
}
