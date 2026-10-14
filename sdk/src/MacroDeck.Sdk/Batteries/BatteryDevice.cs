namespace MacroDeck.Sdk.Batteries;

/// <summary>
/// One battery-powered device as its provider reports it. Every property is checked when it is set, so
/// an instance that exists is always publishable.
/// </summary>
public sealed record BatteryDevice
{
	private readonly string _id = string.Empty;
	private readonly string _name = string.Empty;
	private readonly string _kind = BatteryDeviceKinds.Other;
	private readonly int? _levelPercent;
	private readonly TimeSpan? _timeRemaining;
	private readonly TimeSpan? _timeToFull;

	/// <summary>
	/// The provider's own stable id for the device. Publishing the same id again updates that device
	/// rather than adding another. Never put a hardware serial or other identifier the user would not want
	/// shared here: every plugin can read it.
	/// </summary>
	/// <exception cref="ArgumentException">Empty, whitespace or longer than <see cref="BatteryLimits.MaxIdLength" />.</exception>
	public required string Id
	{
		get => _id;
		init => _id = Checked(value, BatteryLimits.MaxIdLength, nameof(Id));
	}

	/// <summary>The name shown for the device.</summary>
	/// <exception cref="ArgumentException">Empty, whitespace or longer than <see cref="BatteryLimits.MaxNameLength" />.</exception>
	public required string Name
	{
		get => _name;
		init => _name = Checked(value, BatteryLimits.MaxNameLength, nameof(Name));
	}

	/// <summary>One of <see cref="BatteryDeviceKinds" />. Defaults to <see cref="BatteryDeviceKinds.Other" />.</summary>
	/// <exception cref="ArgumentException">Empty, whitespace or longer than <see cref="BatteryLimits.MaxKindLength" />.</exception>
	public string Kind
	{
		get => _kind;
		init => _kind = Checked(value, BatteryLimits.MaxKindLength, nameof(Kind));
	}

	/// <summary>Charge level from 0 to 100, or <c>null</c> when the device does not report one.</summary>
	/// <exception cref="ArgumentOutOfRangeException">Outside 0..100.</exception>
	public int? LevelPercent
	{
		get => _levelPercent;
		init
		{
			if (value is < 0 or > 100)
			{
				throw new ArgumentOutOfRangeException(nameof(LevelPercent),
					value,
					"A battery level is between 0 and 100.");
			}

			_levelPercent = value;
		}
	}

	public BatteryChargeState ChargeState { get; init; } = BatteryChargeState.Unknown;

	/// <summary>
	/// Whether the device is reachable right now. A provider keeps a device it lost contact with published
	/// as not connected, so readers keep its name, and removes it only once it is gone for good.
	/// </summary>
	public bool IsConnected { get; init; } = true;

	/// <summary>Estimated time until empty, when the device reports one.</summary>
	/// <exception cref="ArgumentOutOfRangeException">Negative.</exception>
	public TimeSpan? TimeRemaining
	{
		get => _timeRemaining;
		init => _timeRemaining = NotNegative(value, nameof(TimeRemaining));
	}

	/// <summary>Estimated time until fully charged, when the device reports one.</summary>
	/// <exception cref="ArgumentOutOfRangeException">Negative.</exception>
	public TimeSpan? TimeToFull
	{
		get => _timeToFull;
		init => _timeToFull = NotNegative(value, nameof(TimeToFull));
	}

	public BatteryHealth Health { get; init; } = BatteryHealth.Unknown;

	private static string Checked(string value, int maxLength, string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
		if (value.Length > maxLength)
		{
			throw new ArgumentException($"{name} is longer than {maxLength} characters.", name);
		}

		return value;
	}

	private static TimeSpan? NotNegative(TimeSpan? value, string name)
	{
		if (value < TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(name, value, "A battery time estimate cannot be negative.");
		}

		return value;
	}
}
