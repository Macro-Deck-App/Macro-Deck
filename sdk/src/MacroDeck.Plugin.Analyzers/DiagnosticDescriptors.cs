using Microsoft.CodeAnalysis;

namespace MacroDeck.Plugin.Analyzers;

/// <summary>
/// Every <see cref="DiagnosticDescriptor" /> this package's analyzers report. Centralized so
/// <c>AnalyzerReleases.Shipped.md</c> and the "every descriptor is actionable" test have one place to
/// check against, and so two analyzers can never accidentally give the same id two different messages.
/// </summary>
internal static class DiagnosticDescriptors
{
	// The docs site publishes this page; the anchors are the contract that page has to honour, fixed
	// now so a rule's help link never has to change later. The trailing slash is required: Create()
	// appends "#{id}" directly, and without it the anchor would run into the last path segment.
	private const string HelpBaseUrl =
		"https://docs.macro-deck.app/reference/analyzers/";

	public static readonly DiagnosticDescriptor InvalidManifestIdentity = Create(DiagnosticIds.InvalidManifestIdentity,
		title: "manifest.json declares an invalid or missing identity",
		messageFormat: "{0}",
		category: DiagnosticCategories.Identity,
		severity: DiagnosticSeverity.Error,
		description: "A plugin's id, name and version come from manifest.json, not a builder call. id " +
		"must be a reverse-domain package id: lowercase, hyphen-separated segments joined by dots, " +
		"e.g. 'com.example.my-plugin'. name and version must both be present and non-empty. " +
		"PluginHostBuilder.Build() rejects all three the same way, at runtime; this rule catches " +
		"them at compile time instead.");

	public static readonly DiagnosticDescriptor InvalidDeclaredLocalId = Create(DiagnosticIds.InvalidDeclaredLocalId,
		title: "Declared local id is not valid",
		messageFormat: "{0} id '{1}' is not usable. {2} Use lowercase, hyphen-separated segments starting " +
		"with a letter, such as 'set-volume'.",
		category: DiagnosticCategories.Identity,
		severity: DiagnosticSeverity.Error,
		description: "A declared local id - an action's Id, a DeclaredCapability.LocalId - is held to the " +
		"canonical lowercase-kebab syntax, bounded length, and must never contain '::', which the " +
		"host reserves to join it to its owner.");

	public static readonly DiagnosticDescriptor UnsupportedManifestIconExtension = Create(
		DiagnosticIds.UnsupportedManifestIconExtension,
		title: "manifest.json declares an icon with an unsupported extension",
		messageFormat: "{0}",
		category: DiagnosticCategories.Identity,
		severity: DiagnosticSeverity.Error,
		description: "A plugin's icon comes from the file manifest.json's \"icon\" points at. Only " +
		"'.svg', '.png', '.jpg', '.jpeg' and '.webp' have a media type this project knows how to " +
		"serve; every other extension fails at build time instead of shipping an icon nothing can " +
		"render.");

	public static readonly DiagnosticDescriptor RestatedManifestIdentityMember = Create(
		DiagnosticIds.RestatedManifestIdentityMember,
		title: "IPluginIntegration type restates manifest-owned identity",
		messageFormat: "{0}",
		category: DiagnosticCategories.Identity,
		severity: DiagnosticSeverity.Error,
		description: "manifest.json is the one source for a plugin's id, name, version and icon since " +
		"#522. A type implementing IPluginIntegration that also declares a public Id/Name/Version " +
		"string or an IsInitialized bool, or that implements IIntegrationIconProvider, restates data " +
		"the host never reads from it and that can drift from the manifest silently. Delete the " +
		"member; the manifest already says it.");

	public static readonly DiagnosticDescriptor DuplicateCapabilityId = Create(DiagnosticIds.DuplicateCapabilityId,
		title: "Capability id is declared more than once",
		messageFormat: "{0} id '{1}' is also declared by '{2}'. {0} ids must be unique within a plugin - " +
		"rename one of them.",
		category: DiagnosticCategories.Registration,
		severity: DiagnosticSeverity.Error,
		description: "Two statically visible declarations of the same capability kind share a constant " +
		"id. The host qualifies a capability by (kind, local id), so a collision within one kind " +
		"makes one of the two unreachable.");

