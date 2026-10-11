---
title: Steam
description: How Macro Deck shows the game you are playing, starts Steam games and exposes your Steam status as variables, with or without a Steam Web API key.
---

Macro Deck finds the Steam installation on your computer and starts games through it. With an optional
Steam Web API key it can also show your online status and how many of your friends are online, for as many
Steam accounts as you like.

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
- **SteamID**: your 17-digit SteamID64, the link to your Steam profile, or your custom profile name. A custom
  name needs the Web API key.
- **Web API key**: a key from Steam that lets Macro Deck ask Steam about your profile. Open the Steam Web API
  key page, sign in, and register a key. The page asks for a domain name; any value works. Steam does not give
  keys to limited accounts, which are accounts that never spent money on Steam. The key is stored encrypted
  on this computer and only used to ask Steam about your profile.

Enter the SteamID and the key together, or neither; without them the configuration only covers this computer.
Macro Deck checks both with Steam before it saves them
and tells you when the profile is unknown, the key is rejected or Steam cannot be reached.

Under **Advanced** you can add **Additional library folders**: game libraries on this computer that Macro Deck
does not find by itself. Add them one by one and pick each with the folder dialog. Steam's own library folders
on other drives are found automatically. The folders count for the whole computer, whichever configuration they
are in.

If something is wrong with an account, the integration shows a notice, for example when Steam rejected the key
or when it does not hand out the friends of an account because the friends list is private. To show the friends
of an account, open Steam, choose **Edit Profile**, then **Privacy Settings**, and set **Friends List** to
**Public**.

## What needs what

| | Works without a key | Needs the key |
| --- | --- | --- |
| Installed games, starting games, opening Steam, store and library pages | Yes | |
| The game you are playing on this computer and for how long | Yes | |
| A game you are playing on another computer or device | | Yes |
| Friends online and friends in a game | | Yes, and a public friends list |

If your Steam profile or friends list is private, Steam does not give out the friends list. Macro Deck then
treats the friends variables as unavailable and does not show wrong numbers.

## Actions

| Action | What it does |
| --- | --- |
| Launch Steam game | Starts a game. Pick one of your installed games or type a Steam app ID |
| Launch Steam | Opens the Steam client |
| Open Steam store page | Opens a game's store page in Steam |
| Open Steam library page | Opens a game's page in your Steam library |

A game is started through Steam itself, the same as double-clicking it in your library, so Steam may first
ask you to sign in or update the game. Starting a game does not need the key.

## Widgets

### Steam currently playing

Shows the game you are playing with its cover, its name and how long you have been playing it. When you are
not playing, it says so. When Steam is not found on this computer it says that instead. In its settings:

- **Account**: leave it on **This computer** to show the game that is running here. Pick an account to show
  the game that account is playing, wherever it plays it. This needs the Web API key of that account.
- **Show session time** turns the time off.

### Steam game launcher

Starts a game with one tap. Choose in its settings:

- **Games**: **Recently played** shows the games you played most recently, **One game** always shows the game
  you pick.
- **Number of games**: how many recently played games to show, from 1 to 6.

A widget one cell wide shows one game. A wider or taller widget shows up to three, and a widget at least two
cells wide and two tall shows up to six. Tapping a game does nothing while your computer is locked.

Covers come from Steam's own servers. A game without a cover shows its name instead.

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
- **The status and friends of your accounts** are asked from Steam every 30 and 60 seconds, and every widget
  and variable uses the same answers.

## Privacy

Macro Deck contacts Steam only for the things above: with a key, `api.steampowered.com` for the status and
friends of that account, and `cdn.cloudflare.steamstatic.com` for game covers. Without a key it only asks for
covers when a widget shows them.

## Not part of this yet

A friends widget, a widget for your whole library with playtime and statistics, closing a running game,
playtime from your account and favorites are not supported yet.
