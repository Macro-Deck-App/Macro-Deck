namespace MacroDeck.Ui.Components;

/// <summary>The layout axes a <see cref="UiStack" /> understands.</summary>
public static class UiComponentDirections
{
	/// <summary>Children are laid out top to bottom.</summary>
	public const string Vertical = "vertical";

	/// <summary>Children are laid out leading to trailing.</summary>
	public const string Horizontal = "horizontal";

	/// <summary>The directions this profile ships.</summary>
	public static readonly IReadOnlyList<string> WellKnown = [Vertical, Horizontal];
}

/// <summary>How a <see cref="UiStack" /> distributes free space along its main axis.</summary>
public static class UiComponentJustify
{
	/// <summary>Children sit at the start of the axis.</summary>
	public const string Start = "start";

	/// <summary>Children sit centred on the axis.</summary>
	public const string Center = "center";

	/// <summary>Children sit at the end of the axis.</summary>
	public const string End = "end";

	/// <summary>Free space is distributed between children, none at the edges.</summary>
	public const string SpaceBetween = "space-between";

	/// <summary>The values this profile ships.</summary>
	public static readonly IReadOnlyList<string> WellKnown = [Start, Center, End, SpaceBetween];
}

/// <summary>How a <see cref="UiStack" /> aligns children on its cross axis, and how a
/// <see cref="UiTextRun" /> aligns inside its own box.</summary>
public static class UiComponentAlignments
{
	/// <summary>Aligned to the leading edge.</summary>
	public const string Start = "start";

	/// <summary>Centred.</summary>
	public const string Center = "center";

	/// <summary>Aligned to the trailing edge.</summary>
	public const string End = "end";

	/// <summary>Stretched to the container's cross extent. Meaningless on a text.</summary>
	public const string Stretch = "stretch";

	/// <summary>
	/// Aligned so the children's text sits on one line, rather than so their boxes line up. Only a
	/// <see cref="UiStack" /> understands it, and only children that draw text take part - anything
	/// else in the row aligns to the trailing edge, which is what its box shares with a baseline.
	///
	/// <para>
	/// This is what a value and its unit need and <see cref="End" /> cannot give them: two runs at
	/// different sizes have different descenders, so aligning their boxes leaves the smaller run sitting
	/// visibly low.
	/// </para>
	/// </summary>
	public const string Baseline = "baseline";

	/// <summary>The values this profile ships.</summary>
	public static readonly IReadOnlyList<string> WellKnown = [Start, Center, End, Stretch, Baseline];
}

/// <summary>
/// The semantic colours a <see cref="UiTextRun" /> can carry. A role rather than a literal colour, so
/// the reader resolves it against its own active theme and a theme change repaints without the producer
/// being involved.
/// </summary>
public static class UiComponentTextRoles
{
	/// <summary>The most prominent text colour.</summary>
	public const string Primary = "primary";

	/// <summary>A subdued text colour, for supporting detail.</summary>
	public const string Secondary = "secondary";

	/// <summary>The least prominent text colour, for incidental detail.</summary>
	public const string Muted = "muted";

	/// <summary>The roles this profile ships.</summary>
	public static readonly IReadOnlyList<string> WellKnown = [Primary, Secondary, Muted];
}

/// <summary>The font weights a <see cref="UiTextRun" /> can carry.</summary>
public static class UiComponentTextWeights
{
	/// <summary>Normal weight.</summary>
	public const string Regular = "regular";

	/// <summary>Slightly heavier than <see cref="Regular" />.</summary>
	public const string Medium = "medium";

	/// <summary>Heavier than <see cref="Medium" />, lighter than <see cref="Bold" />.</summary>
	public const string SemiBold = "semibold";

	/// <summary>The heaviest weight this profile ships.</summary>
	public const string Bold = "bold";

	/// <summary>The weights this profile ships.</summary>
	public static readonly IReadOnlyList<string> WellKnown = [Regular, Medium, SemiBold, Bold];
}

