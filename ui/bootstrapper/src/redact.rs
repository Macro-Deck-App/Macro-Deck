use std::sync::OnceLock;

pub const PLACEHOLDER: &str = "***";
const USER_PLACEHOLDER: &str = "<user>";

const SENSITIVE_KEYS: &[&str] = &[
    "access_token",
    "refresh_token",
    "id_token",
    "client_secret",
    "private_key",
    "api_key",
    "api_token",
    "apikey",
    "apitoken",
    "authorization",
    "credentials",
    "credential",
    "signature",
    "password",
    "passwd",
    "token",
    "secret",
    "jwt",
    "auth",
    "pwd",
    "key",
];

const AUTH_SCHEMES: &[&str] = &["bearer", "basic", "digest"];

pub fn redact(text: &str) -> String {
    let paths = redact_user_paths(
        text,
        home_directory(),
        cfg!(windows) || cfg!(target_os = "macos"),
    );
    let pairs = redact_pairs(&paths);
    let userinfo = redact_url_userinfo(&pairs);
    redact_jwts(&userinfo)
}

fn home_directory() -> Option<&'static str> {
    static HOME: OnceLock<Option<String>> = OnceLock::new();
    HOME.get_or_init(|| {
        let raw = if cfg!(windows) {
            std::env::var("USERPROFILE").ok().or_else(|| {
                let drive = std::env::var("HOMEDRIVE").ok()?;
                let path = std::env::var("HOMEPATH").ok()?;
                Some(format!("{drive}{path}"))
            })
        } else {
            std::env::var("HOME").ok()
        }?;
        normalize_home(&raw)
    })
    .as_deref()
}

fn normalize_home(raw: &str) -> Option<String> {
    let trimmed = raw.trim().trim_end_matches(['/', '\\']);
    if trimmed.is_empty() || !is_absolute_home(trimmed) {
        return None;
    }
    Some(trimmed.to_string())
}

fn is_absolute_home(path: &str) -> bool {
    let bytes = path.as_bytes();
    if bytes.is_empty() {
        return false;
    }
    if bytes[0] == b'/' {
        return true;
    }
    if bytes.len() > 2 && bytes[0] == b'\\' && bytes[1] == b'\\' {
        return true;
    }
    bytes.len() > 2
        && bytes[0].is_ascii_alphabetic()
        && bytes[1] == b':'
        && matches!(bytes[2], b'/' | b'\\')
}

fn is_path_body_byte(byte: u8) -> bool {
    byte.is_ascii_alphanumeric() || matches!(byte, b'_' | b'.' | b'-') || byte >= 0x80
}

fn is_separator(byte: u8) -> bool {
    byte == b'/' || byte == b'\\'
}

fn is_segment_terminator(byte: u8) -> bool {
    byte <= 0x20
        || matches!(
            byte,
            b'/' | b'\\' | b':' | b'*' | b'?' | b'"' | b'<' | b'>' | b'|'
        )
}

fn has_valid_left_boundary(bytes: &[u8], index: usize) -> bool {
    index == 0 || !is_path_body_byte(bytes[index - 1])
}

pub(crate) fn redact_user_paths(text: &str, home: Option<&str>, ignore_case: bool) -> String {
    let after_home = match home {
        Some(home) if !home.is_empty() => redact_current_home(text, home, ignore_case),
        _ => text.to_string(),
    };
    redact_foreign_users(&after_home)
}

fn redact_current_home(text: &str, home: &str, ignore_case: bool) -> String {
    let bytes = text.as_bytes();
    let home_bytes = home.as_bytes();
    let mut out = String::with_capacity(text.len());
    let mut copied = 0usize;
    let mut i = 0usize;

    while i < bytes.len() {
        if !has_valid_left_boundary(bytes, i) {
            i += 1;
            continue;
        }
        if let Some(end) = match_home_at(bytes, i, home_bytes, ignore_case) {
            if end == bytes.len() || !is_path_body_byte(bytes[end]) {
                out.push_str(&text[copied..i]);
                out.push('~');
                copied = end;
                i = end;
                continue;
            }
        }
        i += 1;
    }

    out.push_str(&text[copied..]);
    out
}

