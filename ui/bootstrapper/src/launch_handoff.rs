use std::path::{Path, PathBuf};
use std::process::Command;
use std::time::Duration;

use serde::{Deserialize, Serialize};
use tauri::plugin::TauriPlugin;
use tauri::{AppHandle, Emitter, Manager, Wry};

use crate::{host, logging, updater};

const RECORD_FILE: &str = "launch-handoff.json";

const RECORD_MAX_AGE: Duration = Duration::from_secs(30);

#[derive(Debug, Serialize, Deserialize)]
pub struct LaunchRecord {
    version: String,
    executable: PathBuf,
}

pub fn plugin() -> TauriPlugin<Wry> {
    tauri::plugin::Builder::new("launch-handoff")
        .setup(|app, _api| {
            announce(app);
            Ok(())
        })
        .build()
}

fn record_path(app: &AppHandle) -> Option<PathBuf> {
    app.path()
        .app_config_dir()
        .ok()
        .map(|dir| dir.join(RECORD_FILE))
}

fn announce(app: &AppHandle) {
    if !host::is_packaged() {
        return;
    }
    let (Some(path), Some(executable)) = (record_path(app), host::shell_executable()) else {
        return;
    };
    let record = LaunchRecord {
        version: updater::current_version(app),
        executable,
    };
    let written = path
        .parent()
        .map_or(Ok(()), std::fs::create_dir_all)
        .and_then(|()| std::fs::write(&path, serde_json::to_vec(&record).unwrap_or_default()));
    if let Err(error) = written {
        logging::warn(&format!(
            "[handoff] could not write the launch record: {error}"
        ));
    }
}

pub fn clear(app: &AppHandle) {
    if let Some(path) = record_path(app) {
        let _ = std::fs::remove_file(path);
    }
}

// The record is trusted like the loopback secret next to it: only processes of this user can write it.
pub fn handoff_target(
    own_version: &str,
    record: Option<LaunchRecord>,
    age: Option<Duration>,
    is_file: impl Fn(&Path) -> bool,
) -> Option<PathBuf> {
    let record = record?;
    let fresh = age.is_some_and(|age| age <= RECORD_MAX_AGE);
    let other_build = record.version.trim() != own_version.trim();
    (fresh && other_build && record.executable.is_absolute() && is_file(&record.executable))
        .then_some(record.executable)
}

pub fn take_target(app: &AppHandle) -> Option<PathBuf> {
    if !host::is_packaged() {
        return None;
    }
    let path = record_path(app)?;
    let age = std::fs::metadata(&path)
        .and_then(|metadata| metadata.modified())
        .ok()
        .and_then(|modified| modified.elapsed().ok());
    let record = std::fs::read(&path)
        .ok()
        .and_then(|raw| serde_json::from_slice::<LaunchRecord>(&raw).ok());
    let _ = std::fs::remove_file(&path);
    handoff_target(&updater::current_version(app), record, age, Path::is_file)
}

pub fn hand_over(app: &AppHandle, executable: PathBuf, argv: Vec<String>, cwd: String) {
    logging::info(&format!(
        "[handoff] {} was launched while this build runs; handing over to it",
        executable.display()
    ));
    if updater::is_installing(app) || !crate::claim_quit() {
        logging::info("[handoff] a quit or an update is already in progress; not handing over");
        return;
    }
    if let Err(error) = app.emit(crate::HOST_STOPPING_EVENT, ()) {
        logging::error(&format!(
            "[handoff] could not emit host-stopping event: {error}"
        ));
    }
    let app = app.clone();
    tauri::async_runtime::spawn(async move {
        host::stop(&app).await;
        let mut command = Command::new(&executable);
        command
            .args(argv.iter().skip(1))
            .env(host::RELAUNCH_MARKER_ENVIRONMENT_VARIABLE, "1");
        if Path::new(&cwd).is_dir() {
            command.current_dir(&cwd);
        }
        match command.spawn() {
            Ok(_) => app.exit(0),
            Err(error) => {
                logging::error(&format!(
                    "[handoff] could not start {}: {error}; restarting this build",
                    executable.display()
                ));
                host::relaunch(&app);
            }
        }
    });
}

#[cfg(test)]
mod tests {
    use super::*;

    fn record(version: &str, executable: PathBuf) -> Option<LaunchRecord> {
        Some(LaunchRecord {
            version: version.to_string(),
            executable,
        })
    }

    fn installed(name: &str) -> PathBuf {
        std::env::temp_dir().join(name)
    }

    const JUST_NOW: Option<Duration> = Some(Duration::from_secs(1));

    #[test]
    fn a_fresh_launch_of_another_version_is_handed_over() {
        assert_eq!(
            handoff_target(
                "3.0.0-beta.19",
                record(
                    "3.0.0-beta.20",
                    installed("Macro Deck_3.0.0-beta.20_amd64.AppImage")
                ),
                JUST_NOW,
                |_| true
            ),
            Some(installed("Macro Deck_3.0.0-beta.20_amd64.AppImage"))
        );
    }

    #[test]
    fn a_launch_of_the_same_version_stays_in_the_running_instance() {
        assert_eq!(
            handoff_target(
                "3.0.0-beta.19",
                record("3.0.0-beta.19", installed("md.AppImage")),
                JUST_NOW,
                |_| true
            ),
            None
        );
    }

    #[test]
    fn a_stale_or_undated_record_is_ignored() {
        assert_eq!(
            handoff_target(
                "3.0.0-beta.19",
                record("3.0.0-beta.20", installed("md.AppImage")),
                Some(Duration::from_secs(31)),
                |_| true
            ),
            None
        );
        assert_eq!(
            handoff_target(
                "3.0.0-beta.19",
                record("3.0.0-beta.20", installed("md.AppImage")),
                None,
                |_| true
            ),
            None
        );
    }

    #[test]
    fn a_relative_or_missing_executable_is_never_started() {
        assert_eq!(
            handoff_target(
                "3.0.0-beta.19",
                record("3.0.0-beta.20", PathBuf::from("md.AppImage")),
                JUST_NOW,
                |_| true
            ),
            None
        );
        assert_eq!(
            handoff_target(
                "3.0.0-beta.19",
                record("3.0.0-beta.20", installed("md.AppImage")),
                JUST_NOW,
                |_| false
            ),
            None
        );
    }

    #[test]
    fn no_record_means_no_handover() {
        assert_eq!(
            handoff_target("3.0.0-beta.19", None, JUST_NOW, |_| true),
            None
        );
    }

    #[test]
    fn the_launch_record_is_written_before_single_instance_can_exit_the_process() {
        let main = include_str!("main.rs");
        let handoff = main
            .find(".plugin(launch_handoff::plugin())")
            .expect("main.rs must register the launch handoff plugin");
        let single_instance = main
            .find(".plugin(tauri_plugin_single_instance::init(")
            .expect("main.rs must register the single-instance plugin");
        assert!(
            handoff < single_instance,
            "plugins set up in registration order, and a second instance exits inside single-instance's setup"
        );
    }
}