/// <summary>
/// The derivations a <see cref="UiDynamicText" /> can show of a
/// <see cref="Model.Widgets.UiTimeReference" />.
///
/// <para>
/// <b>A closed list with stated results, not a format string.</b> Handing a reader a CLDR pattern or a
/// <c>strftime</c> string would make the contract the union of every platform's formatter, and two
/// readers would disagree wherever their pattern languages do. Each value below instead names what the
/// run must end up showing, which a browser and a mobile runtime can both satisfy with their own
/// formatter.
/// </para>
///
/// <para>
/// <b>The reader's language decides the shape unless the producer names another one.</b> Under
/// <see cref="Time" /> and <see cref="Date" /> the hour cycle, separators, digit system, writing
/// direction and the position of the day period all come from the reader's own locale data. That locale
/// data moves - a reader's idea of a language's hour cycle can change with its CLDR version - so
/// conformance for those two means "formatted the way this reader formats that language", never a fixed
/// string.
/// </para>
///
/// <para>
/// The <c>time-*</c> and <c>date-*</c> values below exist because a deck is a fixed display its owner
/// arranges, not a document each reader localises: a clock configured as a 12-hour face has to stay one
/// on every device it is shown on. Each of them still names a whole result rather than handing the
/// reader a pattern, and the reader still supplies the digit system and the writing direction. What a
/// producer must <i>never</i> do is compose a time out of several runs - the profile deliberately offers
/// no way to, since assembling hour-minute, seconds and day period as three nodes renders
/// <c>9:40:32오후</c> in Korean and reverses in right-to-left languages.
/// </para>
///
/// <para>
/// <b>Everything but <see cref="Time" />, <see cref="Date" /> and <see cref="ZoneName" /> needs
/// component version 2.</b> Negotiation catches an unknown node <i>type</i>, never an unknown property
/// <i>value</i>, so a reader that predates one of these draws an empty run rather than degrading. A
/// producer using one therefore sets <see cref="Dsl.UiElement.RequiredComponentVersion" /> to 2 on that
/// node and gives it a <see cref="Dsl.UiElement.Fallback" /> spelled with <see cref="Time" /> or
/// <see cref="Date" />, so an older reader shows the locale-default clock instead of a blank one.
/// </para>
/// </summary>
public static class UiTimeFormats
{
	/// <summary>
	/// The clock time of the referenced instant, formatted in the reader's own language, including the
	/// seconds exactly when <see cref="UiComponentProperties.Seconds" /> is set. The hour cycle follows the
	/// user's app-wide time format preference (12-hour, 24-hour or the operating system's) when the host
	/// supplies one, and the reader's language otherwise; the pinned <c>time-*</c> formats are unaffected.
	///
	/// <para>
	/// <b>The seconds are drawn differently, and that is normative</b> - the properties do not say so,
	/// and a reader that ignores it draws a visibly different clock. The seconds, together with the
	/// separator in front of them, are drawn at <c>0.55</c> of the run's own
	/// <see cref="UiComponentProperties.Size" /> and in <see cref="UiComponentTextRoles.Muted" />; everything
	/// else in the run uses the node's own size and role. Digits are drawn with equal advance width, so
	/// the run does not shift sideways as it counts.
	/// </para>
	/// </summary>
	public const string Time = "time";

	/// <summary>The referenced instant's date as an abbreviated weekday, a day of the month and an
	/// abbreviated month, ordered and punctuated by the reader's own language.</summary>
	public const string Date = "date";

	/// <summary>
	/// A display name for the reference's zone: the last segment of the IANA id with underscores
	/// replaced by spaces, so <c>America/New_York</c> reads <c>New York</c>. Empty when the reference
	/// carries no zone, so a caption bound to it disappears rather than showing the reader's own zone
	/// back to them.
	/// </summary>
	public const string ZoneName = "zone-name";

