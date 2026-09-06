using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeck.Sdk.Identity;
using Mediator;
using VariableWriteStatus = MacroDeck.Sdk.Variables.VariableWriteStatus;

namespace MacroDeckHost.Application.Services;

public class VariableService : IVariableService
{
	private readonly VariableRegistry _registry;
	private readonly IUserVariableStore _userStore;
	private readonly IMediator _mediator;
	private readonly VariableCatalogProviders _providers;
	private readonly IVariableRefreshSignal _refreshSignal;
	private readonly IMusicPlayerPollNudge _musicPlayerPollNudge;

	public VariableService(
		VariableRegistry registry,
		IUserVariableStore userStore,
		IMediator mediator,
		VariableCatalogProviders providers,
		IVariableRefreshSignal refreshSignal,
		IMusicPlayerPollNudge musicPlayerPollNudge)
	{
		_registry = registry;
		_userStore = userStore;
		_mediator = mediator;
		_providers = providers;
		_refreshSignal = refreshSignal;
		_musicPlayerPollNudge = musicPlayerPollNudge;
	}

	public Task<IReadOnlyList<VariableEntity>> GetAll() => Task.FromResult(_registry.GetAll());

	public Task<IReadOnlyList<VariableEntity>> GetByScope(VariableScope scope, string? scopeRefId)
		=> Task.FromResult(_registry.GetByScope(scope, scopeRefId));

	public Task<VariableEntity?> GetById(Guid id) => Task.FromResult(_registry.GetById(id));

	public Task<IReadOnlyList<VariableEntity>> GetByOwnerIntegration(string integrationId)
		=> Task.FromResult(_registry.GetByOwnerIntegration(integrationId));

	public Task<VariableEntity?> Resolve(string name, VariableScope contextScope, string? contextScopeRefId)
	{
		if (!VariableNameSanitizer.IsValid(name))
		{
			return Task.FromResult<VariableEntity?>(null);
		}

		if (contextScope != VariableScope.Global && contextScopeRefId is not null)
		{
			var local = _registry.FindByName(contextScope, contextScopeRefId, name);
			if (local is not null)
			{
				return Task.FromResult<VariableEntity?>(local);
			}
		}

		return Task.FromResult(_registry.FindByName(VariableScope.Global, null, name));
	}

	public Task<Result<VariableEntity, VariableError>> CreateUserVariable(
		string name,
		VariableScope scope,
		string? scopeRefId,
		VariableType type,
		object? initialValue,
		int? decimalPlaces)
		=> CreateInternal(name,
			scope,
			scopeRefId,
			type,
			initialValue,
			decimalPlaces,
			VariableClassification.User,
			null);

	public async Task<Result<VariableEntity, VariableError>> UpdateUserVariable(
		Guid id,
		string? name,
		int? decimalPlaces)
	{
		var entity = _registry.GetById(id);
		if (entity is null)
		{
			return Result.Fail<VariableEntity, VariableError>(VariableError.NotFound, "Variable not found");
		}

		if (entity.Classification != VariableClassification.User)
		{
			return Result.Fail<VariableEntity, VariableError>(VariableError.NotEditable,
				"Only user-classified variables can be renamed or have their precision changed");
		}

		return await ApplyUpdate(entity, name, null, decimalPlaces, null);
	}

	public async Task<Result<VariableEntity, VariableError>> SetValue(
		Guid id,
		object? value,
		CancellationToken cancellationToken = default)
	{
		var entity = _registry.GetById(id);
		if (entity is null)
		{
			return Result.Fail<VariableEntity, VariableError>(VariableError.NotFound, "Variable not found");
		}

		if (IsNonFinite(value))
		{
			return Result.Fail<VariableEntity, VariableError>(VariableError.InvalidValue,
				"A variable value must be a finite number");
		}

		switch (entity.Classification)
		{
			case VariableClassification.User:
				return await ApplyUpdate(entity, null, value, null, null);

			case VariableClassification.Widget:
				return Result.Fail<VariableEntity, VariableError>(VariableError.NotWritable,
					"A widget-owned variable is written by its widget, not from outside");

			default:
				return await DispatchToOwner(entity, value, cancellationToken);
		}
	}

