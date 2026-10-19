// Screen color picker for when WebView2 refuses to open its own eyedropper: on some systems
// EyeDropper.open() rejects within milliseconds without ever showing the loupe.

use std::ffi::c_void;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::OnceLock;
use std::time::{Duration, Instant};

use windows::core::{w, PCWSTR};
use windows::Win32::Foundation::{
    GetLastError, COLORREF, ERROR_CLASS_ALREADY_EXISTS, HINSTANCE, HWND, LPARAM, LRESULT, POINT,
    RECT, WPARAM,
};
use windows::Win32::Graphics::Gdi::{
    BeginPaint, BitBlt, CreateCompatibleBitmap, CreateCompatibleDC, CreateDIBSection, CreateFontW,
    CreateSolidBrush, DeleteDC, DeleteObject, DrawTextW, EndPaint, FillRect, FrameRect, GdiFlush,
    GetDC, GetMonitorInfoW, IntersectRect, InvalidateRect, MonitorFromPoint, ReleaseDC,
    SelectObject, SetBkMode, SetStretchBltMode, SetTextColor, StretchBlt, BITMAPINFO,
    BITMAPINFOHEADER, BI_RGB, CAPTUREBLT, CLEARTYPE_QUALITY, CLIP_DEFAULT_PRECIS, COLORONCOLOR,
    DEFAULT_CHARSET, DEFAULT_PITCH, DIB_RGB_COLORS, DT_CENTER, DT_SINGLELINE, DT_VCENTER,
    FW_SEMIBOLD, HBITMAP, HDC, HGDIOBJ, MONITORINFO, MONITOR_DEFAULTTONEAREST, OUT_DEFAULT_PRECIS,
    PAINTSTRUCT, SRCCOPY, TRANSPARENT,
};
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::UI::HiDpi::{GetDpiForMonitor, MDT_EFFECTIVE_DPI};
use windows::Win32::UI::Input::KeyboardAndMouse::{
    GetKeyState, ReleaseCapture, SetCapture, VIRTUAL_KEY, VK_DOWN, VK_ESCAPE, VK_LEFT, VK_RETURN,
    VK_RIGHT, VK_SHIFT, VK_SPACE, VK_UP,
};
use windows::Win32::UI::WindowsAndMessaging::{
    CreateWindowExW, DefWindowProcW, DestroyWindow, DispatchMessageW, GetCursorPos, GetMessageW,
    GetSystemMetrics, GetWindowLongPtrW, LoadCursorW, PostQuitMessage, RegisterClassExW, SetCursor,
    SetCursorPos, SetForegroundWindow, SetWindowLongPtrW, ShowWindow, TranslateMessage,
    GWLP_USERDATA, IDC_CROSS, MSG, SM_CXVIRTUALSCREEN, SM_CYVIRTUALSCREEN, SM_XVIRTUALSCREEN,
    SM_YVIRTUALSCREEN, SW_SHOW, WA_INACTIVE, WM_ACTIVATE, WM_DESTROY, WM_DISPLAYCHANGE,
    WM_ERASEBKGND, WM_KEYDOWN, WM_KEYUP, WM_LBUTTONDOWN, WM_LBUTTONUP, WM_MOUSEMOVE, WM_PAINT,
    WM_RBUTTONUP, WNDCLASSEXW, WS_EX_TOOLWINDOW, WS_EX_TOPMOST, WS_POPUP,
};

use crate::logging;

const CLASS_NAME: PCWSTR = w!("MacroDeckScreenColorPicker");

// A click within this window is still the one that opened the picker, as in Chromium's eyedropper.
const SELECTION_DELAY: Duration = Duration::from_millis(500);

const BASE_DPI: u32 = 96;
const LOUPE_PIXELS: i32 = 11;
const LOUPE_CELL: i32 = 10;
const LOUPE_GAP: i32 = 16;
const LABEL_HEIGHT: i32 = 24;
const FONT_HEIGHT: i32 = 14;
const SWATCH_SIZE: i32 = 14;
const LABEL_PADDING: i32 = 6;
const FAST_STEP: i32 = 10;

static OPEN: AtomicBool = AtomicBool::new(false);
static CLASS_REGISTERED: OnceLock<bool> = OnceLock::new();

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
struct Bounds {
    left: i32,
    top: i32,
    right: i32,
    bottom: i32,
}

struct OpenGuard;

impl Drop for OpenGuard {
    fn drop(&mut self) {
        OPEN.store(false, Ordering::Release);
    }
}

