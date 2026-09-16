using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Variables;
using Microsoft.Extensions.DependencyInjection;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class ActionVariableTargets : IDisposable
{
	private readonly ServiceProvider _provider;

	public ActionVariableTargets(string integrationId)
	{
		Service = TestVariableServices.Create(new VariableRegistry(), new NullStore(), new RecordingMediator());
		_provider = new ServiceCollection()
			.AddScoped<IVariableService>(_ => Service)
			.BuildServiceProvider();
		UserVariables = new UserVariableWriter(_provider.GetRequiredService<IServiceScopeFactory>());
		IntegrationVariables = new IntegrationVariableApi(integrationId, Service);
	}

	public VariableService Service { get; }

	public UserVariableWriter UserVariables { get; }

	public IntegrationVariableApi IntegrationVariables { get; }

	public Task CreateUserVariable(string name, DomainVariableType type, object? value)
		=> Service.CreateUserVariable(name, VariableScope.Global, null, type, value, null);

	public Task<VariableEntity?> Find(string name) => Service.Resolve(name, VariableScope.Global, null);

	public async Task<object?> ValueOf(string name)
		=> await Find(name) is { } entity ? VariableValueSerializer.Deserialize(entity.Type, entity.Value) : null;

	public void Dispose()
	{
		UserVariables.Dispose();
		_provider.Dispose();
	}

	private sealed class NullStore : IUserVariableStore
	{
		public IReadOnlyList<VariableEntity> Load() => [];

		public void Save(IEnumerable<VariableEntity> userVariables)
		{
		}
	}
}