	// The declaration is the gate, so a variable whose owner declares no write capability is refused
	// without the provider being contacted at all - that is what lets a client decide before a write
	// whether to offer an editing control.
	private async Task<Result<VariableEntity, VariableError>> DispatchToOwner(
		VariableEntity entity,
		object? value,
		CancellationToken cancellationToken)
	{
		if (entity.Write is null)
		{
			return Result.Fail<VariableEntity, VariableError>(VariableError.NotWritable,
				$"Variable '{entity.Name}' does not accept writes");
		}

		var ownerIntegrationId = entity.OwnerIntegrationId;
		if (ownerIntegrationId is null || entity.DefinitionId is null)
		{
			return Result.Fail<VariableEntity, VariableError>(VariableError.OwnerUnavailable,
				$"Variable '{entity.Name}' has no reachable owner");
		}

		var provider = _providers.ResolveOwner(ownerIntegrationId);
		if (provider is null)
		{
			return Result.Fail<VariableEntity, VariableError>(VariableError.OwnerUnavailable,
				$"Integration '{ownerIntegrationId}' is not currently available");
		}

		var result = await provider.SetValueAsync(entity.DefinitionId, value, cancellationToken)
			.ConfigureAwait(false);

		if (result.Status != VariableWriteStatus.Applied)
		{
			// Only the literal half of the provider's message: a localized one would come through as its
			// key here, and the client renders its own message for a refused write anyway.
			return Result.Fail<VariableEntity, VariableError>(ErrorOf(result.Status), result.Message.Literal);
		}

		// Applied means the provider took the value, not that the host has seen the result: the
		// authoritative value arrives on the read side, so the entity is returned un-echoed and the two
		// signals below are what pull that read forward.
		_refreshSignal.RequestRefresh(ownerIntegrationId, entity.Id);
		_musicPlayerPollNudge.NoteActionExecuted(ownerIntegrationId);

		return Result.Ok<VariableEntity, VariableError>(entity);
	}

	private static VariableError ErrorOf(VariableWriteStatus status) => status switch
	{
		VariableWriteStatus.NotWritable => VariableError.NotWritable,
		VariableWriteStatus.NotFound => VariableError.NotFound,
		VariableWriteStatus.Unavailable => VariableError.OwnerUnavailable,
		VariableWriteStatus.InvalidValue => VariableError.InvalidValue,
		_ => VariableError.WriteFailed
	};

	private static bool IsNonFinite(object? value)
		=> (value is double d && !double.IsFinite(d)) || (value is float f && !float.IsFinite(f));

	public async Task<Result<VariableError>> DeleteUserVariable(Guid id)
	{
		var entity = _registry.GetById(id);
		if (entity is null)
		{
			return Result.Fail(VariableError.NotFound, "Variable not found");
		}

		if (entity.Classification != VariableClassification.User)
		{
			return Result.Fail(VariableError.NotEditable, "Only user-classified variables can be deleted");
		}

		_registry.Remove(id);
		PersistUserVariables();
		await _mediator.Publish(new VariableDeletedNotification(entity));
		return Result.Ok<VariableError>();
	}

	public async Task<Result<VariableEntity, VariableError>> CreateIntegrationVariable(
		string integrationId,
		string name,
		VariableScope scope,
		string? scopeRefId,
		VariableType type,
		object? initialValue,
		int? decimalPlaces,
		string? definitionId = null,
		VariableDeclaration? declaration = null,
		VariableUpdateMode updateMode = VariableUpdateMode.Polled)
	{
		if (string.IsNullOrWhiteSpace(integrationId))
		{
			return Result.Fail<VariableEntity, VariableError>(VariableError.ValidationError, "integrationId required");
		}

		var canonicalName = VariableNameSanitizer.IsValid(name) ? name : VariableNameSanitizer.Sanitize(name);
		var resolvedDefinitionId = definitionId ?? MacroDeck.Sdk.Variables.VariableDefinitionId.FromName(canonicalName);

		if (resolvedDefinitionId is not null &&
			!MacroDeckId.IsValidLocalId(resolvedDefinitionId, LocalIdKind.Declared))
		{
			return Result.Fail<VariableEntity, VariableError>(VariableError.InvalidName,
				$"Variable definition id '{resolvedDefinitionId}' must be lowercase and hyphen-separated");
		}

		var reused = await TryReuseByDefinition(integrationId,
			resolvedDefinitionId,
			LocalIdKind.Declared,
			scope,
			scopeRefId,
			type,
			canonicalName,
			declaration);
		if (reused is not null)
		{
			return reused;
		}

		return await CreateInternal(name,
			scope,
			scopeRefId,
			type,
			initialValue,
			decimalPlaces,
			VariableClassification.Integration,
			integrationId,
			resolvedDefinitionId,
			declaration,
			updateMode);
	}

