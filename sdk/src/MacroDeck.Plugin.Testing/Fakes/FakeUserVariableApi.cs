using System.Globalization;
using MacroDeck.Sdk.Variables;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// In-memory <see cref="IUserVariableApi" /> - the user's variables, reached through the same
/// apply-and-report contract the real host uses so a plugin under test has to handle
/// <see cref="UserVariableWriteStatus" /> honestly instead of assuming every write lands. A fake that
/// always returned <c>Applied()</c> would let a plugin ship without ever handling
/// <see cref="UserVariableWriteStatus.NotFound" />, which is exactly the bug this fake exists to catch.
///
/// <para>
/// Seed variables with <see cref="SeedGlobal" /> and <see cref="SeedWidgetScoped" /> before exercising
/// the plugin under test, then use <see cref="GetCurrentValue" /> to see what a write actually did.
/// Resolution mirrors <see cref="IUserVariableApi.ApplyAsync" />'s own contract: a widget-scoped
/// variable wins over a global one of the same name, and a <c>null</c> owner resolves globally only.
/// </para>
///
/// <para>
/// A leading <c>vars.</c> on the name is stripped before lookup, matching the interface's own contract
/// that the prefix is accepted. Nothing else about host name canonicalization is reproduced - seed and
/// apply using the same spelling.
/// </para>
/// </summary>
public sealed class FakeUserVariableApi : IUserVariableApi
{
	private readonly Lock _gate = new();
	private readonly Dictionary<string, VariableRecord> _global = new(StringComparer.Ordinal);
	private readonly Dictionary<(string WidgetId, string Name), VariableRecord> _widgetScoped = new();
	private readonly HashSet<string> _widgets = new(StringComparer.Ordinal);
	private readonly Func<string, bool>? _widgetExists;

	/// <summary>Creates a fake that knows only the widgets seeded on it.</summary>
	public FakeUserVariableApi()
	{
	}

	/// <summary>
	/// Creates a fake that also asks <paramref name="widgetExists" /> whether a widget exists, so a
	/// context can defer to its widget fake instead of making a test seed the same widget twice.
	/// </summary>
	public FakeUserVariableApi(Func<string, bool> widgetExists)
	{
		_widgetExists = widgetExists;
	}

	/// <summary>
	/// Declares a widget as existing, so <see cref="CreateAsync" /> will scope a variable to it. Seeding a
	/// widget-scoped variable declares its widget too.
	/// </summary>
	public void SeedWidget(string widgetId)
	{
		lock (_gate)
		{
			_widgets.Add(widgetId);
		}
	}

	/// <summary>Seeds (or replaces) a global variable.</summary>
	public void SeedGlobal(string name, VariableType type, object? value, bool ownedByIntegration = false)
	{
		lock (_gate)
		{
			_global[name] = new VariableRecord(type, value, ownedByIntegration);
		}
	}

	/// <summary>
	/// Seeds (or replaces) a variable scoped to one widget, which shadows a global variable of the same
	/// name for that widget.
	/// </summary>
	public void SeedWidgetScoped(
		string widgetId,
		string name,
		VariableType type,
		object? value,
		bool ownedByIntegration = false)
	{
		lock (_gate)
		{
			_widgets.Add(widgetId);
			_widgetScoped[(widgetId, name)] = new VariableRecord(type, value, ownedByIntegration);
		}
	}

	/// <summary>
	/// The current value of a seeded variable, resolved the same way <see cref="ApplyAsync" /> resolves
	/// it. Returns <c>null</c> both when nothing by that name is visible and when the value itself is
	/// <c>null</c> - use <see cref="SeedGlobal" />/<see cref="SeedWidgetScoped" />'s return to know which,
	/// if it matters to the assertion.
	/// </summary>
	public object? GetCurrentValue(string name, string? ownerWidgetId = null)
	{
		lock (_gate)
		{
			return Resolve(name, ownerWidgetId)?.Value;
		}
	}

