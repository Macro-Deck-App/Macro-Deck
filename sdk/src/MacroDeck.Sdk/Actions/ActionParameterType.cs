namespace MacroDeck.Sdk.Actions;

#pragma warning disable CA1720 // Teil des JSON-Protokolls – umbenennen würde Breaking Change erzeugen
public enum ActionParameterType
{
	String,
	Number,
	Boolean,
	Password,
	Secret,
	Choice,
	DynamicChoice,
	Autocomplete,
	MultiSelect,
	Color,
	File,
	Folder,
	Hotkey,
	Duration,
	DateTime,
	Json,
	Code,
	KeyValue,
	Object,
	Array,
	IpAddress,
	Url,
	Icon,
	Image,
	KeyboardSequence,
	KeyboardCombo,
	WidgetTarget
}
#pragma warning restore CA1720