pub fn pick() -> Option<String> {
    if OPEN.swap(true, Ordering::AcqRel) {
        logging::info("[screen-color] picker is already open");
        return None;
    }
    let _guard = OpenGuard;

    let opened = Instant::now();
    let picked = unsafe { run() };
    let elapsed = opened.elapsed().as_millis();
    match &picked {
        Some(hex) => logging::info(&format!("[screen-color] picked {hex} after {elapsed} ms")),
        None => logging::info(&format!(
            "[screen-color] closed without a pick after {elapsed} ms"
        )),
    }
    picked
}

struct Capture {
    dc: HDC,
    bitmap: HBITMAP,
    previous: HGDIOBJ,
    pixels: *const u8,
    origin: POINT,
    width: i32,
    height: i32,
}

impl Capture {
    unsafe fn take() -> Option<Capture> {
        let origin = POINT {
            x: GetSystemMetrics(SM_XVIRTUALSCREEN),
            y: GetSystemMetrics(SM_YVIRTUALSCREEN),
        };
        let width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        let height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if width <= 0 || height <= 0 {
            logging::error("[screen-color] the virtual screen has no size");
            return None;
        }

        let screen = GetDC(None);
        let dc = CreateCompatibleDC(Some(screen));
        let info = BITMAPINFO {
            bmiHeader: BITMAPINFOHEADER {
                biSize: std::mem::size_of::<BITMAPINFOHEADER>() as u32,
                biWidth: width,
                biHeight: -height,
                biPlanes: 1,
                biBitCount: 32,
                biCompression: BI_RGB.0,
                ..Default::default()
            },
            ..Default::default()
        };
        let mut bits: *mut c_void = std::ptr::null_mut();
        let bitmap = match CreateDIBSection(Some(dc), &info, DIB_RGB_COLORS, &mut bits, None, 0) {
            Ok(bitmap) => bitmap,
            Err(error) => {
                logging::error(&format!(
                    "[screen-color] could not allocate the capture: {error}"
                ));
                let _ = DeleteDC(dc);
                ReleaseDC(None, screen);
                return None;
            }
        };
        let previous = SelectObject(dc, bitmap.into());
        let capture = Capture {
            dc,
            bitmap,
            previous,
            pixels: bits as *const u8,
            origin,
            width,
            height,
        };

        let copied = BitBlt(
            dc,
            0,
            0,
            width,
            height,
            Some(screen),
            origin.x,
            origin.y,
            SRCCOPY | CAPTUREBLT,
        );
        ReleaseDC(None, screen);
        if let Err(error) = copied {
            logging::error(&format!(
                "[screen-color] could not capture the screen: {error}"
            ));
            return None;
        }
        let _ = GdiFlush();
        Some(capture)
    }

    fn pixel(&self, screen: POINT) -> Option<[u8; 4]> {
        let index = pixel_index(screen, self.origin, self.width, self.height)?;
        Some(unsafe { std::ptr::read(self.pixels.add(index) as *const [u8; 4]) })
    }
}

impl Drop for Capture {
    fn drop(&mut self) {
        unsafe {
            SelectObject(self.dc, self.previous);
            let _ = DeleteObject(self.bitmap.into());
            let _ = DeleteDC(self.dc);
        }
    }
}

struct Picker {
    capture: Capture,
    opened: Instant,
    loupe: Option<Bounds>,
    color: [u8; 4],
    dpi: u32,
    activated: bool,
    mouse_armed: bool,
    key_armed: Option<VIRTUAL_KEY>,
    result: Option<String>,
}

