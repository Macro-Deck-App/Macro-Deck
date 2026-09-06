using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Store.Model;

// Only a plugin can ever reach PublisherVerified, and only once its own embedded signature has been
// verified at install time. Registry authentication alone never attests a publisher.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StoreTrustPresentation
{
	RegistryAuthenticated,
	PublisherVerified
}