	public async Task<Result<VariableEntity, VariableError>> MaterializeCatalogVariable(
		string integrationId,
		string resourceId,
		string name,
		VariableType type,
		int? decimalPlaces,
		VariableDeclaration? declaration = null)
	{
		if (string.IsNullOrWhiteSpace(integrationId))
		{
			return Result.Fail<VariableEntity, VariableError>(VariableError.ValidationError, "integrationId required");
		}

		if (!MacroDeckId.IsValidLocalId(resourceId, LocalIdKind.Resource))
		{
			return Result.Fail<VariableEntity, VariableError>(VariableError.InvalidName,
				$"Catalog variable resource id '{resourceId}' is not a valid resource id");
		}

		var canonicalName = VariableNameSanitizer.IsValid(name) ? name : VariableNameSanitizer.Sanitize(name);

		var reused = await TryReuseByDefinition(integrationId,
			resourceId,
			LocalIdKind.Resource,
			VariableScope.Global,
			null,
			type,
			canonicalName,
			declaration);
		if (reused is not null)
		{
			return reused;
		}

		return await CreateInternal(canonicalName,
			VariableScope.Global,
			null,
			type,
			null,
			decimalPlaces,
			VariableClassification.Integration,
			integrationId,
			resourceId,
			declaration,
			VariableUpdateMode.Pushed);
	}

	// Shared by the eager and the on-demand path so re-registering the same qualified definition keeps
	// resolving to the same logical variable - the property a reconnecting plugin and a rehydrated binding
	// both depend on. Returns null when nothing is registered under that definition yet.
	private async Task<Result<VariableEntity, VariableError>?> TryReuseByDefinition(
		string integrationId,
		string? definitionId,
		LocalIdKind localIdKind,
		VariableScope scope,
		string? scopeRefId,
		VariableType type,
		string canonicalName,
		VariableDeclaration? declaration)
	{
		if (definitionId is null ||
			!QualifiedId.TryCreate(integrationId, definitionId, localIdKind, out var qualified) ||
			_registry.FindByDefinition(qualified) is not { } existing)
		{
			return null;
		}

		var sameIdentity = existing.Classification == VariableClassification.Integration &&
			string.Equals(existing.OwnerIntegrationId, integrationId, StringComparison.Ordinal) &&
			existing.Scope == scope &&
			existing.ScopeRefId == scopeRefId &&
			existing.Type == type &&
			string.Equals(existing.DefinitionId, definitionId, StringComparison.Ordinal);

		if (!sameIdentity)
		{
			return Result.Fail<VariableEntity, VariableError>(VariableError.AlreadyExists,
				$"Integration '{integrationId}' already declares definition id '{definitionId}' " +
				$"for variable '{existing.Name}'. Definition ids must be unique within an integration.");
		}

		// A configuration can be renamed and a provider can revise a unit or a write capability without the
		// variable's name changing, so the declaration has to be refreshed on re-registration or it stays
		// whatever it was when the host first saw the instance.
		var declarationChanged = ApplyDeclaration(existing, declaration);

		if (string.Equals(existing.Name, canonicalName, StringComparison.Ordinal))
		{
			if (declarationChanged)
			{
				await _mediator.Publish(new VariableUpdatedNotification(existing));
			}

			return Result.Ok<VariableEntity, VariableError>(existing);
		}

		var collision = _registry.FindByName(scope, scopeRefId, canonicalName);
		if (collision is not null && collision.Id != existing.Id)
		{
			return Result.Fail<VariableEntity, VariableError>(VariableError.AlreadyExists,
				$"A variable named '{canonicalName}' already exists in this scope");
		}

		if (!_registry.Rename(existing.Id, canonicalName, DateTime.UtcNow))
		{
			return Result.Fail<VariableEntity, VariableError>(VariableError.NotFound, "Variable not found");
		}

		await _mediator.Publish(new VariableUpdatedNotification(existing));
		return Result.Ok<VariableEntity, VariableError>(existing);
	}

