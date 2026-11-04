use std::ffi::{OsStr, OsString};
use std::os::unix::ffi::{OsStrExt, OsStringExt};
use std::path::{Path, PathBuf};

pub const ORIGINAL_VARIABLE: &str = "MACRODECK_ORIGINAL_FONTCONFIG_FILE";
const FONTCONFIG_VARIABLE: &str = "FONTCONFIG_FILE";
const BUNDLED_FONT: &str = "usr/share/macro-deck/fonts/NotoColorEmoji.ttf";
const DEFAULT_CONFIG: &str = "/etc/fonts/fonts.conf";
const EMOJI_FAMILY: &str = "Noto Color Emoji";
const ABSENT: &[u8] = b"-";
const PRESENT: u8 = b'+';

pub struct Guard {
    pub config: PathBuf,
}

// The bundled WebKit aborts while painting COLRv1 colour glyphs, which recent distributions ship
// as Noto Color Emoji. Its bitmap build replaces the system one for the app's own web processes.
#[cfg(target_os = "linux")]
pub fn install() {
    let Some(appdir) = crate::appimage::dir() else {
        return;
    };
    let Some(cache) = cache_root() else {
        return;
    };
    let current = std::env::var_os(FONTCONFIG_VARIABLE);
    let marker = std::env::var_os(ORIGINAL_VARIABLE);
    match prepare(&appdir, &cache, current.as_deref(), marker.as_deref()) {
        Ok(Some(guard)) => {
            if marker.is_none() {
                std::env::set_var(ORIGINAL_VARIABLE, encode_original(current.as_deref()));
            }
            std::env::set_var(FONTCONFIG_VARIABLE, &guard.config);
            crate::logging::info(
                "[fonts] system COLRv1 emoji fonts are replaced by the bundled emoji font",
            );
        }
        Ok(None) => {}
        Err(error) => {
            crate::logging::warn(&format!("[fonts] emoji font guard not installed: {error}"))
        }
    }
}

#[cfg(target_os = "linux")]
fn cache_root() -> Option<PathBuf> {
    let configured = std::env::var_os("XDG_CACHE_HOME")
        .map(PathBuf::from)
        .filter(|path| path.is_absolute());
    configured.or_else(|| {
        std::env::var_os("HOME")
            .map(|home| PathBuf::from(home).join(".cache"))
            .filter(|path| path.is_absolute())
    })
}

pub fn prepare(
    appdir: &Path,
    cache: &Path,
    current: Option<&OsStr>,
    marker: Option<&OsStr>,
) -> std::io::Result<Option<Guard>> {
    let bundled = appdir.join(BUNDLED_FONT);
    let Ok(metadata) = std::fs::metadata(&bundled) else {
        return Ok(None);
    };

    let root = cache.join("macro-deck").join("fontconfig");
    let fonts = root.join("fonts");
    std::fs::create_dir_all(&fonts)?;
    let fonts = fonts.canonicalize()?;
    let font = fonts.join(format!("NotoColorEmoji-{}.ttf", metadata.len()));
    if std::fs::metadata(&font).map_or(true, |copy| copy.len() != metadata.len()) {
        let partial = fonts.join(format!("NotoColorEmoji.{}.partial", std::process::id()));
        std::fs::copy(&bundled, &partial)?;
        std::fs::rename(&partial, &font)?;
    }

    let config = root.canonicalize()?.join("macro-deck-appimage.conf");
    let original = effective_original(original_value(current, marker), &config);
    let partial = config.with_extension(format!("conf.{}.partial", std::process::id()));
    std::fs::write(&partial, config_xml(&original, &fonts, &font))?;
    std::fs::rename(&partial, &config)?;
    Ok(Some(Guard { config }))
}

fn original_value(current: Option<&OsStr>, marker: Option<&OsStr>) -> Option<OsString> {
    match marker {
        Some(marker) => decode_original(marker),
        None => current.map(OsStr::to_os_string),
    }
}

fn encode_original(value: Option<&OsStr>) -> OsString {
    match value {
        None => OsString::from_vec(ABSENT.to_vec()),
        Some(value) => {
            let mut bytes = vec![PRESENT];
            bytes.extend_from_slice(value.as_bytes());
            OsString::from_vec(bytes)
        }
    }
}

fn decode_original(marker: &OsStr) -> Option<OsString> {
    match marker.as_bytes().split_first() {
        Some((&PRESENT, rest)) => Some(OsString::from_vec(rest.to_vec())),
        _ => None,
    }
}

