use std::fs;
use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};
use tauri::{AppHandle, Manager};

use crate::logging;
use crate::release_notes::{self, ReleaseNotes};
use crate::updater;

const STORE_FILE_NAME: &str = "post-update-changelog.json";

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PostUpdateChangelog {
    pub version: String,
    #[serde(default)]
    pub notes: Option<String>,
    #[serde(default)]
    pub notes_url: Option<String>,
    pub published_at: Option<String>,
}

fn store_path(dir: &Path) -> PathBuf {
    dir.join(STORE_FILE_NAME)
}

pub(crate) fn write(dir: &Path, entry: &PostUpdateChangelog) -> std::io::Result<()> {
    fs::create_dir_all(dir)?;
    let json = serde_json::to_string(entry).map_err(std::io::Error::other)?;
    fs::write(store_path(dir), json)
}

pub(crate) fn read(dir: &Path) -> Option<PostUpdateChangelog> {
    let raw = fs::read_to_string(store_path(dir)).ok()?;
    serde_json::from_str(&raw).ok()
}

pub(crate) fn clear(dir: &Path) {
    let path = store_path(dir);
    if path.exists() {
        if let Err(error) = fs::remove_file(&path) {
            logging::warn(&format!(
                "[post-update-changelog] could not remove {}: {error}",
                path.display()
            ));
        }
    }
}

// The installed version can be newer than the one the notes were fetched for, because the install
// resolves the feed again, so notes only carry over when both versions match.
pub(crate) fn entry_for_install(
    version: &str,
    known_version: Option<&str>,
    notes: Option<&str>,
    notes_url: Option<&str>,
    published_at: Option<&str>,
) -> Option<PostUpdateChangelog> {
    let mut entry = PostUpdateChangelog {
        version: version.to_string(),
        notes: None,
        notes_url: None,
        published_at: published_at.map(str::to_string),
    };
    if known_version == Some(version) {
        let notes = notes.filter(|notes| !notes.trim().is_empty());
        if notes.is_none() && notes_url.is_none() {
            return None;
        }
        entry.notes = notes.map(str::to_string);
    }
    Some(entry)
}

#[derive(Debug, PartialEq, Eq)]
pub(crate) enum Resolution {
    Show {
        changelog: PostUpdateChangelog,
        persist: bool,
    },
    Clear,
}

pub(crate) fn resolve_pending(
    mut entry: PostUpdateChangelog,
    fetched: Option<ReleaseNotes>,
) -> Resolution {
    if entry.notes.is_some() {
        return Resolution::Show {
            changelog: entry,
            persist: false,
        };
    }
    match fetched {
        Some(ReleaseNotes::Published(notes)) => {
            entry.notes = Some(notes);
            Resolution::Show {
                changelog: entry,
                persist: true,
            }
        }
        Some(ReleaseNotes::Empty) => Resolution::Clear,
        Some(ReleaseNotes::Unavailable) | None => {
            entry.notes_url = Some(release_notes::release_page_url(&entry.version));
            Resolution::Show {
                changelog: entry,
                persist: false,
            }
        }
    }
}

// A stored entry whose version is not the running one belongs to an install
// that never completed (a declined UAC prompt, a failed installer) and is discarded.
pub(crate) fn pending_for(dir: &Path, current_version: &str) -> Option<PostUpdateChangelog> {
    match read(dir) {
        Some(entry) if entry.version == current_version => Some(entry),
        _ => {
            clear(dir);
            None
        }
    }
}

fn config_dir(app: &AppHandle) -> PathBuf {
    app.path()
        .app_config_dir()
        .unwrap_or_else(|_| Path::new(".").to_path_buf())
}

pub fn remember(
    app: &AppHandle,
    version: &str,
    known_version: Option<&str>,
    notes: Option<&str>,
    notes_url: Option<&str>,
    published_at: Option<&str>,
) {
    let dir = config_dir(app);
    let Some(entry) = entry_for_install(version, known_version, notes, notes_url, published_at)
    else {
        clear(&dir);
        return;
    };
    if let Err(error) = write(&dir, &entry) {
        logging::warn(&format!(
            "[post-update-changelog] could not remember the changelog of {version}: {error}"
        ));
    }
}

pub fn forget(app: &AppHandle) {
    clear(&config_dir(app));
}

#[tauri::command]
pub async fn get_post_update_changelog(app: AppHandle) -> Option<PostUpdateChangelog> {
    let dir = config_dir(&app);
    let current_version = updater::current_version(&app);
    let entry = pending_for(&dir, &current_version)?;
    let fetched = match entry.notes {
        Some(_) => None,
        None => Some(release_notes::fetch(&entry.version, &current_version).await),
    };
    match resolve_pending(entry, fetched) {
        Resolution::Clear => {
            clear(&dir);
            None
        }
        Resolution::Show { changelog, persist } => {
            // The user may have dismissed the entry while the notes were being fetched.
            if persist && read(&dir).is_some_and(|stored| stored.version == changelog.version) {
                if let Err(error) = write(&dir, &changelog) {
                    logging::warn(&format!(
                        "[post-update-changelog] could not keep the notes of {}: {error}",
                        changelog.version
                    ));
                }
            }
            Some(changelog)
        }
    }
}

#[tauri::command]
pub fn dismiss_post_update_changelog(app: AppHandle) {
    forget(&app);
}

#[cfg(test)]
mod tests {
    use super::*;

    const RELEASE_PAGE: &str = "https://github.com/Macro-Deck-App/Macro-Deck/releases/tag/v3.2.0";

    fn temp_dir(line: u32) -> PathBuf {
        std::env::temp_dir().join(format!(
            "macro-deck-post-update-changelog-{}-{line}",
            std::process::id()
        ))
    }