fn match_home_at(text: &[u8], start: usize, home: &[u8], ignore_case: bool) -> Option<usize> {
    let mut ti = start;
    let mut hi = 0usize;
    while hi < home.len() {
        if is_separator(home[hi]) {
            while hi < home.len() && is_separator(home[hi]) {
                hi += 1;
            }
            let sep_start = ti;
            while ti < text.len() && is_separator(text[ti]) {
                ti += 1;
            }
            if ti == sep_start {
                return None;
            }
        } else {
            if ti >= text.len() {
                return None;
            }
            let matches = if ignore_case {
                home[hi].eq_ignore_ascii_case(&text[ti])
            } else {
                home[hi] == text[ti]
            };
            if !matches {
                return None;
            }
            hi += 1;
            ti += 1;
        }
    }
    Some(ti)
}

const EXEMPT_SINGLE_WORD: &[&[u8]] = &[b"Public", b"Default", b"Shared"];
const EXEMPT_TWO_WORD: &[(&[u8], &[u8])] = &[(b"Default", b"User"), (b"All", b"Users")];

fn exempt_segment_end(bytes: &[u8], seg_start: usize, seg_end: usize) -> Option<usize> {
    let seg = &bytes[seg_start..seg_end];
    if EXEMPT_SINGLE_WORD
        .iter()
        .any(|word| seg.eq_ignore_ascii_case(word))
    {
        return Some(seg_end);
    }
    for (first, second) in EXEMPT_TWO_WORD {
        if seg.eq_ignore_ascii_case(first) && bytes.get(seg_end) == Some(&b' ') {
            let word_start = seg_end + 1;
            let word_end = read_segment(bytes, word_start);
            if bytes[word_start..word_end].eq_ignore_ascii_case(second) {
                return Some(word_end);
            }
        }
    }
    None
}

fn consume_sep_run(bytes: &[u8], start: usize) -> Option<usize> {
    let mut p = start;
    while p < bytes.len() && is_separator(bytes[p]) {
        p += 1;
    }
    (p > start).then_some(p)
}

fn match_literal_ci(bytes: &[u8], start: usize, literal: &[u8]) -> Option<usize> {
    let end = start.checked_add(literal.len())?;
    (end <= bytes.len() && bytes[start..end].eq_ignore_ascii_case(literal)).then_some(end)
}

fn match_literal_cs(bytes: &[u8], start: usize, literal: &[u8]) -> Option<usize> {
    let end = start.checked_add(literal.len())?;
    (end <= bytes.len() && &bytes[start..end] == literal).then_some(end)
}

fn read_segment(bytes: &[u8], start: usize) -> usize {
    let mut p = start;
    while p < bytes.len() && !is_segment_terminator(bytes[p]) {
        p += 1;
    }
    p
}

