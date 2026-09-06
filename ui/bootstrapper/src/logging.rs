use std::collections::VecDeque;
use std::fs;
use std::io::Write;
use std::path::{Path, PathBuf};
use std::sync::{Mutex, OnceLock};

use chrono::Local;

use crate::redact;

const RETAINED_FILES: usize = 14;
const FILE_PREFIX: &str = "bootstrapper-";

pub struct LoggerContext {
    pub override_dir: Option<String>,
    pub portable: bool,
    pub packaged: bool,
    pub development: bool,
    pub data_root_directory_name: &'static str,
    pub exec_dir: PathBuf,
    pub working_directory: PathBuf,
    pub platform_data_root: PathBuf,
}

pub fn resolve_logs_dir(ctx: &LoggerContext) -> PathBuf {
    if let Some(dir) = ctx.override_dir.as_deref() {
        let trimmed = dir.trim();
        if !trimmed.is_empty() {
            return PathBuf::from(trimmed).join("logs");
        }
    }
    if ctx.portable {
        return ctx.exec_dir.join("logs");
    }
    if ctx.development {
        return ctx
            .working_directory
            .join(ctx.data_root_directory_name)
            .join("logs");
    }
    if ctx.packaged {
        return ctx
            .platform_data_root
            .join(ctx.data_root_directory_name)
            .join("logs");
    }
    ctx.exec_dir.join("logs")
}

pub fn platform_data_root(
    os: &str,
    appdata: Option<&str>,
    xdg_data_home: Option<&str>,
    home: &Path,
) -> PathBuf {
    match os {
        "windows" => appdata
            .filter(|value| !value.trim().is_empty())
            .map(PathBuf::from)
            .unwrap_or_else(|| home.join("AppData").join("Roaming")),
        "macos" => home.join("Library").join("Application Support"),
        _ => xdg_data_home
            .filter(|value| !value.trim().is_empty())
            .map(PathBuf::from)
            .unwrap_or_else(|| home.join(".local").join("share")),
    }
}

pub fn format_line(level: &str, message: &str, time: &str) -> String {
    format!("[{time} {level}] {message}\n")
}

struct LoggerState {
    directory: PathBuf,
    last_prune_stamp: Option<String>,
}

static LOGGER: OnceLock<Mutex<LoggerState>> = OnceLock::new();

pub fn init(directory: PathBuf) {
    let _ = LOGGER.set(Mutex::new(LoggerState {
        directory,
        last_prune_stamp: None,
    }));
}

pub fn logs_directory() -> Option<PathBuf> {
    LOGGER
        .get()
        .and_then(|state| state.lock().ok().map(|state| state.directory.clone()))
}

fn prune_old_files(directory: &Path) {
    let Ok(entries) = fs::read_dir(directory) else {
        return;
    };
    let mut files: Vec<String> = entries
        .filter_map(|entry| entry.ok())
        .map(|entry| entry.file_name().to_string_lossy().into_owned())
        .filter(|name| is_log_file_name(name))
        .collect();
    files.sort();
    let excess = files.len().saturating_sub(RETAINED_FILES);
    for name in files.into_iter().take(excess) {
        let _ = fs::remove_file(directory.join(name));
    }
}

pub fn is_log_file_name(name: &str) -> bool {
    name.strip_prefix(FILE_PREFIX)
        .and_then(|rest| rest.strip_suffix(".log"))
        .map(|stamp| stamp.len() == 8 && stamp.chars().all(|c| c.is_ascii_digit()))
        .unwrap_or(false)
}

fn write(level: &str, message: &str) {
    let now = Local::now();
    let line = format_line(
        level,
        &redact::redact(message),
        &now.format("%H:%M:%S").to_string(),
    );

    if level == "ERR" || level == "FTL" {
        eprint!("{line}");
    } else {
        print!("{line}");
    }

    let Some(logger) = LOGGER.get() else {
        return;
    };
    let Ok(mut state) = logger.lock() else {
        return;
    };

    // Logging failures must not terminate the app.
    let _ = fs::create_dir_all(&state.directory);
    let stamp = now.format("%Y%m%d").to_string();
    if state.last_prune_stamp.as_deref() != Some(stamp.as_str()) {
        state.last_prune_stamp = Some(stamp.clone());
        prune_old_files(&state.directory);
    }
    let path = state.directory.join(format!("{FILE_PREFIX}{stamp}.log"));
    if let Ok(mut file) = fs::OpenOptions::new().create(true).append(true).open(path) {
        let _ = file.write_all(line.as_bytes());
    }
}

pub fn info(message: &str) {
    write("INF", message);
}

pub fn warn(message: &str) {
    write("WRN", message);
}

