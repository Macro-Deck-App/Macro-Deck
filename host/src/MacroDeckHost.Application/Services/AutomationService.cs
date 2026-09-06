using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Mediator;

namespace MacroDeckHost.Application.Services;

public class AutomationService : IAutomationService
{
	private const int MaxNameLength = 100;
	private const int MaxDescriptionLength = 500;

	private readonly IAutomationCache _automationCache;
	private readonly IMediator _mediator;

	public AutomationService(IAutomationCache automationCache, IMediator mediator)
	{
		_automationCache = automationCache;
		_mediator = mediator;
	}

	public IReadOnlyList<AutomationEntity> GetAll() =>
		_automationCache.GetAll()
			.OrderBy(automation => automation.Name, StringComparer.CurrentCultureIgnoreCase)
			.ToList();

	public AutomationEntity? GetById(Guid id) => _automationCache.GetById(id);

	public async Task<Result<AutomationEntity, AutomationError>> Create(
		string name,
		string? description,
		string? flows,
		bool enabled = true)
	{
		var validation = Validate(name, description);
		if (validation is not null)
		{
			return validation;
		}

		var now = DateTime.UtcNow;
		var automation = new AutomationEntity
		{
			Id = Guid.NewGuid(),
			Name = name.Trim(),
			Description = description?.Trim() ?? string.Empty,
			Enabled = enabled,
			Flows = flows ?? string.Empty,
			CreatedAt = now,
			UpdatedAt = now
		};

		await _automationCache.AddOrUpdate(automation);
		await _mediator.Publish(new AutomationCreatedNotification(automation));

		return Result.Ok<AutomationEntity, AutomationError>(automation);
	}

	public async Task<Result<AutomationEntity, AutomationError>> Update(
		Guid id,
		string? name,
		string? description,
		string? flows,
		bool? enabled)
	{
		var automation = _automationCache.GetById(id);
		if (automation is null)
		{
			return Result.Fail<AutomationEntity, AutomationError>(AutomationError.NotFound, "Automation not found");
		}

		if (name is not null)
		{
			var validation = Validate(name, null);
			if (validation is not null)
			{
				return validation;
			}

			automation.Name = name.Trim();
		}

		if (description is not null)
		{
			var validation = Validate(null, description);
			if (validation is not null)
			{
				return validation;
			}

			automation.Description = description.Trim();
		}

		if (flows is not null)
		{
			automation.Flows = flows;
		}

		if (enabled is not null)
		{
			automation.Enabled = enabled.Value;
		}

		automation.UpdatedAt = DateTime.UtcNow;

		await _automationCache.AddOrUpdate(automation);
		await _mediator.Publish(new AutomationUpdatedNotification(automation));

		return Result.Ok<AutomationEntity, AutomationError>(automation);
	}

	public async Task<Result<AutomationEntity, AutomationError>> Duplicate(Guid id)
	{
		var source = _automationCache.GetById(id);
		if (source is null)
		{
			return Result.Fail<AutomationEntity, AutomationError>(AutomationError.NotFound, "Automation not found");
		}

		return await Create(NextCopyName(source.Name), source.Description, source.Flows, enabled: false);
	}

	public async Task<Result<AutomationError>> Delete(Guid id)
	{
		var automation = _automationCache.GetById(id);
		if (automation is null)
		{
			return Result.Fail(AutomationError.NotFound, "Automation not found");
		}

		await _automationCache.Remove(id);
		await _mediator.Publish(new AutomationDeletedNotification(id));

		return Result.Ok<AutomationError>();
	}

	private static Result<AutomationEntity, AutomationError>? Validate(string? name, string? description)
	{
		if (name is not null)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return Result.Fail<AutomationEntity, AutomationError>(AutomationError.ValidationError,
					"Name is required");
			}

			if (name.Trim().Length > MaxNameLength)
			{
				return Result.Fail<AutomationEntity, AutomationError>(AutomationError.ValidationError,
					$"Name cannot be longer than {MaxNameLength} characters");
			}
		}

		if (description is not null && description.Length > MaxDescriptionLength)
		{
			return Result.Fail<AutomationEntity, AutomationError>(AutomationError.ValidationError,
				$"Description cannot be longer than {MaxDescriptionLength} characters");
		}

		return null;
	}

	private string NextCopyName(string sourceName)
	{
		var baseName = $"{sourceName} (copy)";
		if (baseName.Length > MaxNameLength)
		{
			baseName = baseName[..MaxNameLength];
		}

		var taken = _automationCache.GetAll()
			.Select(automation => automation.Name)
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