	/// <summary>
	/// Resolves <paramref name="name" /> in scope and applies <paramref name="operation" />, reproducing
	/// the real host's status contract: <see cref="UserVariableWriteStatus.NotFound" /> when nothing
	/// resolves, <see cref="UserVariableWriteStatus.NotEditable" /> for a variable seeded with
	/// <c>ownedByIntegration: true</c>, and <see cref="UserVariableWriteStatus.InvalidValue" /> when the
	/// operation does not apply to the variable's type or the operand does not parse as it.
	/// </summary>
	public Task<UserVariableWriteResult> ApplyAsync(
		string name,
		string? ownerWidgetId,
		UserVariableOperation operation,
		string? value,
		CancellationToken cancellationToken = default)
	{
		lock (_gate)
		{
			var record = Resolve(name, ownerWidgetId);
			if (record is null)
			{
				return Task.FromResult(UserVariableWriteResult.Failed(UserVariableWriteStatus.NotFound,
					$"No variable named '{name}' is visible here."));
			}

			if (record.OwnedByIntegration)
			{
				return Task.FromResult(UserVariableWriteResult.Failed(UserVariableWriteStatus.NotEditable,
					$"'{name}' is owned by an integration and cannot be edited."));
			}

			var result = operation switch
			{
				UserVariableOperation.Set => ApplySet(record, value),
				UserVariableOperation.Add => ApplyAdd(record, value),
				UserVariableOperation.Toggle => ApplyToggle(record),
				UserVariableOperation.Append => ApplyAppend(record, value),
				_ => Invalid($"Unknown operation '{operation}'.")
			};

			return Task.FromResult(result);
		}
	}

	// Must be called under _gate. Checks the widget scope first, per ApplyAsync's own documented
	// contract for ownerWidgetId, then falls back to global; a null owner never consults widget scope.
	/// <summary>
	/// Creates a variable, reproducing the real host's refusals rather than always succeeding:
	/// <see cref="UserVariableCreateStatus.UnknownWidget" /> for a widget this fake has not been told
	/// about, <see cref="UserVariableCreateStatus.AlreadyExists" /> for a name already taken in the same
	/// scope, and <see cref="UserVariableCreateStatus.InvalidValue" /> when the initial value does not
	/// parse as the declared type.
	/// </summary>
	public Task<UserVariableCreateResult> CreateAsync(
		string name,
		string? ownerWidgetId,
		VariableType type,
		string? initialValue = null,
		int? decimalPlaces = null,
		CancellationToken cancellationToken = default)
	{
		var canonical = Canonical(name);
		if (canonical.Length == 0)
		{
			return Task.FromResult(UserVariableCreateResult.Failed(UserVariableCreateStatus.InvalidName,
				$"'{name}' is not a variable name."));
		}

		if (!TryParseInitial(type, initialValue, out var value))
		{
			return Task.FromResult(UserVariableCreateResult.Failed(UserVariableCreateStatus.InvalidValue,
				$"'{initialValue}' is not a {type}."));
		}

		lock (_gate)
		{
			if (string.IsNullOrWhiteSpace(ownerWidgetId))
			{
				if (!_global.TryAdd(canonical, new VariableRecord(type, value, false)))
				{
					return Task.FromResult(UserVariableCreateResult.Failed(UserVariableCreateStatus.AlreadyExists,
						$"'{canonical}' already exists."));
				}

				return Task.FromResult(UserVariableCreateResult.Created());
			}

			if (!_widgets.Contains(ownerWidgetId) && _widgetExists?.Invoke(ownerWidgetId) != true)
			{
				return Task.FromResult(UserVariableCreateResult.Failed(UserVariableCreateStatus.UnknownWidget,
					$"No widget with id '{ownerWidgetId}' exists."));
			}

			if (!_widgetScoped.TryAdd((ownerWidgetId, canonical), new VariableRecord(type, value, false)))
			{
				return Task.FromResult(UserVariableCreateResult.Failed(UserVariableCreateStatus.AlreadyExists,
					$"'{canonical}' already exists on widget '{ownerWidgetId}'."));
			}

			return Task.FromResult(UserVariableCreateResult.Created());
		}
	}

