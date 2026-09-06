// Where the bootstrapper is currently running from (issue #355). A DMG-launched
// app runs either from the read-only mounted image or, under Gatekeeper
// quarantine, from a translocated read-only copy - neither is a valid install:
// the initial setup must not write there, and the in-place updater would be
// swapping a read-only bundle. main.rs gates startup on `current().is_installed()`
// and offers to move the app to /Applications instead of starting normally.
// Windows and Linux install through their own installers and are always
// `Installed`.

use std::path::{Path, PathBuf};
use std::sync::OnceLock;

#[cfg(target_os = "macos")]
use std::process::{Command, Stdio};

#[cfg(target_os = "macos")]
use tauri_plugin_dialog::{DialogExt, MessageDialogButtons, MessageDialogKind};

#[cfg(target_os = "macos")]
use crate::localization::{self, keys};
use crate::logging;
#[cfg(target_os = "macos")]
use crate::window;

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum InstallState {
    Installed,
    MountedImage,
    Translocated,
}

impl InstallState {
    pub fn is_installed(self) -> bool {
        matches!(self, InstallState::Installed)
    }
}

const APP_TRANSLOCATION_COMPONENT: &str = "AppTranslocation";

fn classify<W: FnOnce(&Path) -> bool>(
    target_os: &str,
    current_exe: &Path,
    directory_is_writable: W,
) -> InstallState {
    if target_os != "macos" {
        return InstallState::Installed;
    }

    if current_exe
        .components()
        .any(|component| component.as_os_str() == APP_TRANSLOCATION_COMPONENT)
    {
        return InstallState::Translocated;
    }

    if current_exe.starts_with("/Volumes/") {
        // A legitimate install on an external writable drive must not be
        // blocked, so this only gates a genuinely read-only mount. When the
        // bundle shape cannot be recognized, probe the executable's own
        // parent directory instead - still writable-or-not, just without a
        // bundle root to key off.
        let probe_dir = bundle_root(current_exe)
            .and_then(Path::parent)
            .or_else(|| current_exe.parent());
        return match probe_dir {
            Some(dir) if directory_is_writable(dir) => InstallState::Installed,
            _ => InstallState::MountedImage,
        };
    }

    InstallState::Installed
}

fn directory_is_writable(dir: &Path) -> bool {
    let probe = dir.join(format!(".macro-deck-write-probe-{}", std::process::id()));
    match std::fs::File::create(&probe) {
        Ok(_) => {
            let _ = std::fs::remove_file(&probe);
            true
        }
        Err(_) => false,
    }
}

static INSTALL_STATE: OnceLock<InstallState> = OnceLock::new();

pub fn current() -> InstallState {
    *INSTALL_STATE.get_or_init(|| match std::env::current_exe() {
        Ok(current_exe) => classify(std::env::consts::OS, &current_exe, directory_is_writable),
        Err(error) => {
            // A diagnostic failure must not block startup.
            logging::warn(&format!(
                "[install] could not resolve the running executable: {error}"
            ));
            InstallState::Installed
        }
    })
}

fn bundle_root(current_exe: &Path) -> Option<&Path> {
    let macos_dir = current_exe.parent()?;
    if macos_dir.file_name()? != "MacOS" {
        return None;
    }
    let contents_dir = macos_dir.parent()?;
    if contents_dir.file_name()? != "Contents" {
        return None;
    }
    let bundle_dir = contents_dir.parent()?;
    if bundle_dir.extension()? != "app" {
        return None;
    }
    Some(bundle_dir)
}

const APPLICATIONS_DIR: &str = "/Applications";

#[cfg_attr(not(target_os = "macos"), allow(dead_code))]
fn destination_for(bundle_root: &Path) -> Option<PathBuf> {
    Some(Path::new(APPLICATIONS_DIR).join(bundle_root.file_name()?))
}