	/// <summary>The referenced instant's offset from UTC at that instant, written <c>UTC+02:00</c>,
	/// <c>UTC-05:00</c> or <c>UTC+00:00</c> - a sign, two hour digits, a colon and two minute digits, so
	/// a zone whose offset is not a whole hour reads correctly. Daylight saving is part of the instant,
	/// not of the zone, so the same reference reads differently in January and July. Empty when the
	/// reference carries no zone, for the reason <see cref="ZoneName" /> is.</summary>
	public const string ZoneOffset = "zone-offset";

	/// <summary>The clock time on a 12-hour face, with the day period the reader's language writes and in
	/// the position that language puts it. The hour runs <c>1</c> to <c>12</c> with no leading zero.
	/// Drawn like <see cref="Time" /> in every other respect, seconds included.</summary>
	public const string Time12Hour = "time-12h";

	/// <summary>As <see cref="Time12Hour" />, with the hour padded to two digits - <c>01</c> to
	/// <c>12</c>.</summary>
	public const string Time12HourPadded = "time-12h-padded";

	/// <summary>The clock time on a 24-hour face, with no day period, and the hour padded to two digits -
	/// <c>00</c> to <c>23</c>. Drawn like <see cref="Time" /> in every other respect, seconds
	/// included.</summary>
	public const string Time24Hour = "time-24h";

	/// <summary>As <see cref="Time24Hour" />, with the hour unpadded - <c>0</c> to <c>23</c>.</summary>
	public const string Time24HourUnpadded = "time-24h-unpadded";

	/// <summary>The date as day, month and two-digit year in that order, each separated by <c>/</c> and
	/// each padded to two digits: the last day of 2025 reads <c>31/12/25</c>. The field order and the
	/// separator are the producer's, which is the whole point of the value; the digit system and the
	/// writing direction remain the reader's.</summary>
	public const string DateDayFirst = "date-day-first";

	/// <summary>As <see cref="DateDayFirst" /> with the month first - <c>12/31/25</c>.</summary>
	public const string DateMonthFirst = "date-month-first";

	/// <summary>The date as an ISO 8601 calendar date - a four-digit year, a two-digit month and a
	/// two-digit day joined by <c>-</c>, so the last day of 2025 reads <c>2025-12-31</c>.</summary>
	public const string DateIso = "date-iso";

	/// <summary>The date written out: the full weekday name, the day of the month and the full month
	/// name, ordered and punctuated by the reader's own language. The names are locale text, so unlike
	/// the numeric dates this one's shape stays the reader's.</summary>
	public const string DateLong = "date-long";

	/// <summary>The formats this profile ships. A reader draws nothing for one it does not know, rather
	/// than failing the tree - which is why everything past <see cref="ZoneName" /> is negotiated at
	/// component version 2 instead.</summary>
	public static readonly IReadOnlyList<string> WellKnown =
	[
		Time, Date, ZoneName, ZoneOffset,
		Time12Hour, Time12HourPadded, Time24Hour, Time24HourUnpadded,
		DateDayFirst, DateMonthFirst, DateIso, DateLong,
	];
}

/// <summary>
/// The derivations a <see cref="UiProgressText" /> can show of a
/// <see cref="Model.Widgets.UiProgressReference" />.
///
/// <para>
/// <b>A closed list with stated results</b>, for the reason <see cref="UiTimeFormats" /> gives. Each
/// value names what the run must end up showing; the reader's own locale data decides the digit system and
/// the writing direction, which is why a producer must not compose one of these out of a number and a
/// separator itself.
/// </para>
///
/// <para>
/// <b>Every one of them is a duration, not a clock time</b>, so none of them takes a zone and none of them
/// is drawn the way <see cref="UiTimeFormats.Time" /> is: a duration has no day period and no hour
/// cycle to choose between. A reader draws hours only when the duration reaches one, so a three-minute
/// track reads <c>3:07</c> rather than <c>0:03:07</c>, and pads every segment below the leading one to two
/// digits. Digits are drawn with equal advance width, so the run does not shift sideways as it counts.
/// </para>
/// </summary>
public static class UiProgressFormats
{
	/// <summary>How far the position has advanced, drawn as a duration.</summary>
	public const string Elapsed = "elapsed";

