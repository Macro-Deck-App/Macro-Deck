---
title: Jellyfin
description: How Macro Deck connects to one or more Jellyfin servers, shows what is playing, controls Jellyfin clients and reacts when playback starts or stops.
---

Macro Deck connects to Jellyfin servers and works with the sessions on them: the Jellyfin apps, browsers
and TVs that are signed in and playing. It does not need a Jellyfin plugin.

## Connect a server

Open **Integrations**, choose **Jellyfin** and add a configuration. Give it a name, enter the server
address with its port (for example `http://192.168.1.10:8096`, or `https://media.example.com/jellyfin`
behind a reverse proxy) and choose how Macro Deck signs in:

- **API key** (recommended): create a key in the Jellyfin dashboard under **API Keys**. Macro Deck then sees
  and controls every session on the server.
- **Username and password**: Macro Deck signs in once and keeps only the access token Jellyfin hands out,
  never the password. It sees only the sessions that account may control. To sign in again, edit the
  configuration and enter the password.

Add one configuration per server. Each one shows whether it is connected. When a server goes away, Macro
Deck keeps retrying in the background and reports it under **Integrations** after a few failed attempts.

Macro Deck follows a server live through its WebSocket on Jellyfin 10.11 and later. When a server does not
send live updates, such as Jellyfin 10.10 with an API key or an account that is not an administrator,
Macro Deck asks for the sessions every two seconds instead.

## Sessions and devices

A Jellyfin client becomes a **device** in Macro Deck the first time Macro Deck sees it while it can be
remote-controlled or plays something. Macro Deck remembers devices for 30 days after it last saw them, so
a widget or button set up for your TV keeps working while the TV is off. A device that cannot be
remote-controlled, such as some smart TV apps, only gets [variables](#variables), not a player entry.

Every server appears in two forms wherever you pick a player:

- **The server name**, for example **Home**: follows whichever session played something most recently.
- **Server · Device**, for example **Home · Living Room TV**: always that device. While the device is off,
  it shows as not connected.

## Music Player widget

Choose a Jellyfin server or device as the player of a **Music Player** widget. It shows what plays, with
artwork, progress, and play, pause and skip buttons. Jellyfin is used for more than music, so the lines
adapt to what plays:

| Playing | Title | Second line | Third line | Artwork |
| --- | --- | --- | --- | --- |
| Song | Track | Artist | Album | Album cover |
| Episode | Episode | Series | Season | Series poster |
| Movie | Movie | | Year | Poster |

## Actions

Each Jellyfin action controls the server or device you pick for it:

- **Play**, **Pause**, **Toggle Play/Pause**, **Stop**, **Next Track**, **Previous Track**
- **Seek** to a position, **Seek forward** and **Seek backward** by a number of seconds
- **Set Volume**, **Volume Up**, **Volume Down**, **Mute**, **Unmute**, **Toggle mute**
- **Toggle Shuffle** and **Set Repeat Mode**
- **Show message**: a heading, a text and how long the client shows it
- **Play media**: search for a movie, episode, song or album and start it on the device. It replaces what
  is playing there

Not every Jellyfin client supports every command. A client reports what it can do, and Macro Deck only
sends what the client supports: a press on a command the client cannot carry out fails with a message
instead of silently doing nothing. Volume and mute usually work only in the Jellyfin apps, not in a
browser tab.

## Variables

Each server has:

| Variable | Meaning |
| --- | --- |
| `jellyfin_<server>_is_connected` | Whether Macro Deck reaches the server |
| `jellyfin_<server>_active_sessions` | Sessions playing or paused on the server |

`jellyfin_active_sessions` counts the sessions playing or paused on all servers together.

Each device has variables named `jellyfin_<server>_<device>_...`, for example
`jellyfin_home_living_room_tv_title`:

| Ending | Meaning |
| --- | --- |
| `is_active` | Something is playing or paused |
| `is_playing`, `is_paused` | Playback state |
| `title`, `series`, `album`, `media_type` | What plays. `album` is the season for an episode, `media_type` is `Movie`, `Episode`, `Audio` and so on |
| `position`, `duration`, `progress_percent` | Progress in seconds and percent |
| `user`, `client`, `device` | Who plays, in which app, on which device |
| `is_transcoding` | Jellyfin converts the media for this device |
| `volume`, `is_muted` | The device's volume, where it reports one |

Some of them also control the device: write a value to them, or bind a **Slider** to them, and Macro Deck sends
the matching command.

| Write to | Does |
| --- | --- |
| `volume` | Sets the volume, 0 to 100 |
| `position`, `progress_percent` | Jumps to that point, in seconds or percent. A slider sends it when you let go |
| `is_playing`, `is_paused` | Plays or pauses |
| `is_muted` | Mutes or unmutes |

When the device cannot do it, for example volume in a browser tab, the write is refused.

When playback ends, the text variables become empty and the others `false` or `0`, so they never show a
stale title. The names come from the configuration and device names; renaming a configuration renames its
variables.

## Triggers

Jellyfin offers these events for automations and widget flows:

- **Playback started**, **Playback paused**, **Playback resumed**, **Playback stopped**
- **Playing item changed**, when a session moves on to the next episode or song
- **Session connected** and **Session disconnected**
- **Connected** and **Disconnected**, for the server itself

Each event can be limited to one **Server** and one **Device**; left empty, it runs for all of them. The
actions it runs can use the event's values, such as `{{ event.itemName }}`:

| Value | Holds |
| --- | --- |
| `server`, `configuration` | The server's name and the id of its configuration |
| `device`, `deviceId`, `client`, `sessionId` | The device, its id, the Jellyfin app and the session |
| `user` | Who plays |
| `itemName`, `itemType`, `itemId` | What plays: its title, its type (`Movie`, `Episode`, `Audio` and so on) and its id |
| `series`, `album` | The series, and the album or season |

For example, to dim the lights when a movie starts, add an automation on **Playback started** for your TV
with a condition that `{{ event.itemType }}` is `Movie`, and call the Home Assistant light action. A second
automation on **Playback stopped** turns them up again.

When Macro Deck starts, what already plays counts as the starting point and does not fire **Playback
started**. **Session disconnected** fires when the server ends the session, which Jellyfin does a while
after a client closes.

The Music Player events (**Playback started** and the others under **Music Player**) also fire for
Jellyfin, once for the server and once for the device. For automations, the Jellyfin events are the better
choice: they fire once per device and carry the user and the type of media.

## Jellyfin Sessions widget

The **Jellyfin Sessions** widget shows what plays on your servers, with the cover or poster. A small widget
shows the most recent playback, a wide one adds who plays it, on which device and how far along it is, and
a large one shows a single playback big or lists several with their progress. A tall, narrow one lists them
without covers. Choose one server or all of them in the widget settings.
