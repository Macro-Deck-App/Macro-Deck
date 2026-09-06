using MacroDeck.Sdk.Identity;

namespace MacroDeck.Sdk.Variables;

/// <summary>
/// Derives a variable's stable definition id from its canonical name, for the providers that do not
/// declare one explicitly.
///
/// <para>
/// The two syntaxes are deliberately different and must not be confused: a variable *name* is what the
/// user types into a template (<c>system_volume_percent</c>, underscore-separated), while a definition
/// id is a capability id like any other (<c>system-volume-percent</c>, hyphen-separated). Canonical
/// names are already restricted to <c>^[a-z][a-z0-9_]*$</c>, so swapping the separator always yields a
/// valid declared local id.
/// </para>
/// </summary>
public static class VariableDefinitionId
{
	/// <summary>
	/// The definition id for a canonical variable name, or null when the name cannot produce a valid
	/// one - a template name, or a name that was never canonicalized.
	/// </summary>
	public static string? FromName(string? canonicalName)
	{
		if (string.IsNullOrEmpty(canonicalName) || VariableNameTemplate.IsTemplate(canonicalName))
		{
			return null;
		}

		var candidate = canonicalName.Replace('_', '-');
		return MacroDeckId.IsValidLocalId(candidate, LocalIdKind.Declared) ? candidate : null;
	}
}