	public static readonly DiagnosticDescriptor UnknownCapabilityKind = Create(DiagnosticIds.UnknownCapabilityKind,
		title: "Capability handler declares an unknown kind",
		messageFormat: "'{0}' is not a known capability kind. ICapabilityHandler.Kind must return one of: {1}.",
		category: DiagnosticCategories.Registration,
		severity: DiagnosticSeverity.Error,
		description: "The host only recognises a fixed set of capability kinds. A handler that returns " +
		"anything else can never be matched to an operation the host sends.");

	public static readonly DiagnosticDescriptor UnroutedCapabilityHandler = Create(
		DiagnosticIds.UnroutedCapabilityHandler,
		title: "Capability handler is never registered as ICapabilityHandler",
		messageFormat: "'{0}' is registered with AddSingleton<{0}>() but never also as ICapabilityHandler, " +
		"so it never reaches the capability catalog. Call builder.RegisterCapabilityHandler<{0}>() " +
		"instead, or also register services.AddSingleton<ICapabilityHandler, {0}>().",
		category: DiagnosticCategories.Registration,
		severity: DiagnosticSeverity.Warning,
		description: "The capability catalog is built from every ICapabilityHandler service the " +
		"container knows about (IEnumerable<ICapabilityHandler>). Registering a handler only under " +
		"its own concrete type builds the object but never adds it to that collection. " +
		"AddMacroDeckCapabilityHandler<T>() itself is internal to MacroDeck.Plugin.Hosting - " +
		"PluginHostBuilder.RegisterCapabilityHandler<T>() is the public door onto it.");

	public static readonly DiagnosticDescriptor RawIntegrationRegistration = Create(
		DiagnosticIds.RawIntegrationRegistration,
		title: "Integration is registered without builder.RegisterIntegration",
		messageFormat: "'{0}' implements IPluginIntegration but is registered directly with AddSingleton. " +
		"Call builder.RegisterIntegration<{0}>() instead, so its actions and its other capability " +
		"handlers are wired up.",
		category: DiagnosticCategories.Registration,
		severity: DiagnosticSeverity.Warning,
		description: "PluginHostBuilder.RegisterIntegration<T>() does more than register T: it also adds " +
		"the actions capability handler and conditionally adds a handler for every other capability " +
		"interface T implements (variables, events, issues, and so on). A raw AddSingleton skips all " +
		"of that. Rehomed from IIntegration/AddMacroDeckIntegration onto IPluginIntegration/" +
		"RegisterIntegration when the out-of-process plugin contract split from the in-process one - " +
		"same rule, same id, matching a call through the door that now exists for it.");

	public static readonly DiagnosticDescriptor ReservedRoutePath = Create(DiagnosticIds.ReservedRoutePath,
		title: "Route path is reserved by the SDK",
		messageFormat: "'{0}' falls under the SDK's reserved '/_macrodeck' prefix, so the SDK's own " +
		"middleware answers it before this route ever runs. Choose a different path.",
		category: DiagnosticCategories.Registration,
		severity: DiagnosticSeverity.Error,
		description: "Every path under '/_macrodeck' is reserved for the SDK's own health, readiness, " +
		"info and diagnostics endpoints, checked at build by PluginHostBuilder.Build() and rejected " +
		"if a mapped route collides with it.");

	public static readonly DiagnosticDescriptor InertIntegrationAttribute = Create(
		DiagnosticIds.InertIntegrationAttribute,
		title: "[MacroDeckIntegration] has no effect on an out-of-process plugin",
		messageFormat: "'{0}' implements IPluginIntegration but carries [MacroDeckIntegration]. Both " +
		"Platforms and EnabledByDefault are inert here: platform gating comes from manifest.json's " +
		"entrypoints, and enabled-by-default is derived from whether the plugin declares a " +
		"config-flow capability. Remove the attribute.",
		category: DiagnosticCategories.Registration,
		severity: DiagnosticSeverity.Warning,
		description: "[MacroDeckIntegration] carries only Platforms and EnabledByDefault - no identity - " +
		"and an out-of-process plugin is not discovered through it: the host launches the plugin " +
		"process named in manifest.json's entrypoints for the current platform, and derives " +
		"enabled-by-default from the plugin's declared capabilities. The attribute does nothing there, " +
		"which is why this is a Warning rather than the Error severity this package reserves for " +
		"'this cannot work' - the attribute is inert, not harmful.");

