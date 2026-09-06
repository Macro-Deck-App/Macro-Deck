using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Store.Model;

// Automation, folder and widget templates have no member: the host cannot install them, so they are
// dropped while reading the registry rather than filtered at each surface.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StoreExtensionKind
{
	Plugin,
	IconPack,
	ProfileTemplate
}