	public Task<VariableEntity?> GetByDefinition(QualifiedId definitionId)
		=> Task.FromResult(_registry.FindByDefinition(definitionId));

	public async Task<Result<VariableEntity, VariableError>> ReportIntegrationVariableValue(
		string integrationId,
		Guid id,
		object? value,
		VariableBounds? bounds = null)
	{
		var entity = _registry.GetById(id);
		if (entity is null)
		{
			return Result.Fail<VariableEntity, VariableError>(VariableError.NotFound, "Variable not found");
		}

		if (entity.Classification != VariableClassification.Integration ||
			!string.Equals(entity.OwnerIntegrationId, integrationId, StringComparison.Ordinal))
		{
			return Result.Fail<VariableEntity, VariableError>(VariableError.NotOwnedByIntegration,
				"Variable is not owned by the calling integration");
		}

		return await ApplyUpdate(entity, null, value, null, bounds);
	}

	public async Task<Result<VariableError>> SetIntegrationVariableAvailability(string integrationId,
		Guid id,
		bool available)
	{
		var entity = _registry.GetById(id);
		if (entity is null)
		{
			return Result.Fail(VariableError.NotFound, "Variable not found");
		}

		if (entity.Classification != VariableClassification.Integration ||
			!string.Equals(entity.OwnerIntegrationId, integrationId, StringComparison.Ordinal))
		{
			return Result.Fail(VariableError.NotOwnedByIntegration, "Variable is not owned by the calling integration");
		}

		var wasAvailable = _registry.IsAvailable(id);
		_registry.SetAvailable(id, available);
		if (_registry.IsAvailable(id) != wasAvailable)
		{
			await _mediator.Publish(new VariableUpdatedNotification(entity));
			await _mediator.Publish(new VariableValueChangedNotification(entity, entity.Value));
		}

		return Result.Ok<VariableError>();
	}

	public async Task<Result<VariableError>> DeleteIntegrationVariable(string integrationId, Guid id)
	{
		var entity = _registry.GetById(id);
		if (entity is null)
		{
			return Result.Fail(VariableError.NotFound, "Variable not found");
		}

		if (entity.Classification != VariableClassification.Integration ||
			!string.Equals(entity.OwnerIntegrationId, integrationId, StringComparison.Ordinal))
		{
			return Result.Fail(VariableError.NotOwnedByIntegration, "Variable is not owned by the calling integration");
		}

		_registry.Remove(id);
		await _mediator.Publish(new VariableDeletedNotification(entity));
		return Result.Ok<VariableError>();
	}

	public async Task DeleteByScopeInstance(VariableScope scope, string scopeRefId)
	{
		var existing = _registry.GetByScope(scope, scopeRefId);
		var removedUser = false;
		foreach (var v in existing)
		{
			_registry.Remove(v.Id);
			removedUser |= v.Classification == VariableClassification.User;
		}

		if (removedUser)
		{
			PersistUserVariables();
		}

		foreach (var v in existing)
		{
			await _mediator.Publish(new VariableDeletedNotification(v));
		}
	}

	public async Task UpsertWidgetVariable(
		VariableScope scope,
		string scopeRefId,
		string name,
		VariableType type,
		object? value)
	{
		if (string.IsNullOrEmpty(scopeRefId) || !VariableNameSanitizer.IsValid(name))
		{
			return;
		}

		var serialized = VariableValueSerializer.Serialize(type, value, null);
		var existing = _registry.FindByName(scope, scopeRefId, name);
		if (existing is null)
		{
			var entity = NewEntity(name,
				scope,
				scopeRefId,
				type,
				VariableClassification.Widget,
				null,
				serialized,
				null);
			_registry.Upsert(entity);
			await _mediator.Publish(new VariableCreatedNotification(entity));
			return;
		}

		if (existing.Classification != VariableClassification.Widget || existing.Value == serialized)
		{
			return;
		}

		var prevValue = existing.Value;
		existing.Value = serialized;
		existing.UpdatedAt = DateTime.UtcNow;
		_registry.Upsert(existing);
		await _mediator.Publish(new VariableUpdatedNotification(existing));
		await _mediator.Publish(new VariableValueChangedNotification(existing, prevValue));
	}