fn match_foreign_shape(bytes: &[u8], i: usize) -> Option<(usize, usize, bool)> {
    if bytes[i].is_ascii_alphabetic() && bytes.get(i + 1) == Some(&b':') {
        if let Some(shape) = (|| {
            let p = consume_sep_run(bytes, i + 2)?;
            let p = match_literal_ci(bytes, p, b"Users")?;
            let seg_start = consume_sep_run(bytes, p)?;
            let seg_end = read_segment(bytes, seg_start);
            (seg_end > seg_start).then_some((seg_start, seg_end, true))
        })() {
            return Some(shape);
        }
    }

    if bytes.get(i) == Some(&b'\\') && bytes.get(i + 1) == Some(&b'\\') {
        if let Some(shape) = (|| {
            let server_start = i + 2;
            let mut p = server_start;
            while p < bytes.len() && !is_segment_terminator(bytes[p]) {
                p += 1;
            }
            if p == server_start {
                return None;
            }
            let p = consume_sep_run(bytes, p)?;
            let p = match_literal_ci(bytes, p, b"Users")?;
            let seg_start = consume_sep_run(bytes, p)?;
            let seg_end = read_segment(bytes, seg_start);
            (seg_end > seg_start).then_some((seg_start, seg_end, true))
        })() {
            return Some(shape);
        }
    }

    // /home/segment (case-sensitive "home"). Nothing under /home is a shared profile directory, so
    // a real account named "shared" or "public" must not be spared - hence has_exempt_names = false.
    if let Some(shape) = (|| {
        let p = match_literal_cs(bytes, i, b"/home")?;
        let seg_start = consume_sep_run(bytes, p)?;
        let seg_end = read_segment(bytes, seg_start);
        (seg_end > seg_start).then_some((seg_start, seg_end, false))
    })() {
        return Some(shape);
    }

    if let Some(shape) = (|| {
        let p = match_literal_cs(bytes, i, b"/Users")?;
        let seg_start = consume_sep_run(bytes, p)?;
        let seg_end = read_segment(bytes, seg_start);
        (seg_end > seg_start).then_some((seg_start, seg_end, true))
    })() {
        return Some(shape);
    }

    None
}

fn find_bytes(haystack: &[u8], needle: &[u8]) -> Option<usize> {
    if needle.is_empty() || haystack.len() < needle.len() {
        return None;
    }
    haystack
        .windows(needle.len())
        .position(|window| window == needle)
}

fn is_url_boundary(byte: u8) -> bool {
    byte.is_ascii_whitespace()
        || matches!(
            byte,
            b'"' | b'\''
                | b','
                | b';'
                | b'('
                | b')'
                | b'['
                | b']'
                | b'{'
                | b'}'
                | b'<'
                | b'>'
                | b'\\'
        )
}

fn is_within_non_file_url(bytes: &[u8], match_start: usize) -> bool {
    let mut token_start = match_start;
    while token_start > 0 && !is_url_boundary(bytes[token_start - 1]) {
        token_start -= 1;
    }
    let token = &bytes[token_start..match_start];
    match find_bytes(token, b"://") {
        Some(pos) => !token[..pos].eq_ignore_ascii_case(b"file"),
        None => false,
    }
}

fn redact_foreign_users(text: &str) -> String {
    let bytes = text.as_bytes();
    let mut out = String::with_capacity(text.len());
    let mut copied = 0usize;
    let mut i = 0usize;

    while i < bytes.len() {
        if !has_valid_left_boundary(bytes, i) {
            i += 1;
            continue;
        }
        let Some((seg_start, seg_end, has_exempt_names)) = match_foreign_shape(bytes, i) else {
            i += 1;
            continue;
        };
        if is_within_non_file_url(bytes, i) {
            i += 1;
            continue;
        }
        if let Some(exempt_end) = has_exempt_names
            .then(|| exempt_segment_end(bytes, seg_start, seg_end))
            .flatten()
        {
            i = exempt_end;
            continue;
        }
        out.push_str(&text[copied..seg_start]);
        out.push_str(USER_PLACEHOLDER);
        copied = seg_end;
        i = seg_end;
    }

    out.push_str(&text[copied..]);
    out
}

fn is_sensitive_key(key: &str, separator: u8) -> bool {
    if key.eq_ignore_ascii_case("code") {
        return separator == b'=';
    }
    SENSITIVE_KEYS
        .iter()
        .any(|candidate| key.eq_ignore_ascii_case(candidate))
}

fn is_key_byte(byte: u8) -> bool {
    byte.is_ascii_alphanumeric() || byte == b'_'
}

fn is_value_terminator(byte: u8) -> bool {
    matches!(
        byte,
        b' ' | b'\t' | b'\n' | b'\r' | b'&' | b'"' | b'\'' | b'{' | b'}' | b'\\'
    )
}

