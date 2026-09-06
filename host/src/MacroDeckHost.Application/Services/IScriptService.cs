using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Services;

public interface IScriptService
{
	IReadOnlyList<ScriptEntity> GetAll();

	ScriptEntity? GetById(Guid id);

	Task<Result<ScriptEntity, ScriptError>> Create(
		string name,
		string? description,
		string? flows,
		Guid? id = null,
		IReadOnlyList<ScriptInput>? inputs = null,
		bool runsOnWidget = false);

	Task<Result<ScriptEntity, ScriptError>> Update(
		Guid id,
		string? name,
		string? description,
		string? flows,
		IReadOnlyList<ScriptInput>? inputs = null,
		bool? runsOnWidget = null);

	Task<Result<ScriptEntity, ScriptError>> Duplicate(Guid id);

	Task<Result<ScriptError>> Delete(Guid id);
}
