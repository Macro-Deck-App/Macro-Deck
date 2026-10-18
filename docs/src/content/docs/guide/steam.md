---
title: Steam
description: How Macro Deck shows the game you are playing, your friends and your library, starts and closes Steam games and exposes your Steam status as variables and events, with or without a Steam Web API key.
---

Macro Deck finds the Steam installation on your computer and starts games through it. With an optional
Steam Web API key it can also show your online status, your friends and your library with its playtime, for as
many Steam accounts as you like.

## Turning it on

The Steam integration is off until you turn it on, because it only does something on a computer that has
Steam installed. Open **Integrations**, choose **Steam** and turn it on.

Actions and widgets work right away for everything on this computer: the games you have installed, the game you
are playing and starting games. The variables come with a configuration, which needs a name and nothing else.
A SteamID and a key are only needed for the information that only Steam's servers have.

## Setting it up (optional)

Choose **Steam** under **Integrations** and add a configuration. Each configuration is one Steam account, and you
can add as many as you like, for example your own and one of a friend or family member who gave you their key:

- **Configuration name**: how the account is called in the widget settings and in its variable names, for
  example `Main`.
- **Web API key**: a key from Steam that lets Macro Deck ask Steam about your profile. Open the Steam Web API
  key page, sign in, and register a key. The page asks for a domain name; any value works. Steam does not give
  keys to limited accounts, which are accounts that never spent money on Steam. The key is stored encrypted
  on this computer and only used to ask Steam about your profile.
- **SteamID**: your 17-digit SteamID64, the link to your Steam profile, or your custom profile name.

Enter the SteamID and the key together, or neither; without them the configuration only covers this computer.
Macro Deck checks both with Steam before it saves them
and tells you when the profile is unknown, the key is rejected or Steam cannot be reached.

Under **Advanced** you can add **Additional library folders**: game libraries on this computer that Macro Deck
does not find by itself. Add them one by one and pick each with the folder dialog. Steam's own library folders
on other drives are found automatically. The folders count for the whole computer, whichever configuration they
are in.

If something is wrong with an account, the integration shows a notice, for example when Steam rejected the key
or when it does not hand out the friends or the games of an account because they are private. To show the
friends of an account, open Steam, choose **Edit Profile**, then **Privacy Settings**, and set **Friends List** to
**Public**. For owned games and playtime, set **Game details** to **Public** as well.

## What needs what

| | Works without a key | Needs the key |
| --- | --- | --- |
| Installed games, starting games, opening Steam, store and library pages | Yes | |
| The game you are playing on this computer and for how long, closing it (Windows) | Yes | |
| A game you are playing on another computer or device | | Yes |
| Friends online and in a game, the friends widget and the friend events | | Yes, and a public friends list |
| Your online status and its event | | Yes |
| Owned games, total playtime and playtime of the last two weeks | | Yes, and public game details |

If your Steam profile or friends list is private, Steam does not give out the friends list. If your game details
are private, Steam does not give out your owned games and playtime. Macro Deck then treats those variables as
unavailable and does not show wrong numbers.

## Actions

| Action | What it does |
| --- | --- |
| Launch Steam game | Starts a game. Pick one of your installed games or type a Steam app ID |
| Launch Steam | Opens the Steam client |
| Open Steam store page | Opens a game's store page in Steam |
| Open Steam library page | Opens a game's page in your Steam library |
| Close Steam game | Closes the Steam game that is running on this computer |

A game is started through Steam itself, the same as double-clicking it in your library, so Steam may first
ask you to sign in or update the game. Starting a game does not need the key.

**Close Steam game** asks the game that runs on this computer to close, the same as closing its window, and
waits up to 10 seconds. A game that does not close by then, or has no window to close, keeps running, unless
you turn on **Force quit**: then Macro Deck ends it. Force quit can lose progress the game has not saved yet. Closing a game works on
Windows only: on Linux and macOS a game cannot be asked to close the way its window would be, so the action
says it is not supported there. The action only closes a game running on this computer, never one an account
plays elsewhere.

## Widgets

The Steam widgets have a dark face in Steam's colors unless you pick a background color or a transparent
background in their settings.

### Steam currently playing

Shows the game you are playing with its cover, its name and how long you have been playing it. A widget one
cell wide shows the cover across the whole widget, a wider one shows the cover next to the name. When you are
not playing, it says so. When Steam is not found on this computer it says that instead. In its settings:

- **Account**: leave it on **This computer** to show the game that is running here. Pick an account to show
  the game that account is playing, wherever it plays it. This needs the Web API key of that account.
- **Show session time** turns the time off.

### Steam game launcher

Starts a game with one tap. Choose in its settings:

- **Games**: **Recently played** shows the games you played most recently, **Favorites** shows the games you
  pick under **Favorite games**, in the order you put them, and **One game** always shows the game you pick.
- **Number of games**: how many recently played games to show, from 1 to 6.

A widget one cell wide shows one game. A wider or taller widget shows up to three, and a widget at least two
cells wide and two tall shows up to six.

Covers come from Steam's own servers. A game without a cover shows its name instead.

### Steam friends

