# ADR 0100: Store entries are shared as https links, and `macrodeck://` carries only a package id

Status: Accepted

## Context

People want to send each other a Store entry. A link has to work for someone who does not have Macro Deck
yet, so it must be a web address; a custom URL scheme has no reliable browser fallback when no application
handles it. It also has to open the entry in the desktop app for someone who does. Both directions are
attack surface: a web page, a chat message or another program can craft any `macrodeck://` string, and a
package's name and description are text a third party wrote.

The bootstrapper already turns OS hand-offs into one queue for the UI (opened files, ADR 0006), the host
already knows which registry it trusts (ADR 0044), and the registry has no field for unlisted packages: a
package is unlisted when `index.json` does not name it, and withdrawn when `security.json` says so.

The public Store website is not part of this repository. Only the identity provider's Terraform
configuration (another repository) names `store.macro-deck.app`, and no repository holds the site yet.

## Decision

### One canonical, registry-less address

A Store entry is shared as `https://store.macro-deck.app/<package-id>`. The package id is the whole
address: no kind, no registry, no source. Package ids are unique across kinds, because the Creator Portal's
project table holds one id per project; a host that meets an id in two kinds resolves it to nothing. The registry's pattern allows two dotted parts, so `robots.txt` is a valid id: the site's static and
well-known routes win over package ids, and only a string matching the pattern is looked up at all.

Only entries of the official registry resolve. Nothing in either address can name another registry, URL,
manifest or file, and no mechanism for that is added later. The desktop app copies this address and never
an internal registry URL.

### `macrodeck://` is a closed grammar

The scheme is `macrodeck://<destination>/<argument>`, and the destination is an allow-list. Today there is
one: `macrodeck://store/<package-id>`. The argument must match the registry's package id pattern and is at
most 128 characters. A link with a query, fragment, userinfo, port, extra path segment, percent-encoding,
whitespace, control character or non-ASCII character is rejected whole, never repaired. A new destination
is a new allow-listed host with its own argument grammar, added deliberately.

The link is data for a lookup and nothing else. It is never turned into a command line, a path, a request
to another host, or a navigation target.

### Registration belongs to the packaging

The scheme is registered by the installers and bundles from `release.conf.json`, the same place as the
file associations, so a development build never claims it. The bootstrapper receives the URL the way it
receives files: argv on Windows and Linux (cold start, or forwarded by the single-instance lock to the
running app) and an open event on macOS. Everything is validated in the bootstrapper, queued, announced to
the UI and taken by it.

### The desktop app validates on its own

The UI asks the host to resolve the id. The host answers only for the official registry, and only for a
package the registry lists and has not withdrawn; the kind and id used to navigate come from that answer,
not from the link. A link is therefore never trusted for having come from the website. While the registry
has not loaded yet, the UI waits and retries before it reports failure.

### The website resolves the same way and renders on the server

The Store site resolves `<package-id>` against its synchronised copy of the official registry, and the two
sides agree on the outcome: a listed package opens, and everything else (unknown, unlisted, withdrawn) is
not found. A crawler must receive the entry's metadata in the first HTML response, so the page is rendered on
the server: title, description, canonical URL, `og:title`, `og:description`, `og:image`, `og:url`,
`og:type` and Twitter card tags, all specific to the entry. Package text reaches the page only through the
framework's escaping bindings, never as markup, and is never concatenated into a head tag by hand.

The page offers an explicit **Open in Macro Deck** action pointing at `macrodeck://store/<package-id>`,
and a "Don't have Macro Deck yet?" section linking to the download page. It does not try to detect an
installation with timers or redirects, and it never launches the scheme without a user gesture.

The site is an attack surface of its own, so these hold for it:

- The canonical URL, `og:url` and the `macrodeck://` link are built from a constant origin and the matched
  id, never from the Host header, forwarded headers or the request URL. Query parameters are ignored and
  dropped, and a redirect for a trailing slash or letter case is a relative path built from the matched id.
- The registry snapshot is verified as the host verifies it: pinned root, sequence anti-rollback, maximum
  age and withdrawal first. Stale or unverifiable data fails closed.
- Unknown, unlisted and withdrawn answer with the same status, body and headers, `noindex`, a short cache
  time and no cookie. Only 200 pages are cached for long, and the cache key is the path alone.
- Package text is plain text: length-limited, stripped of control and bidirectional characters, never
  markdown or HTML. Structured data (JSON-LD) and any transferred server state are produced by a JSON
  serializer that escapes `<`, `>`, `&`, U+2028 and U+2029.
- Images and links come only from the signed registry. Icons are re-hosted on the site's own origin as
  raster images, never hot-linked; outbound links must be `https` and carry `rel="noopener nofollow ugc"`.
  A server-side card renderer must not fetch private addresses or follow redirects, and limits time and size.
- A strict Content Security Policy without third-party or un-nonced inline script, `frame-ancestors 'none'`
  so the launch button cannot be framed, `nosniff`, a Referrer-Policy and HSTS.

The desktop app has a plain Copy link action for the address, switched off until the site is live. The Web Share API is left to the website,
because the desktop WebViews support it inconsistently and it cannot be verified on every one of them.

Two choices stay with the site's authors: whether the preview image is the package icon (a square card,
whatever size the publisher supplied) or a generated branded card with the icon on it (better previews,
but an image-rendering dependency to approve), and whether an unlisted entry may be opened by its direct
address. Unlisted entries are treated as not found until that is decided, which matches how the desktop
Store hides them.

## Consequences

- Removing or renaming `store.macro-deck.app` breaks every link ever shared, and the `macrodeck` scheme
  cannot be reclaimed once other software depends on it. Both are long-lived names.
- The registered scheme makes Macro Deck a target for links from any web page, so the grammar stays
  closed and the validator's tests are part of the contract.
- AppImage does not register a scheme handler by itself and is not supported.
- An update restart on macOS and Linux re-executes the original arguments, so a link that started the app can open its entry a second time.
- The website and its metadata contract are not implemented here.

## References

- [Store links](https://docs.macro-deck.app/reference/store-links/)
- [ADR 0006](0006-tauri-bootstrapper-is-the-installed-entry-point.md)
- [ADR 0044](0044-plugin-and-store-trust-enforcement.md)
