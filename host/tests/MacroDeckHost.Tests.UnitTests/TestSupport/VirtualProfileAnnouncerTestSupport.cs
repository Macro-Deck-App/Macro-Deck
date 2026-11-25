using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class ObservingUiTransport : IUiTransport
{
	public List<object> Sent { get; } = [];

	public List<(string Group, object Message)> GroupMessages { get; } = [];

	public Action<object>? OnSend { get; set; }

	public Task Send<T>(T message, CancellationToken cancellationToken = default)
		where T : class
	{
		OnSend?.Invoke(message);
		Sent.Add(message);
		return Task.CompletedTask;
	}

	public Task SendToGroup<T>(string group, T message, CancellationToken cancellationToken = default)
		where T : class
	{
		GroupMessages.Add((group, message));
		return Task.CompletedTask;
	}

	public Task SendToConnection<T>(string connectionId, T message, CancellationToken cancellationToken = default)
		where T : class
	{
		return Task.CompletedTask;
	}

	public Task AddToGroup(string connectionId, string group, CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}

	public Task RemoveFromGroup(string connectionId, string group, CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}
}

internal sealed class MediatorScopeFactory : IServiceScopeFactory, IServiceScope, IServiceProvider
{
	private readonly IMediator _mediator;

	public MediatorScopeFactory(IMediator mediator)
	{
		_mediator = mediator;
	}

	public IServiceProvider ServiceProvider => this;

	public IServiceScope CreateScope()
	{
		return this;
	}

	public object? GetService(Type serviceType)
	{
		return serviceType == typeof(IMediator) ? _mediator : null;
	}

	public void Dispose()
	{
	}
}

internal static class TestVirtualProfileAnnouncer
{
	public static VirtualProfileAnnouncer Create(
		IProfileRegistry profiles,
		IIntegrationRegistry integrations,
		IUiTransport transport,
		UiSessionRegistry sessions,
		WidgetUiProviderRegistry widgetUiProviders,
		IMediator mediator)
	{
		return new VirtualProfileAnnouncer(profiles,
			integrations,
			transport,
			sessions,
			widgetUiProviders,
			new MediatorScopeFactory(mediator));
	}
}
