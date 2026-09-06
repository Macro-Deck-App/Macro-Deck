using System.Globalization;
using MacroDeckHost.Application.Scripts;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
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

	public void Dispose() => _gate.Dispose();

	public async Task<UserVariableWriteResult> ApplyAsync(
		string name,
		string? ownerWidgetId,
		UserVariableOperation operation,
		string? value,
		CancellationToken cancellationToken = default)
	{
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

		await _gate.WaitAsync(cancellationToken);
		try
		{
			var entity = await Resolve(service, canonical, ownerWidgetId);
			if (entity is null)
			{
				return UserVariableWriteResult.Failed(UserVariableWriteStatus.NotFound,
					$"No variable named '{canonical}' exists.");
			}

			// Add/Toggle/Append compute from the value the host last saw. For a variable whose owner is
			// not the host that is a reading the owner may already have moved on from, so an increment
			// against one would silently be lost - Set is the only operation that survives the trip.
			if (entity.Classification != VariableClassification.User &&
				operation != UserVariableOperation.Set)
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
				return update.Success
					? UserVariableWriteResult.Applied()
					: Failed(update.Error, update.ErrorMessage);
			}

			ownerDispatchId = entity.Id;
			ownerDispatchValue = computed.Value;
		}
		finally
		{
			_gate.Release();
		}

		var dispatched = await service.SetValue(ownerDispatchId, ownerDispatchValue, cancellationToken);
		return dispatched.Success
			? UserVariableWriteResult.Applied()
			: Failed(dispatched.Error, dispatched.ErrorMessage);
	}

	private static UserVariableWriteResult Failed(VariableError? error, string? message)
		=> UserVariableWriteResult.Failed(error switch
			{
				VariableError.NotWritable => UserVariableWriteStatus.NotEditable,
				VariableError.NotEditable => UserVariableWriteStatus.NotEditable,
				VariableError.OwnerUnavailable => UserVariableWriteStatus.Unavailable,
				VariableError.NotFound => UserVariableWriteStatus.NotFound,
				_ => UserVariableWriteStatus.InvalidValue
			},
			message ?? error?.ToString() ?? "The variable could not be written.");

	public async Task<UserVariableCreateResult> CreateAsync(
		string name,
		string? ownerWidgetId,
		SdkVariableType type,
		string? initialValue = null,
		int? decimalPlaces = null,
		CancellationToken cancellationToken = default)
	{
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

		var widgetScoped = !string.IsNullOrWhiteSpace(ownerWidgetId);

		await _gate.WaitAsync(cancellationToken);
		try
		{
			await using var scope = _scopeFactory.CreateAsyncScope();

			// The caller supplies the widget id, so an unchecked one would let a plugin create a variable
			// under a widget that does not exist - or under an id it simply guessed.
			if (widgetScoped &&
				!scope.ServiceProvider.GetRequiredService<IWidgetAppearanceService>().Exists(ownerWidgetId!))
			{
				return UserVariableCreateResult.Failed(UserVariableCreateStatus.UnknownWidget,
					$"No widget with id '{ownerWidgetId}' exists.");
			}

			var service = scope.ServiceProvider.GetRequiredService<IVariableService>();

			var result = await service.CreateUserVariable(canonical,
				widgetScoped ? VariableScope.Widget : VariableScope.Global,
				widgetScoped ? ownerWidgetId : null,
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

	private static UserVariableCreateStatus CreateStatusOf(VariableError? error) => error switch
	{
		VariableError.AlreadyExists => UserVariableCreateStatus.AlreadyExists,
		VariableError.InvalidName => UserVariableCreateStatus.InvalidName,
		_ => UserVariableCreateStatus.InvalidValue
	};

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
			_ => ComputedValue.Ok(text)
		};
	}

	private static Task<VariableEntity?> Resolve(IVariableService service, string name, string? ownerWidgetId)
		=> string.IsNullOrWhiteSpace(ownerWidgetId)
			? service.Resolve(name, VariableScope.Global, null)
			: service.Resolve(name, VariableScope.Widget, ownerWidgetId);

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
		=> decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed);

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
		public static ComputedValue Ok(object value) => new(true, value, null);

		public static ComputedValue Invalid(string message) => new(false, null, message);
	}
}
