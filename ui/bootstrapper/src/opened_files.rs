// Files the OS handed us because the user opened one of Macro Deck's own
// registered file types - double-clicked in the file manager, or "Open with...".
//
// The platforms disagree on how that arrives. Windows and Linux pass the path
// on the command line: on argv at a cold start, and through the single-instance
// plugin when the app is already running. macOS never uses argv for this and
// sends RunEvent::Opened instead, for both cases - including before any window
// exists.
//
// So everything funnels into one queue, and `take_opened_files` is the only
// thing that takes anything out of it. The `file-open` event just says that
// something is waiting: an event nobody listens to yet is a missed nudge rather
// than a lost file, which is what makes a file opened during startup - or while
// the user is still on the setup screen - survive until someone can act on it.
// The UI subscribes long after the page loads (it waits for a reachable host),
// so anything that consumed the queue on the way to the webview would drop it.

use std::sync::Mutex;

use tauri::{AppHandle, Emitter, Manager};

use crate::logging;
use crate::window;

pub const FILE_OPEN_EVENT: &str = "file-open";

pub const ARCHIVE_EXTENSIONS: [&str; 4] = [
    "macrodeckprofile",
    "macrodeckfolder",
    "macrodeckwidget",
    "macrodeckiconpack",
];

pub const OPENABLE_EXTENSIONS: [&str; 5] = [
    "macrodeckprofile",
    "macrodeckfolder",
    "macrodeckwidget",
    "macrodeckiconpack",
    "macrodeckplugin",
];

#[derive(Default)]
pub struct PendingOpenFiles(Mutex<Vec<String>>);

impl PendingOpenFiles {
    fn push(&self, paths: Vec<String>) {
        if let Ok(mut queued) = self.0.lock() {
            queued.extend(paths);
        }
    }

    /// Whether anything is waiting. Only ever read - announcing a file must never consume it.
    fn is_waiting(&self) -> bool {
        self.0
            .lock()
            .map(|queued| !queued.is_empty())
            .unwrap_or(false)
    }

    fn take(&self) -> Vec<String> {
        self.0
            .lock()
            .map(|mut queued| queued.drain(..).collect())
            .unwrap_or_default()
    }
}

pub fn accepts(path: &str) -> bool {
    std::path::Path::new(path)
        .extension()
        .and_then(|extension| extension.to_str())
        .map(|extension| extension.to_ascii_lowercase())
        .is_some_and(|extension| OPENABLE_EXTENSIONS.contains(&extension.as_str()))
}

pub fn paths_from_args(args: impl Iterator<Item = String>) -> Vec<String> {
    args.skip(1)
        .filter(|argument| !argument.starts_with('-'))
        .filter(|argument| accepts(argument))
        .map(|argument| canonicalize(&argument))
        .collect()
}

#[cfg_attr(not(target_os = "macos"), allow(dead_code))]
pub fn paths_from_urls(urls: &[url::Url]) -> Vec<String> {
    urls.iter()
        .filter(|url| url.scheme() == "file")
        .filter_map(|url| url.to_file_path().ok())
        .filter_map(|path| path.to_str().map(str::to_string))
        .filter(|path| accepts(path))
        .collect()
}

pub fn queue(app: &AppHandle, paths: Vec<String>) {
    if paths.is_empty() {
        return;
    }

    logging::info(&format!(
        "[open] {} file(s) opened from the OS",
        paths.len()
    ));
    if let Some(pending) = app.try_state::<PendingOpenFiles>() {
        pending.push(paths);
    }

    if window::is_main_window_loaded() {
        notify(app);
    }
}

/// Tells the UI that files are waiting. Deliberately leaves the queue alone - only
/// `take_opened_files` empties it, so a nudge that arrives before anyone listens costs nothing.
pub fn notify(app: &AppHandle) {
    let waiting = app
        .try_state::<PendingOpenFiles>()
        .is_some_and(|pending| pending.is_waiting());
    if !waiting {
        return;
    }

    if let Err(error) = app.emit(FILE_OPEN_EVENT, ()) {
        logging::error(&format!("[open] could not emit file-open event: {error}"));
    }
}

