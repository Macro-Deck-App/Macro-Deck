---
title: SoundPad
description: Control SoundPad from Macro Deck on Windows, show the playing sound in the Music Player widget, and what moves over from the Macro Deck 2 SoundPad plugin.
---

The **SoundPad** integration plays sounds from your SoundPad list, starts and stops recordings, and shows
what SoundPad is playing in the **Music Player** widget. It is available on Windows only, because SoundPad
runs only there. Macro Deck talks to SoundPad on the same PC.

## Set up SoundPad

1. Start SoundPad on the PC that runs Macro Deck.
2. In Macro Deck, open **Integrations**, find **SoundPad** and start its setup.
3. Continue. Macro Deck checks that SoundPad answers and finishes the setup.

If the setup says SoundPad did not answer, make sure SoundPad is running, then try again.

Keep SoundPad running while you use it from Macro Deck. When you close SoundPad, Macro Deck reconnects on
its own within a few seconds after you start it again; you do not have to set it up again.

## Music Player widget

Place a **Music Player** widget and choose **SoundPad** as its player. It shows the sound that is playing,
how far it has played and how long it is, and the volume. Play, pause, previous, next, seek and volume work
from the widget. Previous and next play the neighbouring sound in your SoundPad list. SoundPad has no
shuffle or repeat, so those buttons do nothing.

SoundPad does not tell other apps which sound it is playing. Macro Deck shows the sound it started itself,
and for a sound you started in SoundPad, the sound SoundPad last marked as played. If SoundPad does not
record when sounds were played, the title stays empty while the sound plays.

While SoundPad is closed, the widget shows that it is reconnecting.

## Actions

| Action | What it does |
| --- | --- |
| Play Sound | Plays a sound you pick from your SoundPad list. Leave the sound empty to pick one when you press the button |
| Play Random Sound | Plays a random sound, from all sounds or from one **Category**, on your speakers, through your microphone, or both |
| Stop Playback | Stops the sound that is playing |
| Start Recording | Starts a SoundPad recording from the **Microphone**, the **Speakers** or SoundPad's default |
| Stop Recording | Stops the recording |
| Toggle Mute | Mutes or unmutes SoundPad |

The usual music player actions (play, pause, next, previous, volume and seek) work with SoundPad too.

**Play Sound** remembers the sound by its file. Sorting your SoundPad list or adding sounds does not change
which sound the button plays. Moving or renaming the file does: pick the sound again. Because the button
stores the file's path on your PC, it does not play that sound on another PC you import your setup on.

## Variables

| Variable | Shows |
| --- | --- |
| `soundpad_current_sound` | The sound that is playing |
| `soundpad_current_artist` | Its artist, when the sound has one |
| `soundpad_playback_state` | `playing`, `paused` or `stopped` |
| `soundpad_is_playing` | Whether a sound is playing |
| `soundpad_recording` | Whether SoundPad is recording |
| `soundpad_volume` | SoundPad's volume in percent. You can change it, for example with a slider |
| `soundpad_position`, `soundpad_duration` | How far the sound has played and how long it is, in seconds |
| `soundpad_is_muted` | Whether SoundPad is muted |
| `soundpad_is_connected` | Whether Macro Deck reaches SoundPad right now |

## Coming from Macro Deck 2

When you migrate from Macro Deck 2, buttons that used the SoundPad plugin by PhoenixWyllow move over:

- **Play** becomes **Play Sound** with the same sound. The old plugin stored the sound's position in your
  list, so the button plays whatever sound is at that position. Pick the sound again once to make the
  button follow the sound instead.
- **Stop Playback**, **Start Recording** (with the same microphone or speakers choice) and **Stop Recording**
  become the actions of the same name.
- Buttons that showed `soundpad_recording` keep working.

The old plugin's per-sound variables, `soundpad_1_playing` and so on, have no replacement. Use
`soundpad_current_sound` instead.

The old plugin had no settings, so set up SoundPad once under **Integrations** after the migration. Until
then, the migrated buttons do nothing.
