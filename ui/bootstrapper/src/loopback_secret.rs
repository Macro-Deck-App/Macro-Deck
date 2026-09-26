use std::path::{Path, PathBuf};
use std::sync::Mutex;
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use hmac::{Hmac, Mac};
use serde::{Deserialize, Serialize};
use sha2::Sha256;

pub const ENVIRONMENT_VARIABLE: &str = "MACRODECK_LOOPBACK_SECRET";
pub const HEADER: &str = "X-MacroDeck-Loopback-Secret";
const SECRET_FILE: &str = "loopback-secret";
const SECRET_BYTES: usize = 32;
const CODE_LIFETIME_SECONDS: u64 = 60;
const CODE_LABEL: &str = "macro-deck-loopback-code:";
const PROOF_LABEL: &str = "macro-deck-loopback-proof:";

#[derive(Default)]
struct Credential {
    secret: Option<String>,
    verified_port: Option<u16>,
}

static CURRENT: Mutex<Credential> = Mutex::new(Credential {
    secret: None,
    verified_port: None,
});

pub fn generate() -> String {
    let mut bytes = [0u8; SECRET_BYTES];
    getrandom::fill(&mut bytes).expect("the OS random source is unavailable");
    hex::encode(bytes)
}

pub fn new_nonce() -> String {
    let mut bytes = [0u8; 16];
    getrandom::fill(&mut bytes).expect("the OS random source is unavailable");
    hex::encode(bytes)
}

pub fn is_valid_secret(value: &str) -> bool {
    value.len() >= SECRET_BYTES * 2
        && value.len().is_multiple_of(2)
        && value.bytes().all(|b| b.is_ascii_hexdigit())
}

pub fn set_secret(secret: String) {
    if let Ok(mut current) = CURRENT.lock() {
        current.secret = Some(secret);
        current.verified_port = None;
    }
}

pub fn forget_verified_port() {
    if let Ok(mut current) = CURRENT.lock() {
        current.verified_port = None;
    }
}

pub fn secret() -> Option<String> {
    let held = CURRENT
        .lock()
        .ok()
        .and_then(|current| current.secret.clone());
    held.or_else(development_secret)
}

// A debug bootstrapper never spawns the host, so it reads what the separately started dev host wrote,
// again on every call because that host rewrites the file each time it starts.
fn development_secret() -> Option<String> {
    if !cfg!(debug_assertions) {
        return None;
    }
    if let Some(value) = std::env::var(ENVIRONMENT_VARIABLE)
        .ok()
        .filter(|v| is_valid_secret(v))
    {
        return Some(value);
    }
    let path = std::env::var_os("MACRODECK_LOOPBACK_SECRET_FILE")
        .map(PathBuf::from)
        .unwrap_or_else(|| {
            Path::new(env!("CARGO_MANIFEST_DIR")).join("../../.data/config/loopback-secret")
        });
    std::fs::read_to_string(path)
        .ok()
        .map(|raw| raw.trim().to_string())
        .filter(|v| is_valid_secret(v))
}

pub fn persisted_path(config_dir: &Path) -> PathBuf {
    config_dir.join(SECRET_FILE)
}

pub fn load(config_dir: &Path) -> Option<String> {
    std::fs::read_to_string(persisted_path(config_dir))
        .ok()
        .map(|raw| raw.trim().to_string())
        .filter(|v| is_valid_secret(v))
}

pub fn persist(config_dir: &Path, secret: &str) -> std::io::Result<()> {
    std::fs::create_dir_all(config_dir)?;
    let temporary = config_dir.join(format!(".{SECRET_FILE}.{}.tmp", new_nonce()));
    let result = write_owner_only(&temporary, secret)
        .and_then(|_| std::fs::rename(&temporary, persisted_path(config_dir)));
    if result.is_err() {
        let _ = std::fs::remove_file(&temporary);
    }
    result
}

// Created with the restrictive mode, then renamed over the old file, because an existing file keeps
// its permissions when it is merely overwritten. Windows config dirs are per-user already.
fn write_owner_only(path: &Path, secret: &str) -> std::io::Result<()> {
    use std::io::Write;
    let mut options = std::fs::OpenOptions::new();
    options.write(true).create_new(true);
    #[cfg(unix)]
    {
        use std::os::unix::fs::OpenOptionsExt;
        options.mode(0o600);
    }
    let mut file = options.open(path)?;
    file.write_all(secret.as_bytes())?;
    file.sync_all()
}

