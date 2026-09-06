using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeck.Sdk.Identity;
using Mediator;

namespace MacroDeckHost.Application.Variables;

public sealed class VariableBindingService : IVariableBindingService
{
	private readonly VariableCatalogProviders _providers;
	private readonly IVariableBindingStore _bindingStore;
	private readonly IVariableService _variableService;
	private readonly VariableRegistry _registry;
	private readonly VariableNameFactory _nameFactory;
	private readonly IVariableSubscriptionCoordinator _coordinator;
	private readonly IMediator _mediator;
	private readonly VariableBindingLookup _lookup;

	// Bind/Unbind/Rename each do a load-modify-save against the same store; without a lock spanning the
	// whole sequence, two of them interleaving can both load the same snapshot and each save over the
	// other's change, silently dropping one binding. Static, not per-instance: this service is registered
	// scoped, so two concurrent UI requests each get their own instance and would otherwise each get their
	// own, ineffective lock guarding the one file both are writing to.
	private static readonly SemaphoreSlim _mutationLock = new(1, 1);

	public VariableBindingService(
		VariableCatalogProviders providers,
		IVariableBindingStore bindingStore,
		IVariableService variableService,
		VariableRegistry registry,
		VariableNameFactory nameFactory,
		IVariableSubscriptionCoordinator coordinator,
		IMediator mediator,
		VariableBindingLookup lookup)
	{
		_providers = providers;
		_bindingStore = bindingStore;
		_variableService = variableService;
		_registry = registry;
		_nameFactory = nameFactory;
		_coordinator = coordinator;
		_mediator = mediator;
		_lookup = lookup;
	}

