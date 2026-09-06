using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Variables;

/// <summary>
/// Binds a provider catalog resource to a materialized variable, and keeps the binding and the variable
/// in sync afterwards. See <see cref="MacroDeck.Sdk.Variables.IVariableProvider"/> for the resource model
/// this operates over.
/// </summary>
public interface IVariableBindingService
{
	/// <summary>
	/// Binds a resource and returns the variable it materialized. The variable, not the binding record, is
	/// what every caller goes on to use - it is what the UI shows, what a template references, and what
	/// <see cref="UnbindAsync"/> takes back.
	/// </summary>
	Task<Result<VariableEntity, VariableBindingError>> BindAsync(
		string integrationId,
		string localResourceId,
		string? requestedName,
		VariableType? typeOverride,
		CancellationToken cancellationToken = default);

	Task<Result<VariableBindingError>> UnbindAsync(Guid variableId,
		CancellationToken cancellationToken = default);

	Task<Result<VariableBindingError>> RenameAsync(
		Guid variableId,
		string name,
		CancellationToken cancellationToken = default);

	IReadOnlyList<VariableBinding> GetBindings();

	VariableBinding? FindByVariableId(Guid variableId);
}
