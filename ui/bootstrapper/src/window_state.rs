// Window geometry persistence (issue #597), owned by this crate rather than
// handed to tauri-plugin-window-state (issue #464's choice). The plugin's
// restore actively worked against a display-aware restore: its `set_size` is
// unclamped and runs after `set_position`; its on-screen check tests only the
// window's corner points against each monitor's *full* size rather than its
// work area, so a window larger than a monitor reports no intersection -
// exactly the 4K-to-1080p case this issue is about; its `prev_x`/`prev_y` -
// the only candidate source for a maximized window's *normal* bounds - is
// really "position before the last Moved", not "position before maximizing",
// and cannot be written from outside the crate; and its `WindowStateCache`
// mutex is what deadlocked the app at startup in beta.19 (see
// `window::create_main_window`'s comment on `dispatch_to_main_thread`).

use std::collections::HashMap;
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Mutex;

use serde::{Deserialize, Serialize};
use tauri::{AppHandle, Manager, PhysicalPosition, PhysicalSize, WebviewWindow};

use crate::logging;
use crate::window_geometry::{self, Display, Rect, RestoreRequest};

const STORE_FILE_NAME: &str = "window-geometry.json";
const LEGACY_STORE_FILE_NAME: &str = ".window-state.json";
const RECORD_VERSION: u32 = 1;

static LAST_NORMAL_BOUNDS: Mutex<Option<Rect>> = Mutex::new(None);
static PENDING_MAXIMIZED: AtomicBool = AtomicBool::new(false);

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
struct StoredRecord {
    version: u32,
    bounds: Rect,
    maximized: bool,
    display: Option<Display>,
}

#[derive(Debug, Deserialize)]
struct LegacyWindowState {
    x: i32,
    y: i32,
    width: u32,
    height: u32,
    #[serde(default)]
    maximized: bool,
}

fn store_path(dir: &Path) -> PathBuf {
    dir.join(STORE_FILE_NAME)
}

fn legacy_store_path(dir: &Path) -> PathBuf {
    dir.join(LEGACY_STORE_FILE_NAME)
}

fn record_is_valid(record: &StoredRecord) -> bool {
    if record.version != RECORD_VERSION {
        return false;
    }
    if record.bounds.width == 0 || record.bounds.height == 0 {
        return false;
    }
    match &record.display {
        Some(display) => {
            display.scale_factor.is_finite()
                && display.scale_factor > 0.0
                && display.bounds.width > 0
                && display.bounds.height > 0
                && display.work_area.width > 0
                && display.work_area.height > 0
        }
        None => true,
    }
}

fn parse_stored(raw: &str) -> Option<StoredRecord> {
    let record: StoredRecord = serde_json::from_str(raw).ok()?;
    record_is_valid(&record).then_some(record)
}

fn serialize_stored(record: &StoredRecord) -> String {
    serde_json::to_string(record).unwrap_or_default()
}

fn read_stored(dir: &Path) -> Option<StoredRecord> {
    fs::read_to_string(store_path(dir))
        .ok()
        .and_then(|raw| parse_stored(&raw))
}

fn write_stored(dir: &Path, record: &StoredRecord) -> std::io::Result<()> {
    fs::create_dir_all(dir)?;
    fs::write(store_path(dir), serialize_stored(record))
}

fn parse_legacy(raw: &str, label: &str) -> Option<LegacyWindowState> {
    let mut states: HashMap<String, LegacyWindowState> = serde_json::from_str(raw).ok()?;
    let state = states.remove(label)?;
    if state.width == 0 || state.height == 0 {
        return None;
    }
    Some(state)
}

fn read_legacy(dir: &Path, label: &str) -> Option<LegacyWindowState> {
    fs::read_to_string(legacy_store_path(dir))
        .ok()
        .and_then(|raw| parse_legacy(&raw, label))
}

fn config_dir(app: &AppHandle) -> PathBuf {
    app.path()
        .app_config_dir()
        .unwrap_or_else(|_| Path::new(".").to_path_buf())
}

fn frame_insets(window: &WebviewWindow) -> (u32, u32) {
    match (window.outer_size(), window.inner_size()) {
        (Ok(outer), Ok(inner)) => (
            outer.width.saturating_sub(inner.width),
            outer.height.saturating_sub(inner.height),
        ),
        _ => (0, 0),
    }
}