	public static readonly DiagnosticDescriptor MissingCancellationForwarding = Create(
		DiagnosticIds.MissingCancellationForwarding,
		title: "Cancellation token is not forwarded",
		messageFormat: "This call does not forward the cancellation token available here ({0}). Pass it " +
		"through so cancelling the invocation actually stops the work instead of continuing it in " +
		"the background.",
		category: DiagnosticCategories.Async,
		severity: DiagnosticSeverity.Warning,
		description: "Inside ICapabilityHandler.InvokeAsync and IActionExecutor.ExecuteAsync, a call that " +
		"passes CancellationToken.None, default, or omits an optional token parameter discards the " +
		"cancellation the caller asked for, even though a real token is reachable right there.");

	public static readonly DiagnosticDescriptor BlockingCall = Create(DiagnosticIds.BlockingCall,
		title: "Blocking call inside a capability member",
		messageFormat: "'{0}' blocks the calling thread inside a member of a type implementing {1}. The " +
		"dispatcher only has 32 concurrent slots, so a block here starves other invocations - await " +
		"the call instead.",
		category: DiagnosticCategories.Async,
		severity: DiagnosticSeverity.Warning,
		description: "Task.Result, Task.Wait(), GetAwaiter().GetResult() and Thread.Sleep all block the " +
		"thread that is running them. Every ICapabilityHandler, IActionExecutor and IConfigFlow " +
		"member runs on the shared invocation dispatcher, which a blocked thread cannot serve.");

	public static readonly DiagnosticDescriptor AsyncVoidMember = Create(DiagnosticIds.AsyncVoidMember,
		title: "async void member on an SDK contract type",
		messageFormat: "'{0}' is declared async void on a type implementing {1}. An exception thrown here " +
		"crashes the process instead of failing the one invocation - return Task instead.",
		category: DiagnosticCategories.Async,
		severity: DiagnosticSeverity.Warning,
		description: "An async void method cannot be awaited, so nothing observes or handles an exception " +
		"it throws; it escapes to the synchronization context and typically terminates the process. " +
		"async Task is always safe to use in its place, including for a method that returns nothing.");

	public static readonly DiagnosticDescriptor SingletonCapabilityContext = Create(
		DiagnosticIds.SingletonCapabilityContext,
		title: "Singleton constructor takes ICapabilityInvocationContext",
		messageFormat: "'{0}' is registered as a singleton, but its constructor takes " +
		"ICapabilityInvocationContext, which only exists inside one invocation's scope. Resolving " +
		"'{0}' fails every time, because the container validates scopes. Resolve " +
		"ICapabilityInvocationContext from inside InvokeAsync instead of taking it as a constructor " +
		"parameter.",
		category: DiagnosticCategories.Lifecycle,
		severity: DiagnosticSeverity.Error,
		description: "PluginHostBuilder enables ValidateScopes, so a singleton that depends on a scoped " +
		"service throws InvalidOperationException the first time the container tries to construct " +
		"it - a startup crash, not a bug that only shows up under load.");

	public static readonly DiagnosticDescriptor ListenerUrlOverride = Create(DiagnosticIds.ListenerUrlOverride,
		title: "Plugin overrides its own listener URL",
		messageFormat: "{0} overrides the plugin's own listener URL. The supervisor already assigned one " +
		"and probes it before the plugin finishes starting, so this makes the health check silently " +
		"and permanently fail. Remove it and let the host choose.",
		category: DiagnosticCategories.Lifecycle,
		severity: DiagnosticSeverity.Warning,
		description: "ASPNETCORE_URLS is set by the supervisor to a port it already bound and knows is " +
		"free, before the plugin process starts. UseUrls, a 'urls' configuration write, or " +
		"ASPNETCORE_URLS in launchSettings.json all make the plugin listen somewhere the supervisor " +
		"is not probing, with no error surfaced for it.");