#[cfg(target_os = "macos")]
pub async fn prompt_and_install(app: tauri::AppHandle) {
    let current_exe = std::env::current_exe().ok();
    let bundle_dir = current_exe.as_deref().and_then(bundle_root);
    let destination = bundle_dir.and_then(destination_for);

    let (Some(bundle_dir), Some(destination)) = (bundle_dir, destination) else {
        window::show_error_dialog(
            &app,
            &localization::t(keys::ERRORS_COULD_NOT_BE_MOVED_TITLE),
            &localization::t(keys::ERRORS_INSTALL_LOCATION_UNKNOWN),
        );
        crate::request_quit(&app);
        return;
    };

    let message = if destination.exists() {
        localization::t(keys::ERRORS_MOVE_PROMPT_REPLACES_EXISTING)
    } else {
        localization::t(keys::ERRORS_MOVE_PROMPT)
    };

    let should_move = app
        .dialog()
        .message(message)
        .title(localization::t(keys::ERRORS_MOVE_TO_APPLICATIONS_TITLE))
        .kind(MessageDialogKind::Info)
        .buttons(MessageDialogButtons::OkCancelCustom(
            localization::t(keys::ERRORS_MOVE_BUTTON_MOVE_TO_APPLICATIONS),
            localization::t(keys::TRAY_QUIT),
        ))
        .blocking_show();

    if !should_move {
        crate::request_quit(&app);
        return;
    }

    match move_to_applications(bundle_dir, &destination) {
        Ok(()) => {
            spawn_relaunch_waiter(&destination);
            crate::request_quit(&app);
        }
        Err(detail) => {
            window::show_error_dialog(
                &app,
                &localization::t(keys::ERRORS_COULD_NOT_BE_MOVED_TITLE),
                &localization::t_args(keys::ERRORS_MOVE_FAILED, &[("detail", &detail)]),
            );
            crate::request_quit(&app);
        }
    }
}

#[cfg(target_os = "macos")]
fn move_to_applications(source_bundle: &Path, destination: &Path) -> Result<(), String> {
    let pid = std::process::id();
    let file_name = destination
        .file_name()
        .ok_or_else(|| localization::t(keys::ERRORS_MOVE_NO_DESTINATION_FILE_NAME))?
        .to_string_lossy();
    let staging = Path::new(APPLICATIONS_DIR).join(format!(".{file_name}.{pid}.new"));
    let backup = Path::new(APPLICATIONS_DIR).join(format!(".{file_name}.{pid}.old"));

    if staging.exists() {
        let _ = std::fs::remove_dir_all(&staging);
    }

    logging::info(&format!(
        "[install] copying {} to {}",
        source_bundle.display(),
        staging.display()
    ));
    // ditto preserves symlinks/xattrs/the code signature (and the stapled
    // notarization ticket); --noqtn drops quarantine so the installed copy is
    // not translocated again on the next launch. When this process itself
    // runs translocated, `source_bundle` IS the translocated mount - that
    // read-only view is signature-faithful, and resolving the real path
    // (SecTranslocateCreateOriginalPathForURL) is private API, so copying
    // from the translocated view rather than the original is deliberate.
    let output = Command::new("/usr/bin/ditto")
        .arg("--noqtn")
        .arg(source_bundle)
        .arg(&staging)
        .output()
        .map_err(|error| {
            localization::t_args(
                keys::ERRORS_MOVE_COPY_FAILED,
                &[("error", &error.to_string())],
            )
        })?;
    if !output.status.success() {
        let _ = std::fs::remove_dir_all(&staging);
        let stderr = String::from_utf8_lossy(&output.stderr).into_owned();
        return Err(localization::t_args(
            keys::ERRORS_MOVE_COPY_FAILED,
            &[("error", &stderr)],
        ));
    }

    let mut replaced_existing = false;
    if destination.exists() {
        if let Err(error) = std::fs::rename(destination, &backup) {
            let _ = std::fs::remove_dir_all(&staging);
            return Err(localization::t_args(
                keys::ERRORS_MOVE_SET_ASIDE_FAILED,
                &[("error", &error.to_string())],
            ));
        }
        replaced_existing = true;
    }

    if let Err(error) = std::fs::rename(&staging, destination) {
        if replaced_existing {
            let _ = std::fs::rename(&backup, destination);
        }
        let _ = std::fs::remove_dir_all(&staging);
        return Err(localization::t_args(
            keys::ERRORS_MOVE_INSTALL_FAILED,
            &[("error", &error.to_string())],
        ));
    }
    logging::info(&format!("[install] installed to {}", destination.display()));

    if backup.exists() {
        if let Err(error) = std::fs::remove_dir_all(&backup) {
            logging::warn(&format!(
                "[install] could not remove the backup at {}: {error}",
                backup.display()
            ));
        }
    }

    Ok(())
}