fn monitor_to_display(monitor: &tauri::window::Monitor) -> Display {
    let work_area = monitor.work_area();
    Display {
        name: monitor.name().cloned(),
        bounds: Rect::new(
            monitor.position().x,
            monitor.position().y,
            monitor.size().width,
            monitor.size().height,
        ),
        work_area: Rect::new(
            work_area.position.x,
            work_area.position.y,
            work_area.size.width,
            work_area.size.height,
        ),
        scale_factor: monitor.scale_factor(),
    }
}

fn collect_monitors(window: &WebviewWindow) -> Vec<Display> {
    window
        .available_monitors()
        .unwrap_or_default()
        .iter()
        .map(monitor_to_display)
        .collect()
}

fn primary_index(window: &WebviewWindow, monitors: &[Display]) -> Option<usize> {
    let primary = monitor_to_display(&window.primary_monitor().ok().flatten()?);
    monitors
        .iter()
        .position(|m| m.bounds == primary.bounds && m.name == primary.name)
        .or_else(|| monitors.iter().position(|m| m.bounds == primary.bounds))
}

fn set_pending_maximized(maximized: bool) {
    PENDING_MAXIMIZED.store(maximized, Ordering::SeqCst);
}

fn apply_bounds(window: &WebviewWindow, bounds: Rect) {
    let (inset_width, inset_height) = frame_insets(window);
    let inner_width = bounds.width.saturating_sub(inset_width).max(1);
    let inner_height = bounds.height.saturating_sub(inset_height).max(1);

    if let Err(error) = window.set_position(PhysicalPosition::new(bounds.x, bounds.y)) {
        logging::warn(&format!(
            "[window] could not restore saved window position: {error}"
        ));
    }
    if let Err(error) = window.set_size(PhysicalSize::new(inner_width, inner_height)) {
        logging::warn(&format!(
            "[window] could not restore saved window size: {error}"
        ));
    }
}

pub fn restore_geometry(window: &WebviewWindow) {
    let app = window.app_handle();
    let dir = config_dir(app);
    let label = window.label().to_string();

    let (saved, saved_display, maximized) = match read_stored(&dir) {
        Some(record) => (record.bounds, record.display, record.maximized),
        None => match read_legacy(&dir, &label) {
            Some(legacy) => {
                let (inset_width, inset_height) = frame_insets(window);
                let bounds = Rect::new(
                    legacy.x,
                    legacy.y,
                    legacy.width.saturating_add(inset_width),
                    legacy.height.saturating_add(inset_height),
                );
                (bounds, None, legacy.maximized)
            }
            None => {
                set_pending_maximized(false);
                record_normal_bounds(window);
                return;
            }
        },
    };

    let monitors = collect_monitors(window);
    if monitors.is_empty() {
        set_pending_maximized(false);
        record_normal_bounds(window);
        return;
    }
    let primary = primary_index(window, &monitors);

    let request = RestoreRequest {
        saved,
        saved_display,
        monitors,
        primary,
    };

    if let Some(plan) = window_geometry::plan_restore(&request) {
        apply_bounds(window, plan.bounds);
    }

    set_pending_maximized(maximized);
    record_normal_bounds(window);
}

pub fn restore_maximized(window: &WebviewWindow) {
    if !PENDING_MAXIMIZED.load(Ordering::SeqCst) {
        return;
    }
    if let Err(error) = window.maximize() {
        logging::warn(&format!(
            "[window] could not restore saved window maximized state: {error}"
        ));
    }
}

pub fn record_normal_bounds(window: &WebviewWindow) {
    if window.is_maximized().unwrap_or(false) || window.is_minimized().unwrap_or(false) {
        return;
    }
    let (Ok(position), Ok(size)) = (window.outer_position(), window.outer_size()) else {
        return;
    };
    let bounds = Rect::new(position.x, position.y, size.width, size.height);
    if let Ok(mut cached) = LAST_NORMAL_BOUNDS.lock() {
        *cached = Some(bounds);
    }
}

