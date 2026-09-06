using SdkVariableType = MacroDeck.Sdk.Variables.VariableType;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;

namespace MacroDeckHost.Application.Variables;

public static class SdkVariableTypeMapper
{
	public static DomainVariableType ToDomain(SdkVariableType type) => type switch
	{
		SdkVariableType.Numeric => DomainVariableType.Numeric,
		SdkVariableType.Boolean => DomainVariableType.Boolean,
		_ => DomainVariableType.Text
	};
}