fn keyed(secret: &str, message: &str) -> Option<Hmac<Sha256>> {
    let mut mac = Hmac::<Sha256>::new_from_slice(&hex::decode(secret).ok()?).ok()?;
    mac.update(message.as_bytes());
    Some(mac)
}

fn mac(secret: &str, message: &str) -> Option<String> {
    Some(hex::encode(keyed(secret, message)?.finalize().into_bytes()))
}

pub fn session_code(secret: &str, now_unix_seconds: u64, nonce: &str) -> Option<String> {
    let expiry = now_unix_seconds + CODE_LIFETIME_SECONDS;
    let tag = mac(secret, &format!("{CODE_LABEL}{expiry}.{nonce}"))?;
    Some(format!("{expiry}.{nonce}.{tag}"))
}

pub fn fresh_session_code() -> Option<String> {
    let now = SystemTime::now().duration_since(UNIX_EPOCH).ok()?.as_secs();
    session_code(&secret()?, now, &new_nonce())
}

// Proxy variables must not apply: every request here goes to 127.0.0.1 and some carry the secret.
pub fn http_client(timeout: Duration) -> reqwest::Result<reqwest::Client> {
    reqwest::Client::builder()
        .timeout(timeout)
        .no_proxy()
        .build()
}

#[derive(Serialize)]
struct ProofRequest<'a> {
    nonce: &'a str,
}

#[derive(Deserialize)]
struct ProofResponse {
    proof: String,
}

// The secret is only ever released to a port that answered this challenge for the current secret.
pub async fn prove(port: u16, timeout: Duration) -> bool {
    let Some(secret) = secret() else {
        return false;
    };
    let nonce = new_nonce();
    let Ok(client) = http_client(timeout) else {
        return false;
    };
    let answer = match client
        .post(format!("http://127.0.0.1:{port}/api/auth/loopback-proof"))
        .json(&ProofRequest { nonce: &nonce })
        .send()
        .await
    {
        Ok(response) if response.status().is_success() => {
            response.json::<ProofResponse>().await.ok()
        }
        _ => None,
    };
    let proven = match (
        answer.and_then(|answer| hex::decode(answer.proof).ok()),
        keyed(&secret, &format!("{PROOF_LABEL}{nonce}")),
    ) {
        (Some(answer), Some(expected)) => expected.verify_slice(&answer).is_ok(),
        _ => false,
    };
    if let Ok(mut current) = CURRENT.lock() {
        if proven {
            current.verified_port = Some(port);
        } else if current.verified_port == Some(port) {
            current.verified_port = None;
        }
    }
    proven
}

pub async fn secret_for(port: u16) -> Option<String> {
    let verified = CURRENT
        .lock()
        .ok()
        .is_some_and(|current| current.verified_port == Some(port));
    if verified || prove(port, Duration::from_secs(2)).await {
        secret()
    } else {
        None
    }
}