#[tauri::command]
pub fn take_opened_files(app: AppHandle) -> Vec<String> {
    app.try_state::<PendingOpenFiles>()
        .map(|pending| pending.take())
        .unwrap_or_default()
}

fn canonicalize(path: &str) -> String {
    std::fs::canonicalize(path)
        .ok()
        .and_then(|resolved| resolved.to_str().map(str::to_string))
        .unwrap_or_else(|| path.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    // A file: URL only round-trips back to a path when it carries the shape that platform's paths
    // have - on Windows a drive letter, everywhere else a leading slash.
    fn opened_file(name: &str) -> (url::Url, String) {
        let path = if cfg!(windows) {
            format!("C:\\tmp\\{name}")
        } else {
            format!("/tmp/{name}")
        };
        let url = url::Url::from_file_path(&path).expect("the test path must be absolute");
        (url, path)
    }

    #[test]
    fn accepts_every_registered_archive_type() {
        assert!(accepts("/tmp/Default.macroDeckProfile"));
        assert!(accepts("/tmp/Lights.macroDeckFolder"));
        assert!(accepts("/tmp/widgets.macroDeckWidget"));
        assert!(accepts("/tmp/Neon.macroDeckIconPack"));
    }

    #[test]
    fn accepts_a_plugin() {
        assert!(accepts("/tmp/MyPlugin.macroDeckPlugin"));
    }

    #[test]
    fn accepts_ignores_extension_case() {
        assert!(accepts("/tmp/Default.MACRODECKPROFILE"));
        assert!(accepts("/tmp/Default.macrodeckprofile"));
        assert!(accepts("/tmp/MyPlugin.MACRODECKPLUGIN"));
        assert!(accepts("/tmp/MyPlugin.macrodeckplugin"));
    }

    #[test]
    fn import_picker_extensions_do_not_include_the_plugin() {
        assert!(!ARCHIVE_EXTENSIONS.contains(&"macrodeckplugin"));
        assert!(OPENABLE_EXTENSIONS.contains(&"macrodeckplugin"));
    }

    #[test]
    fn rejects_everything_else() {
        assert!(!accepts("/tmp/pack.zip"));
        assert!(!accepts("/tmp/notes.txt"));
        assert!(!accepts("/tmp/no-extension"));
        assert!(!accepts(""));
    }

    #[test]
    fn paths_from_args_skips_the_executable_and_flags() {
        let args = [
            "/Applications/Macro Deck.app/Contents/MacOS/MacroDeck",
            "--autostart",
            "--minimized",
            "/tmp/Default.macroDeckProfile",
        ]
        .map(str::to_string);

        let paths = paths_from_args(args.into_iter());

        assert_eq!(paths, vec!["/tmp/Default.macroDeckProfile".to_string()]);
    }

    #[test]
    fn paths_from_args_never_takes_the_first_argument() {
        let args = ["/tmp/argv0.macroDeckProfile"].map(str::to_string);

        assert!(paths_from_args(args.into_iter()).is_empty());
    }

    #[test]
    fn paths_from_args_keeps_paths_containing_spaces() {
        let args = ["MacroDeck", "/tmp/My Deck.macroDeckFolder"].map(str::to_string);

        let paths = paths_from_args(args.into_iter());

        assert_eq!(paths, vec!["/tmp/My Deck.macroDeckFolder".to_string()]);
    }

    #[test]
    fn paths_from_args_takes_every_openable_argument() {
        let args = [
            "MacroDeck",
            "/tmp/one.macroDeckProfile",
            "/tmp/skip.txt",
            "/tmp/two.macroDeckWidget",
        ]
        .map(str::to_string);

        assert_eq!(paths_from_args(args.into_iter()).len(), 2);
    }

    #[test]
    fn paths_from_args_takes_a_plugin() {
        let args = [
            "MacroDeck",
            "--minimized",
            "/tmp/MyPlugin.macroDeckPlugin",
            "/tmp/notes.txt",
        ]
        .map(str::to_string);

        assert_eq!(
            paths_from_args(args.into_iter()),
            vec!["/tmp/MyPlugin.macroDeckPlugin".to_string()]
        );
    }

    #[test]
    fn paths_from_urls_takes_a_plugin() {
        let (url, path) = opened_file("My Plugin.macroDeckPlugin");

        assert_eq!(paths_from_urls(&[url]), vec![path]);
    }

    #[test]
    fn paths_from_urls_maps_file_urls_and_drops_the_rest() {
        let (profile, path) = opened_file("Default.macroDeckProfile");
        let urls = [
            profile,
            opened_file("notes.txt").0,
            url::Url::parse("https://macro-deck.app/Default.macroDeckProfile").unwrap(),
        ];

        let paths = paths_from_urls(&urls);

        assert_eq!(paths, vec![path]);
    }

    // The queue needs no running app; only its wiring into main.rs does, hence the last test.
    fn queue_of(paths: &[&str]) -> PendingOpenFiles {
        let pending = PendingOpenFiles::default();
        pending.push(paths.iter().map(|path| path.to_string()).collect());
        pending
    }

    #[test]
    fn announcing_a_file_does_not_consume_it() {
        let pending = queue_of(&["/tmp/MyPlugin.macroDeckPlugin"]);

        // Everything `notify` reads of the queue. The UI subscribes only once the host is reachable,
        // long after the page load that announces a file opened at startup - so an announcement that
        // took the file with it would leave nothing to install (issue #608).
        assert!(pending.is_waiting());
        assert!(pending.is_waiting());

        assert_eq!(
            pending.take(),
            vec!["/tmp/MyPlugin.macroDeckPlugin".to_string()]
        );
    }

    #[test]
    fn a_file_is_handed_over_exactly_once() {
        let pending = queue_of(&["/tmp/MyPlugin.macroDeckPlugin"]);

        assert_eq!(pending.take().len(), 1);
        assert!(pending.take().is_empty());
        assert!(!pending.is_waiting());
    }

    #[test]
    fn a_file_opened_later_joins_the_queue_and_is_announced() {
        let pending = queue_of(&["/tmp/One.macroDeckPlugin"]);
        assert_eq!(pending.take().len(), 1);

        pending.push(vec!["/tmp/Two.macroDeckPlugin".to_string()]);

        assert!(pending.is_waiting());
        assert_eq!(pending.take(), vec!["/tmp/Two.macroDeckPlugin".to_string()]);
    }

    #[test]
    fn files_opened_in_one_gesture_are_all_handed_over_in_order() {
        let pending = queue_of(&["/tmp/One.macroDeckPlugin", "/tmp/Two.macroDeckProfile"]);

        assert_eq!(
            pending.take(),
            vec![
                "/tmp/One.macroDeckPlugin".to_string(),
                "/tmp/Two.macroDeckProfile".to_string()
            ]
        );
    }

    #[test]
    fn an_empty_queue_is_never_announced() {
        assert!(!PendingOpenFiles::default().is_waiting());
    }

    #[test]
    fn the_page_load_handler_announces_without_consuming_the_queue() {
        let main_source = include_str!("main.rs");
        let handler = main_source
            .split("PageLoadEvent::Finished")
            .nth(1)
            .and_then(|rest| rest.split(".setup(").next())
            .expect("main.rs must handle PageLoadEvent::Finished");

        assert!(handler.contains("opened_files::notify"));
        assert!(!handler.contains("opened_files::flush"));
        assert!(!handler.contains("opened_files::take_opened_files"));
    }

    #[test]
    fn paths_from_urls_decodes_escaped_characters() {
        let (url, path) = opened_file("My Deck.macroDeckFolder");

        assert!(url.as_str().contains("My%20Deck"));
        assert_eq!(paths_from_urls(&[url]), vec![path]);
    }
}