fn effective_original(original: Option<OsString>, generated: &Path) -> PathBuf {
    match original {
        Some(path) if !path.is_empty() && Path::new(&path) != generated => PathBuf::from(path),
        _ => PathBuf::from(DEFAULT_CONFIG),
    }
}

pub fn config_xml(original: &Path, fonts: &Path, font: &Path) -> String {
    format!(
        "<?xml version=\"1.0\"?>\n\
<!DOCTYPE fontconfig SYSTEM \"urn:fontconfig:fonts.dtd\">\n\
<fontconfig>\n\
<include ignore_missing=\"yes\">{original}</include>\n\
<dir>{fonts}</dir>\n\
<selectfont>\n\
<rejectfont><pattern><patelt name=\"family\"><string>{family}</string></patelt></pattern></rejectfont>\n\
<acceptfont><pattern><patelt name=\"file\"><string>{font}</string></patelt></pattern></acceptfont>\n\
</selectfont>\n\
</fontconfig>\n",
        original = escape(original),
        fonts = escape(fonts),
        font = escape(font),
        family = EMOJI_FAMILY
    )
}

fn escape(path: &Path) -> String {
    path.to_string_lossy()
        .replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
        .replace('"', "&quot;")
        .replace('\'', "&apos;")
}

pub fn restore_variables(variables: Vec<(OsString, OsString)>) -> Vec<(OsString, OsString)> {
    let Some(marker) = variables
        .iter()
        .find(|(name, _)| name == ORIGINAL_VARIABLE)
        .map(|(_, value)| value.clone())
    else {
        return variables;
    };
    let mut restored: Vec<(OsString, OsString)> = variables
        .into_iter()
        .filter(|(name, _)| name != ORIGINAL_VARIABLE && name != FONTCONFIG_VARIABLE)
        .collect();
    if let Some(original) = decode_original(&marker) {
        restored.push((OsString::from(FONTCONFIG_VARIABLE), original));
    }
    restored
}

#[cfg(target_os = "linux")]
pub fn restore_for_child(command: &mut std::process::Command) {
    let Some(marker) = std::env::var_os(ORIGINAL_VARIABLE) else {
        return;
    };
    command.env_remove(ORIGINAL_VARIABLE);
    match decode_original(&marker) {
        Some(original) => command.env(FONTCONFIG_VARIABLE, original),
        None => command.env_remove(FONTCONFIG_VARIABLE),
    };
}

#[cfg(test)]
mod tests {
    use super::*;

