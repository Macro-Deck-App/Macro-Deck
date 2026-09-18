---
title: Projects and Store listing
description: Create a Project in the Creator Portal and fill in the Store listing.
---

## Create a Project

On the **Dashboard**, select **Create Project**.

![The Create Project dialog with Plugin / Integration selected](../../../assets/creator-portal/create-project.png)

| Field | Example | Notes |
| --- | --- | --- |
| Project type | Plugin / Integration | Cannot be changed later. |
| Display Name | `Weather Deck` | Up to 100 characters, unique in the Store. Can be changed later. |
| Package ID | `com.example.weather-deck` | Permanent. For a plugin it must equal the `id` in its `manifest.json`. |

A Project starts as **Draft**. Nothing is public until a review is approved.

## Store listing

**General Information** holds what the Store shows about the Project.

![General Information of an icon pack: Display Name, Summary, Description, Author link, Licence and Tags](../../../assets/creator-portal/iconpack-general.png)

| Field | Example |
| --- | --- |
| Summary | `Buttons that greet your deck.` |
| Description | Markdown, shown on the package page |
| Author link | `https://github.com/example` |
| Licence | `MIT` |
| Tags | `utilities`, `home-automation` (up to ten) |

- **Save** stages the changes. They go live with your next approved submission.
- A plugin's description, publisher and licence come from its `manifest.json`, so the portal only asks
  for the Summary and Tags.
- The Store listing falls back to the Display Name when there is no Summary.

## Images

| Image | Plugin | Icon pack |
| --- | --- | --- |
| Icon | From `manifest.json`, added when you create a release | Uploaded under **Images** |
| Screenshots | Up to ten | Up to ten |

A Project cannot be submitted without an icon. New images wait for review like the listing text.
Reordering screenshots that were already approved takes effect immediately.

## Move to an Organization

A Project can move into an Organization you are an **Owner** of, so the Store publishes it under the
Organization's name. Open **General Information**, choose the Organization under **Transfer** and
select **Move**, then confirm with the Project's Display Name.

![The Transfer panel with Example Labs chosen and the Move button](../../../assets/creator-portal/project-transfer.png)

- The Store shows the Organization as publisher from the next release on, and releases are signed
  with the Organization's key. The Package ID stays the same, so installed copies keep updating.
- For a plugin, the next build must name the Organization as `publisher.name` in its `manifest.json`.
- Every member of the Organization can work on the Project. It leaves your personal context.
- Only your own Project, or one in an Organization you own, can be moved.
- A Project cannot move while a review is in progress, an approved version waits to be released, or
  a Store change is still running.

## Delete or unlist

- **Delete Project** works until a version has been published. It removes the versions, builds and
  images.
- After that, a Project can only be unlisted: it disappears from the Store, but installed copies keep
  working and the Package ID stays taken. You can list it again later.
