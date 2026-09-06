namespace MacroDeck.Plugin.Protocol.Limits;

/// <summary>
/// Every numeric limit v1 enforces, advertised at runtime in <c>PluginProtocolDescriptor</c> and
/// <c>PluginSessionResponse</c> so plugins read them rather than hard-coding them.
/// </summary>
public static class ProtocolLimits
{
	public const int MaxMessageBytes = 256 * 1024;

	// Post-base64, since JSON is the mandatory encoding for asset chunks.
	public const int MaxAssetBytes = 8 * 1024 * 1024;

	public const int MaxAssetChunkBytes = 64 * 1024;

	public const int MaxInboundQueueDepth = 256;

	public const int MaxOutboundQueueDepth = 256;

	public const int QueueHighWatermark = 192;

	public const int QueueLowWatermark = 64;

	public const int MaxConcurrentInvocations = 32;

	public const int MaxDeclaredCapabilities = 512;

	public const int MaxIdempotencyKeyLength = 128;

	/// <summary>Bounds <c>PluginSessionRequest.DeclaredName</c> - attacker-controlled text from an
	/// untrusted process that the host stores and renders in the UI. Same bound as the manifest reader
	/// applies to <c>PluginManifest.Name</c>.</summary>
	public const int MaxDeclaredNameLength = 128;

	/// <summary>Bounds <c>PluginSessionRequest.DeclaredVersion</c> - attacker-controlled text from an
	/// untrusted process that the host stores and renders in the UI. Matches <c>PluginId.MaxLength</c>,
	/// generous for any real version string while still bounded.</summary>
	public const int MaxDeclaredVersionLength = 128;

	/// <summary>Bounds <c>PluginSdkUsage.SdkVersion</c> - same reasoning as
	/// <see cref="MaxDeclaredVersionLength" />, and deliberately the same value: both are version strings
	/// from an untrusted process that the host stores and renders.</summary>
	public const int MaxSdkVersionLength = 128;

	/// <summary>Bounds the build-time usage manifest a plugin reports. Generous against any real plugin's
	/// deprecated-API surface while keeping the handshake body far below
	/// <see cref="MaxMessageBytes" />; a plugin over the cap reports a truncated list rather than being
	/// rejected. Mirrored by <c>SdkUsageManifestGenerator.MaxReportedApis</c>, which cannot reference this
	/// constant because a Roslyn analyzer targets netstandard2.0.</summary>
	public const int MaxReportedDeprecatedApis = 64;

	/// <summary>Bounds one entry of that manifest - a documentation comment id, e.g.
	/// <c>M:MacroDeck.Sdk.Foo.Bar(System.String)</c>. Mirrored by
	/// <c>SdkUsageManifestGenerator.MaxApiIdLength</c>.</summary>
	public const int MaxDeprecatedApiIdLength = 256;

	/// <summary>Bounds the compatibility report the host sends back, so a plugin declaring a pathological
	/// number of capabilities cannot make the host build an unbounded response to its own request.</summary>
	public const int MaxCompatibilityFindings = 128;

	public const int MaxErrorMessageLength = 256;

	public const int MaxErrorDetailEntries = 16;

	public const int MaxJsonDepth = 32;

	public const int MaxSessionsPerPlugin = 1;

	// Bound log.publish ingestion. The rate is deliberately conservative because the host's file sink
	// caps retained files at 14 with no size limit - one unbounded file per day - so an unthrottled
	// plugin could fill the disk and drown the host's own entries.
	public const int MaxLogEventsPerBatch = 64;

	public const int MaxLogMessageLength = 4096;

	public const int MaxLogPropertiesPerEvent = 32;

	public const int MaxLogPropertyNameLength = 64;

	public const int MaxLogPropertyValueLength = 512;

	public const int MaxLogSourceContextLength = 128;

	public const int MaxLogExceptionLength = 8192;

	public const int MaxLogExceptionDepth = 5;

	public const int MaxLogInboundQueueDepth = 256;

	public const int MaxLogEventsPerSecond = 20;

	/// <summary>Bounds the cultures one plugin may declare, so a plugin cannot make the host issue an
	/// unbounded number of catalog round-trips on a language change.</summary>
	public const int MaxLocalizationCultures = 64;

	/// <summary>Bounds one culture's catalog. Generous against any real plugin's string count while
	/// keeping a catalog reply within <see cref="MaxMessageBytes" /> at typical key and value lengths.</summary>
	public const int MaxLocalizationEntries = 2000;

	/// <summary>Bounds a localization key - a dotted name such as <c>Configuration.Title</c>.</summary>
	public const int MaxLocalizationKeyLength = 128;

	/// <summary>Bounds one localized value. Attacker-controlled text from an untrusted process that the
	/// host caches and every connected client renders, so it is bounded well below a message.</summary>
	public const int MaxLocalizationValueLength = 4096;

	public const int MaxLogEventBurst = 500;

	/// <summary>Bounds one serialized UI tree. Deliberately below <see cref="MaxMessageBytes" /> so a
	/// tree plus its envelope always fits one frame; a provider whose tree does not fit gets
	/// <c>PAYLOAD_TOO_LARGE</c> rather than a malformed-envelope close.</summary>
	public const int MaxUiTreeBytes = 192 * 1024;

	/// <summary>Bounds one serialized UI patch. A patch approaching a whole tree means the provider
	/// should have re-snapshotted instead.</summary>
	public const int MaxUiPatchBytes = 64 * 1024;

	public const int MaxUiNodesPerTree = 2000;

	public const int MaxUiUpdatesPerSecond = 30;

	public const int MaxUiUpdateBurst = 90;

	/// <summary>Bounds a <c>UiResource</c>'s declared <c>byteLength</c>, and the bytes the host's own
	/// resource store accepts for one resource - capped on registration so a declaration inside a tree can
	/// never promise more than the host would actually serve.</summary>
	public const int MaxUiResourceBytes = 2 * 1024 * 1024;

	public const int MaxUiAttachmentsPerSession = 16;

	/// <summary>Bounds how much aggregate patch traffic one provider can direct at the shared inbound
	/// queue, which is <see cref="MaxInboundQueueDepth" /> deep for the whole connection. Does not bound
	/// widget and preview surfaces - see <see cref="MaxUiWidgetSessionsPerProvider" />.</summary>
	public const int MaxUiSessionsPerProvider = 8;

	/// <summary>
	/// Bounds the <c>widget</c> and <c>preview</c> surfaces one provider may have open, counted separately
	/// from <see cref="MaxUiSessionsPerProvider" />.
	///
	/// <para>
	/// A widget session is per widget per viewer rather than per provider, so a provider that supplies a
	/// widget type carries as many sessions as there are tiles of it on screen - a single folder holds
	/// dozens - while a configuration or dialog surface is one at a time. Counting both against one number
	/// would either refuse a plugin its own deck or lift the bound off the surfaces it was earned on.
	/// </para>
	/// </summary>
	public const int MaxUiWidgetSessionsPerProvider = 128;

	/// <summary>Bounds one <c>discover</c> page, so a provider cannot return tens of thousands of
	/// resources in a single message.</summary>
	public const int MaxVariableCatalogPageSize = 200;

	/// <summary>Bounds how many resources one plugin's variable working set may cover at once.</summary>
	public const int MaxVariableSubscriptions = 1024;

	/// <summary>Bounds one <c>value</c> push. The host's callback throttle bounds the rate of host
	/// callbacks; this bounds the size of one.</summary>
	public const int MaxVariableValuesPerBatch = 128;
}
