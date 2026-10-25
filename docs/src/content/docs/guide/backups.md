---
title: Backups
description: Create backups, keep the recovery key safe, and restore a backup, also one from another computer.
---

**Settings > Backups** keeps full backups of your profiles, scripts, automations, variables, icons,
imported fonts, plugins, integrations, app settings and account. Every backup is encrypted.

## Create a backup

Choose **Create backup**. Under **Schedule** you can also let Macro Deck make backups on a schedule and
before Macro Deck or plugin updates. **Keep latest** sets how many backups stay before older ones are
deleted.

While a backup is made, Macro Deck shows which step it is on and how far it is, and once it can estimate it,
about how long is left. Until then the bar only shows that it is working. Backups that Macro Deck makes on its
own show the same progress: before a Macro Deck update, before a plugin update and before a restore.

A running backup also appears in the notification center with its progress and a **Cancel** button, so you
can follow it after closing its dialog; a finished backup stays there as **Backup completed**, a failed one as
an error. **Cancel backup** stops a backup while it runs and keeps nothing of it. When the backup was made before an
update, a plugin update or a restore, cancelling it cancels that too, so nothing changes without its backup.

Use **Download** in a backup's menu to save it as a `.macroDeckBackup` file, for example to keep a copy
somewhere else or to move to another computer. In the Macro Deck app you choose where to save it; in a
browser it goes to your downloads.

## The recovery key

Backups are encrypted with a recovery key. Without it, a backup can only be restored on the installation
that made it. Show it in **Settings > Backups > Recovery key** with **Show recovery key** and keep it
somewhere safe, separate from your backups.

When you regenerate the recovery key, backups made before that still need the old key.

## Restore a backup

1. Open the backup's menu and choose **Restore…**.
2. Choose what to restore. Anything you select replaces the same data on this installation.
3. Confirm with **Restore**.

Imported fonts belong to **Icons, icon packs and fonts**. Restoring that group from a backup made before
Macro Deck could import fonts removes the fonts you imported since.

The restore is applied the next time Macro Deck starts. The installed app restarts on its own. Before that,
Macro Deck checks the backup you chose (step 1 of 2) and then makes a backup marked **Safety backup** (step 2
of 2), so you can go back.

## Restore a backup from another computer

1. On the other computer, use **Download** on the backup you want, and note that installation's recovery
   key.
2. On this computer, choose **Import backup** in **Settings > Backups** and pick the `.macroDeckBackup`
   file.
3. Macro Deck asks for the backup's recovery key. Enter the other installation's key and choose
   **Continue**. A wrong key is rejected and you can try again.
4. Choose what to restore and confirm.

Backups this installation cannot open on its own are marked **Needs recovery key** in the list. Choose
**Restore…** on one of them to enter its key.

:::caution[The recovery key changes with the restore]
Restoring integrations from another installation also brings its recovery key along: afterwards, this
installation uses the key you just entered. Backups this installation made before, including the
**Safety backup** made just before the restore, still need its previous recovery key. Save that key
before you restore.

If Macro Deck asks for a recovery key when it starts after such a restore, enter the other installation's
key.
:::
