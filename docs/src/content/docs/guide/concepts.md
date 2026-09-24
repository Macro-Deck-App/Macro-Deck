---
title: Concepts
description: Profiles, folders, widgets, actions, variables, automations and integrations, by example.
---

A streaming setup, built up piece by piece.

![The Home folder of a Streaming profile: folder buttons, a mic button, a counter, a slider, a clock, a CPU graph and the weather](../../../assets/guide/deck.png)

## Profiles

One profile per use: **Streaming**, **Work**, **Gaming**. A profile holds all its folders and
widgets. Switch between them at the top. Each device can open with its own profile.

## Folders

Inside **Streaming**: a start folder **Home** with **Scenes**, **Audio** and **Chat** subfolders,
listed on the right. A button with **Change Folder to** opens a subfolder, **Go Back** returns.

## Widgets

The tiles in a folder:

| Widget | Example |
| --- | --- |
| Action Button | **Scenes**, **Go live**, **BRB** |
| Slider | Mic volume |
| Clock | The current time and date |
| History Graph | CPU load over the last minutes |
| Weather | Today and the next days for your city |
| Music Player | What Spotify is playing, with play and skip |

## Actions and triggers

What a widget does, and when. The **Scenes** button runs **Change Folder to** on a short press:

![The widget editor of the Scenes button: a Short Press trigger with a Change Folder to action](../../../assets/guide/widget-editor-scenes.png)

| Trigger | Example action |
| --- | --- |
| Short Press | Switch OBS scene |
| Long Press | Start the stream |
| Double Tap | Mute all audio |
| Event | Turn the mic slider's accent red when OBS reports **Streaming Started** |

Every widget with actions, sliders included, can add event triggers next to its press triggers.

Once a widget has a Double Tap action, its Short Press waits a moment to see whether a second tap follows,
so a single tap runs slightly later. A double tap runs only the Double Tap action. On a slider, a double tap
still moves the level with each tap.

Actions run top to bottom. **If / Else**, **Repeat** and **Wait** build longer flows, for example:
*mute the mic, wait 3 seconds, switch the scene*. **Run** tries them out right away.

## Button states

The **Mic** button has two states, **Live** and **Muted**, each with its own label and color. Every
tap switches to the next one.

![The widget editor of the Mic button: Multi state with the states Live and Muted](../../../assets/guide/widget-editor-mic.png)

With **State mapping**, the state follows a variable instead, for example Discord's **Self Muted**.
The button then shows the truth even when you mute in Discord itself.

Some actions know their own state, for example **Mute / Unmute** or OBS's scene actions. The action list
marks them, and each one gets a button to let it drive this button's states. When you add such an action,
the editor offers this right away. The action then decides which states exist, and you still style each of
them. The **×** next to *Provided by* brings your own states back. Plugin actions that supply an icon work the same
way for the button's icon.

## Variables

Values you can show and use anywhere:

- **Integration variables:** the CPU load, the date, the weather, the current OBS scene.
- **User variables:** your own, for example a `deaths` counter.

Show one in a label with `{{ vars.deaths }}`, like the **Deaths: 3** button above.

Every speaker and microphone gets its own volume and mute variable, named after the device, for
example `system_audio_input_usb_mic_volume_percent`. An unplugged device keeps its variables; they
read as unavailable until it is back. The volume actions can control the default output, the default
input or one specific device.

Some integrations, such as Home Assistant, offer far more values than they list up front. Pick the
integration in the variable browser and search for an entity by name or variable name, then open it
to see its state and attributes. If you know the entity id, for example `light.office_lamp` or
`light.office_lamp/brightness`, type it under **Enter a resource ID** instead.

![The Variables page with the user variable deaths and system variables](../../../assets/guide/variables.png)

A user variable can also **read from a file**, like OBS's *Read from file*: choose **Read from file** as
its source when you create it and pick the file. The variable shows the file's content and follows every
change another program or script makes to it. A trailing line break is ignored, and a number or true/false
variable needs content of that kind. While the file is missing, unreadable or does not fit, the variable
reads as unavailable.

Such a variable is read-only. Turn on **Allow write-back** to let changes made in Macro Deck, for example
with a slider or **Set Variable**, go into the file too. **File settings** in the variable's menu changes
the file or write-back later. When you export widgets, a variable that reads from a file travels as an
empty variable: neither its path nor the file's content goes into the archive. Macro Deck watches the file
for changes; a file on a network share may not report them.