Shows how many of your friends are online and how many of them are playing. A larger widget also lists the
friends who are online, with their profile picture and the game they play or their status: two in a widget two
cells wide, four in one two cells tall, and five in one two cells wide and two tall. Friends who are playing
come first. In its settings, pick the **Account** whose friends to show; left empty, it uses the first account
with a Web API key. The widget needs a key and a public friends list.

### Steam library

Shows the hours you have played in total and how many games you own. A larger widget also shows the hours of
the last two weeks and the games you played last, with how long you played them. In its settings, pick the
**Account** whose library to show; left empty, it uses the first account with a Web API key. Without any key,
it shows the games installed on this computer and the ones you played here last. Steam does not give out
achievements this way, so the widget does not show them.

## Events

The Steam events start automations. In each one you can pick the account it is about; leave it empty to react
to every account.

| Event | When it happens | What it carries |
| --- | --- | --- |
| Game started | A game starts on this computer, or with a key, a game an account plays on another device | Account, game ID, game name |
| Game closed | Such a game ends | Account, game ID, game name, how long it ran in seconds |
| Friend came online | A friend goes from offline to online | Account, friend, friend name, game ID, game name |
| Friend went offline | A friend goes offline | Account, friend, friend name |
| Friend started a game | A friend starts playing a game, or switches to another game | Account, friend, friend name, game ID, game name |
| Online status changed | Your own Steam status changes, for example from Online to Away | Account, status, previous status |

For a game on this computer, the account is **This computer**. A game an account plays on this computer is
reported as this computer's, and not again for the account. Only when Steam's servers report the game before
this computer notices it, which can happen on macOS, is it reported for the account as well. Games, friends and
statuses that are already there when Macro Deck starts are not reported, and neither is anything that changes while Steam cannot be reached: the events start
again from what Steam reports once it is back. The friend events and the status event need the key. Friends
are checked once a minute and your status every 30 seconds, so these events can arrive up to that much later.
A status is one of `online`, `busy`, `away`, `snooze`, `trade`, `play` or `offline`.

## Variables

Every configuration has its own set of variables, named after its configuration name, for example
`steam_main_is_playing` for a configuration called `Main`. There are no variables without a configuration, so
add one to get them; it needs no SteamID or key. Renaming the configuration later keeps the names, so your
buttons and scripts keep working. The **Variables** page shows the exact names.

| Variable | Contains | When nothing is played |
| --- | --- | --- |
| `steam_<name>_is_playing` | Whether you are playing a game | `false` |
| `steam_<name>_current_game_name` | The name of the game | empty |
| `steam_<name>_current_game_id` | The Steam app ID of the game | `0` |
| `steam_<name>_current_game_playtime_seconds` | How long this game has been running, in seconds | `0` |
| `steam_<name>_games_installed` | The number of installed games | unavailable when Steam is not found |
| `steam_<name>_friends_online` | How many friends of that account are online | unavailable without a key or with a private friends list |
| `steam_<name>_friends_ingame` | How many friends of that account are playing a game | unavailable without a key or with a private friends list |
| `steam_<name>_games_owned` | How many games that account owns | unavailable without a key or with private game details |
| `steam_<name>_total_playtime_hours` | The hours that account has played in total | unavailable without a key or with private game details |
| `steam_<name>_recent_playtime_hours` | The hours that account played in the last two weeks | unavailable without a key or with private game details |

The game is the one running on this computer, for every configuration alike, because Macro Deck cannot tell
which Steam account is signed in here. So add configurations for accounts you play with on this computer. When
nothing runs here and the configuration has a key, it is the game that account plays on any other device. The installed games are the same for every configuration, because
they belong to this computer.

A configuration's friends variables are unavailable while Steam rejects its key or cannot be reached, and also
while its friends list is private.

The session time starts when Macro Deck first notices the game, not when Steam started it. A game that
was already running when Macro Deck started begins at zero then. A friend counts as online when they are
anything but offline.

## How Macro Deck finds things

- **The Steam installation** is found in the usual places on Windows, macOS and Linux, including the Flatpak
  and Snap versions of Steam. It is looked for again once a minute while Steam is not found.
- **Installed games** are read from Steam's own library files, on every drive Steam uses. A drive that is not
  connected is skipped. Steam's tools and compatibility layers are not listed as games.
- **The running game** is read from Steam on this computer every few seconds. On macOS and Linux, Macro Deck
  relies on a file Steam keeps up to date while it runs; this has not been tested on macOS, so the game may be
  noticed late or not at all there.
- **The status and friends of your accounts** are asked from Steam every 30 and 60 seconds, and **the owned
  games and playtime** every 10 minutes. Every widget, variable and event uses the same answers.
- **The game to close** is found from the folder Steam installed it in.

## Privacy

Macro Deck contacts Steam only for the things above: with a key, `api.steampowered.com` for the status, the
friends and the owned games and playtime of that account, `cdn.cloudflare.steamstatic.com` for game covers, and
`avatars.fastly.steamstatic.com` or another of Steam's avatar servers for the profile pictures of the friends a
widget shows. Without a key it only asks for covers when a widget shows them.

