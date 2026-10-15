using System.Globalization;
using MacroDeckHost.Application.Scripts;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables.Colors;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;
using MacroDeck.Sdk.Variables;
using Microsoft.Extensions.DependencyInjection;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;
using SdkVariableType = MacroDeck.Sdk.Variables.VariableType;

namespace MacroDeckHost.Application.Variables;

public sealed class UserVariableWriter : IUserVariableApi, IDisposable
{
	private readonly IServiceScopeFactory _scopeFactory;

	// Resolve, compute and write are one critical section so two flow steps running at the same time
	// cannot both read the same value and lose one of the increments. This only covers writes through
	// this API - the UI and integration write paths are not serialized against it. A write dispatched to a
	// provider deliberately leaves the gate first: the provider may call straight back into ApplyAsync,
	// and this semaphore is process-wide, so holding it across provider code would deadlock the host.
	private readonly SemaphoreSlim _gate = new(1, 1);

	public UserVariableWriter(IServiceScopeFactory scopeFactory)
	{
		_scopeFactory = scopeFactory;
	}

	public void Dispose()
	{
		_gate.Dispose();
	}

	public Task<UserVariableWriteResult> ApplyAsync(
		string name,
		string? ownerWidgetId,
		UserVariableOperation operation,
		string? value,
		CancellationToken cancellationToken = default)
	{
		return ApplyInContextAsync(name, new UserVariableContext(ownerWidgetId), operation, value, cancellationToken);
	}

	public async Task<UserVariableWriteResult> ApplyInContextAsync(
		string name,
		UserVariableContext context,
		UserVariableOperation operation,
		string? value,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);

		var canonical = VariableNameSanitizer.IsValid(name) ? name : VariableNameSanitizer.Sanitize(name);
		if (!VariableNameSanitizer.IsValid(canonical))
		{
			return UserVariableWriteResult.Failed(UserVariableWriteStatus.NotFound,
				$"'{name}' is not a variable name.");
		}

		// Reads inside the run return the input, so writing the shadowed global would make the two
		// disagree for the rest of the run. Refuse instead of writing something nobody can observe.
		if (ScriptInputScope.IsDeclared(canonical))
		{
			return UserVariableWriteResult.Failed(UserVariableWriteStatus.NotEditable,
				$"'{canonical}' is a script input and cannot be assigned.");
		}

		// IVariableService is scoped; a singleton must not capture one.
		await using var scope = _scopeFactory.CreateAsyncScope();
		var service = scope.ServiceProvider.GetRequiredService<IVariableService>();

		Guid ownerDispatchId;
		object? ownerDispatchValue;
		string? ownerIntegrationId;

		await _gate.WaitAsync(cancellationToken);
		try
		{
			var entity = await service.Resolve(canonical, VariableScopeChain.For(context));
			if (entity is null)
			{
				return UserVariableWriteResult.Failed(UserVariableWriteStatus.NotFound,
					$"No variable named '{canonical}' exists.");
			}

			// Add/Toggle/Append compute from the value the host last saw. For a variable whose owner is
			// not the host that is a reading the owner may already have moved on from, so an increment
			// against one would silently be lost - Set is the only operation that survives the trip.
			if (entity.Classification != VariableClassification.User && operation != UserVariableOperation.Set)
			{
				return UserVariableWriteResult.Failed(UserVariableWriteStatus.NotEditable,
					$"'{canonical}' is managed by the app and only accepts being set.");
			}

			var computed = Compute(entity, operation, value);
			if (!computed.Success)
			{
				return UserVariableWriteResult.Failed(UserVariableWriteStatus.InvalidValue, computed.Message!);
			}

			if (entity.Classification == VariableClassification.User)
			{
				var update = await service.SetValue(entity.Id, computed.Value, cancellationToken);
				if (update.Error is VariableError.FileReadOnly or VariableError.TemplateReadOnly)
				{
					return UserVariableWriteResult.Failed(UserVariableWriteStatus.NotEditable,
						await ActiveLocalization.Resolve(scope.ServiceProvider,
							AppStrings.Integrations.Variables.Errors.VariableReadOnly()));
				}

				return update.Success
					? UserVariableWriteResult.Applied()
					: await Failed(scope.ServiceProvider, update.Error, update.ErrorMessage, entity.OwnerIntegrationId);
			}

			ownerDispatchId = entity.Id;
			ownerDispatchValue = computed.Value;
			ownerIntegrationId = entity.OwnerIntegrationId;
		}
		finally
		{
			_gate.Release();
		}

