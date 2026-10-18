---
title: Discord Webhooks
description: How Macro Deck sends rich Discord messages with embeds, mentions and a live preview through saved or per-action webhooks, and how it keeps the webhook URL secret and pings nobody by accident.
---

A Discord webhook lets anything that knows its URL post into one channel. The Discord Webhooks integration
saves webhooks under a name and gives an action a visual editor for the message, with a preview that looks
like Discord. It does not need the Discord app or a Discord account on this computer.

The [Discord](/guide/concepts/#integrations-and-the-store) integration has an older **Execute webhook** action
with one embed and no preview. It keeps working; use Discord Webhooks for anything richer.

## Creating a webhook in Discord

In Discord, open the settings of the channel, go to **Integrations**, then **Webhooks**, and create a webhook
or pick one. **Copy Webhook URL** copies its address, which starts with `https://discord.com/api/webhooks/`.
Anyone who has this URL can post into the channel, so treat it like a password.

## Saved webhooks and webhooks in one action

You can give an action its webhook in two ways:

- **Saved webhook**: open **Integrations**, choose **Discord Webhooks** and add a configuration with a
  **Name** and the **Webhook URL**. Add one configuration per channel. Every action can then pick the webhook
  by its name, and when the URL changes you change it in one place.
- **Custom URL**: in the action, set **Webhook** to **Custom URL** and paste the URL there. It belongs to this
  action only.

Macro Deck accepts only Discord webhook addresses on `discord.com`, `discordapp.com`, `ptb.discord.com` and
`canary.discord.com`. The URL is stored encrypted. It is never shown again, never written to the log and never
part of the preview. To change it, enter it again.

## Writing the message

Add the action **Send Discord webhook message** to a button, automation or script. The editor has four tabs:

| Tab | What you set there |
| --- | --- |
| **Message** | The message text, with Discord Markdown and line breaks, and buttons that insert a user or role mention, `@everyone` or `@here` at the end of the text |
| **Embeds** | Up to 10 embeds, each with title and title link, description, color, timestamp, author with link and icon, footer with icon, image, thumbnail and up to 25 fields that can be shown side by side |
| **Profile** | A username and avatar for this message instead of the webhook's own |
| **Mentions** | Who may be notified by the mentions in the message, and names for IDs in the preview |

A user mention needs the user's Discord ID: turn on **Developer Mode** in Discord's advanced settings, then
right-click the user or role and choose **Copy ID**.

An embed's **Timestamp** is either a fixed date and time or **Time of sending**, the moment the action runs.

All text fields accept [template variables](/guide/concepts/#template-variables). They are filled in when the
action runs, so a message can carry the current scene, song or viewer count.

## Mentions and notifications

By default a message notifies nobody, even when its text mentions someone, `@everyone` or `@here`. The mention
is still shown in Discord, it just does not ping. On the **Mentions** tab you choose for users and for roles
whether nobody, everyone mentioned or only the IDs you list is notified, and whether `@everyone` and `@here`
notify. The preview lists the mentions that will not notify anyone.

## The preview

The preview below the editor changes as you type and shows the message the way Discord draws it: formatting,
code, quotes, headings, spoilers, mentions, links, embed colors and fields side by side. It never sends
anything to Discord. A few things it cannot show exactly:

- **Images are not loaded.** The avatar, author and footer icons, images and thumbnails appear as a placeholder
  naming the site they come from.
- **Names of users, roles and channels** are only known to your Discord server. A mention shows its ID; give
  an ID a name under **Names in the preview** on the **Mentions** tab to see it there. Those names are only used
  for the preview.
- **Variables** appear as you wrote them, because they are only filled in when the action runs.

Problems the message has, such as a text that is too long or a link that is not a web address, are listed
above the preview while you edit, and the action does not send a message that has any of them.

## Sending a test message

**Send test message** sends the message to Discord right away, so you can check how it really looks. It asks
first and then shows **Send now** and **Cancel**; nothing is sent until you choose **Send now**. Any edit
cancels a pending test. Variables are sent as written, not filled in.

A test uses the saved webhook you picked. With **Custom URL**, it only works with a URL you entered since you
opened the editor, because a stored URL is never shown again; enter it again to send a test.

## Limits

Discord refuses a message that breaks one of its limits, and Macro Deck checks them before sending:

| What | Limit |
| --- | --- |
| Message text | 2000 characters |
| Embeds per message | 10 |
| Embed title | 256 characters |
| Embed description | 4096 characters |
| Fields per embed | 25 |
| Field name, field value | 256 and 1024 characters |
| Author name, footer text | 256 and 2048 characters |
| Text of all embeds together | 6000 characters |
| Username | 80 characters, and it must not contain "clyde" or "discord" |
| IDs allowed to be notified | 100 users and 100 roles |

A message needs text or at least one embed. When Discord limits how often a webhook may post, Macro Deck waits
and tries once more if the wait is 5 seconds or less; otherwise the action fails with a message saying so.

## Older apps and the Companion app

The visual editor saves the message through a newer part of Macro Deck. An older desktop app and the Companion
app show the editor but do not save the changes made in it. They show the action's plain settings instead
where they cannot draw the editor, and the message there is a list of keys and values such as `content` and
`embeds.0.title`. Edit rich messages in the current desktop app.
