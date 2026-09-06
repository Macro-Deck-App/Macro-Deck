## Release 3.0.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
MDP1001 | MacroDeck.Identity | Error | ManifestIdentityAnalyzer
MDP1002 | MacroDeck.Identity | Error | DeclaredLocalIdGrammarAnalyzer
MDP2001 | MacroDeck.Registration | Error | DuplicateCapabilityIdAnalyzer
MDP2002 | MacroDeck.Registration | Error | UnknownCapabilityKindAnalyzer
MDP2003 | MacroDeck.Registration | Warning | UnroutedCapabilityHandlerAnalyzer
MDP2004 | MacroDeck.Registration | Warning | RawIntegrationRegistrationAnalyzer
MDP2005 | MacroDeck.Registration | Error | ReservedRoutePathAnalyzer
MDP3001 | MacroDeck.Async | Warning | MissingCancellationForwardingAnalyzer
MDP3002 | MacroDeck.Async | Warning | BlockingCallAnalyzer
MDP3003 | MacroDeck.Async | Warning | AsyncVoidMemberAnalyzer
MDP4001 | MacroDeck.Lifecycle | Error | SingletonCapabilityContextAnalyzer
MDP4002 | MacroDeck.Lifecycle | Warning | ListenerUrlOverrideAnalyzer
MDP5001 | MacroDeck.Compatibility | Warning | ObsoleteSdkMemberAnalyzer
MDP5002 | MacroDeck.Compatibility | Warning | DeprecatedSdkApiAnalyzer
MDP5003 | MacroDeck.Compatibility | Warning | DeprecationMetadataAnalyzer
MDP5004 | MacroDeck.Compatibility | Error | DeprecatedSdkApiAnalyzer
