---
title: YouTube
description: Connect your YouTube channel with your own Google Cloud project, and what the YouTube integration can and cannot do.
---

The **YouTube** integration shows your live stream's state as variables, fires events for Super Chats,
Super Stickers and memberships, shows your live chat in a widget where you can moderate it, and changes your
stream's title, description and tags, starts an ad break, and starts and ends the stream.

YouTube does not offer a shared login that Macro Deck could use for everyone. You connect your channel
through a Google Cloud project of your own, which takes about ten minutes once. The project is free, and its
quota and permissions belong to you alone.

## Create the Google Cloud project

1. Open the [Google Cloud console](https://console.cloud.google.com/) with the Google account that owns your
   channel, and create a project, for example **Macro Deck**.
2. Open **APIs & Services > Library**, search for **YouTube Data API v3** and click **Enable**.
3. Open **APIs & Services > OAuth consent screen** (in newer consoles **Google Auth Platform**). Choose
   **External**, enter an app name and your email address, and save.
4. Under **Audience** (or **Test users**), add the Google account that owns your channel as a test user.
5. Open **APIs & Services > Credentials**, click **Create credentials > OAuth client ID** and choose the
   application type **TVs and Limited Input devices**. Any other type is refused when you sign in.
6. Copy the **Client ID** and the **Client secret** that Google shows.

## Connect the channel

1. In Macro Deck, open **Integrations**, find **YouTube** and start its setup.
2. Paste the **Client ID** and **Client secret**, and continue.
3. Macro Deck shows a code. Open the link next to it, sign in with the Google account that owns your channel,
   enter the code and allow access. Google warns that it has not verified the app: that is your own project,
   so continue.
4. Macro Deck names the connection after your channel, for example **YouTube (My Channel)**.

To connect a second channel, run the setup again. Channels that use the same client ID share that project's
quota.

### Signing in again every week

While your project's consent screen is in testing, Google ends the sign-in after 7 days. **Integrations** then
shows that the YouTube sign-in expired; reconnect the channel with its **Fix** button and enter the client ID
and secret again. To stay signed in, publish the consent screen in the Google Cloud console. Google may ask you
to verify the app for that, because YouTube access is a sensitive permission.

## Quota

Google gives every project 10,000 units of quota a day. It resets at midnight Pacific Time. Reading the stream
state costs about one unit a minute, reading the chat a few units every few seconds while you are live, and
every change, such as a chat message, a ban or a new title, costs 50.

Macro Deck keeps count of what it spends. Near the limit it reads the chat less often, and when the quota is
almost used up it stops reading until midnight Pacific Time; **Integrations** shows a note until then. Actions
still work until Google refuses them. If Google raised your project's quota, enter the new limit under **Daily
quota (units)** in the advanced settings of the setup. The count starts again when Macro Deck restarts, so it
is an estimate; Google's own count is in the Google Cloud console under **APIs & Services > YouTube Data API
v3 > Quotas**.

## What you can use

- **Variables** named `youtube_<channel>_is_live`, `youtube_<channel>_stream_title`,
  `youtube_<channel>_viewer_count`, `youtube_<channel>_like_count`, `youtube_<channel>_subscriber_count`,
  `youtube_<channel>_uptime_seconds`, `youtube_<channel>_stream_thumbnail_url` and
  `youtube_<channel>_display_name`. `<channel>` is your channel's handle, or its ID when the handle is taken by
  another connected channel. Viewers stay empty when you hide the viewer count, subscribers when you hide the
  subscriber count; YouTube rounds the subscriber count.
- **Events**: **Stream Online**, **Stream Offline**, **Super Chat**, **Super Sticker**, **New Member**,
  **Member Milestone** and **Gifted Memberships**, plus **YouTube Event (Advanced)** for any of them. Use them
  in automations.
- **Actions**: **Send Chat Message**, **Set Stream Title**, **Set Stream Description**, **Set Stream Tags**,
  **Start Ad Break**, **Go Live** and **End Stream**.
- **Widgets**: **YouTube Chat** and **YouTube Stream Stats**, offered once a channel is connected. They work
  like the [Twitch Chat and Twitch Stream Stats widgets](/guide/concepts/#widgets).

The title, description and tags apply to the broadcast that is live, or to the next scheduled one when you
are not live. **Go Live** starts the next scheduled broadcast whose stream is already arriving at YouTube:
start your streaming software first. A broadcast that starts by itself when the stream arrives, and the
**Stream now** broadcast in YouTube Studio, are started by YouTube and not by this action. **Start Ad Break**
only works while you are live and your channel can show mid-roll ads.

Chat, Super Chats and membership events arrive only while a broadcast is live and Macro Deck is not waiting
for the quota to reset. Events that happened while Macro Deck was not running are not fired later, and
starting Macro Deck while you are already live does not fire **Stream Online**.

## Moderating YouTube chat

Press the **YouTube Chat** widget to open the chat with moderation, as for
[Twitch](/guide/concepts/#moderating-from-the-chat-widgets). Deleting a message, timing out and banning work
as on Twitch. **Unban** only lifts a ban that you made through Macro Deck during the current broadcast, and
only until Macro Deck restarts or the YouTube integration is set up again, because YouTube does not tell
Macro Deck which bans exist. Lift any other ban in YouTube Studio.

## Not available

YouTube does not offer these to apps, so the integration cannot do them: raids, shoutouts, VIPs, channel
points, predictions, hype trains, goals, clips, stream markers, chat modes, clearing the chat, a chatter
count and follow events.
