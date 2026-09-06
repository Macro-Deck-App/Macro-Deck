namespace MacroDeck.Sdk.ConfigFlow;

/// <summary>
/// Optional entry metadata supplied by hosts that distinguish creating a configuration from editing
/// an existing one. Flows must continue to work when only <see cref="IConfigFlowContext"/> is available.
/// </summary>
public interface IConfigFlowEntryContext : IConfigFlowContext
{
	/// <summary>
	/// The existing or host-requested entry title. A null value means the flow must choose a title for
	/// the new entry.
	/// </summary>
	string? EntryTitle { get; }
}