    fn scratch(name: &str) -> PathBuf {
        let dir = std::env::temp_dir()
            .canonicalize()
            .unwrap()
            .join(format!("md-font-guard-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    fn appdir_with_font(root: &Path, bytes: &[u8]) -> PathBuf {
        let appdir = root.join("appdir");
        let font = appdir.join(BUNDLED_FONT);
        std::fs::create_dir_all(font.parent().unwrap()).unwrap();
        std::fs::write(font, bytes).unwrap();
        appdir
    }

    fn pair(name: &str, value: &str) -> (OsString, OsString) {
        (OsString::from(name), OsString::from(value))
    }

    #[test]
    fn the_config_keeps_the_original_and_replaces_only_the_system_emoji_family() {
        let xml = config_xml(
            Path::new("/etc/fonts/fonts.conf"),
            Path::new("/cache/fonts"),
            Path::new("/cache/fonts/NotoColorEmoji-1.ttf"),
        );
        assert!(xml.contains("<include ignore_missing=\"yes\">/etc/fonts/fonts.conf</include>"));
        assert!(xml.contains("<dir>/cache/fonts</dir>"));
        assert!(xml.contains(
            "<rejectfont><pattern><patelt name=\"family\"><string>Noto Color Emoji</string>"
        ));
        assert!(xml.contains(
            "<acceptfont><pattern><patelt name=\"file\"><string>/cache/fonts/NotoColorEmoji-1.ttf</string>"
        ));
    }

    #[test]
    fn markup_characters_in_paths_are_escaped() {
        let xml = config_xml(
            Path::new("/home/a&b/<fonts>.conf"),
            Path::new("/cache/\"q\""),
            Path::new("/cache/it's.ttf"),
        );
        assert!(xml.contains("/home/a&amp;b/&lt;fonts&gt;.conf"));
        assert!(xml.contains("/cache/&quot;q&quot;"));
        assert!(xml.contains("/cache/it&apos;s.ttf"));
    }

    #[test]
    fn without_a_bundled_font_nothing_is_replaced() {
        let root = scratch("nofont");
        let guard = prepare(&root.join("appdir"), &root.join("cache"), None, None).unwrap();
        assert!(guard.is_none());
        assert!(!root.join("cache").exists());
        std::fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn the_bundled_font_is_copied_to_a_stable_place_and_referenced() {
        let root = scratch("copy");
        let appdir = appdir_with_font(&root, b"font-bytes");
        let cache = root.join("cache");

        let guard = prepare(&appdir, &cache, None, None).unwrap().unwrap();
        let copy = cache.join("macro-deck/fontconfig/fonts/NotoColorEmoji-10.ttf");
        assert_eq!(std::fs::read(&copy).unwrap(), b"font-bytes");
        let xml = std::fs::read_to_string(&guard.config).unwrap();
        assert!(xml.contains(&copy.canonicalize().unwrap().to_string_lossy().into_owned()));
        assert!(xml.contains("/etc/fonts/fonts.conf"));

        let again = prepare(&appdir, &cache, None, None).unwrap().unwrap();
        assert_eq!(again.config, guard.config);
        std::fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn the_users_own_fontconfig_file_stays_the_included_original() {
        let root = scratch("original");
        let appdir = appdir_with_font(&root, b"font");
        let guard = prepare(
            &appdir,
            &root.join("cache"),
            Some(OsStr::new("/home/u/fonts.conf")),
            None,
        )
        .unwrap()
        .unwrap();
        let xml = std::fs::read_to_string(guard.config).unwrap();
        assert!(xml.contains(">/home/u/fonts.conf</include>"));
        std::fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn a_restarted_instance_keeps_the_recorded_original_and_never_includes_itself() {
        let root = scratch("restart");
        let appdir = appdir_with_font(&root, b"font");
        let cache = root.join("cache");
        let first = prepare(
            &appdir,
            &cache,
            Some(OsStr::new("/home/u/fonts.conf")),
            None,
        )
        .unwrap()
        .unwrap();
        let marker = encode_original(Some(OsStr::new("/home/u/fonts.conf")));

        let second = prepare(
            &appdir,
            &cache,
            Some(first.config.as_os_str()),
            Some(&marker),
        )
        .unwrap()
        .unwrap();
        let xml = std::fs::read_to_string(&second.config).unwrap();
        assert!(xml.contains(">/home/u/fonts.conf</include>"));
        assert!(!xml.contains(&format!(">{}</include>", first.config.display())));
        std::fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn a_lost_marker_falls_back_to_the_system_config_instead_of_including_itself() {
        let root = scratch("lost");
        let appdir = appdir_with_font(&root, b"font");
        let cache = root.join("cache");
        let first = prepare(&appdir, &cache, None, None).unwrap().unwrap();

        let second = prepare(&appdir, &cache, Some(first.config.as_os_str()), None)
            .unwrap()
            .unwrap();
        let xml = std::fs::read_to_string(second.config).unwrap();
        assert!(xml.contains(">/etc/fonts/fonts.conf</include>"));
        std::fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn children_get_the_original_fontconfig_variable_back() {
        let set = restore_variables(vec![
            pair("HOME", "/home/u"),
            pair(FONTCONFIG_VARIABLE, "/cache/generated.conf"),
            (
                OsString::from(ORIGINAL_VARIABLE),
                encode_original(Some(OsStr::new("/home/u/fonts.conf"))),
            ),
        ]);
        assert!(set.contains(&pair("HOME", "/home/u")));
        assert!(set.contains(&pair(FONTCONFIG_VARIABLE, "/home/u/fonts.conf")));
        assert!(!set.iter().any(|(name, _)| name == ORIGINAL_VARIABLE));
    }

    #[test]
    fn children_get_no_fontconfig_variable_when_there_was_none() {
        let unset = restore_variables(vec![
            pair(FONTCONFIG_VARIABLE, "/cache/generated.conf"),
            (OsString::from(ORIGINAL_VARIABLE), encode_original(None)),
        ]);
        assert!(unset.is_empty(), "left over: {unset:?}");
    }

    #[test]
    fn an_environment_without_the_guard_is_left_alone() {
        let variables = vec![
            pair(FONTCONFIG_VARIABLE, "/home/u/fonts.conf"),
            pair("HOME", "/home/u"),
        ];
        assert_eq!(restore_variables(variables.clone()), variables);
    }

    #[test]
    fn an_empty_original_is_remembered_as_empty_not_as_absent() {
        let marker = encode_original(Some(OsStr::new("")));
        assert_eq!(decode_original(&marker), Some(OsString::new()));
        assert_eq!(decode_original(&encode_original(None)), None);
    }
}
