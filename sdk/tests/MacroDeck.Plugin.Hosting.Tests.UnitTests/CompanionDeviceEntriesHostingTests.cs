using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.CompanionDeviceEntries;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class CompanionDeviceEntriesHostingTests
{
	[Test]
	public void Only_a_plugin_with_the_marker_declares_the_capability_once_at_the_provider_id()
	{
		using var withMarker = MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration(_ => new TestCompanionDeviceEntriesIntegration())
			.Build();
		using var withoutMarker = MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration(_ => new TestIntegration())
			.Build();

		var declared = Declared(withMarker)
			.Where(capability => capability.Kind == CapabilityKinds.CompanionDeviceEntries)
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(declared.Select(capability => capability.LocalId),
				Is.EqualTo(new[] { ProviderCapabilityId.LocalId }));
			Assert.That(Declared(withoutMarker).Select(capability => capability.Kind),
				Does.Not.Contain(CapabilityKinds.CompanionDeviceEntries));
		});
	}

	[Test]
	public async Task Describe_succeeds_without_data_and_any_other_operation_is_unsupported()
	{
		var handler = new CompanionDeviceEntriesCapabilityHandler([new TestCompanionDeviceEntriesIntegration()]);

		var describe = await InvokeAsync(handler, CapabilityOperations.CompanionDeviceEntries.Describe);
		var unknown = await InvokeAsync(handler, "entries");

		Assert.Multiple(() =>
		{
			Assert.That(describe.Error, Is.Null);
			Assert.That(describe.Data, Is.Null);
			Assert.That(unknown.Error?.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
		});
	}

	private static IReadOnlyList<DeclaredCapability> Declared(PluginApplication application)
	{
		return
		[
			.. application.Services.GetServices<ICapabilityHandler>()
				.SelectMany(handler => handler.DeclareCapabilities())
		];
	}

	private static Task<CapabilityInvocationResult> InvokeAsync(
		CompanionDeviceEntriesCapabilityHandler handler,
		string operation)
	{
		return handler.InvokeAsync(new CapabilityInvocation
			{
				Kind = CapabilityKinds.CompanionDeviceEntries,
				LocalId = ProviderCapabilityId.LocalId,
				Operation = operation,
				CorrelationId = "c1",
				Services = new ServiceCollection().BuildServiceProvider()
			},
			CancellationToken.None);
	}
}