	public async Task<Result<VariableEntity, VariableBindingError>> BindAsync(
		string integrationId,
		string localResourceId,
		string? requestedName,
		VariableType? typeOverride,
		CancellationToken cancellationToken = default)
	{
		var provider = _providers.Resolve(integrationId);
		if (provider is null)
		{
			return Fail<VariableEntity>(VariableBindingError.ProviderUnavailable,
				$"Integration '{integrationId}' does not currently offer a variable catalog");
		}

		// null means invalid, not absent - a resource that is merely gone right now still resolves, and
		// binding it must succeed. Only "this provider does not know this id at all" fails here.
		var definition = await provider.ResolveAsync(localResourceId, cancellationToken).ConfigureAwait(false);
		if (definition is null)
		{
			return Fail<VariableEntity>(VariableBindingError.Unresolvable,
				$"'{localResourceId}' is not a resource '{integrationId}' recognises");
		}

		if (!definition.IsBindable)
		{
			return Fail<VariableEntity>(VariableBindingError.NotBindable,
				$"'{definition.Id}' is not bindable");
		}

		var resourceId = definition.Id;
		if (resourceId is null || !MacroDeckId.IsValidLocalId(resourceId, LocalIdKind.Resource))
		{
			return Fail<VariableEntity>(VariableBindingError.InvalidResourceId,
				$"'{definition.Id}' is not a valid resource id");
		}

		string name;
		if (requestedName is not null)
		{
			var sanitized = VariableNameSanitizer.IsValid(requestedName)
				? requestedName
				: VariableNameSanitizer.Sanitize(requestedName);
			if (!VariableNameSanitizer.IsValid(sanitized))
			{
				return Fail<VariableEntity>(VariableBindingError.InvalidName,
					"Name must match ^[a-z][a-z0-9_]*$");
			}

			var collision = _registry.FindByName(VariableScope.Global, null, sanitized);
			if (collision is not null &&
				!(collision.Classification == VariableClassification.Integration &&
					string.Equals(collision.OwnerIntegrationId, integrationId, StringComparison.Ordinal) &&
					string.Equals(collision.DefinitionId, resourceId, StringComparison.Ordinal)))
			{
				return Fail<VariableEntity>(VariableBindingError.AlreadyExists,
					$"A variable named '{sanitized}' already exists");
			}

			name = sanitized;
		}
		else
		{
			name = _nameFactory.Derive(integrationId, resourceId, definition.Name);
		}

		var type = typeOverride ?? SdkVariableTypeMapper.ToDomain(definition.Type);

		var binding = new VariableBinding
		{
			Id = Guid.CreateVersion7(),
			IntegrationId = integrationId,
			LocalResourceId = resourceId,
			Name = name,
			Type = type,
			DecimalPlaces = definition.DecimalPlaces,
			CreatedAt = DateTime.UtcNow
		};

		// Persisted before materializing: a crash between the two leaves a binding that boot replays,
		// never a variable with no binding behind it.
		await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (!_bindingStore.TryLoad(out var current))
			{
				return Fail<VariableEntity>(VariableBindingError.StoreUnavailable,
					"Could not read the binding store; refusing to bind without a durable read");
			}

			var bindings = current.ToList();
			bindings.Add(binding);
			if (!_bindingStore.Save(bindings))
			{
				return Fail<VariableEntity>(VariableBindingError.StoreUnavailable,
					"Could not persist the new binding");
			}

			var materialized = await _variableService.MaterializeCatalogVariable(integrationId,
					resourceId,
					name,
					type,
					definition.DecimalPlaces,
					VariableDeclarationFactory.From(definition))
				.ConfigureAwait(false);

			if (!materialized.Success)
			{
				bindings.Remove(binding);
				_bindingStore.Save(bindings);
				return Fail<VariableEntity>(VariableBindingError.AlreadyExists,
					materialized.ErrorMessage ?? "Could not materialize the bound variable");
			}

			await _coordinator.ReconcileAsync(cancellationToken).ConfigureAwait(false);

			return Result.Ok<VariableEntity, VariableBindingError>(materialized.Data!);
		}
		finally
		{
			_mutationLock.Release();
		}
	}

	public async Task<Result<VariableBindingError>> UnbindAsync(
		Guid variableId,
		CancellationToken cancellationToken = default)
	{
		var entity = _registry.GetById(variableId);
		if (entity is null || entity.OwnerIntegrationId is null || entity.DefinitionId is null)
		{
			return Fail(VariableBindingError.NotFound, "Variable not found");
		}

		await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (!_bindingStore.TryLoad(out var current))
			{
				return Fail(VariableBindingError.StoreUnavailable,
					"Could not read the binding store; refusing to unbind without a durable read");
			}

			var bindings = current.ToList();
			var binding = FindBinding(bindings, entity.OwnerIntegrationId, entity.DefinitionId);
			if (binding is null)
			{
				return Fail(VariableBindingError.NotFound, "No binding for this variable");
			}

			bindings.Remove(binding);
			if (!_bindingStore.Save(bindings))
			{
				return Fail(VariableBindingError.StoreUnavailable, "Could not persist the unbind");
			}

			await _variableService.DeleteIntegrationVariable(entity.OwnerIntegrationId, variableId)
				.ConfigureAwait(false);
			await _coordinator.ReconcileAsync(cancellationToken).ConfigureAwait(false);

			return Result.Ok<VariableBindingError>();
		}
		finally
		{
			_mutationLock.Release();
		}
	}

	public async Task<Result<VariableBindingError>> RenameAsync(
		Guid variableId,
		string name,
		CancellationToken cancellationToken = default)
	{
		var entity = _registry.GetById(variableId);
		if (entity is null || entity.OwnerIntegrationId is null || entity.DefinitionId is null)
		{
			return Fail(VariableBindingError.NotFound, "Variable not found");
		}

		var canonical = VariableNameSanitizer.IsValid(name) ? name : VariableNameSanitizer.Sanitize(name);
		if (!VariableNameSanitizer.IsValid(canonical))
		{
			return Fail(VariableBindingError.InvalidName, "Name must match ^[a-z][a-z0-9_]*$");
		}

		await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (!_bindingStore.TryLoad(out var current))
			{
				return Fail(VariableBindingError.StoreUnavailable,
					"Could not read the binding store; refusing to rename without a durable read");
			}

			var bindings = current.ToList();
			var binding = FindBinding(bindings, entity.OwnerIntegrationId, entity.DefinitionId);
			if (binding is null)
			{
				return Fail(VariableBindingError.NotFound, "No binding for this variable");
			}

			// UpdateUserVariable cannot be reused here: it hard-rejects anything that is not User-classified.
			if (!_registry.Rename(variableId, canonical, DateTime.UtcNow))
			{
				return Fail(VariableBindingError.AlreadyExists,
					$"A variable named '{canonical}' already exists");
			}

			binding.Name = canonical;
			if (!_bindingStore.Save(bindings))
			{
				return Fail(VariableBindingError.StoreUnavailable, "Could not persist the rename");
			}

			await _mediator.Publish(new VariableUpdatedNotification(entity), cancellationToken).ConfigureAwait(false);

			return Result.Ok<VariableBindingError>();
		}
		finally
		{
			_mutationLock.Release();
		}
	}

	public IReadOnlyList<VariableBinding> GetBindings() => _bindingStore.Load();

	public VariableBinding? FindByVariableId(Guid variableId) => _lookup.FindByVariableId(variableId);

	private static VariableBinding? FindBinding(
		IEnumerable<VariableBinding> bindings,
		string integrationId,
		string localResourceId)
		=> bindings.FirstOrDefault(b =>
			string.Equals(b.IntegrationId, integrationId, StringComparison.Ordinal) &&
			string.Equals(b.LocalResourceId, localResourceId, StringComparison.Ordinal));

	private static Result<T, VariableBindingError> Fail<T>(VariableBindingError error, string message)
		=> Result.Fail<T, VariableBindingError>(error, message);

	private static Result<VariableBindingError> Fail(VariableBindingError error, string message)
		=> Result.Fail(error, message);
}
