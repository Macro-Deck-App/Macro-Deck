using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Store.Model;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StoreCatalogSection
{
	// All is the default ordering: by name while browsing, by search relevance once a term is supplied.
	// Name asks for name order whatever the search does, which is what a sort control has to mean.
	All,
	Newest,
	RecentlyUpdated,
	Name,
	Featured
}
