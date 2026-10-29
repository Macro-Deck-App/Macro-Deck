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

Some parts depend on others, so selecting one also selects what it needs. You can clear those again, and
the dialog then explains what will not work.

Integrations, app settings and accounts depend on each other: the integrations hold the keys that protect
the HTTPS certificate in the app settings and the sign-in and pairing keys of the accounts. Profiles,
scripts, automations and plugins need integrations, and accounts need profiles. So selecting any of these
groups also selects integrations, app settings and accounts, and with them profiles, plugins, variables and
icons. Clear what you do not want to replace. If you leave out one of integrations, app settings and
accounts and the backup was made on another computer or some time ago, everyone may be signed out, devices
may have to be paired again, or HTTPS may have to be set up again.

A backup from another computer that was made with a version before Macro Deck 3.0.0-beta.5 has no pairing
key, so devices have to be paired again after restoring it.

Imported fonts belong to **Icons, icon packs and fonts**. Restoring that group from a backup made before
Macro Deck could import fonts removes the fonts you imported since.

The restore is applied the next time Macro Deck starts. The installed app restarts on its own. Before that,
Macro Deck checks the backup you chose (step 1 of 2) and then makes a backup marked **Safety backup** (step 2
of 2), so you can go back.

A backup from a newer version of Macro Deck that stores its data in a way this version does not know cannot
be restored. Macro Deck says so while it checks the backup, before anything changes; update Macro Deck
first, then restore it. A backup whose data this version cannot convert, for example because the backup is
damaged, is refused the same way and says that it cannot be restored with this version.

If the restore cannot finish, for example because Macro Deck was closed while it was being applied, Macro
Deck puts your data back the way it was before the restore and shows a **Restore failed** notification.
Nothing from the backup is kept, so start the restore again. If the notification asks you to restart, some
data has not been put back yet: restart Macro Deck and it finishes the job.

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