unsafe fn run() -> Option<String> {
    let instance: HINSTANCE = match GetModuleHandleW(None) {
        Ok(module) => module.into(),
        Err(error) => {
            logging::error(&format!("[screen-color] no module handle: {error}"));
            return None;
        }
    };
    if !*CLASS_REGISTERED.get_or_init(|| register_class(instance)) {
        return None;
    }

    let capture = Capture::take()?;
    let (origin, width, height) = (capture.origin, capture.width, capture.height);
    let picker = Box::into_raw(Box::new(Picker {
        opened: Instant::now(),
        loupe: None,
        color: [0, 0, 0, 0],
        dpi: BASE_DPI,
        activated: false,
        mouse_armed: false,
        key_armed: None,
        result: None,
        capture,
    }));

    let hwnd = match CreateWindowExW(
        WS_EX_TOPMOST | WS_EX_TOOLWINDOW,
        CLASS_NAME,
        PCWSTR::null(),
        WS_POPUP,
        origin.x,
        origin.y,
        width,
        height,
        None,
        None,
        Some(instance),
        None,
    ) {
        Ok(hwnd) => hwnd,
        Err(error) => {
            logging::error(&format!(
                "[screen-color] could not create the overlay: {error}"
            ));
            drop(Box::from_raw(picker));
            return None;
        }
    };
    SetWindowLongPtrW(hwnd, GWLP_USERDATA, picker as isize);
    update_loupe(hwnd, &mut *picker);
    let _ = ShowWindow(hwnd, SW_SHOW);
    if !SetForegroundWindow(hwnd).as_bool() {
        logging::info("[screen-color] the overlay did not become the foreground window");
    }
    SetCapture(hwnd);
    if let Ok(cursor) = LoadCursorW(None, IDC_CROSS) {
        SetCursor(Some(cursor));
    }

    let mut message = MSG::default();
    while GetMessageW(&mut message, None, 0, 0).0 > 0 {
        let _ = TranslateMessage(&message);
        DispatchMessageW(&message);
    }

    SetWindowLongPtrW(hwnd, GWLP_USERDATA, 0);
    Box::from_raw(picker).result
}

unsafe fn register_class(instance: HINSTANCE) -> bool {
    let class = WNDCLASSEXW {
        cbSize: std::mem::size_of::<WNDCLASSEXW>() as u32,
        lpfnWndProc: Some(window_proc),
        hInstance: instance,
        hCursor: LoadCursorW(None, IDC_CROSS).unwrap_or_default(),
        lpszClassName: CLASS_NAME,
        ..Default::default()
    };
    if RegisterClassExW(&class) != 0 || GetLastError() == ERROR_CLASS_ALREADY_EXISTS {
        return true;
    }
    logging::error(&format!(
        "[screen-color] could not register the overlay class: {:?}",
        GetLastError()
    ));
    false
}

enum Outcome {
    Handled,
    Default,
    Close,
}

unsafe extern "system" fn window_proc(
    hwnd: HWND,
    message: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    match message {
        WM_ERASEBKGND => return LRESULT(1),
        WM_DESTROY => {
            SetWindowLongPtrW(hwnd, GWLP_USERDATA, 0);
            PostQuitMessage(0);
            return LRESULT(0);
        }
        _ => {}
    }

    let picker = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut Picker;
    if picker.is_null() {
        return DefWindowProcW(hwnd, message, wparam, lparam);
    }

    // ReleaseCapture and DestroyWindow re-enter this procedure, so no borrow may outlive handle.
    let outcome = handle(hwnd, &mut *picker, message, wparam, lparam);
    match outcome {
        Outcome::Handled => LRESULT(0),
        Outcome::Default => DefWindowProcW(hwnd, message, wparam, lparam),
        Outcome::Close => {
            let _ = ReleaseCapture();
            let _ = DestroyWindow(hwnd);
            LRESULT(0)
        }
    }
}