	public static readonly DiagnosticDescriptor ObsoleteSdkMember = Create(DiagnosticIds.ObsoleteSdkMember,
		title: "Obsolete Macro Deck SDK member",
		messageFormat: "'{0}' is obsolete; see the compiler's own obsolete warning on this line for why. " +
		"MDP5001 is a separate, gateable id for exactly this kind of deprecation, so it can be " +
		"escalated on its own without escalating every obsolete warning in your own code.",
		category: DiagnosticCategories.Compatibility,
		severity: DiagnosticSeverity.Warning,
		description: "Reports the same usages the compiler's CS0618/CS0619 already report, but only for " +
		"[Obsolete] members declared in MacroDeck.Sdk, MacroDeck.Plugin.Hosting or " +
		"MacroDeck.Plugin.Protocol, under a dedicated id a plugin author can gate independently of " +
		"obsolete-API warnings from anywhere else.");

	public static readonly DiagnosticDescriptor DeprecatedSdkApi = Create(DiagnosticIds.DeprecatedSdkApi,
		title: "Deprecated Macro Deck SDK API",
		messageFormat: "'{0}' is deprecated since Macro Deck {1} and is planned for removal in {2} - {3}",
		category: DiagnosticCategories.Compatibility,
		severity: DiagnosticSeverity.Warning,
		description: "Reports use of an SDK API carrying [MacroDeckDeprecated], which adds the removal " +
		"version, the replacement and the migration guidance that [Obsolete] alone has nowhere to put. " +
		"MDP5001 stands down wherever this rule applies, so a deprecated API is reported once, with the " +
		"more actionable message.");

	public static readonly DiagnosticDescriptor IncompleteDeprecationMetadata =
		Create(DiagnosticIds.IncompleteDeprecationMetadata,
			title: "Incomplete or inconsistent Macro Deck deprecation metadata",
			messageFormat: "The deprecation of '{0}' is not usable: {1}",
			category: DiagnosticCategories.Compatibility,
			severity: DiagnosticSeverity.Warning,
			description: "A [MacroDeckDeprecated] declaration has to carry a companion [Obsolete], a " +
			"removal version later than the version it was deprecated in, and non-empty guidance. A " +
			"deprecation missing any of those reaches plugin authors as a warning they cannot act on, " +
			"and reaches the host as a finding it cannot describe.");

	public static readonly DiagnosticDescriptor RemovedSdkApi = Create(DiagnosticIds.RemovedSdkApi,
		title: "Macro Deck SDK API is past its declared removal version",
		messageFormat: "'{0}' declared removal in Macro Deck {1}, which this SDK ({2}) has already " +
		"reached, so it should no longer exist - {3}",
		category: DiagnosticCategories.Compatibility,
		severity: DiagnosticSeverity.Error,
		description: "An API still present after the release it promised to be removed in means the " +
		"deprecation lifecycle was not followed. Reported as an error rather than a warning because " +
		"the promise has already been broken for every plugin author who read it.");

	public static readonly DiagnosticDescriptor MissingDefaultLocalizationResource = Create(
		DiagnosticIds.MissingDefaultLocalizationResource,
		title: "Localization key has no default-language resource",
		messageFormat: "{0}",
		category: DiagnosticCategories.Localization,
		severity: DiagnosticSeverity.Error,
		description: "The default-language file - Strings.resx, with no culture suffix - is the one every " +
		"translation is checked against and the last resource the runtime falls back to. A key that " +
		"exists only in a translation can never be resolved for a reader on any other language, so it " +
		"is a missing default rather than an extra translation.");

	public static readonly DiagnosticDescriptor LocalizationPlaceholderMismatch = Create(
		DiagnosticIds.LocalizationPlaceholderMismatch,
		title: "Translation's placeholders differ from the default language's",
		messageFormat: "{0}",
		category: DiagnosticCategories.Localization,
		severity: DiagnosticSeverity.Error,
		description: "Placeholders are named, and the generated method's parameters come from the " +
		"default-language template. A translation naming a placeholder the default language does not " +
		"have would render that placeholder literally to every reader on that language, and one " +
		"omitting a placeholder would silently drop the value a caller passed.");