	/// <summary>How much is left - the whole length less the position. Empty when the reference carries no
	/// length, so a caption bound to it disappears rather than counting down from nothing.</summary>
	public const string Remaining = "remaining";

	/// <summary>The whole length, which does not advance. Empty when the reference carries none.</summary>
	public const string Duration = "duration";

	/// <summary>The formats this profile ships. A reader draws nothing for one it does not know, rather than
	/// failing the tree.</summary>
	public static readonly IReadOnlyList<string> WellKnown = [Elapsed, Remaining, Duration];
}

/// <summary>
/// How a reader draws a change of <see cref="UiComponentProperties.Source" />.
///
/// <para>
/// There is deliberately no value spelling "replace it immediately": absence already spells it, and it is
/// what a reader that has never heard of this property does anyway.
/// </para>
/// </summary>
public static class UiComponentImageTransitions
{
	/// <summary>
	/// The outgoing artwork keeps being drawn until the incoming artwork has decoded, then the incoming one
	/// fades in over the top of it and the outgoing one is dropped.
	///
	/// <para>
	/// <b>The timing below is normative</b>, for the reason <see cref="UiRangeBar" />'s geometry is:
	/// none of it follows from the properties, and two readers that disagree on it animate visibly
	/// differently. The incoming artwork is revealed over <c>220 ms</c> on an ease-out curve, from fully
	/// transparent and scaled to <c>1.02</c> about its own centre, to fully opaque at its natural scale.
	/// </para>
	///
	/// <para>
	/// Artwork that <i>fails</i> to decode replaces the outgoing artwork at once, with no fade: holding the
	/// previous artwork would attribute it to whatever the element now stands for. A source that changes
	/// again while a fade is running abandons that fade rather than queueing behind it.
	/// </para>
	/// </summary>
	public const string Crossfade = "crossfade";

	/// <summary>The transitions this profile ships. A reader replaces the artwork immediately for one it
	/// does not know, rather than failing the tree.</summary>
	public static readonly IReadOnlyList<string> WellKnown = [Crossfade];
}

/// <summary>
/// How round a button draws its own corners.
///
/// <para>
/// <b>Absence is the profile's own rule</b>, and there is deliberately no value spelling it: a button
/// rounds itself by <c>0.12</c> of its own height, so a button half the height of another is half as
/// round - which is what keeps a small one from reading as a pill and a large one as a square. A button
/// that is the whole widget tree is the standing exception and takes the tile's corner without saying so,
/// because a face that does not follow the tile it fills is the first thing a reader notices.
/// </para>
///
/// <para>
/// <b>An older reader ignores this key and paints its own corner</b>, which still draws the button. That
/// is the harmless failure, and the reason this is a property rather than a component version: a
/// <c>Fallback</c> subtree would trade a slightly wrong corner for a different picture entirely.
/// </para>
/// </summary>
public static class UiComponentButtonCorners
{
	/// <summary>
	/// The corner of the tile the button is drawn in - the reader's own widget corner radius, the same one
	/// a button that fills the whole tree already takes.
	///
	/// <para>
	/// What a full-bleed backdrop needs. A button laid out to the edges of a tile is that tile's face, and
	/// a corner taken from its own height instead cuts an arc across the tile at whatever radius the
	/// height happens to give - visible as soon as the two disagree, and unfixable from the producer's
	/// side, since a view does not know the height it will be drawn at.
	/// </para>
	/// </summary>
	public const string Tile = "tile";

	/// <summary>The corners this profile ships. A reader falls back to its own rule for one it does not
	/// know, rather than failing the tree.</summary>
	public static readonly IReadOnlyList<string> WellKnown = [Tile];
}

/// <summary>How a button's artwork fills its box.</summary>
public static class UiComponentImageFits
{
	/// <summary>The whole artwork is visible, letterboxed where its shape differs from the box.</summary>
	public const string Contain = "contain";

	/// <summary>The box is wholly covered, cropping the artwork where its shape differs.</summary>
	public const string Cover = "cover";

