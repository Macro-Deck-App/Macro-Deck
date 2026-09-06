namespace MacroDeck.Ui.Tests.UnitTests.Dsl;

// What is left of the test-local stand-ins the identity and reactivity fixtures used before the real
// configuration primitives existed: the fixtures now author UiFlow, UiStep, UiConfigStack, UiAdvancedSection,
// UiStringInput, UiChoiceInput, UiDurationInput, UiObjectInput and UiArrayInput directly, so only the item
// record the SampleFlow fixture repeats over is still test-owned.

/// <summary>An item in the SampleFlow fixture's <c>headers</c> array.</summary>
internal sealed record HeaderItem(string Id);
