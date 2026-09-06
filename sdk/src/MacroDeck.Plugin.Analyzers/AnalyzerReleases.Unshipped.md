; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
MDP1003 | MacroDeck.Identity | Error | UnsupportedManifestIconExtensionAnalyzer
MDP1004 | MacroDeck.Identity | Error | RestatedManifestIdentityMemberAnalyzer
MDP2006 | MacroDeck.Registration | Warning | InertIntegrationAttributeAnalyzer
MDLOC001 | MacroDeck.Localization | Error | LocalizationGenerator
MDLOC002 | MacroDeck.Localization | Error | LocalizationGenerator
MDLOC003 | MacroDeck.Localization | Error | LocalizationGenerator
MDLOC004 | MacroDeck.Localization | Error | LocalizationGenerator
MDLOC005 | MacroDeck.Localization | Error | LocalizationGenerator
MDLOC006 | MacroDeck.Localization | Error | RemovedLocalizationKeyAnalyzer
MDLOC007 | MacroDeck.Localization | Error | LocalizationGenerator
MDLOC008 | MacroDeck.Localization | Error | LocalizationGenerator

; MDP2004 was rehomed from IIntegration/AddMacroDeckIntegration onto IPluginIntegration/RegisterIntegration
; (same id - its meaning, "register the integration through the intended door", did not change). This is
; not recorded as a "### Changed Rules" entry below: the Roslyn release-tracking tooling's own
; Rule ID | New Category | New Severity | Old Category | Old Severity | Notes table (RS2000/RS2001, on
; via EnforceExtendedAnalyzerRules) rejects a row whose New/Old Category and New/Old Severity columns are
; identical - RS2007 "invalid entry" - and MDP2004 changed neither its category nor its severity, only
; what it matches and what it says. See https://docs.macro-deck.app/sdk/analyzers/#mdp2004 and this
; rule's own DiagnosticDescriptors.RawIntegrationRegistration.description for the rehoming, instead.