	/// <summary>The fits this profile ships. A reader falls back to <see cref="Contain" /> for one it does
	/// not know, rather than failing the tree.</summary>
	public static readonly IReadOnlyList<string> WellKnown = [Contain, Cover];
}

/// <summary>
/// How a button's ring is drawn. The first six are tinted in
/// <see cref="UiComponentProperties.BorderColor" />; <see cref="HueShift" /> and <see cref="Rgb" /> cycle
/// through colours of their own and ignore it.
///
/// <para>
/// There is deliberately no <c>off</c>: absence of <see cref="UiComponentProperties.BorderStyle" /> already
/// spells "no ring".
/// </para>
/// </summary>
public static class UiComponentBorderStyles
{
	/// <summary>A flat ring that does not animate.</summary>
	public const string Static = "static";

	/// <summary>Two quick opacity pulses, then a pause.</summary>
	public const string Heartbeat = "heartbeat";

	/// <summary>One slow opacity swell.</summary>
	public const string Breathing = "breathing";

	/// <summary>A hard on/off alternation.</summary>
	public const string Blink = "blink";

	/// <summary>A bright head travelling around the ring, trailing off behind it.</summary>
	public const string Comet = "comet";

	/// <summary>Dashes travelling around the ring.</summary>
	public const string Ants = "ants";

	/// <summary>One hue traversing the spectrum around the whole ring.</summary>
	public const string HueShift = "hue-shift";

	/// <summary>A full spectrum spread around the ring, rotating.</summary>
	public const string Rgb = "rgb";

	/// <summary>The styles this profile ships. A reader draws no ring for one it does not know, rather than
	/// failing the tree.</summary>
	public static readonly IReadOnlyList<string> WellKnown =
		[Static, Heartbeat, Breathing, Blink, Comet, Ants, HueShift, Rgb];
}

/// <summary>The outlines a <see cref="UiShape" /> draws. Adding one raises <c>ui.shape</c>'s component version,
/// because a reader draws nothing for a value it does not know.</summary>
public static class UiComponentShapes
{
	/// <summary>The whole box.</summary>
	public const string Rectangle = "rectangle";

	/// <summary>The whole box with corners of <see cref="UiShape.CornerRadius" />.</summary>
	public const string RoundedRectangle = "rounded-rectangle";

	/// <summary>A circle inscribed in the smaller side of the box, centred.</summary>
	public const string Circle = "circle";

	/// <summary>The whole box with corners of half its smaller side.</summary>
	public const string Capsule = "capsule";

	/// <summary>The outline <see cref="UiShape.Path" /> describes.</summary>
	public const string Path = "path";

	/// <summary>The outlines this profile ships.</summary>
	public static readonly IReadOnlyList<string> WellKnown = [Rectangle, RoundedRectangle, Circle, Capsule, Path];
}

