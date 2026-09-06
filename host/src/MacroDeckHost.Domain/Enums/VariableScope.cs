using System.Text.Json.Serialization;
using MacroDeckHost.Domain.Serialization;

namespace MacroDeckHost.Domain.Enums;

[JsonConverter(typeof(VariableScopeJsonConverter))]
public enum VariableScope
{
	// Persisted values are compatibility-sensitive; append new scopes instead of reordering them.
	Global = 0,

	// Named ActionButton until the scope was generalized to every widget type. The converter still
	// reads the old spelling, and the numeric value is unchanged for files that stored the integer.
	Widget = 1
}
