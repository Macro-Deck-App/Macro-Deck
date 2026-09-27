namespace MacroDeck.Sdk.Ui;

public enum UiResourceErrorCode
{
	/// <summary>The registration could not be completed, for example because the connection to Macro Deck
	/// dropped. Retrying later can succeed.</summary>
	Failed = 0,

	/// <summary>This Macro Deck, or this context, cannot register UI resources, or cannot resolve bundled
	/// plugin icons.</summary>
	Unsupported = 1,

	/// <summary>The plugin's resources would exceed their combined size or count. Remove resources, register
	/// smaller ones, or reuse names.</summary>
	QuotaExceeded = 2,

	/// <summary>Too many registrations in quick succession. Retry later.</summary>
	RateLimited = 3,

	/// <summary>The plugin's bundled icon packs hold no pack with that key, or the pack no icon with that
	/// name.</summary>
	PluginIconNotFound = 4
}
