---
title: Publish an icon pack
description: Create a version, upload the .macroDeckIconPack and submit it for review.
---

An icon pack needs no repository. The version is the file you upload.

```text
Start this version  ->  upload .macroDeckIconPack  ->  Add to submission  ->  Submit for Review
```

## 1. Export the pack

In Macro Deck, open **Icon packs**, open the pack's menu and select **Export pack**. You get a
`.macroDeckIconPack` file.

Before you export, open the pack's menu, select **Edit pack** and set **AI-created icons**. The setting is
saved in the exported pack's `pack.json` as the same [`ai` declaration](/reference/manifest/#ai) plugins
use. **Not declared** is never treated as free of AI.

## 2. Create the Project

Create a Project of type **Icon Pack** and fill in [General Information](/creator-portal/projects/#store-listing).
Upload the **Icon** under **Images**; an icon pack cannot be submitted without one.

## 3. Start a version

Open **Versions**.

![Next version with Version and Changelog fields and Start this version](../../../assets/creator-portal/iconpack-versions-empty.png)

| Field | Example |
| --- | --- |
| Version | `1.0.0` |
| Changelog | `First release: 120 line icons.` |

Select **Start this version**. Only one version can be open at a time.

## 4. Upload the package

![Version 1.0.0 in preparation with the upload area for the .macroDeckIconPack](../../../assets/creator-portal/iconpack-upload.png)

Drop the `.macroDeckIconPack` on the upload area or choose it. The portal checks size, checksum and
contents before the version can be submitted. Until it is submitted, you can replace the file.

## 5. Submit

Select **Add to submission**, then **Submit for Review** in the Project header. Continue with
[Review and release](/creator-portal/review/).

## Release an update

Export the pack again, then **Start this version** with a higher version, for example `1.1.0`, upload
and submit. A declined version stays editable: replace the file and submit it again.
