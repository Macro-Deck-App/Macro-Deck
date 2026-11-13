use std::ffi::OsString;
use std::io::Write;
use std::path::{Path, PathBuf};
use std::process::Stdio;
use std::sync::OnceLock;
use std::time::Duration;

use crate::host;
use crate::localization::{self, keys};
use crate::mcp_bridge::{self, HostSource};

pub const DISABLE_PLUGINS: &str = "--disable-plugins";

const INFO_OPTIONS: [(&str, &str); 4] = [
    ("--help", "--help"),
    ("-h", "--help"),
    ("--version", "--version"),
    ("--list-plugins", "--list-plugins"),
];

const RUNNING_PROBE_TIMEOUT: Duration = Duration::from_secs(2);

// The first matching entry of INFO_OPTIONS wins, so --help beats --version beats --list-plugins,
// the same order the host applies.
pub fn info_option(args: &[OsString]) -> Option<&'static str> {
    INFO_OPTIONS.iter().find_map(|(given, forwarded)| {
        args.iter()
            .skip(1)
            .any(|argument| argument == given)
            .then_some(*forwarded)
    })
}

pub fn wants_plugins_disabled(args: &[OsString]) -> bool {
    args.iter()
        .skip(1)
        .any(|argument| argument == DISABLE_PLUGINS)
}

pub fn plugins_disabled() -> bool {
    static DISABLED: OnceLock<bool> = OnceLock::new();
    *DISABLED.get_or_init(|| wants_plugins_disabled(&std::env::args_os().collect::<Vec<_>>()))
}

pub fn run_info(option: &str, resource_dir: Option<PathBuf>) -> i32 {
    attach_parent_console();
    let Some(binary) = host::host_binary_candidates_in(resource_dir)
        .into_iter()
        .find(|path| path.exists())
    else {
        eprintln!("{}", localization::t(keys::CLI_HOST_MISSING));
        return 1;
    };
    let config_dir = config_dir();
    relay(&binary, config_dir.as_deref(), option)
}

fn relay(binary: &Path, config_dir: Option<&Path>, option: &str) -> i32 {
    let mut command = host::host_command(binary, config_dir);
    let spawned = command
        .arg(option)
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn();
    let mut child = match spawned {
        Ok(child) => child,
        Err(error) => {
            eprintln!(
                "{}",
                localization::t_args(keys::CLI_HOST_FAILED, &[("error", &error.to_string())])
            );
            return 1;
        }
    };

    let stderr = child.stderr.take().map(|mut stderr| {
        std::thread::spawn(move || {
            let _ = std::io::copy(&mut stderr, &mut std::io::stderr());
        })
    });
    if let Some(mut stdout) = child.stdout.take() {
        let mut out = std::io::stdout().lock();
        let _ = std::io::copy(&mut stdout, &mut out);
        let _ = out.flush();
    }
    if let Some(thread) = stderr {
        let _ = thread.join();
    }

    match child.wait() {
        Ok(status) => status.code().unwrap_or(1),
        Err(error) => {
            eprintln!(
                "{}",
                localization::t_args(keys::CLI_HOST_FAILED, &[("error", &error.to_string())])
            );
            1
        }
    }
}

// Only a host that proves the loopback secret counts: a stale port file or another program on that
// port must not keep a safe-mode launch from starting.
pub fn already_running_exit_code() -> Option<i32> {
    let config_dir = config_dir()?;
    let source = HostSource::Installed { config_dir };
    let running =
        tauri::async_runtime::block_on(mcp_bridge::locate(&source, RUNNING_PROBE_TIMEOUT)).is_ok();
    if !running {
        return None;
    }

    attach_parent_console();
    let message = localization::t_args(keys::CLI_ALREADY_RUNNING, &[("option", DISABLE_PLUGINS)]);
    eprintln!("{message}");
    Some(host::HOST_ALREADY_RUNNING_EXIT_CODE)
}

fn config_dir() -> Option<PathBuf> {
    mcp_bridge::config_dir(host::BUILD_CHANNEL, &|name| std::env::var_os(name))
}