	public async Task RemoveWidgetVariable(VariableScope scope, string scopeRefId, string name)
	{
		var existing = _registry.FindByName(scope, scopeRefId, name);
		if (existing is null || existing.Classification != VariableClassification.Widget)
		{
			return;
		}

		_registry.Remove(existing.Id);
		await _mediator.Publish(new VariableDeletedNotification(existing));
	}

	private async Task<Result<VariableEntity, VariableError>> CreateInternal(
		string name,
		VariableScope scope,
		string? scopeRefId,
		VariableType type,
		object? initialValue,
		int? decimalPlaces,
		VariableClassification classification,
		string? ownerIntegrationId,
		string? definitionId = null,
		VariableDeclaration? declaration = null,
		VariableUpdateMode updateMode = VariableUpdateMode.Polled)
	{
		var canonicalName = VariableNameSanitizer.IsValid(name) ? name : VariableNameSanitizer.Sanitize(name);
		if (!VariableNameSanitizer.IsValid(canonicalName))
		{
			return Result.Fail<VariableEntity, VariableError>(VariableError.InvalidName,
				"Name must match ^[a-z][a-z0-9_]*$");
		}

		if (scope == VariableScope.Global && scopeRefId is not null)
		{
			return Result.Fail<VariableEntity, VariableError>(VariableError.ValidationError,
				"Global variables must not have a scopeRefId");
		}

		if (scope != VariableScope.Global && string.IsNullOrEmpty(scopeRefId))
		{
			return Result.Fail<VariableEntity, VariableError>(VariableError.ValidationError,
				"Non-global variables require a scopeRefId");
		}

		if (decimalPlaces is < 0 or > 28)
		{
			return Result.Fail<VariableEntity, VariableError>(VariableError.InvalidValue,
				"decimalPlaces must be between 0 and 28");
		}

		var entity = NewEntity(canonicalName,
			scope,
			scopeRefId,
			type,
			classification,
			ownerIntegrationId,
			VariableValueSerializer.Serialize(type, initialValue, decimalPlaces),
			decimalPlaces,
			definitionId,
			declaration,
			updateMode);
		if (!_registry.TryAdd(entity))
		{
			return Result.Fail<VariableEntity, VariableError>(VariableError.AlreadyExists,
				$"A variable named '{canonicalName}' already exists in this scope");
		}

		if (classification == VariableClassification.User)
		{
			PersistUserVariables();
		}

		await _mediator.Publish(new VariableCreatedNotification(entity));
		return Result.Ok<VariableEntity, VariableError>(entity);
	}

	private async Task<Result<VariableEntity, VariableError>> ApplyUpdate(
		VariableEntity entity,
		string? name,
		object? value,
		int? decimalPlaces,
		VariableBounds? bounds)
	{
		var prevValue = entity.Value;
		var valueChanged = false;
		var metadataChanged = false;

		if (name is not null && name != entity.Name)
		{
			var canonical = VariableNameSanitizer.IsValid(name) ? name : VariableNameSanitizer.Sanitize(name);
			if (!VariableNameSanitizer.IsValid(canonical))
			{
				return Result.Fail<VariableEntity, VariableError>(VariableError.InvalidName,
					"Name must match ^[a-z][a-z0-9_]*$");
			}

			if (canonical != entity.Name)
			{
				var collision = _registry.FindByName(entity.Scope, entity.ScopeRefId, canonical);
				if (collision is not null && collision.Id != entity.Id)
				{
					return Result.Fail<VariableEntity, VariableError>(VariableError.AlreadyExists,
						$"A variable named '{canonical}' already exists in this scope");
				}

				entity.Name = canonical;
				metadataChanged = true;
			}
		}

		if (decimalPlaces.HasValue &&
			entity.Type == VariableType.Numeric &&
			entity.DecimalPlaces != decimalPlaces.Value)
		{
			if (decimalPlaces.Value is < 0 or > 28)
			{
				return Result.Fail<VariableEntity, VariableError>(VariableError.InvalidValue,
					"decimalPlaces must be between 0 and 28");
			}

			entity.DecimalPlaces = decimalPlaces.Value;
			metadataChanged = true;
		}

		if (value is not null)
		{
			var storedValue = VariableValueSerializer.Serialize(entity.Type, value, entity.DecimalPlaces);
			if (storedValue != prevValue)
			{
				entity.Value = storedValue;
				valueChanged = true;
			}
		}

		// A null VariableBounds means "not supplied": an unavailable reading must leave a range that is
		// still correct alone rather than collapsing a slider to zero width for the duration of a blip.
		if (bounds is not null &&
			(entity.Min != bounds.Min || entity.Max != bounds.Max || entity.Step != bounds.Step))
		{
			entity.Min = bounds.Min;
			entity.Max = bounds.Max;
			entity.Step = bounds.Step;
			metadataChanged = true;
		}

		var wasAvailable = _registry.IsAvailable(entity.Id);

		entity.UpdatedAt = DateTime.UtcNow;
		_registry.Upsert(entity);
		if (entity.Classification == VariableClassification.User)
		{
			PersistUserVariables();
		}

		var becameAvailable = !wasAvailable;
		if (valueChanged || metadataChanged || becameAvailable)
		{
			await _mediator.Publish(new VariableUpdatedNotification(entity));
		}

		if (valueChanged || becameAvailable)
		{
			await _mediator.Publish(new VariableValueChangedNotification(entity, prevValue));
		}

		return Result.Ok<VariableEntity, VariableError>(entity);
	}

