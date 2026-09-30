---
title: Run scripts from other apps
description: Run Macro Deck scripts from Tasker, MacroDroid, other apps and home screen shortcuts, with an Android intent or a run-script link, and the automation key that protects it.
---

The Companion app can run a Macro Deck [script](/guide/concepts/#scripts-and-automations) on one of your computers when another app asks
for it: an automation app such as Tasker or MacroDroid on Android, or any app that can open a link on iPhone and
iPad. On iPhone and iPad, the **Run Script** action in Shortcuts does the same without any setup; see
[Shortcuts and Siri](/guide/companion-app/ios/#shortcuts-and-siri).

A script runs the same way it does from Shortcuts: it needs a license or a running trial, the computer reachable
over the network, and the computer unlocked. Scripts cannot run over a USB cable this way. Only scripts that do
not run on a widget are offered.

## Turn it on

1. Open the app settings and choose **Automation**.
2. Turn on **Allow external automation**. The app creates an **automation key**.
3. Open one of your computers to see its scripts, and tap a script to copy what an automation needs to run it.

![The Automation settings on Android: Allow external automation turned on, the automation key and the scripts of a computer](../../../../assets/guide/companion/android-automation.png)

Every request from another app has to carry the automation key. An app that does not have it cannot run
anything, and learns nothing about your computers or scripts. Keep the key private like a password:

- **Generate new key** replaces it. Every automation that uses the old key stops working until you give it the
  new one. Do this if a key or a link with the key was ever shared.
- Turning **Allow external automation** off stops every request at once and keeps the key for later.

On Android, a run can start while the phone is locked, so an automation can run a script when you arrive home or
at a set time.

## Android: send an intent

Send a broadcast intent to the app:

| Field | Value |
| --- | --- |
| Action | `app.macrodeck.companion.action.RUN_SCRIPT` |
| Package | `app.macrodeck.companion` |
| Target | Broadcast receiver |

Always set the package. Android only delivers the intent to the app when the package is set.

The intent takes these extras, all as text:

| Extra | Value |
| --- | --- |
| `host` | The computer's ID, shown under its name in the **Automation** settings. |
| `hostName` | Instead of `host`: the computer's name as the Connections screen shows it. Letter case does not matter. |
| `script` | The script's ID, shown under its name in the **Automation** settings. |
| `scriptName` | Instead of `script`: the script's name. Letter case does not matter. |
| `input.<name>` | The value of one input, such as `input.scene` = `Live`. Add one extra per input. |
| `inputs` | Instead of single inputs, or together with them: a JSON object such as `{"scene":"Live"}`, or one `name=value` per line. A single `input.<name>` wins over the same name here. |
| `key` | The automation key. |

An ID never changes. A name stops working when you rename the computer or the script, and when two computers or
two scripts have the same name the app does not guess and runs nothing. Input values are checked by Macro Deck:
a number input takes `3` or `2.5`, a true or false input takes `true` or `false`.

**Copy intent details** in the **Automation** settings copies all of it for one script, with the key.

### Tasker

1. Add the action **System > Send Intent**.
2. Set **Action** to `app.macrodeck.companion.action.RUN_SCRIPT`, **Package** to `app.macrodeck.companion` and
   **Target** to **Broadcast Receiver**.
3. Add the extras as `name:value`, one per **Extra** field, such as `host:3f2a9c`, `script:go-live` and
   `key:<your key>`. Use `input.scene:Live` for an input.

### MacroDroid

1. Add the action **Connectivity > Send Intent**.
2. Choose **Broadcast**, set **Action** to `app.macrodeck.companion.action.RUN_SCRIPT` and **Package** to
   `app.macrodeck.companion`.
3. Add the extras `host`, `script` and `key`, and one `input.<name>` per input.

### What happens

- When the script ran, nothing is shown.
- When it did not run, for example because the computer was off, a notification says why. A request with a
  missing or wrong key, or while **Allow external automation** is off, shows nothing.
- An app that sends an ordered broadcast gets the result back: `RESULT_OK` when the script ran, with the extras
  `success`, `finished`, `problem` and `message`. `finished` is `false` while the script is still running on the
  computer. `problem` is one of `offline`, `host_locked`, `license_required`, `not_found`, `invalid_inputs`,
  `no_answer`, `no_result`, `sign_in_required`, `not_saved`, `unsupported`, `host_needs_update`, `failed`,
  `wrong_key`, `key_unavailable`, `disabled`, `missing_host`, `missing_script`, `ambiguous_host` or
  `ambiguous_script`.

The app waits up to 25 seconds for the computer. A script that takes longer keeps running on the computer.

While the phone is idle, Android can hold back the network to save battery. A script then does not run, and the
notification says the computer did not answer. If this happens, exclude Macro Deck from battery optimization, or
run the automation while the screen is on.

## Link

Both platforms open this link:

```text
macrodeck-companion://run-script?host=<computer ID>&script=<script ID>&key=<key>&input.scene=Live
```

It takes the same fields as the Android intent, as query parameters. Encode spaces and special characters, for
example `input.scene=Big%20Scene`. **Copy link** in the **Automation** settings copies the link of a script with
the key.

The app shows whether the script ran. On Android, the link only works from apps that open it directly, such as
Tasker's **Browse URL** or an NFC tag app, and not from a web page. Set the package `app.macrodeck.companion` when
an app lets you, so the link cannot reach another app. On iPhone and iPad, any app or web page can open the link,
so the key is what protects it. Another app could also claim the `macrodeck-companion` link for itself and receive
the key: generate a new key if you ever suspect that.

## Android: home screen shortcuts

Tap a script in the **Automation** settings and choose **Add to home screen** to put it on the home screen. Tapping
it runs the script and says whether it ran. This needs Android 8 or later and a launcher that supports pinned
shortcuts, and it is offered only for scripts that need no input values.

A home screen shortcut is yours, so it works without the key and with **Allow external automation** turned off.
If you remove the computer from the app, its shortcuts say so and stop running.