To save any variable's value on demand, use the **Write Variable to File** action. It replaces the file's
content with the current value, creates the file if needed, and needs a full path whose folder exists.

Only your own variables can read from a file. To keep a file up to date with any other variable, for
example the current OBS scene or `system_cpu_usage_percent`, create an automation: the event
**Variable Changed** watching that variable runs **Write Variable to File** for the same variable.
Every change then lands in the file.

## Scripts and automations

- **Script:** actions you reuse, for example *Go live* used by three buttons.
- **Automation:** actions that run on an event without belonging to a widget. **Evening stream**
  switches the deck to the **Streaming** profile every day at 18:00:

![The automation Evening stream: the event Schedule - Daily At 18:00 runs Change Profile to Streaming](../../../assets/guide/automation.png)

Other events: a device connects, a variable changes, OBS reports **Streaming Started**, a song
starts playing.

## Integrations and the Store

Integrations connect Macro Deck to other apps: OBS, Home Assistant, Voicemeeter, Spotify, Twitch,
Discord and more. Turn on the ones you use under **Integrations**.

![The Integrations page with ADB, Discord, Home Assistant, HTTP and Keyboard](../../../assets/guide/integrations.png)

The **Store** for more plugins and icon packs is not available to everyone yet. Everything published
there will be reviewed and signed first. Members of the Store tester programme can already use it:
sign in with your Macro Deck account under **Settings > Account**. A change to your tester access
can take up to a day to show up. If you were signed in before your Macro Deck version supported
testers, sign out and in once.

**Discover** opens with a search box and a chip for each kind of item (**All**, **Plugins**, **Icon packs**)
with how many there are. Under those, a chip for each Store category that has items of that kind, such as
**Music** or **Streaming**, lists only that category; choose it again to see everything. Below it, every item
is listed as a card showing its kind, rating, installs and whether it is **New** (published in the last 30
days) or recently **Updated**. The list is sorted by **Most popular** unless you pick another order or search.
Once the Store has enough items, rows for **Featured**, **Popular** and **New & updated** appear above the
list, each item in at most one row. Searching also finds items by their tags. **Only available for this
platform** hides items that do not run on your computer, and it stays on until you turn it off; a note says
how many items it hides, with **Show all** next to it. Going back from an item's page returns to the list as
you left it: the same search, kind, category, order and scroll position.

The **⋮** menu at the top right of the Store has **Refresh Store** and **Store settings**. **Refresh Store**
fetches the latest catalog and opens a log of each step as it happens. Macro Deck also refreshes on its own
a few seconds after it starts and then once an hour. While a refresh runs, the Store header says
**Refreshing…** in every window, and choosing **Refresh Store** again opens the log of that refresh instead of
starting a second one. If the store registry is being updated while a refresh runs, the log says so and Macro
Deck tries again a few times over about five minutes before it reports a failure. **Store settings** opens
**Settings > Extensions**.