#[cfg(target_os = "macos")]
fn spawn_relaunch_waiter(destination: &Path) {
    const WAIT_AND_OPEN: &str = r#"p=$PPID; n=0; while kill -0 "$p" 2>/dev/null && [ "$n" -lt 100 ]; do sleep 0.1; n=$((n+1)); done; exec /usr/bin/open "$0""#;
    match Command::new("/bin/sh")
        .arg("-c")
        .arg(WAIT_AND_OPEN)
        .arg(destination)
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()
    {
        Ok(_) => logging::info(&format!(
            "[install] relaunch waiter spawned for {}",
            destination.display()
        )),
        Err(error) => logging::warn(&format!(
            "[install] could not spawn the relaunch waiter: {error}"
        )),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::cell::Cell;

    #[test]
    fn non_macos_targets_are_always_installed_even_under_volumes() {
        for os in ["linux", "windows"] {
            assert_eq!(
                classify(
                    os,
                    Path::new("/Volumes/Macro Deck/Macro Deck.app/Contents/MacOS/MacroDeck"),
                    |_| panic!("the writability probe must not run off macOS"),
                ),
                InstallState::Installed
            );
        }
    }

    #[test]
    fn a_read_only_volume_mount_is_a_mounted_image() {
        assert_eq!(
            classify(
                "macos",
                Path::new("/Volumes/Macro Deck/Macro Deck.app/Contents/MacOS/MacroDeck"),
                |_| false,
            ),
            InstallState::MountedImage
        );
    }

    #[test]
    fn a_writable_volume_mount_is_installed() {
        assert_eq!(
            classify(
                "macos",
                Path::new("/Volumes/Macro Deck/Macro Deck.app/Contents/MacOS/MacroDeck"),
                |_| true,
            ),
            InstallState::Installed
        );
    }

    #[test]
    fn a_translocated_path_is_translocated_without_probing_writability() {
        let probed = Cell::new(false);
        let state = classify(
            "macos",
            Path::new(
                "/private/var/folders/aa/bb/T/AppTranslocation/1234-UUID/d/Macro Deck.app/Contents/MacOS/MacroDeck",
            ),
            |_| {
                probed.set(true);
                true
            },
        );
        assert_eq!(state, InstallState::Translocated);
        assert!(
            !probed.get(),
            "translocation must be decided before any writability probe runs"
        );
    }

    #[test]
    fn a_volume_name_that_merely_contains_apptranslocation_is_not_translocated() {
        assert_eq!(
            classify(
                "macos",
                Path::new(
                    "/Volumes/MyAppTranslocationDisk/Macro Deck.app/Contents/MacOS/MacroDeck"
                ),
                |_| true,
            ),
            InstallState::Installed
        );
    }

    #[test]
    fn an_installed_applications_bundle_never_probes_writability() {
        let state = classify(
            "macos",
            Path::new("/Applications/Macro Deck.app/Contents/MacOS/MacroDeck"),
            |_| panic!("an /Applications launch must never probe writability"),
        );
        assert_eq!(state, InstallState::Installed);
    }

    #[test]
    fn a_per_user_applications_bundle_is_installed() {
        assert_eq!(
            classify(
                "macos",
                Path::new("/Users/x/Applications/Macro Deck.app/Contents/MacOS/MacroDeck"),
                |_| panic!("a per-user Applications launch must never probe writability"),
            ),
            InstallState::Installed
        );
    }

    #[test]
    fn only_installed_reports_is_installed() {
        assert!(InstallState::Installed.is_installed());
        assert!(!InstallState::MountedImage.is_installed());
        assert!(!InstallState::Translocated.is_installed());
    }

    #[test]
    fn bundle_root_finds_the_app_directory() {
        assert_eq!(
            bundle_root(Path::new(
                "/Applications/Macro Deck.app/Contents/MacOS/MacroDeck"
            )),
            Some(Path::new("/Applications/Macro Deck.app"))
        );
    }

    #[test]
    fn bundle_root_rejects_a_path_outside_a_bundle() {
        assert_eq!(bundle_root(Path::new("/usr/bin/MacroDeck")), None);
    }

    #[test]
    fn bundle_root_rejects_a_root_without_an_app_extension() {
        assert_eq!(bundle_root(Path::new("/tmp/Contents/MacOS/x")), None);
    }

    #[test]
    fn bundle_root_finds_the_translocated_app_directory() {
        assert_eq!(
            bundle_root(Path::new(
                "/private/var/folders/aa/bb/T/AppTranslocation/1234-UUID/d/Macro Deck.app/Contents/MacOS/MacroDeck"
            )),
            Some(Path::new(
                "/private/var/folders/aa/bb/T/AppTranslocation/1234-UUID/d/Macro Deck.app"
            ))
        );
    }

    #[test]
    fn destination_for_maps_into_applications() {
        assert_eq!(
            destination_for(Path::new("/Volumes/Macro Deck/Macro Deck.app")),
            Some(PathBuf::from("/Applications/Macro Deck.app"))
        );
        assert_eq!(
            destination_for(Path::new(
                "/Volumes/Macro Deck Development/Macro Deck Development.app"
            )),
            Some(PathBuf::from("/Applications/Macro Deck Development.app"))
        );
    }
}