		var dispatched = await service.SetValue(ownerDispatchId, ownerDispatchValue, cancellationToken);
		return dispatched.Success
			? UserVariableWriteResult.Applied()
			: await Failed(scope.ServiceProvider, dispatched.Error, dispatched.ErrorMessage, ownerIntegrationId);
	}

	private static async Task<UserVariableWriteResult> Failed(
		IServiceProvider services,
		VariableError? error,
		string? message,
		string? ownerIntegrationId)
	{
		if (error is VariableError.OwnerUnavailable)
		{
			return UserVariableWriteResult.Failed(UserVariableWriteStatus.Unavailable,
				await OwnerUnavailableMessage(services, ownerIntegrationId));
		}

		return UserVariableWriteResult.Failed(error switch
			{
				VariableError.NotWritable => UserVariableWriteStatus.NotEditable,
				VariableError.NotEditable => UserVariableWriteStatus.NotEditable,
				VariableError.NotFound => UserVariableWriteStatus.NotFound,
				_ => UserVariableWriteStatus.InvalidValue
			},
			message ?? error?.ToString() ?? "The variable could not be written.");
	}

	private static async Task<string> OwnerUnavailableMessage(IServiceProvider services, string? ownerIntegrationId)
	{
		if (string.IsNullOrEmpty(ownerIntegrationId))
		{
			return await ActiveLocalization.Resolve(services, AppStrings.Errors.Variables.WriteFailed());
		}

		var name = await services.GetRequiredService<VariableOwnerNames>().Resolve(ownerIntegrationId);
		return await ActiveLocalization.Resolve(services, AppStrings.Errors.Variables.OwnerUnavailable(name));
	}

	public Task<UserVariableCreateResult> CreateAsync(
		string name,
		string? ownerWidgetId,
		SdkVariableType type,
		string? initialValue = null,
		int? decimalPlaces = null,
		CancellationToken cancellationToken = default)
	{
		return CreateInContextAsync(name,
			new UserVariableContext(ownerWidgetId),
			type,
			initialValue,
			decimalPlaces,
			cancellationToken);
	}

	public async Task<UserVariableCreateResult> CreateInContextAsync(
		string name,
		UserVariableContext context,
		SdkVariableType type,
		string? initialValue = null,
		int? decimalPlaces = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);

		var canonical = VariableNameSanitizer.IsValid(name) ? name : VariableNameSanitizer.Sanitize(name);
		if (!VariableNameSanitizer.IsValid(canonical))
		{
			return UserVariableCreateResult.Failed(UserVariableCreateStatus.InvalidName,
				$"'{name}' is not a variable name.");
		}

		var domainType = SdkVariableTypeMapper.ToDomain(type);
		var parsed = ParseInitialValue(domainType, initialValue);
		if (!parsed.Success)
		{
			return UserVariableCreateResult.Failed(UserVariableCreateStatus.InvalidValue, parsed.Message!);
		}

		await _gate.WaitAsync(cancellationToken);
		try
		{
			await using var scope = _scopeFactory.CreateAsyncScope();

			// The caller supplies the owner id, so an unchecked one would let a plugin create a variable
			// under a widget, script or automation that does not exist - or under an id it simply guessed.
			var target = CreateTarget(scope.ServiceProvider, context);
			if (target.Refused is { } refused)
			{
				return refused;
			}

			var service = scope.ServiceProvider.GetRequiredService<IVariableService>();

			var result = await service.CreateUserVariable(canonical,
				target.Scope,
				target.ScopeRefId,
				domainType,
				parsed.Value,
				decimalPlaces);

			return result.Success
				? UserVariableCreateResult.Created()
				: UserVariableCreateResult.Failed(CreateStatusOf(result.Error),
					result.ErrorMessage ?? "The variable could not be created.");
		}
		finally
		{
			_gate.Release();
		}
	}

	private static CreateTargetResult CreateTarget(IServiceProvider services, UserVariableContext context)
	{
		if (!string.IsNullOrWhiteSpace(context.ScriptId))
		{
			return Guid.TryParse(context.ScriptId, out var scriptId)
				&& services.GetRequiredService<IScriptService>().GetById(scriptId) is not null
					? new CreateTargetResult(VariableScope.Script, scriptId.ToString(), null)
					: CreateTargetResult.Refuse(UserVariableCreateStatus.UnknownScript,
						$"No script with id '{context.ScriptId}' exists.");
		}

		if (!string.IsNullOrWhiteSpace(context.AutomationId))
		{
			return Guid.TryParse(context.AutomationId, out var automationId)
				&& services.GetRequiredService<IAutomationService>().GetById(automationId) is not null
					? new CreateTargetResult(VariableScope.Automation, automationId.ToString(), null)
					: CreateTargetResult.Refuse(UserVariableCreateStatus.UnknownAutomation,
						$"No automation with id '{context.AutomationId}' exists.");
		}

		if (!string.IsNullOrWhiteSpace(context.OwnerWidgetId))
		{
			return services.GetRequiredService<IWidgetAppearanceService>().Exists(context.OwnerWidgetId)
				? new CreateTargetResult(VariableScope.Widget, context.OwnerWidgetId, null)
				: CreateTargetResult.Refuse(UserVariableCreateStatus.UnknownWidget,
					$"No widget with id '{context.OwnerWidgetId}' exists.");
		}

		return new CreateTargetResult(VariableScope.Global, null, null);
	}

	private readonly record struct CreateTargetResult(
		VariableScope Scope,
		string? ScopeRefId,
		UserVariableCreateResult? Refused)
	{
		public static CreateTargetResult Refuse(UserVariableCreateStatus status, string message)
		{
			return new CreateTargetResult(VariableScope.Global, null, UserVariableCreateResult.Failed(status, message));
		}
	}

	private static UserVariableCreateStatus CreateStatusOf(VariableError? error)
	{
		return error switch
		{
			VariableError.AlreadyExists => UserVariableCreateStatus.AlreadyExists,
			VariableError.InvalidName => UserVariableCreateStatus.InvalidName,
			_ => UserVariableCreateStatus.InvalidValue
		};
	}

	private static ComputedValue ParseInitialValue(DomainVariableType type, string? initialValue)
	{
		// The initial value is optional, so "not given" means the type's own default rather than a value
		// that fails to parse.
		if (initialValue is null)
		{
			return type switch
			{
				DomainVariableType.Numeric => ComputedValue.Ok(0m),
				DomainVariableType.Boolean => ComputedValue.Ok(false),
				DomainVariableType.Color => ComputedValue.Ok(RgbaColor.Black.ToString()),
				_ => ComputedValue.Ok(string.Empty)
			};
		}

		var text = initialValue;

		return type switch
		{
			DomainVariableType.Numeric => TryParseDecimal(text, out var number)
				? ComputedValue.Ok(number)
				: ComputedValue.Invalid($"'{text}' is not a number."),
			DomainVariableType.Boolean => TryParseBoolean(text, out var flag)
				? ComputedValue.Ok(flag)
				: ComputedValue.Invalid($"'{text}' is not true or false."),
			DomainVariableType.Color => ParseColor(text),
			_ => ComputedValue.Ok(text)
		};
	}

	private static ComputedValue ParseColor(string text)
	{
		return RgbaColor.Canonicalize(text) is { } color
			? ComputedValue.Ok(color)
			: ComputedValue.Invalid($"'{text}' is not a color.");
	}

	private static ComputedValue Compute(VariableEntity entity, UserVariableOperation operation, string? value)
	{
		switch (operation)
		{
			case UserVariableOperation.Set:
			{
				var text = value ?? string.Empty;
				return entity.Type switch
				{
					DomainVariableType.Numeric => TryParseDecimal(text, out var number)
						? ComputedValue.Ok(number)
						: ComputedValue.Invalid($"'{text}' is not a number."),
					DomainVariableType.Boolean => TryParseBoolean(text, out var flag)
						? ComputedValue.Ok(flag)
						: ComputedValue.Invalid($"'{text}' is not true or false."),
					DomainVariableType.Color => ParseColor(text),
					_ => ComputedValue.Ok(text)
				};
			}

			case UserVariableOperation.Add:
			{
				if (entity.Type != DomainVariableType.Numeric)
				{
					return ComputedValue.Invalid("Add only applies to a number variable.");
				}

				if (!TryParseDecimal(value, out var operand))
				{
					return ComputedValue.Invalid($"'{value}' is not a number.");
				}

				var current = VariableValueSerializer.Deserialize(entity.Type, entity.Value) as decimal? ?? 0m;
				return ComputedValue.Ok(current + operand);
			}

			case UserVariableOperation.Toggle:
			{
				if (entity.Type != DomainVariableType.Boolean)
				{
					return ComputedValue.Invalid("Toggle only applies to a true/false variable.");
				}

				var current = VariableValueSerializer.Deserialize(entity.Type, entity.Value) as bool? ?? false;
				return ComputedValue.Ok(!current);
			}

			case UserVariableOperation.Append:
			{
				if (entity.Type != DomainVariableType.Text)
				{
					return ComputedValue.Invalid("Append only applies to a text variable.");
				}

				return ComputedValue.Ok(entity.Value + (value ?? string.Empty));
			}

			default:
				return ComputedValue.Invalid($"Unknown operation '{operation}'.");
		}
	}

	private static bool TryParseDecimal(string? value, out decimal parsed)
	{
		return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed);
	}

	// Deliberately stricter than VariableValueSerializer, which reads anything that is not "true"/"1"
	// as false - so a typo would be stored as a value the user never asked for.
	private static bool TryParseBoolean(string value, out bool parsed)
	{
		switch (value.Trim().ToLowerInvariant())
		{
			case "true":
			case "1":
				parsed = true;
				return true;
			case "false":
			case "0":
				parsed = false;
				return true;
			default:
				parsed = false;
				return false;
		}
	}

	private readonly record struct ComputedValue(bool Success, object? Value, string? Message)
	{
		public static ComputedValue Ok(object value)
		{
			return new ComputedValue(true, value, null);
		}

		public static ComputedValue Invalid(string message)
		{
			return new ComputedValue(false, null, message);
		}
	}
}
