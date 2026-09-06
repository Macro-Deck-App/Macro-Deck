namespace MacroDeck.Sdk.Identity;

/// <summary>
/// Which rule a local id has to satisfy. An id an author types into their source can be held to a
/// canonical syntax; an id that is a config-entry GUID or is assembled from runtime state cannot.
/// </summary>
public enum LocalIdKind
{
	/// <summary>
	/// Declared in source by an integration or plugin author and immutable after release: action ids,
	/// event definition ids, variable definition ids. Held to the canonical
	/// <c>lowercase-kebab-case</c> syntax.
	/// </summary>
	Declared,

	/// <summary>
	/// Derived from user configuration or runtime state: music player and weather instance ids (which
	/// are config-entry GUIDs), virtual profile, folder and widget ids, integration issue ids (which an
	/// integration may suffix with an account id). Only has to be a usable id - non-empty, bounded, and
	/// free of the separator and of whitespace.
	/// </summary>
	Resource
}