unsafe fn handle(
    hwnd: HWND,
    picker: &mut Picker,
    message: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> Outcome {
    match message {
        WM_PAINT => {
            paint(hwnd, picker);
            Outcome::Handled
        }
        WM_MOUSEMOVE => {
            update_loupe(hwnd, picker);
            Outcome::Handled
        }
        WM_ACTIVATE => {
            if wparam.0 as u32 & 0xFFFF != WA_INACTIVE {
                picker.activated = true;
                Outcome::Default
            } else if picker.activated {
                logging::info("[screen-color] the overlay lost focus");
                Outcome::Close
            } else {
                Outcome::Default
            }
        }
        WM_DISPLAYCHANGE => Outcome::Close,
        WM_LBUTTONDOWN => {
            picker.mouse_armed = picker.opened.elapsed() >= SELECTION_DELAY;
            Outcome::Handled
        }
        WM_LBUTTONUP if picker.mouse_armed => {
            pick_at_cursor(picker);
            Outcome::Close
        }
        WM_RBUTTONUP => Outcome::Close,
        WM_KEYDOWN => {
            let key = VIRTUAL_KEY(wparam.0 as u16);
            if let Some((dx, dy)) = arrow_step(key, GetKeyState(VK_SHIFT.0 as i32) < 0) {
                let mut cursor = POINT::default();
                if GetCursorPos(&mut cursor).is_ok() {
                    let _ = SetCursorPos(cursor.x + dx, cursor.y + dy);
                    update_loupe(hwnd, picker);
                }
            } else if is_first_press(lparam) && matches!(key, VK_RETURN | VK_SPACE | VK_ESCAPE) {
                picker.key_armed = Some(key);
            }
            Outcome::Handled
        }
        WM_KEYUP if picker.key_armed == Some(VIRTUAL_KEY(wparam.0 as u16)) => {
            if picker.key_armed != Some(VK_ESCAPE) {
                pick_at_cursor(picker);
            }
            Outcome::Close
        }
        WM_LBUTTONUP | WM_KEYUP => Outcome::Handled,
        _ => Outcome::Default,
    }
}

unsafe fn pick_at_cursor(picker: &mut Picker) {
    let mut cursor = POINT::default();
    if GetCursorPos(&mut cursor).is_ok() {
        picker.result = picker.capture.pixel(cursor).map(hex_from_bgra);
    }
}

unsafe fn update_loupe(hwnd: HWND, picker: &mut Picker) {
    let mut cursor = POINT::default();
    if GetCursorPos(&mut cursor).is_err() {
        return;
    }

    let monitor = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);
    let mut info = MONITORINFO {
        cbSize: std::mem::size_of::<MONITORINFO>() as u32,
        ..Default::default()
    };
    let bounds = if GetMonitorInfoW(monitor, &mut info).as_bool() {
        bounds_of(info.rcMonitor)
    } else {
        let origin = picker.capture.origin;
        Bounds {
            left: origin.x,
            top: origin.y,
            right: origin.x + picker.capture.width,
            bottom: origin.y + picker.capture.height,
        }
    };
    let (mut dpi_x, mut dpi_y) = (BASE_DPI, BASE_DPI);
    if GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, &mut dpi_x, &mut dpi_y).is_err() {
        dpi_x = BASE_DPI;
    }

    picker.dpi = dpi_x;
    picker.color = picker.capture.pixel(cursor).unwrap_or_default();
    let size = loupe_size(dpi_x);
    let screen_rect = loupe_rect(cursor, size, scale(LOUPE_GAP, dpi_x), bounds);
    let origin = picker.capture.origin;
    let next = Bounds {
        left: screen_rect.left - origin.x,
        top: screen_rect.top - origin.y,
        right: screen_rect.right - origin.x,
        bottom: screen_rect.bottom - origin.y,
    };

    if let Some(previous) = picker.loupe {
        let _ = InvalidateRect(Some(hwnd), Some(&rect_of(previous)), false);
    }
    let _ = InvalidateRect(Some(hwnd), Some(&rect_of(next)), false);
    picker.loupe = Some(next);
}

unsafe fn paint(hwnd: HWND, picker: &Picker) {
    let mut paint = PAINTSTRUCT::default();
    let target = BeginPaint(hwnd, &mut paint);
    let area = paint.rcPaint;
    let width = area.right - area.left;
    let height = area.bottom - area.top;
    if width > 0 && height > 0 {
        let buffer = CreateCompatibleDC(Some(target));
        let bitmap = CreateCompatibleBitmap(target, width, height);
        let previous = SelectObject(buffer, bitmap.into());

        let _ = BitBlt(
            buffer,
            0,
            0,
            width,
            height,
            Some(picker.capture.dc),
            area.left,
            area.top,
            SRCCOPY,
        );
        if let Some(loupe) = picker.loupe {
            let mut overlap = RECT::default();
            if IntersectRect(&mut overlap, &area, &rect_of(loupe)).as_bool() {
                draw_loupe(
                    buffer,
                    picker,
                    loupe,
                    POINT {
                        x: area.left,
                        y: area.top,
                    },
                );
            }
        }
        let _ = BitBlt(
            target,
            area.left,
            area.top,
            width,
            height,
            Some(buffer),
            0,
            0,
            SRCCOPY,
        );

        SelectObject(buffer, previous);
        let _ = DeleteObject(bitmap.into());
        let _ = DeleteDC(buffer);
    }
    let _ = EndPaint(hwnd, &paint);
}