/// <summary>
/// The glyphs of Macro Deck's built-in icon set a <see cref="UiIcon" /> may name. Additive only: a name is never
/// removed or renamed, because a plugin compiled against it would otherwise draw nothing.
///
/// <para>
/// <b>Names are negotiated per group.</b> A reader draws a glyph only for a name in the groups it carries, so
/// every name added after the first group belongs to a new group that raises <c>ui.icon</c>'s component version.
/// A producer using a name sets <see cref="Dsl.UiElement.RequiredComponentVersion" /> to
/// <see cref="VersionOf" /> that name when it is above 1, with a <see cref="Dsl.UiElement.Fallback" />.
/// </para>
/// </summary>
public static class UiIcons
{
	public const string ActionButtonType = "action-button-type";
	public const string AlertTriangle = "alert-triangle";
	public const string AlignBottom = "align-bottom";
	public const string AlignCenter = "align-center";
	public const string AlignLeft = "align-left";
	public const string AlignMiddle = "align-middle";
	public const string AlignRight = "align-right";
	public const string AlignTop = "align-top";
	public const string ArrowDown = "arrow-down";
	public const string ArrowLeft = "arrow-left";
	public const string ArrowRight = "arrow-right";
	public const string ArrowUp = "arrow-up";
	public const string Bell = "bell";
	public const string BracesX = "braces-x";
	public const string Bug = "bug";
	public const string Chart = "chart";
	public const string Check = "check";
	public const string ChevronRight = "chevron-right";
	public const string Clipboard = "clipboard";
	public const string ClockType = "clock-type";
	public const string Code = "code";
	public const string Copy = "copy";
	public const string Crosshair = "crosshair";
	public const string DeviceDesktop = "device-desktop";
	public const string DeviceFloppy = "device-floppy";
	public const string DevicePhone = "device-phone";
	public const string DeviceTablet = "device-tablet";
	public const string Disc = "disc";
	public const string Discord = "discord";
	public const string DotsVertical = "dots-vertical";
	public const string Download = "download";
	public const string ExternalLink = "external-link";
	public const string FileText = "file-text";
	public const string Folder = "folder";
	public const string FolderPlus = "folder-plus";
	public const string Globe = "globe";
	public const string Grid = "grid";
	public const string Heart = "heart";
	public const string HistoryGraphType = "history-graph-type";
	public const string Image = "image";
	public const string Info = "info";
	public const string Layers = "layers";
	public const string ListPlay = "list-play";
	public const string Lock = "lock";
	public const string LogOut = "log-out";
	public const string MessageSquare = "message-square";
	public const string Minus = "minus";
	public const string Moon = "moon";
	public const string MusicNote = "music-note";
	public const string MusicPlayerType = "music-player-type";
	public const string Pause = "pause";
	public const string Pencil = "pencil";
	public const string Pin = "pin";
	public const string PinOff = "pin-off";
	public const string Play = "play";
	public const string Plus = "plus";
	public const string Power = "power";
	public const string Puzzle = "puzzle";
	public const string Refresh = "refresh";
	public const string Scissors = "scissors";
	public const string Search = "search";
	public const string Settings = "settings";
	public const string Sidebar = "sidebar";
	public const string Sliders = "sliders";
	public const string Star = "star";
	public const string Store = "store";
	public const string Sun = "sun";
	public const string Trash = "trash";
	public const string Undo = "undo";
	public const string Unlock = "unlock";
	public const string Upload = "upload";
	public const string User = "user";
	public const string WeatherType = "weather-type";
	public const string Wifi = "wifi";
	public const string X = "x";
	public const string Zap = "zap";

	/// <summary>The names <c>ui.icon</c> component version 1 draws.</summary>
	public static readonly IReadOnlyList<string> Version1 =
	[
		ActionButtonType, AlertTriangle, AlignBottom, AlignCenter, AlignLeft, AlignMiddle, AlignRight, AlignTop,
		ArrowDown, ArrowLeft, ArrowRight, ArrowUp, Bell, BracesX, Bug, Chart, Check, ChevronRight, Clipboard, ClockType,
		Code, Copy, Crosshair, DeviceDesktop, DeviceFloppy, DevicePhone, DeviceTablet, Disc, Discord, DotsVertical,
		Download, ExternalLink, FileText, Folder, FolderPlus, Globe, Grid, Heart, HistoryGraphType, Image, Info, Layers,
		ListPlay, Lock, LogOut, MessageSquare, Minus, Moon, MusicNote, MusicPlayerType, Pause, Pencil, Pin, PinOff,
		Play, Plus, Power, Puzzle, Refresh, Scissors, Search, Settings, Sidebar, Sliders, Star, Store, Sun, Trash, Undo,
		Unlock, Upload, User, WeatherType, Wifi, X, Zap,
	];

	/// <summary>Every name any version of <c>ui.icon</c> draws.</summary>
	public static readonly IReadOnlyList<string> WellKnown = Version1;

	/// <summary>The <c>ui.icon</c> component version that first draws <paramref name="name" />, or
	/// <see langword="null" /> when no version does.</summary>
	public static int? VersionOf(string name) => Version1.Contains(name, StringComparer.Ordinal) ? 1 : null;
}
