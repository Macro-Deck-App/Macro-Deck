// Bootstrapper-owned preference for which update feed to poll (issue #272): a
// stable install can opt into beta updates, and a beta install can opt back
// out, without waiting for a reinstall. The preference has to live here rather
// than on the host: it must be readable before the host is up, and it decides
// which signed release metadata the shell fetches in the first place.
//
// Kept in its own module rather than growing updater.rs (already ~680 lines):
// this file owns the on-disk preference and its Tauri commands; updater.rs
// owns turning a resolved channel into feed URLs and picking the best
// candidate across them.

use std::fs;
use std::path::{Path, PathBuf};

use serde::Serialize;
use tauri::{AppHandle, Manager};

use crate::logging;
use crate::updater;

const STORE_FILE_NAME: &str = "update-channel";

#[derive(Clone, Copy, PartialEq, Eq, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub enum UpdateChannel {
    Stable,
    Beta,
}

impl UpdateChannel {
    pub fn as_str(self) -> &'static str {
        match self {
            UpdateChannel::Stable => "stable",
            UpdateChannel::Beta => "beta",
        }
    }
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct UpdateChannelStatus {
    pub channel: UpdateChannel,
    pub beta_enabled: bool,
}

impl UpdateChannelStatus {
    fn for_channel(channel: UpdateChannel) -> Self {
        Self {
            channel,
            beta_enabled: channel == UpdateChannel::Beta,
        }
    }
}

pub(crate) fn parse_stored(raw: &str) -> Option<UpdateChannel> {
    match raw.trim().to_ascii_lowercase().as_str() {
        "stable" => Some(UpdateChannel::Stable),
        "beta" => Some(UpdateChannel::Beta),
        _ => None,
    }
}

pub(crate) fn is_prerelease(version: &str) -> bool {
    semver::Version::parse(version)
        .map(|parsed| !parsed.pre.is_empty())
        .unwrap_or(false)
}

pub(crate) fn default_for_version(version: &str) -> UpdateChannel {
    if is_prerelease(version) {
        UpdateChannel::Beta
    } else {
        UpdateChannel::Stable
    }
}

pub(crate) fn resolve_channel(stored: Option<UpdateChannel>, version: &str) -> UpdateChannel {
    stored.unwrap_or_else(|| default_for_version(version))
}

fn store_path(dir: &Path) -> PathBuf {
    dir.join(STORE_FILE_NAME)
}

pub(crate) fn read_stored(dir: &Path) -> Option<UpdateChannel> {
    fs::read_to_string(store_path(dir))
        .ok()
        .and_then(|raw| parse_stored(&raw))
}

pub(crate) fn write_stored(dir: &Path, channel: UpdateChannel) -> std::io::Result<()> {
    fs::create_dir_all(dir)?;
    fs::write(store_path(dir), channel.as_str())
}

fn config_dir(app: &AppHandle) -> PathBuf {
    app.path()
        .app_config_dir()
        .unwrap_or_else(|_| Path::new(".").to_path_buf())
}

pub fn current(app: &AppHandle) -> UpdateChannel {
    let dir = config_dir(app);
    let stored = read_stored(&dir);
    let channel = resolve_channel(stored, &updater::current_version(app));
    if stored.is_none() {
        if let Err(error) = write_stored(&dir, channel) {
            logging::warn(&format!(
                "[update-channel] could not persist the default update channel: {error}"
            ));
        }
    }
    channel
}

pub fn set(app: &AppHandle, channel: UpdateChannel) -> Result<(), String> {
    write_stored(&config_dir(app), channel).map_err(|error| error.to_string())
}

#[tauri::command]
pub fn get_update_channel(app: AppHandle) -> UpdateChannelStatus {
    UpdateChannelStatus::for_channel(current(&app))
}

#[tauri::command]
pub fn set_update_channel(app: AppHandle, channel: String) -> Result<UpdateChannelStatus, String> {
    let parsed =
        parse_stored(&channel).ok_or_else(|| format!("unknown update channel: {channel}"))?;
    set(&app, parsed)?;
    Ok(UpdateChannelStatus::for_channel(parsed))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn default_for_version_is_stable_for_a_full_release() {
        assert_eq!(default_for_version("3.0.0"), UpdateChannel::Stable);
    }

    #[test]
    fn default_for_version_migrates_a_beta_rpm_install_to_beta() {
        assert_eq!(default_for_version("3.0.0-beta.42"), UpdateChannel::Beta);
    }

    #[test]
    fn an_unparseable_version_defaults_to_stable() {
        assert_eq!(default_for_version("not-a-version"), UpdateChannel::Stable);
        assert_eq!(default_for_version(""), UpdateChannel::Stable);
    }

    #[test]
    fn resolve_channel_prefers_an_explicit_choice_over_the_default_in_both_directions() {
        assert_eq!(
            resolve_channel(Some(UpdateChannel::Stable), "3.0.0-beta.1"),
            UpdateChannel::Stable
        );
        assert_eq!(
            resolve_channel(Some(UpdateChannel::Beta), "3.0.0"),
            UpdateChannel::Beta
        );
    }

    #[test]
    fn resolve_channel_falls_back_to_the_derived_default_when_nothing_is_stored() {
        assert_eq!(resolve_channel(None, "3.0.0"), UpdateChannel::Stable);
        assert_eq!(resolve_channel(None, "3.0.0-beta.1"), UpdateChannel::Beta);
    }

    #[test]
    fn parse_stored_distinguishes_unset_from_stored_stable() {
        assert_eq!(parse_stored("stable"), Some(UpdateChannel::Stable));
        assert_eq!(parse_stored(""), None);
        assert_eq!(parse_stored("   "), None);
        assert_eq!(parse_stored("nonsense"), None);
    }

    #[test]
    fn parse_stored_round_trips_a_written_token_case_and_whitespace_insensitively() {
        assert_eq!(parse_stored("beta\n"), Some(UpdateChannel::Beta));
        assert_eq!(parse_stored("Beta"), Some(UpdateChannel::Beta));
        assert_eq!(parse_stored("STABLE"), Some(UpdateChannel::Stable));
        assert_eq!(parse_stored(" stable "), Some(UpdateChannel::Stable));
    }

    #[test]
    fn the_channel_serializes_as_camel_case() {
        assert_eq!(
            serde_json::to_value(UpdateChannel::Stable).unwrap(),
            "stable"
        );
        assert_eq!(serde_json::to_value(UpdateChannel::Beta).unwrap(), "beta");
    }

    #[test]
    fn the_status_serializes_beta_enabled_as_camel_case() {
        let value =
            serde_json::to_value(UpdateChannelStatus::for_channel(UpdateChannel::Beta)).unwrap();
        assert_eq!(value["channel"], "beta");
        assert_eq!(value["betaEnabled"], true);

        let value =
            serde_json::to_value(UpdateChannelStatus::for_channel(UpdateChannel::Stable)).unwrap();
        assert_eq!(value["betaEnabled"], false);
    }

    #[test]
    fn a_written_channel_round_trips_through_disk() {
        let dir = std::env::temp_dir().join(format!(
            "macro-deck-update-channel-{}-{}",
            std::process::id(),
            line!()
        ));
        assert!(read_stored(&dir).is_none());

        write_stored(&dir, UpdateChannel::Beta).expect("write must succeed");
        assert_eq!(read_stored(&dir), Some(UpdateChannel::Beta));

        write_stored(&dir, UpdateChannel::Stable).expect("overwrite must succeed");
        assert_eq!(read_stored(&dir), Some(UpdateChannel::Stable));

        let _ = fs::remove_dir_all(&dir);
    }
}
