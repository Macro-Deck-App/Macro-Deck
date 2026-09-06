using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeck.Sdk.Identity;

namespace MacroDeckHost.Application.Services;

public interface IVariableService
{
	Task<IReadOnlyList<VariableEntity>> GetAll();

	Task<IReadOnlyList<VariableEntity>> GetByScope(VariableScope scope, string? scopeRefId);

	Task<VariableEntity?> GetById(Guid id);

	Task<VariableEntity?> Resolve(string name, VariableScope contextScope, string? contextScopeRefId);

	Task<Result<VariableEntity, VariableError>> CreateUserVariable(
		string name,
		VariableScope scope,
		string? scopeRefId,
		VariableType type,
		object? initialValue,
		int? decimalPlaces);

	/// <summary>
	/// The one write path. Dispatches on the entity's owner: a user variable is stored, a widget variable
	/// is refused, and an integration variable is handed to the owning provider's write capability.
	/// A variable whose owner declares no write capability is refused here without the provider being
	/// contacted at all.
	/// </summary>
	/// <remarks>
	/// An owner-dispatched write that succeeds returns the entity <b>un-echoed</b>: the provider applying a
	/// value does not mean the host has seen the result, so the authoritative value arrives on the read
	/// side and callers must not treat the returned entity's value as the value they wrote.
	/// </remarks>
	Task<Result<VariableEntity, VariableError>> SetValue(
		Guid id,
		object? value,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Edits a user variable's definition - its name and its numeric precision. The value goes through
	/// <see cref="SetValue"/> instead.
	/// </summary>
	Task<Result<VariableEntity, VariableError>> UpdateUserVariable(Guid id, string? name, int? decimalPlaces);

	Task<Result<VariableError>> DeleteUserVariable(Guid id);

	Task<Result<VariableEntity, VariableError>> CreateIntegrationVariable(
		string integrationId,
		string name,
		VariableScope scope,
		string? scopeRefId,
		VariableType type,
		object? initialValue,
		int? decimalPlaces,
		string? definitionId = null,
		VariableDeclaration? declaration = null,
		VariableUpdateMode updateMode = VariableUpdateMode.Polled);

	/// <summary>
	/// Registers the variable a catalog binding stands for. Unlike
	/// <see cref="CreateIntegrationVariable"/> the definition id is a resource id rather than a declared
	/// one, and the variable is pushed rather than polled, so the freshness watchdog does not apply.
	/// Re-materializing the same resource resolves back to the same variable, which is what lets a
	/// binding survive a restart and a provider reconnect.
	/// </summary>
	Task<Result<VariableEntity, VariableError>> MaterializeCatalogVariable(
		string integrationId,
		string resourceId,
		string name,
		VariableType type,
		int? decimalPlaces,
		VariableDeclaration? declaration = null);

	Task<VariableEntity?> GetByDefinition(QualifiedId definitionId);

	/// <summary>
	/// Records what the owning integration just read. <paramref name="bounds"/> is three-state: null leaves
	/// the variable's current range alone, an instance whose fields are all null clears it.
	/// </summary>
	Task<Result<VariableEntity, VariableError>> ReportIntegrationVariableValue(
		string integrationId,
		Guid id,
		object? value,
		VariableBounds? bounds = null);

	Task<Result<VariableError>> SetIntegrationVariableAvailability(string integrationId, Guid id, bool available);

	Task<Result<VariableError>> DeleteIntegrationVariable(string integrationId, Guid id);

	Task<IReadOnlyList<VariableEntity>> GetByOwnerIntegration(string integrationId);

	Task DeleteByScopeInstance(VariableScope scope, string scopeRefId);

	Task UpsertWidgetVariable(VariableScope scope, string scopeRefId, string name, VariableType type, object? value);

	Task RemoveWidgetVariable(VariableScope scope, string scopeRefId, string name);
}
