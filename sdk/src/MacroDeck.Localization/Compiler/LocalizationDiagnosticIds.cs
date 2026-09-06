namespace MacroDeck.Localization.Compiler;

/// <summary>
/// The build diagnostics the localization compiler reports. Shared with the Roslyn analyzer package by
/// source link so one list serves the generator, the runtime validator and the documentation.
/// </summary>
/// <remarks>Diagnostic ids are a public tooling contract: once shipped, an id keeps its meaning.</remarks>
internal static class LocalizationDiagnosticIds
{
	/// <summary>A key exists in a translation but not in the default-language resource.</summary>
	public const string MissingDefaultResource = "MDLOC001";

	/// <summary>A translation's placeholders differ from the default language's.</summary>
	public const string PlaceholderMismatch = "MDLOC002";

	/// <summary>The same key is declared twice within one culture of one scope.</summary>
	public const string DuplicateKey = "MDLOC003";

	/// <summary>A declared parameter type is not one the compiler can emit.</summary>
	public const string UnknownParameterType = "MDLOC004";

	/// <summary>A resource file's culture suffix is not a well-formed culture name.</summary>
	public const string InvalidCultureName = "MDLOC005";

	/// <summary>A referenced Macro Deck catalog key has been removed.</summary>
	public const string RemovedMacroDeckKey = "MDLOC006";

	/// <summary>A plural family is not usable: a bad form name, or no <c>Other</c> form to fall back on.</summary>
	public const string InvalidPluralFamily = "MDLOC007";

	/// <summary>A key is both a member and the group other keys nest under.</summary>
	public const string KeyIsAlsoAGroup = "MDLOC008";
}
