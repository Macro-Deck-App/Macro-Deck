using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Actions;

[JsonConverter(typeof(JsonStringEnumConverter))]
#pragma warning disable CA1720 // Part of the JSON protocol - renaming would be a breaking change
public enum ActionParameterType
{
	String = 0,
	Number = 1,
	Boolean = 2,
	Choice = 3,
	Password = 4,
	Secret = 5,
	DynamicChoice = 6,
	Autocomplete = 7,
	MultiSelect = 8,
	Color = 9,
	File = 10,
	Folder = 11,
	Hotkey = 12,
	Duration = 13,
	DateTime = 14,
	Json = 15,
	Code = 16,
	KeyValue = 17,
	Object = 18,
	Array = 19,
	IpAddress = 20,
	Url = 21,
	Icon = 22,
	Image = 23,
	KeyboardSequence = 24,
	KeyboardCombo = 25,
	WidgetTarget = 26,
}
#pragma warning restore CA1720