// A release build is a GUI-subsystem program: Windows hands it no console handles, so output only
// reaches a terminal after attaching to it. A stream the caller redirected to a file or pipe stays.
#[cfg(windows)]
fn attach_parent_console() {
    use std::os::windows::io::IntoRawHandle;
    use windows::Win32::Foundation::HANDLE;
    use windows::Win32::System::Console::{
        AttachConsole, GetStdHandle, SetStdHandle, ATTACH_PARENT_PROCESS, STD_ERROR_HANDLE,
        STD_HANDLE, STD_OUTPUT_HANDLE,
    };

    unsafe fn missing(stream: STD_HANDLE) -> bool {
        !GetStdHandle(stream).is_ok_and(|handle| !handle.is_invalid() && !handle.0.is_null())
    }

    unsafe {
        let missing_streams: Vec<STD_HANDLE> = [STD_OUTPUT_HANDLE, STD_ERROR_HANDLE]
            .into_iter()
            .filter(|stream| missing(*stream))
            .collect();
        if missing_streams.is_empty() || AttachConsole(ATTACH_PARENT_PROCESS).is_err() {
            return;
        }
        for stream in missing_streams {
            if let Ok(console) = std::fs::OpenOptions::new().write(true).open("CONOUT$") {
                let _ = SetStdHandle(stream, HANDLE(console.into_raw_handle()));
            }
        }
    }
}

#[cfg(not(windows))]
fn attach_parent_console() {}

#[cfg(test)]
mod tests {
    use super::*;

    fn args(values: &[&str]) -> Vec<OsString> {
        values.iter().map(OsString::from).collect()
    }

    #[test]
    fn a_plain_launch_is_not_an_information_request() {
        assert_eq!(info_option(&args(&["macro-deck"])), None);
        assert_eq!(info_option(&args(&["macro-deck", "--minimized"])), None);
    }

    #[test]
    fn each_information_option_is_forwarded_to_the_host() {
        assert_eq!(info_option(&args(&["macro-deck", "-h"])), Some("--help"));
        assert_eq!(
            info_option(&args(&["macro-deck", "--help"])),
            Some("--help")
        );
        assert_eq!(
            info_option(&args(&["macro-deck", "--version"])),
            Some("--version")
        );
        assert_eq!(
            info_option(&args(&["macro-deck", "--list-plugins"])),
            Some("--list-plugins")
        );
    }

    #[test]
    fn help_wins_over_version_and_version_over_the_plugin_list() {
        assert_eq!(
            info_option(&args(&[
                "macro-deck",
                "--list-plugins",
                "--version",
                "--help"
            ])),
            Some("--help")
        );
        assert_eq!(
            info_option(&args(&["macro-deck", "--list-plugins", "--version"])),
            Some("--version")
        );
    }

    #[test]
    fn an_information_option_wins_over_disabling_plugins() {
        let launch = args(&["macro-deck", "--disable-plugins", "--list-plugins"]);
        assert_eq!(info_option(&launch), Some("--list-plugins"));
        assert!(wants_plugins_disabled(&launch));
    }

    #[test]
    fn the_program_name_itself_is_never_an_option() {
        assert_eq!(info_option(&args(&["--version"])), None);
        assert!(!wants_plugins_disabled(&args(&["--disable-plugins"])));
    }

    #[test]
    fn a_command_line_run_of_the_host_matches_the_app_launch_folders() {
        let binary = Path::new("host-dir").join("host-binary");
        let config_dir = Path::new("config-dir");
        let command = host::host_command(&binary, Some(config_dir));
        let runtime_dir = config_dir.join("host-runtime");

        assert_eq!(command.get_current_dir(), Some(Path::new("host-dir")));
        assert!(command.get_envs().any(|(name, value)| {
            name == "DOTNET_BUNDLE_EXTRACT_BASE_DIR" && value == Some(runtime_dir.as_os_str())
        }));
    }

    #[test]
    fn disabling_plugins_is_recognised_next_to_other_arguments() {
        assert!(wants_plugins_disabled(&args(&[
            "macro-deck",
            "--minimized",
            "--disable-plugins"
        ])));
        assert!(!wants_plugins_disabled(&args(&[
            "macro-deck",
            "--minimized"
        ])));
    }
}
