using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Application.Ui.Transport.Messages.Version;
using MacroDeckHost.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Host;

public class UiTransportServiceCollectionExtensionsTests
{
	private sealed class StubFlowExecutor : IFlowExecutor
	{
		public Task<FlowExecutionResult> ExecuteAsync(FlowExecutionRequest request, CancellationToken cancellationToken)
			=> Task.FromResult(new FlowExecutionResult
			{
				ExecutionId = request.ExecutionId,
				Status = FlowExecutionStatus.Succeeded
			});
	}

	[Test]
	public void AddUiTransport_registers_handlers_from_marker_assembly()
	{
		var services = new ServiceCollection();

		services.AddUiTransport(typeof(GetVersionRequestMessageHandler).Assembly);

		var handlerDescriptors = services
			.Where(d => d.ServiceType.IsGenericType &&
				d.ServiceType.GetGenericTypeDefinition() == typeof(IUiTransportMessageHandler<,>))
			.ToList();

		Assert.That(handlerDescriptors, Is.Not.Empty);
		Assert.That(handlerDescriptors,
			Has.Some.Matches<ServiceDescriptor>(d =>
				d.ServiceType == typeof(IUiTransportMessageHandler<GetVersionRequest, GetVersionResponse>)));
	}

	[Test]
	public void AddUiTransport_resolves_known_handler_from_provider()
	{
		var services = new ServiceCollection();

		services.AddUiTransport(typeof(GetVersionRequestMessageHandler).Assembly);

		using var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();

		var handler = scope.ServiceProvider
			.GetService<IUiTransportMessageHandler<GetVersionRequest, GetVersionResponse>>();

		Assert.That(handler, Is.InstanceOf<GetVersionRequestMessageHandler>());
	}

	[Test]
	public void AddUiTransport_resolves_a_handler_with_an_optional_constructor_parameter()
	{
		var services = new ServiceCollection();
		services.AddSingleton(Serilog.Log.Logger);
		services.AddScoped<IFlowExecutor, StubFlowExecutor>();
		services.AddUiTransport(typeof(RunActionFlowRequestMessageHandler).Assembly);

		using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
		using var scope = provider.CreateScope();

		var handler = scope.ServiceProvider
			.GetService<IUiTransportMessageHandler<RunActionFlowRequest, RunActionFlowResponse>>();

		Assert.That(handler, Is.InstanceOf<RunActionFlowRequestMessageHandler>());
	}

	[Test]
	public void AddUiTransport_registers_no_handlers_without_marker_assemblies()
	{
		var services = new ServiceCollection();

		services.AddUiTransport();

		var handlerDescriptors = services
			.Where(d => d.ServiceType.IsGenericType &&
				d.ServiceType.GetGenericTypeDefinition() == typeof(IUiTransportMessageHandler<,>))
			.ToList();

		Assert.That(handlerDescriptors, Is.Empty);
	}
}
