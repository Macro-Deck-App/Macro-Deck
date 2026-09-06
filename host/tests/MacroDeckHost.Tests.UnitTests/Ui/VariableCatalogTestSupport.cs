using System.Globalization;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;

namespace MacroDeckHost.Tests.UnitTests.Ui;

/// <summary>An <see cref="IVariableProvider"/> catalog a test can script - either by handing it a flat
/// item set it paginates itself, or by taking over <see cref="DiscoverHandler"/>/<see cref="ResolveHandler"/>
/// entirely. Every call is recorded so a test can assert on exactly what the host asked for.</summary>
internal sealed class FakeVariableProviderIntegration : IIntegration, IVariableProvider
{
	public string Id { get; init; } = "fake-dynamic-provider";
	public LocalizedText Name { get; init; } = "Fake Dynamic Provider";
	public string Version => "1.0.0";
	public IReadOnlyList<IActionDefinition> Actions => [];
	public bool IsInitialized { get; set; } = true;

	public string ProviderName { get; init; } = string.Empty;
	public IReadOnlyList<VariableDefinition> Variables => [];
	public bool SupportsCatalog => true;
	public bool SupportsPush { get; init; }
	public bool SupportsSearch { get; init; }

	/// <summary>Flat item set the default <see cref="DiscoverAsync"/> pages through when
	/// <see cref="DiscoverHandler"/> is not set.</summary>
	public IReadOnlyList<VariableDefinition> AllItems { get; init; } = [];

	public Func<VariableCatalogQuery, VariableCatalogPage>? DiscoverHandler { get; init; }
	public Func<string, VariableDefinition?>? ResolveHandler { get; init; }

	public List<VariableCatalogQuery> ReceivedQueries { get; } = [];

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public ValueTask<VariableCatalogPage> DiscoverAsync(VariableCatalogQuery query,
		CancellationToken cancellationToken = default)
	{
		ReceivedQueries.Add(query);

		if (DiscoverHandler is not null)
		{
			return ValueTask.FromResult(DiscoverHandler(query));
		}

		var start = int.TryParse(query.ContinuationToken, out var parsed) ? parsed : 0;
		var page = AllItems.Skip(start).Take(query.PageSize).ToList();
		var next = start + page.Count;
		var token = next < AllItems.Count ? next.ToString(CultureInfo.InvariantCulture) : null;
		return ValueTask.FromResult(new VariableCatalogPage { Items = page, ContinuationToken = token });
	}

	public ValueTask<VariableDefinition?> ResolveAsync(string localId, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(ResolveHandler is not null
			? ResolveHandler(localId)
			: AllItems.FirstOrDefault(i => i.Id == localId));

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(VariableReading.Unavailable);

	public ValueTask<IReadOnlyList<VariableValue>> SubscribeAsync(IReadOnlyCollection<string> localIds,
		CancellationToken cancellationToken = default)
		=> ValueTask.FromResult<IReadOnlyList<VariableValue>>([]);
}

/// <summary>An <see cref="IVariableBindingService"/> whose every outcome a test dictates directly,
/// for handler tests that must not depend on the real service's storage, name derivation, or
/// materialization machinery.</summary>
internal sealed class FakeVariableBindingService : IVariableBindingService
{
	public Func<string, string, string?, DomainVariableType?, Result<VariableEntity, VariableBindingError>>?
		OnBind { get; init; }

	public Func<Guid, Result<VariableBindingError>>? OnUnbind { get; init; }
	public Func<Guid, string, Result<VariableBindingError>>? OnRename { get; init; }
	public List<VariableBinding> Bindings { get; init; } = [];
	public Func<Guid, VariableBinding?>? OnFindByVariableId { get; init; }

	public Task<Result<VariableEntity, VariableBindingError>> BindAsync(
		string integrationId,
		string localResourceId,
		string? requestedName,
		DomainVariableType? typeOverride,
		CancellationToken cancellationToken = default)
		=> Task.FromResult(OnBind?.Invoke(integrationId, localResourceId, requestedName, typeOverride) ??
			Result.Fail<VariableEntity, VariableBindingError>(VariableBindingError.ProviderUnavailable));

	public Task<Result<VariableBindingError>> UnbindAsync(Guid variableId,
		CancellationToken cancellationToken = default)
		=> Task.FromResult(OnUnbind?.Invoke(variableId) ??
			Result.Fail(VariableBindingError.NotFound, "not found"));

	public Task<Result<VariableBindingError>> RenameAsync(Guid variableId,
		string name,
		CancellationToken cancellationToken = default)
		=> Task.FromResult(OnRename?.Invoke(variableId, name) ??
			Result.Fail(VariableBindingError.NotFound, "not found"));

	public IReadOnlyList<VariableBinding> GetBindings() => Bindings;

	public VariableBinding? FindByVariableId(Guid variableId)
		=> OnFindByVariableId is not null
			? OnFindByVariableId(variableId)
			: Bindings.FirstOrDefault(b => false);
}
