using System.Text;
using MacroDeckHost.Application.Devices.Surfaces.KeyImages;

namespace MacroDeckHost.Tests.UnitTests.Devices.Surfaces;

internal sealed class FakeKeyImageRenderer : IDeviceKeyImageRenderer
{
	public const string ContentType = "text/plain";

	private readonly Lock _gate = new();
	private readonly List<DeviceKeyImageRenderRequest> _requests = [];

	public event EventHandler? FontsChanged;

	public HashSet<string> TickingTypes { get; } = new(StringComparer.Ordinal);

	public IReadOnlyList<DeviceKeyImageRenderRequest> Requests
	{
		get
		{
			lock (_gate)
			{
				return [.. _requests];
			}
		}
	}

	public static string Describe(DeviceKeyImageRenderRequest request, int rowOffset = 0, int columnOffset = 0)
	{
		var builder = new StringBuilder()
			.Append(request.Kind)
			.Append('|')
			.Append(request.WidgetType)
			.Append('|')
			.Append(request.Tree?.GetProperty("root").GetRawText())
			.Append('|')
			.Append(request.WidgetData?.GetRawText())
			.Append('|')
			.Append(request.BackgroundColor)
			.Append('|')
			.Append(request.Appearance.ThemeMode)
			.Append('|')
			.Append(request.Culture)
			.Append('|')
			.Append(rowOffset)
			.Append(',')
			.Append(columnOffset);
		return builder.ToString();
	}

	public DeviceKeyImageRenderResult Render(DeviceKeyImageRenderRequest request)
	{
		lock (_gate)
		{
			_requests.Add(request);
		}

		var ticking = request.WidgetType is { } type && TickingTypes.Contains(type);
		var second = request.Now.ToUnixTimeSeconds();
		var keys = new List<DeviceKeyImagePicture>();
		for (var row = 0; row < request.Rows; row++)
		{
			for (var column = 0; column < request.Columns; column++)
			{
				var text = Describe(request, row, column) + (ticking ? $"|{second}" : string.Empty);
				keys.Add(new DeviceKeyImagePicture(row, column, Encoding.UTF8.GetBytes(text), ContentType));
			}
		}

		return new DeviceKeyImageRenderResult(keys,
			ticking ? DateTimeOffset.FromUnixTimeSeconds(second + 1) : null,
			[]);
	}

	public void RaiseFontsChanged()
	{
		FontsChanged?.Invoke(this, EventArgs.Empty);
	}
}