	private void PersistUserVariables()
		=> _userStore.Save(_registry.GetAll().Where(v => v.Classification == VariableClassification.User));

	private static VariableEntity NewEntity(
		string name,
		VariableScope scope,
		string? scopeRefId,
		VariableType type,
		VariableClassification classification,
		string? ownerIntegrationId,
		string value,
		int? decimalPlaces,
		string? definitionId = null,
		VariableDeclaration? declaration = null,
		VariableUpdateMode updateMode = VariableUpdateMode.Polled)
		=> new()
		{
			Id = Guid.CreateVersion7(),
			Name = name,
			Scope = scope,
			ScopeRefId = scopeRefId,
			Type = type,
			Classification = classification,
			OwnerIntegrationId = ownerIntegrationId,
			DefinitionId = definitionId,
			Presentation = declaration?.Presentation,
			Unit = declaration?.Unit,
			SemanticKind = declaration?.SemanticKind,
			Attributes = declaration?.Attributes,
			Write = declaration?.Write,
			Value = value,
			UpdateMode = updateMode,
			DecimalPlaces = type == VariableType.Numeric ? decimalPlaces : null,
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow
		};

	// Static attributes and the write capability are re-applied on every re-registration, so a provider
	// that revises them does not have to wait for the variable to be removed and created again.
	private static bool ApplyDeclaration(VariableEntity entity, VariableDeclaration? declaration)
	{
		// Precision only when the declaration states one: CreateIntegrationVariable also passes its own
		// decimalPlaces argument, and letting a declaration without one blank that would lose it.
		var declaredPrecision = declaration?.DecimalPlaces ?? entity.DecimalPlaces;

		var changed = !Equals(entity.Presentation, declaration?.Presentation) ||
			entity.DecimalPlaces != declaredPrecision ||
			!string.Equals(entity.Unit, declaration?.Unit, StringComparison.Ordinal) ||
			!string.Equals(entity.SemanticKind, declaration?.SemanticKind, StringComparison.Ordinal) ||
			!AttributesEqual(entity.Attributes, declaration?.Attributes) ||
			!Equals(entity.Write, declaration?.Write);

		entity.Presentation = declaration?.Presentation;
		entity.DecimalPlaces = declaredPrecision;
		entity.Unit = declaration?.Unit;
		entity.SemanticKind = declaration?.SemanticKind;
		entity.Attributes = declaration?.Attributes;
		entity.Write = declaration?.Write;

		return changed;
	}

	private static bool AttributesEqual(
		IReadOnlyDictionary<string, string>? left,
		IReadOnlyDictionary<string, string>? right)
	{
		if (ReferenceEquals(left, right))
		{
			return true;
		}

		if (left is null || right is null || left.Count != right.Count)
		{
			return false;
		}

		foreach (var entry in left)
		{
			if (!right.TryGetValue(entry.Key, out var other) ||
				!string.Equals(entry.Value, other, StringComparison.Ordinal))
			{
				return false;
			}
		}

		return true;
	}
}