    fn with_notes(notes: &str) -> PostUpdateChangelog {
        entry_for_install(
            "3.2.0",
            Some("3.2.0"),
            Some(notes),
            None,
            Some("2026-09-16"),
        )
        .unwrap()
    }

    fn without_notes() -> PostUpdateChangelog {
        entry_for_install("3.2.0", None, None, None, None).unwrap()
    }

    #[test]
    fn the_changelog_of_the_installed_version_is_offered_after_the_restart() {
        let dir = temp_dir(line!());
        let entry = with_notes("## Fixes\n- a fix");
        write(&dir, &entry).unwrap();

        assert_eq!(pending_for(&dir, "3.2.0"), Some(entry));
        assert!(
            read(&dir).is_some(),
            "the entry stays until the user dismissed it"
        );

        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn an_install_that_never_completed_offers_nothing_and_forgets_the_entry() {
        let dir = temp_dir(line!());
        write(&dir, &with_notes("notes")).unwrap();

        assert_eq!(pending_for(&dir, "3.1.0"), None);
        assert_eq!(read(&dir), None);

        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_fresh_install_has_no_changelog_to_show() {
        let dir = temp_dir(line!());

        assert_eq!(pending_for(&dir, "3.2.0"), None);
    }

    #[test]
    fn dismissing_removes_the_entry() {
        let dir = temp_dir(line!());
        write(&dir, &with_notes("notes")).unwrap();

        clear(&dir);

        assert_eq!(pending_for(&dir, "3.2.0"), None);
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn installing_the_version_whose_notes_were_fetched_keeps_those_notes() {
        let entry = with_notes("## Fixes");
        assert_eq!(entry.notes.as_deref(), Some("## Fixes"));
        assert_eq!(entry.published_at.as_deref(), Some("2026-09-16"));
    }

    #[test]
    fn a_release_known_to_have_no_notes_remembers_nothing() {
        assert_eq!(
            entry_for_install("3.2.0", Some("3.2.0"), None, None, None),
            None
        );
        assert_eq!(
            entry_for_install("3.2.0", Some("3.2.0"), Some("  \n"), None, None),
            None
        );
    }

    #[test]
    fn notes_that_could_not_be_fetched_remember_the_version_for_later() {
        let entry =
            entry_for_install("3.2.0", Some("3.2.0"), None, Some(RELEASE_PAGE), None).unwrap();
        assert_eq!(entry.version, "3.2.0");
        assert_eq!(entry.notes, None);
    }

    #[test]
    fn installing_a_newer_version_than_the_one_checked_never_takes_the_older_notes() {
        let entry = entry_for_install(
            "3.3.0",
            Some("3.2.0"),
            Some("notes of 3.2.0"),
            None,
            Some("2026-09-20"),
        )
        .unwrap();
        assert_eq!(entry.version, "3.3.0");
        assert_eq!(entry.notes, None);

        assert_eq!(
            entry_for_install("3.3.0", Some("3.2.0"), None, None, None).map(|entry| entry.version),
            Some("3.3.0".to_string()),
            "an empty older release says nothing about the newer one"
        );
    }

    #[test]
    fn stored_notes_are_shown_without_asking_github_again() {
        assert_eq!(
            resolve_pending(with_notes("notes"), None),
            Resolution::Show {
                changelog: with_notes("notes"),
                persist: false
            }
        );
    }

    #[test]
    fn notes_fetched_after_the_restart_are_shown_and_kept() {
        let Resolution::Show { changelog, persist } = resolve_pending(
            without_notes(),
            Some(ReleaseNotes::Published("## Fixes".to_string())),
        ) else {
            panic!("published notes must be shown");
        };
        assert_eq!(changelog.notes.as_deref(), Some("## Fixes"));
        assert_eq!(changelog.notes_url, None);
        assert!(persist);
    }

    #[test]
    fn a_release_without_notes_shows_no_whats_new() {
        assert_eq!(
            resolve_pending(without_notes(), Some(ReleaseNotes::Empty)),
            Resolution::Clear
        );
    }

    #[test]
    fn unreachable_github_shows_a_link_to_the_release() {
        let Resolution::Show { changelog, persist } =
            resolve_pending(without_notes(), Some(ReleaseNotes::Unavailable))
        else {
            panic!("the link must be shown");
        };
        assert_eq!(changelog.notes, None);
        assert_eq!(changelog.notes_url.as_deref(), Some(RELEASE_PAGE));
        assert!(!persist);
    }

    #[test]
    fn an_entry_written_by_an_older_version_is_still_read() {
        let dir = temp_dir(line!());
        fs::create_dir_all(&dir).unwrap();
        fs::write(
            store_path(&dir),
            r#"{"version":"3.2.0","notes":"old notes","publishedAt":"2026-09-16"}"#,
        )
        .unwrap();

        let entry = pending_for(&dir, "3.2.0").unwrap();
        assert_eq!(entry.notes.as_deref(), Some("old notes"));
        assert_eq!(entry.notes_url, None);
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn an_unreadable_file_is_treated_as_no_changelog() {
        let dir = temp_dir(line!());
        fs::create_dir_all(&dir).unwrap();
        fs::write(store_path(&dir), "not json").unwrap();

        assert_eq!(pending_for(&dir, "3.2.0"), None);
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn the_entry_serializes_as_camel_case() {
        let value = serde_json::to_value(with_notes("notes")).unwrap();
        assert_eq!(value["version"], "3.2.0");
        assert_eq!(value["notes"], "notes");
        assert_eq!(value["notesUrl"], serde_json::Value::Null);
        assert_eq!(value["publishedAt"], "2026-09-16");
    }
}