	public static readonly DiagnosticDescriptor DuplicateLocalizationKey = Create(
		DiagnosticIds.DuplicateLocalizationKey,
		title: "Localization key is declared twice",
		messageFormat: "{0}",
		category: DiagnosticCategories.Localization,
		severity: DiagnosticSeverity.Error,
		description: "Two entries with the same name in one resource file leave which value wins up to " +
		"file order. Rename one or delete the other.");

	public static readonly DiagnosticDescriptor UnknownLocalizationParameterType = Create(
		DiagnosticIds.UnknownLocalizationParameterType,
		title: "Localization parameter declaration is not usable",
		messageFormat: "{0}",
		category: DiagnosticCategories.Localization,
		severity: DiagnosticSeverity.Error,
		description: "A resource comment may declare a placeholder's type as a bracketed prefix, for " +
		"example '[count:int]'. Only types with one unambiguous text form are accepted, so the C# and " +
		"TypeScript formatters cannot disagree about what a substituted value looks like. A " +
		"declaration naming a placeholder the template does not use is reported the same way, since it " +
		"would generate a parameter nothing consumes.");

	public static readonly DiagnosticDescriptor InvalidLocalizationCulture = Create(
		DiagnosticIds.InvalidLocalizationCulture,
		title: "Resource file declares an invalid culture name",
		messageFormat: "{0}",
		category: DiagnosticCategories.Localization,
		severity: DiagnosticSeverity.Error,
		description: "The suffix in 'Strings.<culture>.resx' selects when the file is used. It must be a " +
		"well-formed BCP-47 name such as 'de', 'de-DE' or 'zh-Hans-CN'. The check is on the name's " +
		"shape on purpose: .NET manufactures a CultureInfo for anything that merely looks plausible, so " +
		"a typo would otherwise become a culture no reader ever asks for.");

	public static readonly DiagnosticDescriptor RemovedMacroDeckLocalizationKey = Create(
		DiagnosticIds.RemovedMacroDeckLocalizationKey,
		title: "Macro Deck localization key has been removed",
		messageFormat: "{0}",
		category: DiagnosticCategories.Localization,
		severity: DiagnosticSeverity.Error,
		description: "Macro Deck's own catalog is a published contract, so a key it retires is recorded " +
		"rather than deleted outright. Referencing a retired key still compiles against the SDK version " +
		"that carries the record, and this rule reports it with the replacement to move to.");

	public static readonly DiagnosticDescriptor InvalidLocalizationPluralFamily = Create(
		DiagnosticIds.InvalidLocalizationPluralFamily,
		title: "Plural family cannot produce a usable member",
		messageFormat: "{0}",
		category: DiagnosticCategories.Localization,
		severity: DiagnosticSeverity.Error,
		description: "An entry marked '[plural]' is one form of a family whose key is the entry's key " +
		"without its trailing form. The form has to be one the framework selects - 'One' or 'Other' - " +
		"and every family needs an 'Other', because that is the form every count other than one " +
		"resolves through and the only one a language with no singular distinction would use.");

	public static readonly DiagnosticDescriptor LocalizationKeyIsAlsoAGroup = Create(
		DiagnosticIds.LocalizationKeyIsAlsoAGroup,
		title: "Localization key is also a group",
		messageFormat: "{0}",
		category: DiagnosticCategories.Localization,
		severity: DiagnosticSeverity.Error,
		description: "A dotted key becomes a nested class, so a key that is also the prefix of other keys " +
		"would generate a method and a class of the same name in the same scope. Reported here rather " +
		"than left to surface as a duplicate-definition error inside generated source the author never " +
		"wrote.");

	private static DiagnosticDescriptor Create(
		string id,
		string title,
		string messageFormat,
		string category,
		DiagnosticSeverity severity,
		string description)
		=> new(id,
			title,
			messageFormat,
			category,
			severity,
			isEnabledByDefault: true,
			description: description,
			helpLinkUri: $"{HelpBaseUrl}#{id.ToLowerInvariant()}");
}
