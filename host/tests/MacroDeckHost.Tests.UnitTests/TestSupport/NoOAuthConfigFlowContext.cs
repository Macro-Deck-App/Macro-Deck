using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class NoOAuthConfigFlowContext : IConfigFlowContext
{
	public static readonly NoOAuthConfigFlowContext Instance = new();

	public IOAuthSession OAuth => throw new NotSupportedException("This flow does not use OAuth.");
}