pub async fn authorize(builder: reqwest::RequestBuilder, port: u16) -> reqwest::RequestBuilder {
    match secret_for(port).await {
        Some(secret) => builder.header(HEADER, secret),
        None => builder,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn expected_proof(secret: &str, nonce: &str) -> Option<String> {
        mac(secret, &format!("{PROOF_LABEL}{nonce}"))
    }

    fn session_cookie_value(secret: &str) -> Option<String> {
        mac(secret, "macro-deck-loopback-session")
    }

    const SECRET: &str = "0f1e2d3c4b5a69788796a5b4c3d2e1f000112233445566778899aabbccddeeff";
    const NONCE: &str = "00112233445566778899aabbccddeeff";

    // Shared with host/tests/.../Auth/LoopbackSecretTests.cs and the E2E code minter.
    #[test]
    fn the_shared_vector_matches_the_host() {
        assert_eq!(
            session_cookie_value(SECRET).unwrap(),
            "7e665b82182292969e91cbae53bca7d3f716abd058a9f70ea4090d84b0fff3f1"
        );
        assert_eq!(
            expected_proof(SECRET, NONCE).unwrap(),
            "964b8fdb3ce49d81ed0660ae293e76bf10eec16fe22020daa3cc5357363a15bb"
        );
        assert_eq!(
            session_code(SECRET, 1_900_000_000, NONCE).unwrap(),
            "1900000060.00112233445566778899aabbccddeeff.c8ba672b12783beb854f70711c5a5a6df1e45a53ef4abec3163c636c932feb73"
        );
    }

    #[test]
    fn generated_secrets_are_valid_and_distinct() {
        let first = generate();
        assert!(is_valid_secret(&first));
        assert_ne!(first, generate());
        assert_eq!(new_nonce().len(), 32);
    }

    #[test]
    fn persisted_secret_round_trips_and_is_owner_only() {
        let dir = std::env::temp_dir().join(format!("md-loopback-secret-{}", new_nonce()));
        std::fs::create_dir_all(&dir).unwrap();
        std::fs::write(persisted_path(&dir), "stale").unwrap();
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            std::fs::set_permissions(persisted_path(&dir), std::fs::Permissions::from_mode(0o644))
                .unwrap();
        }

        persist(&dir, SECRET).unwrap();

        assert_eq!(load(&dir).as_deref(), Some(SECRET));
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            let mode = std::fs::metadata(persisted_path(&dir))
                .unwrap()
                .permissions()
                .mode();
            assert_eq!(mode & 0o777, 0o600);
        }
        assert_eq!(std::fs::read_dir(&dir).unwrap().count(), 1);
        std::fs::remove_dir_all(&dir).unwrap();
    }

    #[test]
    fn a_malformed_persisted_secret_is_ignored() {
        let dir = std::env::temp_dir().join(format!("md-loopback-secret-{}", new_nonce()));
        std::fs::create_dir_all(&dir).unwrap();
        std::fs::write(persisted_path(&dir), "not-a-secret").unwrap();
        assert_eq!(load(&dir), None);
        std::fs::remove_dir_all(&dir).unwrap();
    }

    #[test]
    fn the_client_ignores_proxy_variables() {
        let listener = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
        let port = listener.local_addr().unwrap().port();
        let proxy = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
        let proxy_port = proxy.local_addr().unwrap().port();
        proxy.set_nonblocking(true).unwrap();
        // Proxy variables are read when a client is built, so they are set only around that build.
        std::env::set_var("HTTP_PROXY", format!("http://127.0.0.1:{proxy_port}"));
        std::env::set_var("http_proxy", format!("http://127.0.0.1:{proxy_port}"));
        let client = http_client(Duration::from_secs(5));
        std::env::remove_var("HTTP_PROXY");
        std::env::remove_var("http_proxy");
        let client = client.unwrap();

        let server = std::thread::spawn(move || {
            use std::io::{Read, Write};
            let (mut stream, _) = listener.accept().unwrap();
            let mut buffer = [0u8; 1024];
            let read = stream.read(&mut buffer).unwrap();
            stream
                .write_all(b"HTTP/1.1 204 No Content\r\nContent-Length: 0\r\n\r\n")
                .unwrap();
            String::from_utf8_lossy(&buffer[..read]).to_string()
        });

        let runtime = tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
            .unwrap();
        let status = runtime.block_on(async {
            client
                .get(format!("http://127.0.0.1:{port}/direct"))
                .header(HEADER, SECRET)
                .send()
                .await
                .map(|response| response.status().as_u16())
        });

        let request = server.join().unwrap();
        assert_eq!(status.unwrap(), 204);
        assert!(request.starts_with("GET /direct "));
        assert!(proxy.accept().is_err(), "the proxy was contacted");
    }

    fn serve_one_proof(answer: impl FnOnce(&str) -> String + Send + 'static) -> u16 {
        let listener = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
        let port = listener.local_addr().unwrap().port();
        std::thread::spawn(move || {
            use std::io::{Read, Write};
            let (mut stream, _) = listener.accept().unwrap();
            let mut request = String::new();
            let mut buffer = [0u8; 4096];
            while !request.contains("\r\n\r\n") || !request.ends_with('}') {
                let read = stream.read(&mut buffer).unwrap();
                if read == 0 {
                    break;
                }
                request.push_str(&String::from_utf8_lossy(&buffer[..read]));
            }
            let nonce = request
                .split("\"nonce\":\"")
                .nth(1)
                .and_then(|rest| rest.split('"').next())
                .unwrap_or_default()
                .to_string();
            let body = format!("{{\"proof\":\"{}\"}}", answer(&nonce));
            let response = format!(
                "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {}\r\n\r\n{body}",
                body.len()
            );
            stream.write_all(response.as_bytes()).unwrap();
        });
        port
    }

    #[test]
    fn only_a_listener_that_knows_the_secret_is_proven() {
        set_secret(SECRET.to_string());
        let runtime = tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
            .unwrap();

        let genuine = serve_one_proof(|nonce| expected_proof(SECRET, nonce).unwrap());
        let impostor = serve_one_proof(|nonce| expected_proof(&"ab".repeat(32), nonce).unwrap());

        assert!(runtime.block_on(prove(genuine, Duration::from_secs(5))));
        assert!(!runtime.block_on(prove(impostor, Duration::from_secs(5))));
    }
}
