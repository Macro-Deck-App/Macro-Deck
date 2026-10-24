using System.Text;

namespace MacroDeckHost.Integrations.AutoHotkey.Providers;

internal sealed class OutputRing
{
	public const int DefaultCapacity = 64 * 1024;
	public const int ViewLength = 16 * 1024;

	private readonly Lock _gate = new();
	private readonly StringBuilder _text = new();
	private readonly int _capacity;
	private long _version;

	public OutputRing(int capacity = DefaultCapacity)
	{
		_capacity = capacity;
	}

	public long Version => Interlocked.Read(ref _version);

	public void Append(string text)
	{
		if (text.Length == 0)
		{
			return;
		}

		lock (_gate)
		{
			if (text.Length >= _capacity)
			{
				_text.Clear().Append(text, text.Length - _capacity, _capacity);
			}
			else
			{
				_text.Append(text);
				if (_text.Length > _capacity + _capacity / 2)
				{
					_text.Remove(0, _text.Length - _capacity);
				}
			}

			Interlocked.Increment(ref _version);
		}
	}

	public string Newest(int length = ViewLength)
	{
		lock (_gate)
		{
			var start = Math.Max(0, _text.Length - Math.Min(length, _capacity));
			if (start > 0 && char.IsLowSurrogate(_text[start]))
			{
				start++;
			}

			return _text.ToString(start, _text.Length - start);
		}
	}

	public string All()
	{
		return Newest(_capacity);
	}
}
