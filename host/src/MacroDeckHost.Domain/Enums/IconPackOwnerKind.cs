namespace MacroDeckHost.Domain.Enums;

// A stable wire token sent to UI clients as-is - the client owns its own translation of each value,
// so this must never carry a host-culture-localized label.
public enum IconPackOwnerKind
{
	User,
	Store,
	Plugin
}
