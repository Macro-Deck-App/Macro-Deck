---
title: RSS feeds
description: How Macro Deck follows RSS and Atom feeds, shows their articles in widgets, opens them on the device you tap, refreshes them on demand, and runs actions when new articles arrive.
---

Macro Deck can follow news sites, blogs, podcasts and anything else that publishes an RSS 2.0 or Atom feed.
It checks your feeds in the background, keeps their newest articles, and shows them in two widgets. You can
add as many feeds as you like.

## Adding a feed

Open **Integrations**, choose **RSS feeds** and add a configuration:

- **Feed address**: the address of the feed, usually the link behind a website's RSS icon, for example
  `https://example.com/feed.xml`. An address without `https://` gets it added.
- **Configuration name**: how the feed is called in widgets, triggers and variables. Leave it empty to use
  the title the feed gives itself.
- **Group**: optional. Feeds with the same group, for example `News` or `Podcasts`, can be shown together
  in one widget.
- **Refresh every (minutes)**: how often Macro Deck checks the feed, 30 minutes unless you change it. The
  shortest interval is 5 minutes.

Under the advanced options you can filter what Macro Deck keeps:

- **Only articles containing**: words separated by commas. An article is kept when its title or
  description contains one of them.
- **Skip articles containing**: words separated by commas. An article whose title or description contains
  one of them is left out.
- **Only these categories**: categories separated by commas. An article is kept when the feed lists one of
  them for it.