fn key_range(bytes: &[u8], separator_index: usize) -> Option<(usize, usize)> {
    let mut end = separator_index;
    while end > 0 && (bytes[end - 1] == b' ' || bytes[end - 1] == b'\t') {
        end -= 1;
    }
    if end > 0 && (bytes[end - 1] == b'"' || bytes[end - 1] == b'\'') {
        end -= 1;
    }
    let mut start = end;
    while start > 0 && is_key_byte(bytes[start - 1]) {
        start -= 1;
    }
    if start == end {
        return None;
    }
    Some((start, end))
}

fn skip_auth_scheme(bytes: &[u8], index: usize) -> usize {
    for scheme in AUTH_SCHEMES {
        let end = index + scheme.len();
        if end < bytes.len()
            && bytes[index..end].eq_ignore_ascii_case(scheme.as_bytes())
            && (bytes[end] == b' ' || bytes[end] == b'\t')
        {
            let mut after = end;
            while after < bytes.len() && (bytes[after] == b' ' || bytes[after] == b'\t') {
                after += 1;
            }
            return after;
        }
    }
    index
}

fn redact_pairs(text: &str) -> String {
    let bytes = text.as_bytes();
    let mut out = String::with_capacity(text.len());
    let mut copied = 0usize;
    let mut index = 0usize;

    while index < bytes.len() {
        let separator = bytes[index];
        if separator != b'=' && separator != b':' {
            index += 1;
            continue;
        }
        let Some((key_start, key_end)) = key_range(bytes, index) else {
            index += 1;
            continue;
        };
        if key_start < copied || !is_sensitive_key(&text[key_start..key_end], separator) {
            index += 1;
            continue;
        }

        let mut value = index + 1;
        while value < bytes.len() && (bytes[value] == b' ' || bytes[value] == b'\t') {
            value += 1;
        }
        if value < bytes.len() && (bytes[value] == b'"' || bytes[value] == b'\'') {
            value += 1;
        }
        let value_start = value;
        let mut value_end = skip_auth_scheme(bytes, value);
        while value_end < bytes.len() && !is_value_terminator(bytes[value_end]) {
            value_end += 1;
        }
        if value_end == value_start {
            index += 1;
            continue;
        }

        out.push_str(&text[copied..value_start]);
        out.push_str(PLACEHOLDER);
        copied = value_end;
        index = value_end;
    }

    out.push_str(&text[copied..]);
    out
}

fn redact_url_userinfo(text: &str) -> String {
    let bytes = text.as_bytes();
    let mut out = String::with_capacity(text.len());
    let mut copied = 0usize;
    let mut index = 0usize;

    while let Some(offset) = text[index..].find("://") {
        let authority = index + offset + 3;
        let mut end = authority;
        while end < bytes.len()
            && !matches!(
                bytes[end],
                b'/' | b'?' | b'#' | b' ' | b'\t' | b'\n' | b'\r'
            )
        {
            end += 1;
        }
        let at = bytes[authority..end].iter().position(|byte| *byte == b'@');
        let colon = bytes[authority..end].iter().position(|byte| *byte == b':');
        match (at, colon) {
            (Some(at), Some(colon)) if colon < at => {
                out.push_str(&text[copied..authority]);
                out.push_str(PLACEHOLDER);
                out.push(':');
                out.push_str(PLACEHOLDER);
                copied = authority + at;
            }
            _ => {}
        }
        index = end.max(authority);
    }

    out.push_str(&text[copied..]);
    out
}

fn is_jwt_byte(byte: u8) -> bool {
    byte.is_ascii_alphanumeric() || byte == b'_' || byte == b'-'
}

