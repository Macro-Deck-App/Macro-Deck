namespace MacroDeckHost.Application.Devices.Surfaces.KeyImages;

internal sealed class DeviceKeyImagePresses
{
	private readonly TimeSpan _minVisible;
	private readonly Dictionary<string, PressLook> _looks = new(StringComparer.Ordinal);

	public DeviceKeyImagePresses(TimeSpan minVisible)
	{
		_minVisible = minVisible;
	}

	public DateTimeOffset? NextExpiry => _looks.Values
		.Where(look => !look.Held)
		.Select(look => look.ShownAt + _minVisible)
		.Min();

	public void Press(string widgetId, DateTimeOffset now)
	{
		if (_looks.TryGetValue(widgetId, out var look) && !Expired(look, now))
		{
			look.Held = true;
			return;
		}

		_looks[widgetId] = new PressLook { Held = true };
	}

	public bool Release(string widgetId)
	{
		if (!_looks.TryGetValue(widgetId, out var look))
		{
			return false;
		}

		look.Held = false;
		return true;
	}

	public bool IsPressed(string widgetId, DateTimeOffset now)
	{
		if (!_looks.TryGetValue(widgetId, out var look))
		{
			return false;
		}

		if (!Expired(look, now))
		{
			return true;
		}

		_looks.Remove(widgetId);
		return false;
	}

	public void Shown(IEnumerable<string> widgetIds, DateTimeOffset now)
	{
		foreach (var widgetId in widgetIds)
		{
			if (_looks.TryGetValue(widgetId, out var look))
			{
				look.ShownAt ??= now;
			}
		}
	}

	public void Retain(IReadOnlySet<string> widgetIds)
	{
		foreach (var widgetId in _looks.Keys.Where(widgetId => !widgetIds.Contains(widgetId)).ToList())
		{
			_looks.Remove(widgetId);
		}
	}

	public void Clear()
	{
		_looks.Clear();
	}

	private bool Expired(PressLook look, DateTimeOffset now)
	{
		return !look.Held && look.ShownAt is { } shown && now >= shown + _minVisible;
	}

	private sealed class PressLook
	{
		public bool Held { get; set; }

		public DateTimeOffset? ShownAt { get; set; }
	}
}
