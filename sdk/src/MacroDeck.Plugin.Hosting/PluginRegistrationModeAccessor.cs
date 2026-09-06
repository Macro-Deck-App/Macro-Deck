namespace MacroDeck.Plugin.Hosting;

/// <summary>
/// The resolved registration mode, as a service.
///
/// <para>
/// A wrapper rather than registering the enum itself: the dependency injection container only takes
/// reference types as service keys, and resolving the mode is a decision made once at build from the
/// explicit builder call, the configuration and finally what credentials are present.
/// </para>
/// </summary>
/// <param name="Mode">How this plugin obtains its credentials.</param>
public sealed record PluginRegistrationModeAccessor(PluginRegistrationMode Mode);
