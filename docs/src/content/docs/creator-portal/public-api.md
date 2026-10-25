---
title: Public API
description: Read your Projects, their status and their analytics with an API token, for dashboards, badges and CI.
---

The Public API reads what the Creator Portal shows you about your Projects: the list with each
Project's status, the status of one Project, and its analytics. It only reads and never changes anything.

```text
https://creators.macro-deck.app/api/public/v1
```

## API tokens

Every request carries an API token. Create one in the Creator Portal under **Settings** > **API tokens**.

- **Personal or Organization.** A token made in your personal context reads your personal Projects. A token
  made in an Organization's context reads that Organization's Projects. Switch the context first, then create
  the token. Only the Organization's Owner can create, see and revoke its tokens.
- **Access.** Choose what the token may read:

  | Scope | Allows |
  | --- | --- |
  | `projects:read` | [List Projects](#list-projects) and [Project status](#project-status) |
  | `analytics:read` | [Analytics](#analytics) |

- **Expiry.** 30 days, 90 days, 1 year or never. An expired token stops working and stays in the list until you
  revoke it.
- **Shown once.** The token is shown only when you create it. Store it somewhere safe, such as a CI secret. If you
  lose it, revoke it and create a new one.
- **Revoke** a token in the same list. It stops working immediately.
- At most **25 active tokens** per personal account or Organization.

An Organization token stops working when the person who created it is no longer the Organization's Owner, for
example after transferring ownership or leaving. The list then shows it as **Creator no longer Owner**. Revoke it
and let the current Owner create a new one.

Send the token as a Bearer token:

```bash
curl -H "Authorization: Bearer $MACRO_DECK_TOKEN" \
  https://creators.macro-deck.app/api/public/v1/projects
```

A token only ever reads its own context. Projects of other people, of other Organizations, and of your other
contexts answer `404 Not Found`, exactly like a Package ID that does not exist.

## List Projects

`GET /projects` - scope `projects:read`

Every Project of the token's context, sorted by Display Name.

```json
{
  "projects": [
    {
      "id": "0b6f2c1e-6d0f-4d41-9a51-6a2f4a9e3c11",
      "packageId": "com.example.weather",
      "displayName": "Weather",
      "type": "PluginIntegration",
      "status": "Published",
      "hasPublishedVersion": true,
      "publishedVersion": { "version": "1.4.0", "publishedAt": "2026-10-02T09:15:00+00:00" },
      "latestVersion": { "version": "1.5.0", "status": "InReview" },
      "changesRequested": false,
      "readyToRelease": false,
      "enforcement": null,
      "unlistedAt": null,
      "repositoryUnreachableSince": null,
      "createdAt": "2026-03-11T18:02:41+00:00"
    }
  ]
}
```

## Project status

`GET /projects/{packageId}` - scope `projects:read`

One Project, with the fields above.

| Field | Meaning |
| --- | --- |
| `type` | `PluginIntegration` or `IconPack` |
| `status` | `Draft`, `InReview`, `Published` or `Declined`. A published Project reads `InReview` while a new submission is in review; `hasPublishedVersion` tells you whether it is in the Store. |
| `publishedVersion` | The newest version in the Store, or `null` |
| `latestVersion` | The newest version you started, with its status: `Draft`, `InReview`, `Approved`, `Published`, `Declined`, `Withdrawn` or `Removed` |
| `changesRequested` | A moderator sent the open submission back. The feedback is in the Creator Portal. |
| `readyToRelease` | A version was approved and waits for you to release it |
| `enforcement` | `Unlisted` or `Revoked` while an administrator holds the Project out of the Store, otherwise `null` |
| `unlistedAt` | When the package was taken out of the Store index, otherwise `null` |
| `repositoryUnreachableSince` | Since when the Store cannot reach the repository it publishes from, otherwise `null` |

## Analytics

`GET /projects/{packageId}/analytics?from=YYYY-MM-DD&to=YYYY-MM-DD` - scope `analytics:read`

The download analytics and the ratings of one Project, the same figures as the **Analytics** and **Ratings**
pages of the Creator Portal. Both query parameters are optional: without `from` the range starts at the first
day with data, without `to` it ends yesterday (UTC). A range can span at most 1830 days.

```json
{
  "packageId": "com.example.weather",
  "downloads": {
    "from": "2026-10-01",
    "to": "2026-10-03",
    "firstImportedDay": "2026-03-01",
    "days": [
      { "day": "2026-10-01", "installs": 41, "updates": 210, "repairs": 2, "imported": true, "provisional": false },
      { "day": "2026-10-02", "installs": 38, "updates": 15, "repairs": 0, "imported": true, "provisional": false },
      { "day": "2026-10-03", "installs": 44, "updates": 9, "repairs": 1, "imported": true, "provisional": true }
    ],
    "totals": { "installs": 123, "updates": 234, "repairs": 3 },
    "versions": [
      { "version": "1.4.0", "installs": 120, "updates": 234, "repairs": 3 },
      { "version": "1.3.2", "installs": 3, "updates": 0, "repairs": 0 }
    ],
    "linkDownloadsTotal": 57,
    "allTimeDownloads": 18342
  },
  "ratings": {
    "average": 4.6,
    "ratingCount": 52,
    "reviewCount": 17,
    "distribution": [
      { "stars": 5, "count": 40 },
      { "stars": 4, "count": 8 },
      { "stars": 3, "count": 2 },
      { "stars": 2, "count": 1 },
      { "stars": 1, "count": 1 }
    ]
  }
}
```

- `days` has one entry for every day of the range. `imported: false` means that day has no data yet, which is
  not the same as zero downloads.
- `provisional: true` marks a recent day whose figures may still grow.
- `linkDownloadsTotal` counts downloads through the Store download link, all time. `allTimeDownloads` is the
  all-time Store total.
- `ratings` has the figures only. The text of individual reviews is not part of the Public API.

## Errors

Errors use [problem details](https://www.rfc-editor.org/rfc/rfc9457) (`application/problem+json`), most with a
`code`.

| Status | When |
| --- | --- |
| `400` | A malformed date, `from` after `to`, or a range that is too long |
| `401` | No token, or a malformed, unknown, revoked or expired one, or one that no longer works (see [API tokens](#api-tokens)) |
| `403` | The token lacks the scope (`insufficient-scope`), or it was sent to an endpoint outside the Public API (`api-token-not-allowed`) |
| `404` | The Project does not exist, or the token's context does not reach it |
| `429` | Rate limit reached. Wait the seconds in `Retry-After`. |
| `503` | The token could not be checked right now (`api-token-check-unavailable`). Try again shortly. |

## Rate limits

Limits count all tokens of one personal account or one Organization together, in windows of 15 minutes:

| Endpoints | Requests per window |
| --- | --- |
| [List Projects](#list-projects) and [Project status](#project-status) | 300 |
| [Analytics](#analytics) | 60 |
| All Public API requests together | 1200 |

Creating more tokens does not raise the limits. Analytics change at most once a day, so polling it more often
than every few minutes gains nothing.

## Versions

The version is part of the path. Within `v1`, fields and endpoints are only added, never removed or changed in
meaning, so ignore fields you do not know. A change that would break a client becomes `v2`, and `v1` keeps
working beside it for a transition period announced in advance.
