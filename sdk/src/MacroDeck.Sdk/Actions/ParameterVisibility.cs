namespace MacroDeck.Sdk.Actions;

/// <summary>
/// Shows a parameter only while a sibling parameter in the same action holds one of
/// <see cref="Values" />. Without it every declared parameter is always visible, which is why an
/// action with mutually exclusive modes - pick an authentication kind, pick a body kind - used to
/// render the fields of every mode at once.
///
/// The editor compares the sibling's current value as a string, ignoring case, and treats an unknown
/// <see cref="ParameterName" /> as "always visible" so a typo cannot make a field unreachable. A
/// hidden parameter keeps whatever the user typed and is skipped by flow validation, so switching
/// modes back and forth never loses input and a hidden required field never blocks a save.
///
/// Visibility is presentation only: the host still sends every parameter to the action, so an
/// executor must not infer anything from a field being hidden.
/// </summary>
/// <param name="ParameterName">The sibling parameter whose value decides visibility.</param>
/// <param name="Values">The sibling values that make this parameter visible.</param>
public sealed record ParameterVisibility(string ParameterName, IReadOnlyList<string> Values);
