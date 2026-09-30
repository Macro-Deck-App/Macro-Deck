namespace MacroDeck.Sdk.Widgets;

/// <summary>
/// An action of the widget type's own provider that Macro Deck runs when the widget is short-pressed and the
/// user has not given it a Short Press action of their own.
/// </summary>
/// <param name="ActionId">
/// The id of an action the same provider declares. An action of another integration cannot be named: a
/// default runs without the user ever choosing it. Must not be blank, or the widget type is rejected.
/// </param>
/// <param name="Parameters">
/// Parameter values by parameter name, as text. Each one is typed from the action's declared parameter and
/// rendered like a value the user stored, so a template inside it is resolved. A parameter left out gets its
/// declared default.
/// </param>
public sealed record WidgetDefaultAction(string ActionId, IReadOnlyDictionary<string, string>? Parameters = null);