pub fn flush(app: &AppHandle) {
    let Some(window) = app.get_webview_window(crate::window::MAIN_WINDOW) else {
        return;
    };
    let Some(bounds) = LAST_NORMAL_BOUNDS.lock().ok().and_then(|cached| *cached) else {
        return;
    };

    let record = StoredRecord {
        version: RECORD_VERSION,
        bounds,
        maximized: window.is_maximized().unwrap_or(false),
        display: window
            .current_monitor()
            .ok()
            .flatten()
            .map(|monitor| monitor_to_display(&monitor)),
    };

    let dir = config_dir(app);
    if let Err(error) = write_stored(&dir, &record) {
        logging::warn(&format!("[window] could not save window state: {error}"));
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn sample_record() -> StoredRecord {
        StoredRecord {
            version: RECORD_VERSION,
            bounds: Rect::new(100, 200, 1280, 800),
            maximized: false,
            display: Some(Display {
                name: Some("DISPLAY1".to_string()),
                bounds: Rect::new(0, 0, 1920, 1080),
                work_area: Rect::new(0, 0, 1920, 1040),
                scale_factor: 1.0,
            }),
        }
    }

    #[test]
    fn a_written_record_round_trips_through_disk() {
        let dir = std::env::temp_dir().join(format!(
            "macro-deck-window-geometry-{}-{}",
            std::process::id(),
            line!()
        ));
        assert_eq!(read_stored(&dir), None);

        let record = sample_record();
        write_stored(&dir, &record).expect("write must succeed");
        assert_eq!(read_stored(&dir), Some(record));

        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_record_with_no_display_still_round_trips() {
        let mut record = sample_record();
        record.display = None;
        let raw = serialize_stored(&record);
        assert_eq!(parse_stored(&raw), Some(record));
    }

    #[test]
    fn corrupt_json_degrades_to_none() {
        assert_eq!(parse_stored("not json"), None);
        assert_eq!(parse_stored(""), None);
    }

    #[test]
    fn wrong_version_degrades_to_none() {
        let mut record = sample_record();
        record.version = RECORD_VERSION + 1;
        let raw = serialize_stored(&record);
        assert_eq!(parse_stored(&raw), None);
    }

    #[test]
    fn a_zero_sized_record_degrades_to_none() {
        let mut record = sample_record();
        record.bounds.width = 0;
        let raw = serialize_stored(&record);
        assert_eq!(parse_stored(&raw), None);
    }

    #[test]
    fn a_nonsensical_scale_factor_degrades_to_none() {
        let mut record = sample_record();
        record.display.as_mut().unwrap().scale_factor = f64::NAN;
        let raw = serialize_stored(&record);
        assert_eq!(parse_stored(&raw), None);

        let mut record = sample_record();
        record.display.as_mut().unwrap().scale_factor = 0.0;
        let raw = serialize_stored(&record);
        assert_eq!(parse_stored(&raw), None);
    }

    #[test]
    fn a_zero_area_work_area_degrades_to_none() {
        let mut record = sample_record();
        record.display.as_mut().unwrap().work_area.height = 0;
        let raw = serialize_stored(&record);
        assert_eq!(parse_stored(&raw), None);
    }

    #[test]
    fn legacy_state_for_the_main_window_is_read_by_label() {
        let raw = r#"{"main":{"width":1280,"height":800,"x":50,"y":60,"prev_x":0,"prev_y":0,"maximized":true,"visible":true,"decorated":true,"fullscreen":false}}"#;
        let legacy = parse_legacy(raw, "main").expect("legacy record must parse");
        assert_eq!(legacy.x, 50);
        assert_eq!(legacy.y, 60);
        assert_eq!(legacy.width, 1280);
        assert_eq!(legacy.height, 800);
        assert!(legacy.maximized);
    }

    #[test]
    fn legacy_state_for_a_different_window_label_is_ignored() {
        let raw = r#"{"other":{"width":1280,"height":800,"x":50,"y":60,"prev_x":0,"prev_y":0,"maximized":false,"visible":true,"decorated":true,"fullscreen":false}}"#;
        assert!(parse_legacy(raw, "main").is_none());
    }

    #[test]
    fn a_zero_sized_legacy_record_is_rejected() {
        let raw = r#"{"main":{"width":0,"height":800,"x":50,"y":60,"prev_x":0,"prev_y":0,"maximized":false,"visible":true,"decorated":true,"fullscreen":false}}"#;
        assert!(parse_legacy(raw, "main").is_none());
    }

    #[test]
    fn corrupt_legacy_json_degrades_to_none() {
        assert!(parse_legacy("not json", "main").is_none());
    }
}
