namespace MacroDeck.Plugin.Analyzers;

/// <summary>
/// Every diagnostic id this package ships, as stable, independently gateable strings - see
/// <see cref="DiagnosticDescriptors" /> for what each one means. Ids never change meaning once shipped;
/// a rule that no longer applies is deprecated, not repurposed.
/// </summary>
internal static class DiagnosticIds
{
	public const string InvalidManifestIdentity = "MDP1001";

	public const string InvalidDeclaredLocalId = "MDP1002";

	public const string UnsupportedManifestIconExtension = "MDP1003";

	public const string RestatedManifestIdentityMember = "MDP1004";

	public const string DuplicateCapabilityId = "MDP2001";

	public const string UnknownCapabilityKind = "MDP2002";

	public const string UnroutedCapabilityHandler = "MDP2003";

	public const string RawIntegrationRegistration = "MDP2004";

	public const string ReservedRoutePath = "MDP2005";

	public const string InertIntegrationAttribute = "MDP2006";

	public const string MissingCancellationForwarding = "MDP3001";

	public const string BlockingCall = "MDP3002";

	public const string AsyncVoidMember = "MDP3003";

	public const string SingletonCapabilityContext = "MDP4001";

	public const string ListenerUrlOverride = "MDP4002";

	public const string ObsoleteSdkMember = "MDP5001";

	public const string DeprecatedSdkApi = "MDP5002";

	public const string IncompleteDeprecationMetadata = "MDP5003";

	public const string RemovedSdkApi = "MDP5004";

	// The localization compiler gets its own family rather than a sixth MDP category: it is a separate
	// toolchain with its own resource inputs, and issue #326 specifies these ids verbatim as the
	// plugin-facing contract.
	public const string MissingDefaultLocalizationResource = "MDLOC001";

	public const string LocalizationPlaceholderMismatch = "MDLOC002";

	public const string DuplicateLocalizationKey = "MDLOC003";

	public const string UnknownLocalizationParameterType = "MDLOC004";

	public const string InvalidLocalizationCulture = "MDLOC005";

	public const string RemovedMacroDeckLocalizationKey = "MDLOC006";

	/// <summary>A plural family is not usable: a bad form name, or no <c>Other</c> form to fall back on.</summary>
	public const string InvalidLocalizationPluralFamily = "MDLOC007";

	/// <summary>A localization key is both a member and the group other keys nest under.</summary>
	public const string LocalizationKeyIsAlsoAGroup = "MDLOC008";
}