fn redact_jwts(text: &str) -> String {
    let bytes = text.as_bytes();
    let mut out = String::with_capacity(text.len());
    let mut copied = 0usize;
    let mut index = 0usize;

    while let Some(offset) = text[index..].find("eyJ") {
        let start = index + offset;
        if start > 0 && (is_jwt_byte(bytes[start - 1]) || bytes[start - 1] == b'.') {
            index = start + 3;
            continue;
        }
        let mut end = start;
        let mut dots = 0usize;
        let mut segment_start = start;
        let mut segments_before_dot = [0usize; 2];
        while end < bytes.len() {
            if is_jwt_byte(bytes[end]) {
                end += 1;
            } else if bytes[end] == b'.' && dots < 2 {
                segments_before_dot[dots] = end - segment_start;
                dots += 1;
                end += 1;
                segment_start = end;
            } else {
                break;
            }
        }
        if dots == 2 && segments_before_dot[0] > 3 && segments_before_dot[1] > 0 {
            out.push_str(&text[copied..start]);
            out.push_str(PLACEHOLDER);
            copied = end;
        }
        index = end.max(start + 3);
    }

    out.push_str(&text[copied..]);
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    const JWT: &str = "eyJhbGciOiJIUzI1NiJ9.eyJzY29wZSI6ImFkbWluIn0.dBjftJeZ4CVP-mB92K27uhbUJU1p";

    #[test]
    fn redacts_access_token_query_parameter() {
        assert_eq!(
            redact(&format!(
                "[window] page loaded: http://127.0.0.1:5191/admin?access_token={JWT}&v=3.0.0"
            )),
            "[window] page loaded: http://127.0.0.1:5191/admin?access_token=***&v=3.0.0"
        );
    }

    #[test]
    fn redacts_oauth_code_but_keeps_state() {
        assert_eq!(
            redact("?code=AQD5x9k-secret&state=b7f1"),
            "?code=***&state=b7f1"
        );
    }

    #[test]
    fn redacts_authorization_scheme_value() {
        assert_eq!(
            redact("Authorization: Bearer abcdefghijkl"),
            "Authorization: ***"
        );
    }

    #[test]
    fn redacts_url_userinfo() {
        assert_eq!(
            redact("connecting to http://admin:hunter2@bot.local:8087/api"),
            "connecting to http://***:***@bot.local:8087/api"
        );
    }

    #[test]
    fn redacts_bare_jwt() {
        assert_eq!(
            redact(&format!("token rejected: {JWT}")),
            "token rejected: ***"
        );
    }

    #[test]
    fn redacts_json_and_flag_style_pairs() {
        assert_eq!(
            redact("{\"refresh_token\":\"abc\",\"expires_in\":3600}"),
            "{\"refresh_token\":\"***\",\"expires_in\":3600}"
        );
        assert_eq!(redact("--password=hunter2"), "--password=***");
    }

    #[test]
    fn leaves_ordinary_lines_untouched() {
        for line in [
            "[host] listening on http://127.0.0.1:5191",
            "[window] page loaded: http://127.0.0.1:5191/admin?v=3.0.0",
            "[updater] no update available, code: 204",
            "[host] adopted running host on port 5191",
        ] {
            assert_eq!(redact(line), line, "unexpectedly redacted: {line}");
        }
    }

    #[test]
    fn is_idempotent() {
        let once = redact(&format!("?access_token={JWT}&code=abc"));
        assert_eq!(redact(&once), once);
    }

    #[test]
    fn keeps_multibyte_text_intact() {
        assert_eq!(
            redact("Fehler beim Öffnen: password=geheim für Benutzer"),
            "Fehler beim Öffnen: password=*** für Benutzer"
        );
    }

    #[test]
    fn redacts_current_home_on_all_three_platforms() {
        assert_eq!(
            redact_user_paths(
                "C:\\Users\\suchbyte\\AppData\\Roaming\\MacroDeck\\database.db",
                Some("C:\\Users\\suchbyte"),
                true
            ),
            "~\\AppData\\Roaming\\MacroDeck\\database.db"
        );
        assert_eq!(
            redact_user_paths(
                "/home/suchbyte/.local/share/MacroDeck/database.db",
                Some("/home/suchbyte"),
                false
            ),
            "~/.local/share/MacroDeck/database.db"
        );
        assert_eq!(
            redact_user_paths(
                "/Users/suchbyte/Library/Application Support/MacroDeck/database.db",
                Some("/Users/suchbyte"),
                true
            ),
            "~/Library/Application Support/MacroDeck/database.db"
        );
    }

    #[test]
    fn redacts_foreign_user_in_all_shapes() {
        assert_eq!(
            redact_user_paths(
                "C:\\Users\\otherguy\\Documents\\file.json",
                Some("/home/suchbyte"),
                true
            ),
            "C:\\Users\\<user>\\Documents\\file.json"
        );
        assert_eq!(
            redact_user_paths("/home/other-user/file.json", Some("/home/suchbyte"), false),
            "/home/<user>/file.json"
        );
        assert_eq!(
            redact_user_paths("/Users/other-user/file.json", Some("/home/suchbyte"), true),
            "/Users/<user>/file.json"
        );
        assert_eq!(
            redact_user_paths(
                "\\\\fileserver\\Users\\otherguy\\file.json",
                Some("/home/suchbyte"),
                true
            ),
            "\\\\fileserver\\Users\\<user>\\file.json"
        );
    }

    #[test]
    fn current_home_wins_over_foreign_fallback() {
        assert_eq!(
            redact_user_paths("/home/suchbyte/file.json", Some("/home/suchbyte"), false),
            "~/file.json"
        );
    }

    #[test]
    fn respects_segment_boundary_against_prefix_matching() {
        assert_eq!(
            redact_user_paths("/home/suchbyte2/file.json", Some("/home/suchbyte"), false),
            "/home/<user>/file.json"
        );
    }

    #[test]
    fn redacts_home_at_end_of_string() {
        assert_eq!(
            redact_user_paths(
                "Working directory: /home/suchbyte",
                Some("/home/suchbyte"),
                false
            ),
            "Working directory: ~"
        );
    }

    #[test]
    fn matches_home_regardless_of_separator_form() {
        let home = Some("C:\\Users\\suchbyte");
        assert_eq!(
            redact_user_paths("C:/Users/suchbyte/AppData/x", home, true),
            "~/AppData/x"
        );
        assert_eq!(
            redact_user_paths("C:\\\\Users\\\\suchbyte\\\\AppData\\\\x", home, true),
            "~\\\\AppData\\\\x"
        );
    }

    #[test]
    fn respects_case_regime_flag() {
        assert_eq!(
            redact_user_paths(
                "c:\\users\\SUCHBYTE\\AppData\\x",
                Some("C:\\Users\\suchbyte"),
                true
            ),
            "~\\AppData\\x"
        );
        assert_eq!(
            redact_user_paths("/home/SuchByte/file.json", Some("/home/suchbyte"), false),
            "/home/<user>/file.json"
        );
    }

    #[test]
    fn leaves_exempt_windows_and_macos_directories_untouched() {
        let home = Some("/home/suchbyte");
        for line in [
            "C:\\Users\\Public\\Documents\\shared.json",
            "C:\\Users\\Default\\NTUSER.DAT",
            "C:\\Users\\All Users\\md.json",
            "/Users/Shared/MacroDeck/plugins/x.dll",
            "c:\\users\\public\\x",
        ] {
            assert_eq!(
                redact_user_paths(line, home, true),
                line,
                "unexpectedly redacted exempt directory: {line}"
            );
        }
    }

    #[test]
    fn does_not_over_sanitize_lookalike_text() {
        let home = Some("/home/suchbyte");
        for line in [
            "/opt/suchbyte/data.db",
            "C:\\Projects\\suchbyte\\build.log",
            "Plugin suchbyte.weather v1.2 loaded",
            "https://github.com/suchbyte/Macro-Deck",
            "/usr/lib/macrodeck/plugins",
            "C:\\Program Files\\MacroDeck\\MacroDeck.exe",
        ] {
            assert_eq!(
                redact_user_paths(line, home, false),
                line,
                "unexpectedly redacted ordinary text: {line}"
            );
        }
    }

    #[test]
    fn url_guard_skips_non_file_schemes_but_allows_file_scheme() {
        assert_eq!(
            redact_user_paths(
                "Navigated to https://app.example.com/home/dashboard",
                None,
                false
            ),
            "Navigated to https://app.example.com/home/dashboard"
        );
        assert_eq!(
            redact_user_paths(
                "Loading file:///home/other-user/plugins/manifest.json",
                None,
                false
            ),
            "Loading file:///home/<user>/plugins/manifest.json"
        );
        assert_eq!(
            redact_user_paths(
                "GET https://api.example.com/users/1234abcd/playlists",
                None,
                false
            ),
            "GET https://api.example.com/users/1234abcd/playlists"
        );
    }

    #[test]
    fn a_url_elsewhere_in_the_line_does_not_exempt_a_path() {
        assert_eq!(
            redact_user_paths(
                "{\"url\":\"https://api.example.com/v1\",\"path\":\"/home/other-user/x.json\"}",
                None,
                false
            ),
            "{\"url\":\"https://api.example.com/v1\",\"path\":\"/home/<user>/x.json\"}"
        );
    }

    #[test]
    fn only_exempts_shared_profile_names_where_they_are_one() {
        assert_eq!(
            redact_user_paths("/home/shared/x.json", None, false),
            "/home/<user>/x.json"
        );
        assert_eq!(
            redact_user_paths("/Users/Shared/x.json", None, false),
            "/Users/Shared/x.json"
        );
    }

    #[test]
    fn folds_case_for_ascii_only() {
        assert_eq!(
            redact_user_paths("/home/MÄNUEL/x.json", Some("/home/mänuel"), true),
            "/home/<user>/x.json"
        );
    }

    #[test]
    fn applies_foreign_fallback_when_home_is_unresolvable() {
        assert_eq!(
            redact_user_paths("plugin wrote /home/other-user/file.json", None, false),
            "plugin wrote /home/<user>/file.json"
        );
    }

    #[test]
    fn preserves_surrounding_punctuation_in_a_larger_line() {
        assert_eq!(
            redact_user_paths(
                "[host] failed to open \"/home/other-user/.config/md.json\", retrying",
                Some("/home/suchbyte"),
                false
            ),
            "[host] failed to open \"/home/<user>/.config/md.json\", retrying"
        );
    }

    #[test]
    fn path_redaction_is_idempotent() {
        let home = Some("/home/suchbyte");
        let once = redact_user_paths(
            "home is /home/suchbyte/x, other is /home/other-user/y",
            home,
            false,
        );
        assert_eq!(redact_user_paths(&once, home, false), once);
    }

    #[test]
    fn handles_multibyte_text_without_panicking() {
        assert_eq!(
            redact_user_paths(
                "Fehler beim Öffnen: /home/other-user/datei.json für Benutzer",
                None,
                false
            ),
            "Fehler beim Öffnen: /home/<user>/datei.json für Benutzer"
        );
        assert_eq!(
            redact_user_paths("/home/mänuel/.config/md.json", Some("/home/mänuel"), false),
            "~/.config/md.json"
        );
    }

    #[test]
    fn public_redact_wires_in_path_sanitization_without_breaking_credential_redaction() {
        assert!(redact("[host] plugin wrote /home/other-user/file.json")
            .contains("/home/<user>/file.json"));

        let with_path_and_token = redact(&format!(
            "[host] /home/other-user/md.json ?access_token={JWT}"
        ));
        assert!(with_path_and_token.contains("/home/<user>/md.json"));
        assert!(with_path_and_token.contains("access_token=***"));
    }

    #[test]
    fn a_credential_whose_value_is_a_path_is_redacted_whole() {
        assert_eq!(
            redact("--password=/home/other-user/secret.txt"),
            "--password=***"
        );
        assert_eq!(
            redact("{\"refresh_token\":\"/Users/other-user/token\"}"),
            "{\"refresh_token\":\"***\"}"
        );
    }
}
