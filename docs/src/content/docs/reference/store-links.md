---
title: Store links
description: The address every Store entry can be shared under, the macrodeck:// link that opens it in the app, where it works, and what it refuses.
---

Every entry in the official Store, whether a plugin, an icon pack or a template, has one address you can
share, for example in a README, a Discord message or a video description.

:::caution
The Store website that this address opens is not live yet. Until it is, the `https` address does not show a
page. The `macrodeck://` link described below already works in Macro Deck.
:::

## Sharing an entry

Use the entry's package id, the reverse-DNS name from its manifest:

```text
https://store.macro-deck.app/<package-id>
```

The page shows the entry, an **Open in Macro Deck** button, and download links for people who do not have
Macro Deck yet. Macro Deck will offer **Copy link** on an entry's Store page once the website is live.

Only entries of the official Macro Deck registry have an address. An unknown id, an entry that is not listed
in the Store and a withdrawn package all answer "not found". The address has no way to name another registry,
manifest or download URL.

## Opening an entry from a link

**Open in Macro Deck** uses the `macrodeck://` scheme:

```text
macrodeck://store/<package-id>
```

If Macro Deck is not running, it starts and shows the entry. If it is running, it comes to the front and
shows the entry. Share the `https` address, not the `macrodeck://` one: a browser cannot fall back to
anything when no application handles a custom scheme, and the web page can.

The link is accepted only in this exact form, with a package id as defined by the
[manifest](/reference/manifest/): lowercase, dot-separated, at least two parts, at most 128 characters.
Anything else is ignored and written to the log, without a message on screen: another destination, a second
path segment, a query or fragment, a user name or port, percent-encoded characters, spaces, control
characters or a second address appended to the id.

A launch that carries a link is treated as that link alone, so anything else on the same command line is
ignored. The id is only ever looked up. Macro Deck asks its own copy of the official registry whether the package
exists, is listed and is not withdrawn, and opens the page it finds there. Nothing in the link is used as a
path, a command or a web address. If the registry has not loaded yet, Macro Deck waits for it for about half
a minute before it says the Store is not available.

## Where the link works

| Platform | How the scheme is registered |
| --- | --- |
| Windows | The installer registers `macrodeck` for the current user. |
| macOS | The app bundle declares the scheme, so it works from the first launch from Applications. |
| Linux | The DEB and RPM packages declare it in the desktop entry, which the package manager registers. |

An AppImage is not supported: it registers nothing by itself, and whether a desktop integration tool passes
the link on depends on that tool. Development builds never register the scheme, so they do not take the
link over from an installed Macro Deck.

If Macro Deck was started by a link and later restarts itself to finish an update, macOS and Linux may
open the same entry once more.

If two Macro Deck installations claim the scheme, the operating system decides which one opens.

## For plugin authors

Link to your plugin with the `https` address. It stays the same across versions, and it starts working when
the package is listed in the Store.