- **Allow local network addresses**: see [Feeds in your own network](#feeds-in-your-own-network).

Macro Deck reads the feed once before it saves the configuration and tells you when the address does not
answer in time, cannot be reached, answers with an error, is not an RSS or Atom feed, or is larger than 5 MB.

The list of configurations shows for each feed whether its last refresh worked.

### Feeds in your own network

For your safety Macro Deck refuses feed addresses in local and private networks, such as `localhost`,
`192.168.…`, `10.…` or a name that points to one of them. This also applies when a feed on the internet
redirects there. It keeps a feed you added from reaching your router, printer or other devices in your
network.

If you serve a feed yourself, for example from a NAS or a home server, turn on **Allow local network
addresses** for that one feed. Images of its articles may then come from your network too.

## The Feed List widget

The **Feed List** widget lists the newest articles with their title, the date, and the feed's name when it
shows more than one feed. Choose in its settings:

- **Show**: **All feeds**, **One feed** or **One group**, and which feed or group.
- **Articles**: how many articles it lists, up to 10.
- **Show description** and **Show images**: whether each article shows the start of its text and a small
  image, when the feed has one.
- **When tapped**: what tapping an article does, see [Tapping an article](#tapping-an-article).

Articles that arrived since you added the feed and that you have not tapped yet are shown in bold.

## The Featured Article widget

The **Featured Article** widget shows the newest article large: the feed's name at the top, then the
article's image, its title in up to two lines, the date, and up to three lines of description when **Show
description** is on. The image takes whatever room the text leaves and is cropped to fill it; on a small
widget it shrinks or is left out, so the title always stays readable. Its settings are the same as the Feed
List's, plus:

- **Articles to cycle through** and **Next article after (seconds)**: with a number of seconds above 0, the
  widget takes turns showing that many of the newest articles. With 0 it always shows the newest one.

Both widgets update by themselves after every refresh. They only show what Macro Deck already fetched, so
adding more widgets does not add requests to the feed.

## Tapping an article

**When tapped** offers three choices:

- **Open in the browser** (the default): the article opens in the browser of the device you tapped on, for
  example your phone with the web client, not on the computer Macro Deck runs on. When that device cannot
  open links, for example a hardware deck or an older Companion app, Macro Deck shows the details instead,
  with the address so you can copy it.
- **Show details**: a dialog with the image, title, author, date, description and address, and an **Open**
  button that opens the article on that device.
- **Run the widget's actions only**: nothing opens; only the widget's actions run.

With every choice, a tap marks the article as read and fires the **Feed article opened** trigger. To run
actions for the tapped article, open the widget's actions, add a **Feed article opened** trigger and leave
its **Widget** on **This widget**. The actions can use the article's values, such as `{{ event.title }}` or
`{{ event.url }}`. An empty part of the widget runs the widget's ordinary **Short press** actions.

Some browsers block a link that opens a little after the tap. The web client then shows the link in a bar
you can tap to open it.

## Actions

**Refresh feeds** checks feeds for new articles right away instead of waiting for their next refresh. Choose
a **Feed** to refresh only that one; left empty, it refreshes all of them. The action waits until the
refresh is done, at most 2 minutes, so actions after it already see the new articles. It fails when the
chosen feed no longer exists or a feed could not be refreshed, for the same reasons the feed's
configuration shows. It asks the feed's server right away, even while Macro Deck is waiting longer
after an error or because the server asked it to, so do not run it on a short timer. Otherwise it follows
the same rules as the automatic refresh, including [Feeds in your own network](#feeds-in-your-own-network)
and the limit of four feeds at the same time.

## Triggers

RSS feeds offers these events for automations and widget flows:

- **New feed article**, when a feed has an article that was not there before. With many new articles at
  once, it fires for the newest 20.
- **Feed updated**, when the articles of a feed changed on a refresh.
- **Feed update failed**, when a refresh fails after the one before worked, or fails for a different reason.
- **Feed article opened**, when an article is tapped on a Feed List or Featured Article widget.

Each event can be limited to one **Feed**; left empty, it runs for all of them. **Feed article opened** can
also be limited to one **Widget**. The actions it runs can use the event's values:

| Value | Holds |
| --- | --- |
| `feed`, `feedTitle` | The id of the feed's configuration and its name |
| `title`, `url`, `description` | The article's title, link and description as plain text |
| `author`, `published`, `categories` | Its author, when it was published as an ISO 8601 time, and its categories separated by commas |
| `error` | **Feed update failed** only: `timeout`, `blocked-address`, `invalid-feed`, `too-large`, `network`, or `http-` with the status the server answered, such as `http-404` |
| `widget` | **Feed article opened** only: the id of the widget the article was tapped on |

**Feed updated** carries the newest article of the feed.

Articles a feed already has when you add it never fire **New feed article**, and Macro Deck remembers which
articles it has seen, so a restart does not fire them again. Articles your filters leave out count as seen
too: when you loosen a filter later, older articles show up in the widgets without firing the trigger.
Changing a feed's address starts over, as if you had added a new feed.

## Variables

Each feed gets its own variables, named after the configuration name it had when you added it, for example
`feeds_tech_news_latest_title`. Renaming the configuration later keeps the names. The **Variables** page
shows the exact names.

| Variable | Contains |
| --- | --- |
| `…_latest_title`, `…_latest_url`, `…_latest_description`, `…_latest_author` | The newest article's title, link, description and author |
| `…_latest_published` | When the newest article was published, as an ISO 8601 time |
| `…_item_count` | How many articles Macro Deck keeps for the feed, at most 50 |
| `…_unread_count` | Articles that arrived since you added the feed and have not been tapped in a widget |
| `…_last_updated` | When the feed was last checked successfully, as an ISO 8601 time |
| `…_update_status` | `ok`, `not-modified` (checked, nothing changed), `error`, or `pending` before the first check |
| `feeds_all_item_count`, `feeds_all_unread_count` | The same counts for all feeds together |

## How often Macro Deck checks

Each feed is checked at its own interval. Macro Deck asks the server whether the feed changed since the last
check, so an unchanged feed costs very little. It checks at most four feeds at the same time and waits at
most 20 seconds for an answer.

When a check fails, Macro Deck tries again after 1 minute, then waits twice as long after each further
failure, up to an hour. When the server asks it to wait longer, Macro Deck waits that long, up to 6 hours.
After the next successful check it goes back to the feed's interval.

Macro Deck only contacts the feeds you add and the servers their images come from. It sends nothing but an
ordinary request for the feed. Article text is shown as plain text: formatting, scripts and embedded content
of the feed are never run.
