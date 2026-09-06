// Pure display-aware window placement (issue #597), with no Tauri window
// handle so every rule below is unit-testable. All internal math happens in
// physical px, virtual-desktop coordinates, on the window's *outer* rect;
// logical px only ever appear as the constants below, multiplied by the
// target display's scale factor at the point of use - mixing scale factors
// across a remap is exactly how the previous implementation produced a
// window sized for the wrong monitor.

use serde::{Deserialize, Serialize};

pub const MIN_WINDOW_WIDTH_LOGICAL: f64 = crate::window::MIN_WINDOW_WIDTH;
pub const MIN_WINDOW_HEIGHT_LOGICAL: f64 = crate::window::MIN_WINDOW_HEIGHT;

const GRAB_MIN_OVERLAP_WIDTH_LOGICAL: f64 = 120.0;
// The band this measures is the app's own `data-tauri-drag-region` status
// bar on macOS (TitleBarStyle::Overlay makes outer == inner there) and the
// real OS title bar everywhere else - one rule, two different reasons it
// holds, both of which put the grabbable strip at the very top of the outer
// rect.
const GRAB_BAND_HEIGHT_LOGICAL: f64 = 24.0;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct Rect {
    pub x: i32,
    pub y: i32,
    pub width: u32,
    pub height: u32,
}

impl Rect {
    pub fn new(x: i32, y: i32, width: u32, height: u32) -> Self {
        Self {
            x,
            y,
            width,
            height,
        }
    }

    pub fn left(&self) -> i64 {
        self.x as i64
    }

    pub fn top(&self) -> i64 {
        self.y as i64
    }

    pub fn right(&self) -> i64 {
        self.left().saturating_add(self.width as i64)
    }

    pub fn bottom(&self) -> i64 {
        self.top().saturating_add(self.height as i64)
    }

    pub fn center_x(&self) -> f64 {
        self.x as f64 + self.width as f64 / 2.0
    }

    pub fn center_y(&self) -> f64 {
        self.y as f64 + self.height as f64 / 2.0
    }

    /// Half-open interval intersection: edges that merely touch do not intersect.
    pub fn intersects(&self, other: &Rect) -> bool {
        self.left() < other.right()
            && self.right() > other.left()
            && self.top() < other.bottom()
            && self.bottom() > other.top()
    }

    pub fn is_fully_within(&self, other: &Rect) -> bool {
        self.left() >= other.left()
            && self.right() <= other.right()
            && self.top() >= other.top()
            && self.bottom() <= other.bottom()
    }

    pub fn overlap_width(&self, other: &Rect) -> i64 {
        (self.right().min(other.right()) - self.left().max(other.left())).max(0)
    }

    pub fn overlap_height(&self, other: &Rect) -> i64 {
        (self.bottom().min(other.bottom()) - self.top().max(other.top())).max(0)
    }

    fn intersection_area(&self, other: &Rect) -> i64 {
        self.overlap_width(other)
            .saturating_mul(self.overlap_height(other))
    }

