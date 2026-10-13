namespace MacroDeck.Sdk.Companion;

/// <summary>
/// Asks Macro Deck to keep one configuration entry of this integration per Companion device. Implement it on
/// the integration next to <see cref="ConfigFlow.IConfigFlowProvider" />; it has no members.
/// </summary>
/// <remarks>
/// <para>
/// Macro Deck creates the entry when a device running the Companion app connects and has no usable entry yet,
/// unless the user switched the integration off. The entry's id is the device id, its title is the device's
/// name, and its values are empty. Creating it switches the integration on, as storing any entry does; when the
/// user switches the integration off while the entry is being created, that choice is kept. Macro Deck deletes
/// the entry when the device is removed from Macro Deck, without recording that as the user switching the
/// integration off.
/// </para>
/// <para>
/// Only one integration owns these entries: the first integration built into Macro Deck that implements this
/// interface, in the order integrations register, or, when there is none, the first plugin that does. Macro Deck
/// holds that choice in memory and makes it again on every start. While the built-in Companion integration is
/// present, a plugin implementing this interface never owns the entries. When the owner is switched off, no entry
/// is created for anyone.
/// </para>
/// <para>
/// A plugin implementing it declares the <c>companion-device-entries</c> capability. A plugin owner additionally
/// needs access to Companion devices, see <see cref="ICompanionDeviceApi" />; without it no entry is created.
/// The entry is stored like one the user completed: a plugin that also implements
/// <see cref="ConfigFlow.IConfigEntryLifecycle" /> is asked to prepare it first; after a refusal Macro Deck tries
/// again when the device next reports its state. A Macro Deck that predates the capability rejects the declaration
/// and never creates these entries.
/// </para>
/// </remarks>
public interface ICompanionDeviceConfigEntries;
