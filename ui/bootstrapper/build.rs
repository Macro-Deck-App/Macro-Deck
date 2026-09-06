use std::env;

const BUILD_CHANNEL_ENVIRONMENT_VARIABLE: &str = "MACRODECK_BUILD_CHANNEL";
const DEFAULT_BUILD_CHANNEL: &str = "Development";
const RELEASE_VERSION_ENVIRONMENT_VARIABLE: &str = "MACRODECK_RELEASE_VERSION";

fn main() {
    println!("cargo:rerun-if-env-changed={BUILD_CHANNEL_ENVIRONMENT_VARIABLE}");

    let build_channel = env::var(BUILD_CHANNEL_ENVIRONMENT_VARIABLE)
        .unwrap_or_else(|_| DEFAULT_BUILD_CHANNEL.to_string());
    if build_channel != "Development" && build_channel != "Production" {
        panic!(
            "{BUILD_CHANNEL_ENVIRONMENT_VARIABLE} must be Development or Production; beta is derived from the version"
        );
    }
    println!("cargo:rustc-env=MACRODECK_BUILD_CHANNEL={build_channel}");

    // The RPM is bundled at a mapped version (RPM syntax forbids "-beta.N"), so
    // CI bakes the true release version here for the updater to compare against
    // instead of the config version (issue #271). Empty in dev/local builds,
    // where the config version is already authoritative.
    println!("cargo:rerun-if-env-changed={RELEASE_VERSION_ENVIRONMENT_VARIABLE}");
    let release_version = env::var(RELEASE_VERSION_ENVIRONMENT_VARIABLE).unwrap_or_default();
    println!("cargo:rustc-env={RELEASE_VERSION_ENVIRONMENT_VARIABLE}={release_version}");

    tauri_build::try_build(tauri_build::Attributes::new().app_manifest(
        tauri_build::AppManifest::new().commands(&[
            "get_host_port",
            "get_shell_info",
            "get_cursor_position",
            "open_external",
            "show_open_dialog",
            "save_file",
            "get_hide_dock_icon",
            "set_hide_dock_icon",
            "take_opened_files",
            "take_menu_action",
            "set_hotkey_capture",
            "check_for_update",
            "install_update",
            "get_update_state",
            "request_update_check",
            "cancel_update_download",
            "get_update_channel",
            "set_update_channel",
            "get_update_mode",
            "set_update_mode",
        ]),
    ))
    .expect("failed to run tauri-build");
}