pub fn error(message: &str) {
    write("ERR", message);
}

pub struct LogTail {
    lines: Mutex<VecDeque<String>>,
    capacity: usize,
}

impl LogTail {
    pub fn new(capacity: usize) -> Self {
        Self {
            lines: Mutex::new(VecDeque::new()),
            capacity,
        }
    }

    pub fn push_chunk(&self, chunk: &str) {
        let Ok(mut lines) = self.lines.lock() else {
            return;
        };
        for line in chunk
            .split(['\r', '\n'])
            .filter(|line| !line.trim().is_empty())
        {
            lines.push_back(line.to_string());
            while lines.len() > self.capacity {
                lines.pop_front();
            }
        }
    }

    pub fn joined(&self) -> String {
        self.lines
            .lock()
            .map(|lines| lines.iter().cloned().collect::<Vec<_>>().join("\n"))
            .unwrap_or_default()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn ctx(
        override_dir: Option<&str>,
        portable: bool,
        packaged: bool,
        development: bool,
    ) -> LoggerContext {
        LoggerContext {
            override_dir: override_dir.map(str::to_string),
            portable,
            packaged,
            development,
            data_root_directory_name: if development { ".data" } else { "MacroDeck" },
            exec_dir: PathBuf::from("/opt/macro-deck"),
            working_directory: PathBuf::from("/workspace/macro-deck"),
            platform_data_root: PathBuf::from("/home/user/.local/share"),
        }
    }

    #[test]
    fn override_wins_over_everything() {
        let dir = resolve_logs_dir(&ctx(Some("/data/macro-deck"), true, true, true));
        assert_eq!(dir, PathBuf::from("/data/macro-deck/logs"));
    }

    #[test]
    fn blank_override_is_ignored() {
        let dir = resolve_logs_dir(&ctx(Some("   "), false, true, false));
        assert_eq!(dir, PathBuf::from("/home/user/.local/share/MacroDeck/logs"));
    }

    #[test]
    fn portable_logs_next_to_binary() {
        let dir = resolve_logs_dir(&ctx(None, true, true, true));
        assert_eq!(dir, PathBuf::from("/opt/macro-deck/logs"));
    }

    #[test]
    fn packaged_uses_platform_data_root() {
        let dir = resolve_logs_dir(&ctx(None, false, true, false));
        assert_eq!(dir, PathBuf::from("/home/user/.local/share/MacroDeck/logs"));
    }

    #[test]
    fn development_logs_stay_in_the_working_directory() {
        let dir = resolve_logs_dir(&ctx(None, false, false, true));
        assert_eq!(dir, PathBuf::from("/workspace/macro-deck/.data/logs"));
    }

    #[test]
    fn unpackaged_production_logs_stay_next_to_the_binary() {
        let dir = resolve_logs_dir(&ctx(None, false, false, false));
        assert_eq!(dir, PathBuf::from("/opt/macro-deck/logs"));
    }

    #[test]
    fn platform_roots_match_host_resolver() {
        let home = Path::new("/home/user");
        assert_eq!(
            platform_data_root(
                "windows",
                Some("C:\\Users\\u\\AppData\\Roaming"),
                None,
                home
            ),
            PathBuf::from("C:\\Users\\u\\AppData\\Roaming")
        );
        assert_eq!(
            platform_data_root("windows", None, None, home),
            PathBuf::from("/home/user/AppData/Roaming")
        );
        assert_eq!(
            platform_data_root("macos", None, None, home),
            PathBuf::from("/home/user/Library/Application Support")
        );
        assert_eq!(
            platform_data_root("linux", None, Some("/xdg/data"), home),
            PathBuf::from("/xdg/data")
        );
        assert_eq!(
            platform_data_root("linux", None, None, home),
            PathBuf::from("/home/user/.local/share")
        );
    }

    #[test]
    fn line_format_matches_serilog_layout() {
        assert_eq!(
            format_line("INF", "hello", "12:34:56"),
            "[12:34:56 INF] hello\n"
        );
    }

    #[test]
    fn log_file_name_pattern() {
        assert!(is_log_file_name("bootstrapper-20260721.log"));
        assert!(!is_log_file_name("bootstrapper-2026072.log"));
        assert!(!is_log_file_name("electron-20260721.log"));
        assert!(!is_log_file_name("bootstrapper-20260721.txt"));
    }

    #[test]
    fn log_tail_keeps_last_lines_only() {
        let tail = LogTail::new(3);
        tail.push_chunk("one\ntwo\n");
        tail.push_chunk("three\r\nfour\n\n");
        assert_eq!(tail.joined(), "two\nthree\nfour");
    }
}
