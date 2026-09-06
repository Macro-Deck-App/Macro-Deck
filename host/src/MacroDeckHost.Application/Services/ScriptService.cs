using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Mediator;

namespace MacroDeckHost.Application.Services;

public class ScriptService : IScriptService
{
	private const int MaxNameLength = 100;
	private const int MaxDescriptionLength = 500;

	private readonly IScriptCache _scriptCache;
	private readonly IMediator _mediator;

	public ScriptService(IScriptCache scriptCache, IMediator mediator)
	{
		_scriptCache = scriptCache;
		_mediator = mediator;
	}

	public IReadOnlyList<ScriptEntity> GetAll() =>
		_scriptCache.GetAll()
			.OrderBy(script => script.Name, StringComparer.CurrentCultureIgnoreCase)
			.ToList();

	public ScriptEntity? GetById(Guid id) => _scriptCache.GetById(id);

	public async Task<Result<ScriptEntity, ScriptError>> Create(
		string name,
		string? description,
		string? flows,
		Guid? id = null,
		IReadOnlyList<ScriptInput>? inputs = null,
		bool runsOnWidget = false)
	{
		var validation = ValidateName(name);
		if (validation is not null)
		{
			return validation;
		}

		if (description is not null && description.Length > MaxDescriptionLength)
		{
			return Result.Fail<ScriptEntity, ScriptError>(ScriptError.ValidationError,
				$"Description cannot be longer than {MaxDescriptionLength} characters");
		}

		var inputValidation = ValidateInputs(inputs);
		if (inputValidation is not null)
		{
			return inputValidation;
		}

		var now = DateTime.UtcNow;
		var script = new ScriptEntity
		{
			Id = id ?? Guid.NewGuid(),
			Name = name.Trim(),
			Description = description?.Trim() ?? string.Empty,
			Flows = flows ?? string.Empty,
			Inputs = inputs is null ? [] : [.. inputs],
			RunsOnWidget = runsOnWidget,
			CreatedAt = now,
			UpdatedAt = now
		};

		await _scriptCache.AddOrUpdate(script);
		await _mediator.Publish(new ScriptCreatedNotification(script));

		return Result.Ok<ScriptEntity, ScriptError>(script);
	}

	public async Task<Result<ScriptEntity, ScriptError>> Update(
		Guid id,
		string? name,
		string? description,
		string? flows,
		IReadOnlyList<ScriptInput>? inputs = null,
		bool? runsOnWidget = null)
	{
		var script = _scriptCache.GetById(id);
		if (script is null)
		{
			return Result.Fail<ScriptEntity, ScriptError>(ScriptError.NotFound, "Script not found");
		}

		// Before anything is written onto the cached entity: a rejected update must leave the stored
		// script exactly as it was, and the cache hands out the live instance.
		var inputValidation = ValidateInputs(inputs);
		if (inputValidation is not null)
		{
			return inputValidation;
		}

		if (name is not null)
		{
			var validation = ValidateName(name);
			if (validation is not null)
			{
				return validation;
			}

			script.Name = name.Trim();
		}

		if (description is not null)
		{
			if (description.Length > MaxDescriptionLength)
			{
				return Result.Fail<ScriptEntity, ScriptError>(ScriptError.ValidationError,
					$"Description cannot be longer than {MaxDescriptionLength} characters");
			}

			script.Description = description.Trim();
		}

		if (flows is not null)
		{
			script.Flows = flows;
		}

		if (inputs is not null)
		{
			script.Inputs = [.. inputs];
		}

		if (runsOnWidget is not null)
		{
			script.RunsOnWidget = runsOnWidget.Value;
		}

		script.UpdatedAt = DateTime.UtcNow;

		await _scriptCache.AddOrUpdate(script);
		await _mediator.Publish(new ScriptUpdatedNotification(script));

		return Result.Ok<ScriptEntity, ScriptError>(script);
	}

	public async Task<Result<ScriptEntity, ScriptError>> Duplicate(Guid id)
	{
		var source = _scriptCache.GetById(id);
		if (source is null)
		{
			return Result.Fail<ScriptEntity, ScriptError>(ScriptError.NotFound, "Script not found");
		}

		return await Create(NextCopyName(source.Name),
			source.Description,
			source.Flows,
			null,
			source.Inputs,
			source.RunsOnWidget);
	}

	public async Task<Result<ScriptError>> Delete(Guid id)
	{
		var script = _scriptCache.GetById(id);
		if (script is null)
		{
			return Result.Fail(ScriptError.NotFound, "Script not found");
		}

		await _scriptCache.Remove(id);
		await _mediator.Publish(new ScriptDeletedNotification(id));

		return Result.Ok<ScriptError>();
	}

	private static Result<ScriptEntity, ScriptError>? ValidateInputs(IReadOnlyList<ScriptInput>? inputs)
	{
		if (inputs is null)
		{
			return null;
		}

		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var input in inputs)
		{
			// The name is what the script reads under vars., so it has to satisfy the same rule a variable
			// name does - anything else would be unreadable from a template.
			if (!VariableNameSanitizer.IsValid(input.Name))
			{
				return Result.Fail<ScriptEntity, ScriptError>(ScriptError.ValidationError,
					$"'{input.Name}' is not a valid input name");
			}

			if (!seen.Add(input.Name))
			{
				return Result.Fail<ScriptEntity, ScriptError>(ScriptError.ValidationError,
					$"The input '{input.Name}' is declared more than once");
			}
		}

		return null;
	}

	private static Result<ScriptEntity, ScriptError>? ValidateName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return Result.Fail<ScriptEntity, ScriptError>(ScriptError.ValidationError, "Name is required");
		}

		return name.Trim().Length > MaxNameLength
			? Result.Fail<ScriptEntity, ScriptError>(ScriptError.ValidationError,
				$"Name cannot be longer than {MaxNameLength} characters")
			: null;
	}

	private string NextCopyName(string sourceName)
	{
		var baseName = $"{sourceName} (copy)";
		if (baseName.Length > MaxNameLength)
		{
			baseName = baseName[..MaxNameLength];
		}

		var taken = _scriptCache.GetAll()
			.Select(script => script.Name)
			.ToHashSet(StringComparer.CurrentCultureIgnoreCase);

		if (!taken.Contains(baseName))
		{
			return baseName;
		}

		for (var suffix = 2;; suffix++)
		{
			var candidate = $"{baseName} {suffix}";
			if (!taken.Contains(candidate))
			{
				return candidate.Length > MaxNameLength ? candidate[..MaxNameLength] : candidate;
			}
		}
	}
}