**Installed** at the top of the Store lists the Store's plugins and icon packs you have installed, with their
version and any update waiting for them. While updates are waiting, **Installed** shows how many. Update one
at a time from its card, or all at once with **Update all**. After an update downloads, the card shows
**Installing…** while Macro Deck backs up, installs and restarts the plugin, then the version that is now
installed. See [Updates](/guide/updates/#extension-updates) for update notifications and automatic updates.

Icon packs from the Store are read-only on the **Icon Packs** page. You can use their icons on your buttons
and export the pack, but you can't rename, import, or delete icons in it, or edit its name and details.
Deleting the pack there uninstalls it from the Store. To change the icons, export the pack and import the
copy as a pack of your own.

An item's page shows its screenshots, description, what changed in the latest version and, in the details
beside it, whether it runs on your platform. It lists **Links** its creator provides, such as its **Homepage**,
the source repository, documentation or a place to report an issue. They open in your browser.

An item in a Store category lists it under **Categories**, and any other tags its creator gave it under
**Tags**; select one to see every item in that category or with that tag.
At the bottom, **You might also like** suggests items you have not installed that run on your computer: first
those that share tags with it, then others from the same creator and of the same kind. Going back from a suggestion
returns to the item you opened it from.

Under **AI**, the page shows what the creator declares about artificial intelligence: whether the item lets
you interact with an AI system, generates content with AI, or contains images, sounds or texts created with
AI, and which AI services it uses. Next to its name, an item that uses AI is marked **Uses AI**, and one that
only contains content created with AI, such as an icon pack with AI-created icons, is marked **Made with AI**. If the
creator has not declared anything, the page says so; that does not mean the item uses no AI.

For your own icon packs, select **Edit** on the pack under **Library > Icon Packs** (or **Edit pack** in its
menu) and set **AI-created icons**. The setting is saved in the exported pack. When you merge a pack that
contains AI-created icons into one of yours, your pack is marked as containing them too.

To install an older version, pick it under **Version** next to the install button. The latest version is
selected by default. The button then says **Install**, **Update to** or **Downgrade to** that version, and a
downgrade asks you to confirm first. Versions that cannot be installed on your computer stay in the list but
cannot be selected. Whether a version works with your Macro Deck version is only known once it is downloaded:
if it needs a newer Macro Deck, the Store says so and offers **Check for updates** instead of **Retry**.
**All versions** under **What's new** lists every version with its release notes. See
[Updates](/guide/updates/#extension-updates) for how an older version affects automatic updates.

Select the publisher's name on a card or an item's page to see everything that publisher offers. An installed
plugin's page has **Open settings**, which opens its integration; its back arrow returns to the Store page. An
installed icon pack's page has **Open in Library**. The other way round, a plugin's page under
**Integrations** and a Store icon pack under **Library > Icon Packs** have **View in Store**, for the
description and release notes. An installed plugin is uninstalled from the **General** details of its page
under **Integrations**, or from its Store page.

The Store footer links to the **Creator Portal**, where you can publish your own plugins and icon packs, and to
the imprint and privacy policy.

Store items show their star rating and how many times they have been installed. The install count
leaves out updates and repairs, and appears once an item has been installed at least once. An item's
page lists its **Ratings and reviews**, which anyone can read. When the creator has answered a review,
their reply appears under it as a **Developer response**. To rate or review an item yourself, sign in
with Macro Deck Connect under **Settings > Account** and install the item first: only items you have
installed can be rated. When you sign in, and whenever you install something while signed in, Macro
Deck records your installed Store items for your account so you can rate them. If ratings or install
counts cannot be reached, the Store keeps working without them.

To report a Store item, open its page and choose **Report this item** below the details.
To report a review, choose **Report** next to it. Pick a reason and, if you like, add details;
**Other** needs a short description. Reporting needs a Macro Deck Connect sign-in, and each review
can be reported once per account. Reports go to the Macro Deck moderators and do not hide
anything on their own: the item or review stays visible until a moderator has looked at it. Reporting
Store items only works once the Store supports it; until then, Macro Deck tells you it is not
available.

### Tests

A plugin creator can invite you to test a plugin before it is reviewed. The invitation arrives by
email; accept it in the Creator Portal with the same Macro Deck account you use in Macro Deck. While
you are signed in under **Settings > Account**, **Tests** appears at the top of the Store next to
**Installed**, listing every plugin you test. Each plugin starts collapsed and shows how many test
builds it has; select it to see its builds, newest first. You do not need to be a Store tester to see
it: while the Store itself is not open to you, its notice offers **Open your tests**.

Test builds are not reviewed or signed by Macro Deck: they come straight from the creator and may be
unstable. Choosing **Install** or **Install test build** asks you to confirm that first. A test build
replaces the version of the plugin you have installed, including one from the Store, and the build
that is currently installed shows as **Installed**.

While a test build is installed, the plugin is marked **Test build** under **Tests**, **Installed** and
**Discover**. If the plugin is also published in the Store, **Return to Store version** installs its
current Store release in place of the test build.

## Devices

Every phone, tablet or browser that connects shows up in **Settings > Devices**. Choose there which
profile each device opens with.

## Open source licenses

Macro Deck is built on open source software. **Settings > About > Open source licenses** lists every
third-party component it includes, with its license and the full license text; search by name or license.
On a phone or tablet, the web client's settings offer the same list under **Open source licenses**. The
files `LICENSE`, `NOTICE` and `THIRD-PARTY-NOTICES` also sit next to the Macro Deck host in the installation
folder, and the Linux AppImage adds a notices file for the system libraries it bundles.