    fn center_distance_squared(&self, other: &Rect) -> f64 {
        let dx = self.center_x() - other.center_x();
        let dy = self.center_y() - other.center_y();
        dx * dx + dy * dy
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Display {
    pub name: Option<String>,
    pub bounds: Rect,
    pub work_area: Rect,
    pub scale_factor: f64,
}

#[derive(Debug, Clone)]
pub struct RestoreRequest {
    pub saved: Rect,
    pub saved_display: Option<Display>,
    pub monitors: Vec<Display>,
    pub primary: Option<usize>,
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct RestorePlan {
    pub bounds: Rect,
}

pub fn plan_restore(request: &RestoreRequest) -> Option<RestorePlan> {
    if request.monitors.is_empty() {
        return None;
    }

    let (target_index, bounds) = match &request.saved_display {
        Some(saved_display) => match identity_match_index(saved_display, &request.monitors) {
            Some(index) => {
                let matched = &request.monitors[index];
                if *matched == *saved_display {
                    (index, request.saved)
                } else {
                    let bounds = plan_remap(request.saved, saved_display, matched);
                    let bounds = ensure_intersects(bounds, matched, &request.monitors);
                    (index, bounds)
                }
            }
            None => {
                let index = select_display_index(request.saved, &request.monitors, request.primary);
                let target = &request.monitors[index];
                let bounds = plan_different_display(request.saved, saved_display, target);
                let bounds = ensure_intersects(bounds, target, &request.monitors);
                (index, bounds)
            }
        },
        None => {
            let index = select_display_index(request.saved, &request.monitors, request.primary);
            let target = &request.monitors[index];
            let bounds = plan_unknown_display(request.saved, target);
            let bounds = ensure_intersects(bounds, target, &request.monitors);
            (index, bounds)
        }
    };

    let target = &request.monitors[target_index];
    let bounds = reachability_rescue(bounds, target, &request.monitors);
    Some(RestorePlan { bounds })
}

fn sanitize_scale(scale: f64) -> f64 {
    if scale.is_finite() && scale > 0.0 {
        scale
    } else {
        1.0
    }
}

fn identity_match_index(saved: &Display, monitors: &[Display]) -> Option<usize> {
    if let Some(name) = saved.name.as_deref().filter(|name| !name.is_empty()) {
        let matches: Vec<usize> = monitors
            .iter()
            .enumerate()
            .filter(|(_, m)| m.name.as_deref() == Some(name))
            .map(|(i, _)| i)
            .collect();
        if matches.len() == 1 {
            return Some(matches[0]);
        }
    }

    let matches: Vec<usize> = monitors
        .iter()
        .enumerate()
        .filter(|(_, m)| m.bounds == saved.bounds)
        .map(|(i, _)| i)
        .collect();
    if matches.len() == 1 {
        return Some(matches[0]);
    }

    let matches: Vec<usize> = monitors
        .iter()
        .enumerate()
        .filter(|(_, m)| m.bounds.x == saved.bounds.x && m.bounds.y == saved.bounds.y)
        .map(|(i, _)| i)
        .collect();
    if matches.len() == 1 {
        return Some(matches[0]);
    }

    None
}

fn select_display_index(saved: Rect, monitors: &[Display], primary: Option<usize>) -> usize {
    let areas: Vec<i64> = monitors
        .iter()
        .map(|m| saved.intersection_area(&m.bounds))
        .collect();
    let max_area = *areas.iter().max().unwrap_or(&0);
    if max_area > 0 {
        let candidates: Vec<usize> = (0..monitors.len())
            .filter(|&i| areas[i] == max_area)
            .collect();
        return pick_tiebreak(candidates, primary);
    }

    let distances: Vec<f64> = monitors
        .iter()
        .map(|m| saved.center_distance_squared(&m.bounds))
        .collect();
    let min_distance = distances.iter().cloned().fold(f64::INFINITY, f64::min);
    let candidates: Vec<usize> = (0..monitors.len())
        .filter(|&i| distances[i] == min_distance)
        .collect();
    pick_tiebreak(candidates, primary)
}

fn pick_tiebreak(candidates: Vec<usize>, primary: Option<usize>) -> usize {
    if let Some(primary) = primary {
        if candidates.contains(&primary) {
            return primary;
        }
    }
    candidates.into_iter().min().unwrap_or(0)
}

fn clamped_size(saved: Rect, old_scale: f64, new_scale: f64, work_area: &Rect) -> (u32, u32) {
    let old_scale = sanitize_scale(old_scale);
    let new_scale = sanitize_scale(new_scale);

    let logical_width = saved.width as f64 / old_scale;
    let logical_height = saved.height as f64 / old_scale;
    let physical_width = logical_width * new_scale;
    let physical_height = logical_height * new_scale;

    let min_width = MIN_WINDOW_WIDTH_LOGICAL * new_scale;
    let min_height = MIN_WINDOW_HEIGHT_LOGICAL * new_scale;

    let width = physical_width.min(work_area.width as f64).max(min_width);
    let height = physical_height.min(work_area.height as f64).max(min_height);

    (round_u32(width).max(1), round_u32(height).max(1))
}

fn rect_centered(center: (f64, f64), size: (u32, u32)) -> Rect {
    let x = center.0 - size.0 as f64 / 2.0;
    let y = center.1 - size.1 as f64 / 2.0;
    Rect {
        x: round_i32(x),
        y: round_i32(y),
        width: size.0,
        height: size.1,
    }
}

fn remap_center(saved: Rect, old_work_area: &Rect, new_work_area: &Rect) -> (f64, f64) {
    let cx = saved.center_x();
    let cy = saved.center_y();

    let fx = if old_work_area.width == 0 {
        0.5
    } else {
        (cx - old_work_area.x as f64) / old_work_area.width as f64
    };
    let fy = if old_work_area.height == 0 {
        0.5
    } else {
        (cy - old_work_area.y as f64) / old_work_area.height as f64
    };

    let nx = new_work_area.x as f64 + fx * new_work_area.width as f64;
    let ny = new_work_area.y as f64 + fy * new_work_area.height as f64;
    (nx, ny)
}

fn plan_remap(saved: Rect, saved_display: &Display, target: &Display) -> Rect {
    let size = clamped_size(
        saved,
        saved_display.scale_factor,
        target.scale_factor,
        &target.work_area,
    );
    let center = remap_center(saved, &saved_display.work_area, &target.work_area);
    rect_centered(center, size)
}

// No display was recorded at all (legacy record, first launch after
// upgrade): there is no old work area to remap from, so the best available
// anchor is the saved rect's own centre - the window keeps its logical size
// and shrinks/grows around where it was, rather than jumping to the display
// centre.
fn plan_unknown_display(saved: Rect, target: &Display) -> Rect {
    let size = clamped_size(saved, 1.0, target.scale_factor, &target.work_area);
    let center = (saved.center_x(), saved.center_y());
    rect_centered(center, size)
}

// A display was recorded but it no longer exists (or a different one was
// selected by geometry, e.g. a duplicate name). Unlike the same-display
// remap, there is no reason to believe the saved coordinates mean anything
// on this monitor: if they happen to still land fully inside it, honour them
// verbatim; otherwise fall back to centring rather than half-preserving a
// position that belonged to a different, unrelated display.
fn plan_different_display(saved: Rect, saved_display: &Display, target: &Display) -> Rect {
    let size = clamped_size(
        saved,
        saved_display.scale_factor,
        target.scale_factor,
        &target.work_area,
    );
    let at_saved_position = Rect {
        x: saved.x,
        y: saved.y,
        width: size.0,
        height: size.1,
    };
    if at_saved_position.is_fully_within(&target.bounds) {
        at_saved_position
    } else {
        rect_centered(
            (target.work_area.center_x(), target.work_area.center_y()),
            size,
        )
    }
}

fn ensure_intersects(rect: Rect, target: &Display, monitors: &[Display]) -> Rect {
    if monitors.iter().any(|m| rect.intersects(&m.bounds)) {
        rect
    } else {
        rect_centered(
            (target.work_area.center_x(), target.work_area.center_y()),
            (rect.width, rect.height),
        )
    }
}

fn band_height(scale: f64) -> u32 {
    round_u32(GRAB_BAND_HEIGHT_LOGICAL * scale).max(1)
}

fn is_reachable(rect: Rect, monitors: &[Display], scale: f64) -> bool {
    let band = Rect {
        height: band_height(scale),
        ..rect
    };
    let threshold_width = round_i64(GRAB_MIN_OVERLAP_WIDTH_LOGICAL * scale);
    let threshold_height = round_i64(GRAB_BAND_HEIGHT_LOGICAL * scale);
    monitors.iter().any(|m| {
        band.overlap_width(&m.work_area) >= threshold_width
            && band.overlap_height(&m.work_area) >= threshold_height
    })
}

/// Minimum single-axis translation that brings `band`'s overlap with `work`
/// up to `threshold`, or - when the work area is itself shorter/narrower
/// than the band - up to the best achievable overlap. Never moves away from
/// the work area (R3/R4): always terminates in one step, never thrashes.
fn rescue_axis(
    band_start: i64,
    band_len: i64,
    work_start: i64,
    work_len: i64,
    threshold: i64,
) -> i64 {
    let achievable_max = band_len.min(work_len).max(0);
    let target = threshold.min(achievable_max);
    let work_end = work_start.saturating_add(work_len);
    let band_center2 = band_start.saturating_mul(2).saturating_add(band_len);
    let work_center2 = work_start.saturating_mul(2).saturating_add(work_len);

    if band_center2 <= work_center2 {
        work_start.saturating_add(target).saturating_sub(band_len)
    } else {
        work_end.saturating_sub(target)
    }
}

fn clamp_to_i32(value: i64) -> i32 {
    value.clamp(i32::MIN as i64, i32::MAX as i64) as i32
}

fn reachability_rescue(mut rect: Rect, target: &Display, monitors: &[Display]) -> Rect {
    let scale = sanitize_scale(target.scale_factor);
    if is_reachable(rect, monitors, scale) {
        return rect;
    }

    let threshold_width = round_i64(GRAB_MIN_OVERLAP_WIDTH_LOGICAL * scale);
    let threshold_height = round_i64(GRAB_BAND_HEIGHT_LOGICAL * scale);
    let work = &target.work_area;

    if rect.overlap_width(work) < threshold_width {
        let new_x = rescue_axis(
            rect.left(),
            rect.width as i64,
            work.left(),
            work.width as i64,
            threshold_width,
        );
        rect.x = clamp_to_i32(new_x);
    }

    let height = band_height(scale);
    let band = Rect { height, ..rect };
    if band.overlap_height(work) < threshold_height {
        let new_y = rescue_axis(
            band.top(),
            height as i64,
            work.top(),
            work.height as i64,
            threshold_height,
        );
        rect.y = clamp_to_i32(new_y);
    }

    rect
}

fn round_i32(value: f64) -> i32 {
    if !value.is_finite() {
        return 0;
    }
    let rounded = value.round();
    if rounded >= i32::MAX as f64 {
        i32::MAX
    } else if rounded <= i32::MIN as f64 {
        i32::MIN
    } else {
        rounded as i32
    }
}

fn round_u32(value: f64) -> u32 {
    if !value.is_finite() {
        return 0;
    }
    let rounded = value.round();
    if rounded <= 0.0 {
        0
    } else if rounded >= u32::MAX as f64 {
        u32::MAX
    } else {
        rounded as u32
    }
}

fn round_i64(value: f64) -> i64 {
    if !value.is_finite() {
        return 0;
    }
    let rounded = value.round();
    if rounded >= i64::MAX as f64 {
        i64::MAX
    } else if rounded <= i64::MIN as f64 {
        i64::MIN
    } else {
        rounded as i64
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn d(
        name: &str,
        bounds: (i32, i32, u32, u32),
        work_area: (i32, i32, u32, u32),
        scale: f64,
    ) -> Display {
        Display {
            name: Some(name.to_string()),
            bounds: Rect::new(bounds.0, bounds.1, bounds.2, bounds.3),
            work_area: Rect::new(work_area.0, work_area.1, work_area.2, work_area.3),
            scale_factor: scale,
        }
    }

    fn d_unnamed(
        bounds: (i32, i32, u32, u32),
        work_area: (i32, i32, u32, u32),
        scale: f64,
    ) -> Display {
        Display {
            name: None,
            bounds: Rect::new(bounds.0, bounds.1, bounds.2, bounds.3),
            work_area: Rect::new(work_area.0, work_area.1, work_area.2, work_area.3),
            scale_factor: scale,
        }
    }

    fn r(x: i32, y: i32, width: u32, height: u32) -> Rect {
        Rect::new(x, y, width, height)
    }

    fn plan(
        saved: Rect,
        saved_display: Option<Display>,
        monitors: Vec<Display>,
        primary: Option<usize>,
    ) -> Rect {
        plan_restore(&RestoreRequest {
            saved,
            saved_display,
            monitors,
            primary,
        })
        .expect("expected a restore plan")
        .bounds
    }

    // --- Rect::intersects, ported from window_state.rs's rect_intersects ---

    #[test]
    fn window_fully_inside_the_monitor_intersects() {
        assert!(r(0, 0, 1920, 1080).intersects(&r(100, 100, 800, 600)));
    }

    #[test]
    fn partially_overlapping_windows_intersect() {
        assert!(r(0, 0, 1920, 1080).intersects(&r(1800, 1000, 800, 600)));
    }

    #[test]
    fn a_window_larger_than_and_containing_the_monitor_intersects() {
        assert!(r(500, 500, 400, 300).intersects(&r(0, 0, 3000, 2000)));
    }

    #[test]
    fn a_window_entirely_to_the_left_does_not_intersect() {
        assert!(!r(0, 0, 1920, 1080).intersects(&r(-1000, 100, 800, 600)));
    }

    #[test]
    fn a_window_entirely_above_does_not_intersect() {
        assert!(!r(0, 0, 1920, 1080).intersects(&r(100, -1000, 800, 600)));
    }

    #[test]
    fn edges_merely_touching_do_not_intersect() {
        assert!(!r(0, 0, 1920, 1080).intersects(&r(1920, 0, 800, 600)));
    }

    #[test]
    fn a_negative_coordinate_secondary_monitor_intersects() {
        assert!(r(-1920, -200, 1920, 1080).intersects(&r(-1500, -100, 800, 600)));
    }

    // --- Group A: nothing changed ---

    #[test]
    fn window_on_display_left_of_primary_with_negative_x_is_restored_verbatim() {
        let right = d("Right", (0, 0, 1920, 1080), (0, 0, 1920, 1040), 1.0);
        let left = d("Left", (-1920, 0, 1920, 1080), (-1920, 0, 1920, 1040), 1.0);
        let saved_display = left.clone();
        let bounds = plan(
            r(-1700, 120, 1400, 900),
            Some(saved_display),
            vec![right, left],
            Some(0),
        );
        assert_eq!(bounds, r(-1700, 120, 1400, 900));
    }

    #[test]
    fn deliberately_oversized_window_survives_relaunch_when_nothing_changed() {
        let display = d("DISPLAY1", (0, 0, 1920, 1080), (0, 0, 1920, 1040), 1.0);
        let bounds = plan(
            r(-100, 0, 2200, 1200),
            Some(display.clone()),
            vec![display],
            Some(0),
        );
        assert_eq!(bounds, r(-100, 0, 2200, 1200));
    }

    #[test]
    fn window_dragged_half_off_the_right_edge_is_left_where_the_user_put_it() {
        let display = d("DISPLAY1", (0, 0, 1920, 1080), (0, 0, 1920, 1040), 1.0);
        let bounds = plan(
            r(1500, 300, 1200, 800),
            Some(display.clone()),
            vec![display],
            Some(0),
        );
        assert_eq!(bounds, r(1500, 300, 1200, 800));
    }

    #[test]
    fn window_hanging_below_the_work_area_bottom_is_not_moved_at_the_boundary() {
        let display = d("DISPLAY1", (0, 0, 1920, 1080), (0, 0, 1920, 1040), 1.0);
        let bounds = plan(
            r(300, 1000, 1200, 800),
            Some(display.clone()),
            vec![display],
            Some(0),
        );
        assert_eq!(bounds, r(300, 1000, 1200, 800));
    }

    #[test]
    fn grab_band_threshold_is_measured_in_logical_not_physical_pixels_hidpi() {
        let display = d("HiDPI", (0, 0, 2560, 1600), (0, 0, 2560, 1550), 2.0);
        let bounds = plan(
            r(2360, 300, 1800, 1280),
            Some(display.clone()),
            vec![display],
            Some(0),
        );
        assert_eq!(bounds, r(2320, 300, 1800, 1280));
    }

    #[test]
    fn grab_band_threshold_is_measured_in_logical_not_physical_pixels_standard_dpi() {
        let display = d("HiDPI", (0, 0, 2560, 1600), (0, 0, 2560, 1550), 1.0);
        let bounds = plan(
            r(2360, 300, 1800, 1280),
            Some(display.clone()),
            vec![display],
            Some(0),
        );
        assert_eq!(bounds, r(2360, 300, 1800, 1280));
    }

    // --- Group B: same display, geometry changed (proportional remap) ---

    #[test]
    fn t4k_at_200_percent_downgraded_to_1080p_keeps_relative_position_and_logical_size() {
        let saved_display = d("DISPLAY1", (0, 0, 3840, 2160), (0, 0, 3840, 2080), 2.0);
        let monitor = d("DISPLAY1", (0, 0, 1920, 1080), (0, 0, 1920, 1040), 1.0);
        let bounds = plan(
            r(400, 200, 2400, 1600),
            Some(saved_display),
            vec![monitor],
            Some(0),
        );
        assert_eq!(bounds, r(200, 100, 1200, 800));
    }

    #[test]
    fn size_is_preserved_in_logical_units_not_scaled_by_pixel_resolution() {
        let saved_display = d("DISPLAY1", (0, 0, 3840, 2160), (0, 0, 3840, 2080), 2.0);
        let monitor = d("DISPLAY1", (0, 0, 2560, 1440), (0, 0, 2560, 1400), 1.0);
        let bounds = plan(
            r(240, 240, 2400, 1600),
            Some(saved_display),
            vec![monitor],
            Some(0),
        );
        assert_eq!(bounds, r(360, 300, 1200, 800));
    }

    #[test]
    fn t1080p_upgraded_to_4k_at_200_percent_grows_the_window_to_the_same_logical_size() {
        let saved_display = d("DISPLAY1", (0, 0, 1920, 1080), (0, 0, 1920, 1040), 1.0);
        let monitor = d("DISPLAY1", (0, 0, 3840, 2160), (0, 0, 3840, 2080), 2.0);
        let bounds = plan(
            r(200, 100, 1200, 800),
            Some(saved_display),
            vec![monitor],
            Some(0),
        );
        assert_eq!(bounds, r(400, 200, 2400, 1600));
    }

    #[test]
    fn monitor_moved_to_the_other_side_of_the_arrangement_keeps_relative_position() {
        let saved_display = d("Left", (-1920, 0, 1920, 1080), (-1920, 0, 1920, 1040), 1.0);
        let right = d("Right", (0, 0, 1920, 1080), (0, 0, 1920, 1040), 1.0);
        let left = d("Left", (1920, 0, 1920, 1080), (1920, 0, 1920, 1040), 1.0);
        let bounds = plan(
            r(-1700, 120, 1400, 900),
            Some(saved_display),
            vec![right, left],
            Some(0),
        );
        assert_eq!(bounds, r(2140, 120, 1400, 900));
    }

    #[test]
    fn rotating_a_display_to_portrait_clamps_width_and_keeps_relative_centre() {
        let saved_display = d("DISPLAY1", (0, 0, 1920, 1080), (0, 0, 1920, 1040), 1.0);
        let monitor = d("DISPLAY1", (0, 0, 1080, 1920), (0, 0, 1080, 1880), 1.0);
        let bounds = plan(
            r(260, 70, 1400, 900),
            Some(saved_display),
            vec![monitor],
            Some(0),
        );
        assert_eq!(bounds, r(0, 490, 1080, 900));
    }

    #[test]
    fn taskbar_moved_to_the_top_shifts_the_window_down_by_the_work_area_delta() {
        let saved_display = d("DISPLAY1", (0, 0, 1920, 1080), (0, 0, 1920, 1040), 1.0);
        let monitor = d("DISPLAY1", (0, 0, 1920, 1080), (0, 40, 1920, 1040), 1.0);
        let bounds = plan(
            r(200, 10, 1200, 800),
            Some(saved_display),
            vec![monitor],
            Some(0),
        );
        assert_eq!(bounds, r(200, 50, 1200, 800));
    }

    // --- Group C: saved display is gone ---

    #[test]
    fn window_from_a_disconnected_external_monitor_is_centred_on_the_primary() {
        let saved_display = d(
            "External",
            (1920, 0, 2560, 1440),
            (1920, 0, 2560, 1400),
            1.0,
        );
        let internal = d("Internal", (0, 0, 1920, 1080), (0, 0, 1920, 1040), 1.0);
        let bounds = plan(
            r(2200, 300, 1400, 900),
            Some(saved_display),
            vec![internal],
            Some(0),
        );
        assert_eq!(bounds, r(260, 70, 1400, 900));
    }

    #[test]
    fn window_straddling_a_disconnected_monitor_boundary_is_recentred() {
        let saved_display = d(
            "External",
            (1920, 0, 2560, 1440),
            (1920, 0, 2560, 1400),
            1.0,
        );
        let internal = d("Internal", (0, 0, 1920, 1080), (0, 0, 1920, 1040), 1.0);
        let bounds = plan(
            r(1500, 300, 1200, 800),
            Some(saved_display),
            vec![internal],
            Some(0),
        );
        assert_eq!(bounds, r(360, 120, 1200, 800));
    }

    #[test]
    fn saved_bounds_outside_every_display_are_clamped_and_centred() {
        let saved_display = d(
            "Old",
            (-1920, -1080, 1920, 1080),
            (-1920, -1080, 1920, 1080),
            1.0,
        );
        let new = d("New", (0, 0, 1920, 1080), (0, 0, 1920, 1040), 1.0);
        let bounds = plan(
            r(-1800, -1000, 2400, 1500),
            Some(saved_display),
            vec![new],
            Some(0),
        );
        assert_eq!(bounds, r(0, 0, 1920, 1040));
    }

    #[test]
    fn maximized_window_from_a_removed_display_yields_valid_normal_bounds() {
        let saved_display = d("4K", (1920, 0, 3840, 2160), (1920, 0, 3840, 2080), 2.0);
        let internal = d("Internal", (0, 0, 1920, 1080), (0, 0, 1920, 1040), 1.0);
        let bounds = plan(
            r(5000, 400, 2400, 1600),
            Some(saved_display),
            vec![internal],
            Some(0),
        );
        assert_eq!(bounds, r(360, 120, 1200, 800));
    }

    #[test]
    fn redocking_with_an_external_monitor_added_leaves_the_internal_window_alone() {
        let saved_display = d("Built-in", (0, 0, 2560, 1600), (0, 0, 2560, 1550), 2.0);
        let dell = d("Dell", (-1920, 0, 1920, 1080), (-1920, 0, 1920, 1040), 1.0);
        let builtin = d("Built-in", (0, 0, 2560, 1600), (0, 0, 2560, 1550), 2.0);
        let bounds = plan(
            r(300, 200, 2400, 1600),
            Some(saved_display),
            vec![dell, builtin],
            Some(1),
        );
        assert_eq!(bounds, r(300, 200, 2400, 1600));
    }

    // --- Group D: reachability rescue ---

    #[test]
    fn window_dragged_almost_entirely_off_the_right_edge_regains_a_grabbable_title_bar() {
        let display = d("DISPLAY1", (0, 0, 1920, 1080), (0, 0, 1920, 1040), 1.0);
        let bounds = plan(
            r(1880, 400, 1200, 800),
            Some(display.clone()),
            vec![display],
            Some(0),
        );
        assert_eq!(bounds, r(1800, 400, 1200, 800));
    }

    #[test]
    fn title_bar_below_the_work_area_bottom_is_lifted_on_the_y_axis_only() {
        let display = d("DISPLAY1", (0, 0, 1920, 1080), (0, 0, 1920, 1040), 1.0);
        let bounds = plan(
            r(300, 1030, 1200, 800),
            Some(display.clone()),
            vec![display],
            Some(0),
        );
        assert_eq!(bounds, r(300, 1016, 1200, 800));
    }

    #[test]
    fn window_above_the_work_area_top_after_the_taskbar_moved_up_is_pushed_down() {
        let display = d("DISPLAY1", (0, 0, 1920, 1080), (0, 40, 1920, 1040), 1.0);
        let bounds = plan(
            r(300, 20, 1200, 800),
            Some(display.clone()),
            vec![display],
            Some(0),
        );
        assert_eq!(bounds, r(300, 40, 1200, 800));
    }

    // --- Group E: legacy records (saved_display: None) ---

    #[test]
    fn legacy_record_that_is_far_too_large_is_still_clamped() {
        let display = d("DISPLAY1", (0, 0, 1920, 1080), (0, 0, 1920, 1040), 1.0);
        let bounds = plan(r(200, 100, 3000, 1800), None, vec![display], Some(0));
        assert_eq!(bounds, r(740, 480, 1920, 1040));
    }

    #[test]
    fn legacy_record_that_is_still_valid_is_not_touched() {
        let display = d("DISPLAY1", (0, 0, 1920, 1080), (0, 0, 1920, 1040), 1.0);
        let bounds = plan(r(310, 90, 1280, 860), None, vec![display], Some(0));
        assert_eq!(bounds, r(310, 90, 1280, 860));
    }

    // --- Group F: degenerate and hostile inputs ---

    #[test]
    fn work_area_smaller_than_the_app_minimum_keeps_the_minimum_size_and_a_reachable_title_bar() {
        let saved_display = d("Small", (0, 0, 1280, 1024), (0, 0, 1280, 1000), 1.0);
        let monitor = d("Small", (0, 0, 800, 600), (0, 0, 800, 560), 1.0);
        let bounds = plan(
            r(140, 150, 1000, 700),
            Some(saved_display),
            vec![monitor],
            Some(0),
        );
        assert_eq!(bounds, r(-50, 0, 900, 640));
    }

    #[test]
    fn unsatisfiable_grab_band_does_not_push_the_window_off_screen() {
        let display = d("Weird", (0, 0, 1920, 1080), (0, 0, 1920, 20), 1.0);
        let bounds = plan(
            r(300, 500, 1200, 800),
            Some(display.clone()),
            vec![display.clone()],
            Some(0),
        );
        assert_eq!(bounds, r(300, 0, 1200, 800));

        assert!(bounds.width > 0);
        assert!(bounds.height > 0);
        assert!(bounds.intersects(&display.bounds));
    }

    #[test]
    fn invalid_scale_factor_reported_by_a_driver_is_treated_as_one_zero() {
        let saved_display = d("DISPLAY1", (0, 0, 1920, 1080), (0, 0, 1920, 1040), 1.0);
        let monitor = d("DISPLAY1", (0, 0, 1920, 1080), (0, 0, 1920, 1040), 0.0);
        let bounds = plan(
            r(300, 200, 1200, 800),
            Some(saved_display),
            vec![monitor],
            Some(0),
        );
        assert_eq!(bounds, r(300, 200, 1200, 800));
        assert!(bounds.width >= 900 && bounds.height >= 640);
    }

    #[test]
    fn invalid_scale_factor_reported_by_a_driver_is_treated_as_one_nan() {
        let saved_display = d("DISPLAY1", (0, 0, 1920, 1080), (0, 0, 1920, 1040), 1.0);
        let monitor = d("DISPLAY1", (0, 0, 1920, 1080), (0, 0, 1920, 1040), f64::NAN);
        let bounds = plan(
            r(300, 200, 1200, 800),
            Some(saved_display),
            vec![monitor],
            Some(0),
        );
        assert_eq!(bounds, r(300, 200, 1200, 800));
        assert!(bounds.width >= 900 && bounds.height >= 640);
    }

    #[test]
    fn invalid_scale_factor_on_the_saved_display_is_treated_as_one() {
        let saved_display = d("DISPLAY1", (0, 0, 1920, 1080), (0, 0, 1920, 1040), f64::NAN);
        let monitor = d("DISPLAY1", (0, 0, 1920, 1080), (0, 0, 1920, 1040), 1.0);
        let bounds = plan(
            r(0, 0, 3000, 2000),
            Some(saved_display),
            vec![monitor],
            Some(0),
        );
        assert_eq!(bounds, r(540, 480, 1920, 1040));
        assert!(bounds.width >= 900 && bounds.height >= 640);
    }

    #[test]
    fn saved_display_is_matched_by_geometry_when_two_monitors_report_the_same_name_selects_the_right_twin(
    ) {
        let left = d(
            "Generic PnP Monitor",
            (0, 0, 1920, 1080),
            (0, 0, 1920, 1040),
            1.0,
        );
        let right = d(
            "Generic PnP Monitor",
            (1920, 0, 1920, 1080),
            (1920, 0, 1920, 1040),
            1.0,
        );
        let saved_display = right.clone();
        let bounds = plan(
            r(2000, 100, 1200, 800),
            Some(saved_display),
            vec![left, right],
            Some(0),
        );
        assert_eq!(bounds, r(2000, 100, 1200, 800));
    }

    #[test]
    fn saved_display_is_matched_by_geometry_when_two_monitors_report_the_same_name_remaps_onto_the_right_twins_new_work_area(
    ) {
        let left = d(
            "Generic PnP Monitor",
            (0, 0, 1920, 1080),
            (0, 0, 1920, 1040),
            1.0,
        );
        let right_moved = d(
            "Generic PnP Monitor",
            (1920, 0, 1920, 1080),
            (1920, 40, 1920, 1040),
            1.0,
        );
        let saved_display = d(
            "Generic PnP Monitor",
            (1920, 0, 1920, 1080),
            (1920, 0, 1920, 1040),
            1.0,
        );
        let bounds = plan(
            r(2000, 100, 1200, 800),
            Some(saved_display),
            vec![left, right_moved],
            Some(0),
        );
        assert_eq!(bounds, r(2000, 140, 1200, 800));
    }

    #[test]
    fn displays_with_no_name_are_still_matched_by_geometry() {
        let first = d_unnamed((0, 0, 1920, 1080), (0, 0, 1920, 1040), 1.0);
        let second = d_unnamed((1920, 0, 2560, 1440), (1920, 0, 2560, 1400), 1.0);
        let saved_display = second.clone();
        let bounds = plan(
            r(2200, 200, 1400, 900),
            Some(saved_display),
            vec![first, second],
            Some(0),
        );
        assert_eq!(bounds, r(2200, 200, 1400, 900));
    }

    #[test]
    fn degenerate_monitor_lists_are_handled_without_panicking_empty() {
        let result = plan_restore(&RestoreRequest {
            saved: r(0, 0, 100, 100),
            saved_display: None,
            monitors: vec![],
            primary: None,
        });
        assert!(result.is_none());
    }

    #[test]
    fn degenerate_monitor_lists_are_handled_without_panicking_out_of_range_primary() {
        let display = d("DISPLAY1", (0, 0, 1920, 1080), (0, 0, 1920, 1040), 1.0);
        let bounds = plan(r(200, 100, 3000, 1800), None, vec![display], Some(7));
        assert_eq!(bounds, r(740, 480, 1920, 1040));
    }

    #[test]
    fn degenerate_monitor_lists_are_handled_without_panicking_no_primary_and_gone_display() {
        let a = d("A", (0, 0, 1920, 1080), (0, 0, 1920, 1040), 1.0);
        let b = d("B", (1920, 0, 1920, 1080), (1920, 0, 1920, 1040), 1.0);
        let saved_display = d("Gone", (-1920, 0, 1920, 1080), (-1920, 0, 1920, 1040), 1.0);
        let bounds = plan(
            r(2000, 100, 1200, 800),
            Some(saved_display),
            vec![a, b],
            None,
        );
        assert_eq!(bounds, r(2000, 100, 1200, 800));
    }
}