	private static bool TryParseInitial(VariableType type, string? initialValue, out object? value)
	{
		if (initialValue is null)
		{
			value = type switch
			{
				VariableType.Numeric => 0m,
				VariableType.Boolean => false,
				_ => string.Empty
			};
			return true;
		}

		switch (type)
		{
			case VariableType.Numeric:
				var isNumber = decimal.TryParse(initialValue,
					NumberStyles.Number,
					CultureInfo.InvariantCulture,
					out var number);
				value = isNumber ? number : null;
				return isNumber;

			case VariableType.Boolean:
				var isBoolean = bool.TryParse(initialValue, out var flag);
				value = isBoolean ? flag : null;
				return isBoolean;

			default:
				value = initialValue;
				return true;
		}
	}

	private static string Canonical(string name)
		=> name.StartsWith("vars.", StringComparison.Ordinal) ? name["vars.".Length..] : name;

	private VariableRecord? Resolve(string name, string? ownerWidgetId)
	{
		var canonical = Canonical(name);

		if (ownerWidgetId is not null && _widgetScoped.TryGetValue((ownerWidgetId, canonical), out var scoped))
		{
			return scoped;
		}

		return _global.GetValueOrDefault(canonical);
	}

	private static UserVariableWriteResult ApplySet(VariableRecord record, string? value)
	{
		var text = value ?? string.Empty;

		switch (record.Type)
		{
			case VariableType.Numeric:
				if (!TryParseDecimal(text, out var number))
				{
					return Invalid($"'{text}' is not a number.");
				}

				record.Value = number;
				break;

			case VariableType.Boolean:
				if (!TryParseBoolean(text, out var flag))
				{
					return Invalid($"'{text}' is not true or false.");
				}

				record.Value = flag;
				break;

			default:
				record.Value = text;
				break;
		}

		return UserVariableWriteResult.Applied();
	}

	private static UserVariableWriteResult ApplyAdd(VariableRecord record, string? value)
	{
		if (record.Type != VariableType.Numeric)
		{
			return Invalid("Add only applies to a numeric variable.");
		}

		if (!TryParseDecimal(value, out var operand))
		{
			return Invalid($"'{value}' is not a number.");
		}

		record.Value = ToDecimalOrZero(record.Value) + operand;
		return UserVariableWriteResult.Applied();
	}

	private static UserVariableWriteResult ApplyToggle(VariableRecord record)
	{
		if (record.Type != VariableType.Boolean)
		{
			return Invalid("Toggle only applies to a boolean variable.");
		}

		record.Value = !ToBoolOrFalse(record.Value);
		return UserVariableWriteResult.Applied();
	}

	private static UserVariableWriteResult ApplyAppend(VariableRecord record, string? value)
	{
		if (record.Type != VariableType.Text)
		{
			return Invalid("Append only applies to a text variable.");
		}

		record.Value = (record.Value as string ?? string.Empty) + (value ?? string.Empty);
		return UserVariableWriteResult.Applied();
	}

	private static UserVariableWriteResult Invalid(string message)
		=> UserVariableWriteResult.Failed(UserVariableWriteStatus.InvalidValue, message);

	private static bool TryParseDecimal(string? text, out decimal parsed)
		=> decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed);

	// Deliberately stricter than a loose bool parse, so a typo is rejected rather than silently
	// stored as false - mirrors the real host's own reasoning for the same choice.
	private static bool TryParseBoolean(string? text, out bool parsed)
	{
		switch (text?.Trim().ToLowerInvariant())
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

	private static decimal ToDecimalOrZero(object? value)
	{
		if (value is decimal existing)
		{
			return existing;
		}

		try
		{
			return value is null ? 0m : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
		}
		catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
		{
			return 0m;
		}
	}

	private static bool ToBoolOrFalse(object? value) => value switch
	{
		bool flag => flag,
		string text => TryParseBoolean(text, out var parsed) && parsed,
		_ => false
	};

	// Mutable so an apply can update Value in place under _gate without a second dictionary write.
	private sealed class VariableRecord(VariableType type, object? value, bool ownedByIntegration)
	{
		public VariableType Type { get; } = type;

		public object? Value { get; set; } = value;

		public bool OwnedByIntegration { get; } = ownedByIntegration;
	}
}
