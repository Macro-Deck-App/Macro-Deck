using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Handshake;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Tests.PluginContractTests.Harness;

internal static class PluginTestSession
{
	public static PluginSession Create(string sessionId, int negotiatedVersion, DateTimeOffset openedAt)
		=> new(sessionId,
			"session-token",
			negotiatedVersion,
			ProtocolDescriptorFactory.CreateLimitsDescriptor(),
			ProtocolDescriptorFactory.CreateTimeoutsDescriptor(),
			openedAt);

	public static CapabilityDispatcher Dispatcher(IEnumerable<ICapabilityHandler> handlers,
		PluginConnectionState state,
		TimeProvider timeProvider,
		IHostInvoker? hostInvoker = null,
		IPluginAssetUploader? assetUploader = null)
	{
		var services = new ServiceCollection();
		services.AddScoped<CapabilityInvocationContextHolder>();

		// A handler that builds a RemoteActionInteractions (issue #413 step 6) resolves IHostInvoker
		// from the invocation's own scope - the same singleton every capability handler in a real
		// plugin process would see, provided here so the actions contract test exercises exactly the
		// same code path a real plugin's ActionsCapabilityHandler does. A caller that also builds a
		// handler directly with an IHostInvoker (UiCapabilityHandler is the one so far) passes that same
		// instance here, so a call routed through DI and one built into the handler complete off the
		// same pending-correlation table instead of two invokers racing each other.
		services.AddSingleton(hostInvoker ??
			new HostInvoker(state,
				timeProvider,
				Serilog.Core.Logger.None));

		// Same reasoning as IHostInvoker above: ActionsCapabilityHandler.GetActionIconContentAsync
		// resolves IPluginAssetUploader from the invocation's own scope rather than its constructor, so a
		// caller that wants an upload to actually reach the fixture's asset receiver passes the same
		// instance CapabilityContractFixture.UploadAssetAsync uses.
		services.AddSingleton(assetUploader ??
			new PluginAssetUploader(state,
				timeProvider,
				Serilog.Core.Logger.None));
		services.AddSingleton<ILogger>(Serilog.Core.Logger.None);

		return new CapabilityDispatcher(new CapabilityCatalog(handlers),
			services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
			state,
			timeProvider,
			Serilog.Core.Logger.None);
	}
}