unsafe fn draw_loupe(dc: HDC, picker: &Picker, loupe: Bounds, offset: POINT) {
    let dpi = picker.dpi;
    let cell = scale(LOUPE_CELL, dpi);
    let zoom = cell * LOUPE_PIXELS;
    let left = loupe.left - offset.x;
    let top = loupe.top - offset.y;
    let mut cursor = POINT::default();
    let _ = GetCursorPos(&mut cursor);
    let source_x = cursor.x - picker.capture.origin.x - LOUPE_PIXELS / 2;
    let source_y = cursor.y - picker.capture.origin.y - LOUPE_PIXELS / 2;

    let border = CreateSolidBrush(COLORREF(0x0040_4040));
    let background = CreateSolidBrush(COLORREF(0x0020_2020));
    let light = CreateSolidBrush(COLORREF(0x00FF_FFFF));
    let dark = CreateSolidBrush(COLORREF(0x0000_0000));
    let [blue, green, red, _] = picker.color;
    let swatch_brush = CreateSolidBrush(COLORREF(
        u32::from(red) | u32::from(green) << 8 | u32::from(blue) << 16,
    ));

    SetStretchBltMode(dc, COLORONCOLOR);
    let _ = StretchBlt(
        dc,
        left,
        top,
        zoom,
        zoom,
        Some(picker.capture.dc),
        source_x,
        source_y,
        LOUPE_PIXELS,
        LOUPE_PIXELS,
        SRCCOPY,
    );

    let middle = LOUPE_PIXELS / 2 * cell;
    let outer = RECT {
        left: left + middle - 1,
        top: top + middle - 1,
        right: left + middle + cell + 1,
        bottom: top + middle + cell + 1,
    };
    FrameRect(dc, &outer, dark);
    let inner = RECT {
        left: outer.left + 1,
        top: outer.top + 1,
        right: outer.right - 1,
        bottom: outer.bottom - 1,
    };
    FrameRect(dc, &inner, light);

    let label = RECT {
        left,
        top: top + zoom,
        right: left + zoom,
        bottom: loupe.bottom - offset.y,
    };
    FillRect(dc, &label, background);
    let padding = scale(LABEL_PADDING, dpi);
    let swatch_size = scale(SWATCH_SIZE, dpi);
    let swatch_top = label.top + (label.bottom - label.top - swatch_size) / 2;
    let swatch = RECT {
        left: label.left + padding,
        top: swatch_top,
        right: label.left + padding + swatch_size,
        bottom: swatch_top + swatch_size,
    };
    FillRect(dc, &swatch, swatch_brush);
    FrameRect(dc, &swatch, light);

    let font = CreateFontW(
        -scale(FONT_HEIGHT, dpi),
        0,
        0,
        0,
        FW_SEMIBOLD.0 as i32,
        0,
        0,
        0,
        DEFAULT_CHARSET,
        OUT_DEFAULT_PRECIS,
        CLIP_DEFAULT_PRECIS,
        CLEARTYPE_QUALITY,
        u32::from(DEFAULT_PITCH.0),
        w!("Segoe UI"),
    );
    let previous_font = SelectObject(dc, font.into());
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, COLORREF(0x00FF_FFFF));
    let mut text_rect = RECT {
        left: swatch.right,
        ..label
    };
    let mut text: Vec<u16> = hex_from_bgra(picker.color).encode_utf16().collect();
    DrawTextW(
        dc,
        &mut text,
        &mut text_rect,
        DT_CENTER | DT_VCENTER | DT_SINGLELINE,
    );
    SelectObject(dc, previous_font);

    let frame = RECT {
        left,
        top,
        right: loupe.right - offset.x,
        bottom: loupe.bottom - offset.y,
    };
    FrameRect(dc, &frame, border);

    for object in [border, background, light, dark, swatch_brush] {
        let _ = DeleteObject(object.into());
    }
    let _ = DeleteObject(font.into());
}

fn is_first_press(lparam: LPARAM) -> bool {
    lparam.0 & (1 << 30) == 0
}

fn arrow_step(key: VIRTUAL_KEY, fast: bool) -> Option<(i32, i32)> {
    let step = if fast { FAST_STEP } else { 1 };
    match key {
        VK_LEFT => Some((-step, 0)),
        VK_RIGHT => Some((step, 0)),
        VK_UP => Some((0, -step)),
        VK_DOWN => Some((0, step)),
        _ => None,
    }
}

fn hex_from_bgra(pixel: [u8; 4]) -> String {
    format!("#{:02x}{:02x}{:02x}", pixel[2], pixel[1], pixel[0])
}

fn pixel_index(screen: POINT, origin: POINT, width: i32, height: i32) -> Option<usize> {
    let x = screen.x - origin.x;
    let y = screen.y - origin.y;
    if x < 0 || y < 0 || x >= width || y >= height {
        return None;
    }
    Some((y as usize * width as usize + x as usize) * 4)
}

fn scale(value: i32, dpi: u32) -> i32 {
    (i64::from(value) * i64::from(dpi.max(BASE_DPI)) / i64::from(BASE_DPI)) as i32
}

fn loupe_size(dpi: u32) -> (i32, i32) {
    let zoom = scale(LOUPE_CELL, dpi) * LOUPE_PIXELS;
    (zoom, zoom + scale(LABEL_HEIGHT, dpi))
}

fn loupe_rect(cursor: POINT, size: (i32, i32), gap: i32, monitor: Bounds) -> Bounds {
    let (width, height) = size;
    let mut left = cursor.x + gap;
    if left + width > monitor.right {
        left = cursor.x - gap - width;
    }
    let mut top = cursor.y + gap;
    if top + height > monitor.bottom {
        top = cursor.y - gap - height;
    }
    left = left.min(monitor.right - width).max(monitor.left);
    top = top.min(monitor.bottom - height).max(monitor.top);
    Bounds {
        left,
        top,
        right: left + width,
        bottom: top + height,
    }
}

fn bounds_of(rect: RECT) -> Bounds {
    Bounds {
        left: rect.left,
        top: rect.top,
        right: rect.right,
        bottom: rect.bottom,
    }
}

fn rect_of(bounds: Bounds) -> RECT {
    RECT {
        left: bounds.left,
        top: bounds.top,
        right: bounds.right,
        bottom: bounds.bottom,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const MONITOR: Bounds = Bounds {
        left: -1920,
        top: 0,
        right: 0,
        bottom: 1080,
    };

    #[test]
    fn hex_is_read_from_bgra_bytes() {
        assert_eq!(hex_from_bgra([0x33, 0x22, 0x11, 0xFF]), "#112233");
        assert_eq!(hex_from_bgra([0xFF, 0x00, 0x00, 0x00]), "#0000ff");
    }

    #[test]
    fn a_screen_point_left_of_the_primary_monitor_maps_into_the_capture() {
        let origin = POINT { x: -1920, y: -200 };

        assert_eq!(
            pixel_index(POINT { x: -1920, y: -200 }, origin, 3840, 1280),
            Some(0)
        );
        assert_eq!(
            pixel_index(POINT { x: 10, y: 0 }, origin, 3840, 1280),
            Some((200 * 3840 + 1930) * 4)
        );
    }

    #[test]
    fn a_screen_point_outside_the_capture_has_no_pixel() {
        let origin = POINT { x: -1920, y: 0 };

        assert_eq!(
            pixel_index(POINT { x: -1921, y: 0 }, origin, 3840, 1080),
            None
        );
        assert_eq!(
            pixel_index(POINT { x: 1920, y: 0 }, origin, 3840, 1080),
            None
        );
        assert_eq!(
            pixel_index(POINT { x: 0, y: 1080 }, origin, 3840, 1080),
            None
        );
    }

    #[test]
    fn the_loupe_grows_with_the_monitor_scale() {
        assert_eq!(scale(10, 96), 10);
        assert_eq!(scale(10, 144), 15);
        assert_eq!(scale(10, 240), 25);
        assert_eq!(loupe_size(240), (275, 335));
    }

    #[test]
    fn the_loupe_sits_below_right_of_the_cursor_when_there_is_room() {
        let rect = loupe_rect(POINT { x: -1000, y: 500 }, (110, 134), 16, MONITOR);

        assert_eq!(
            rect,
            Bounds {
                left: -984,
                top: 516,
                right: -874,
                bottom: 650
            }
        );
    }

    #[test]
    fn the_loupe_flips_at_the_right_and_bottom_monitor_edges() {
        let rect = loupe_rect(POINT { x: -5, y: 1075 }, (110, 134), 16, MONITOR);

        assert_eq!(rect.right, -21);
        assert_eq!(rect.bottom, 1059);
    }

    #[test]
    fn the_loupe_stays_on_the_cursor_monitor_at_the_left_and_top_edges() {
        let rect = loupe_rect(POINT { x: -1915, y: 3 }, (110, 134), 16, MONITOR);

        assert!(rect.left >= MONITOR.left && rect.right <= MONITOR.right);
        assert!(rect.top >= MONITOR.top && rect.bottom <= MONITOR.bottom);
    }

    #[test]
    fn a_loupe_larger_than_the_monitor_is_pinned_to_its_top_left() {
        let small = Bounds {
            left: 0,
            top: 0,
            right: 100,
            bottom: 100,
        };

        let rect = loupe_rect(POINT { x: 50, y: 50 }, (275, 335), 40, small);

        assert_eq!((rect.left, rect.top), (0, 0));
    }
}
