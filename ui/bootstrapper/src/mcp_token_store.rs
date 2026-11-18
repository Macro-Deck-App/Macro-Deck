use std::ffi::OsString;
use std::io::Read;

use crate::host;
use crate::mcp_bridge;

pub const SUBCOMMAND: &str = "mcp-token";
const STORE: &str = "store";
const DELETE: &str = "delete";
const MAX_TOKEN_LENGTH: u64 = 4096;

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Command {
    Store(String),
    Delete(String),
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ReadError {
    NotFound,
    Unavailable,
}

pub fn is_invocation(args: impl IntoIterator<Item = OsString>) -> bool {
    args.into_iter().nth(1).is_some_and(|arg| arg == SUBCOMMAND)
}

pub fn parse_command(args: &[OsString]) -> Option<Command> {
    let [_, subcommand, action, id] = args else {
        return None;
    };
    if subcommand != SUBCOMMAND {
        return None;
    }
    let id = id
        .to_str()
        .filter(|id| is_token_id(id))?
        .to_ascii_lowercase();
    match action.to_str()? {
        STORE => Some(Command::Store(id)),
        DELETE => Some(Command::Delete(id)),
        _ => None,
    }
}

pub fn is_token_id(id: &str) -> bool {
    let groups: Vec<&str> = id.split('-').collect();
    groups.len() == 5
        && groups.iter().zip([8, 4, 4, 4, 12]).all(|(group, length)| {
            group.len() == length && group.chars().all(|c| c.is_ascii_hexdigit())
        })
}

pub fn service(build_channel: &str) -> String {
    format!("{}.mcp-token", mcp_bridge::identifier(build_channel))
}

// Only the host calls this, with the token on stdin so it never shows up in a process listing.
pub fn run(args: &[OsString]) -> i32 {
    let Some(command) = parse_command(args) else {
        eprintln!("Macro Deck MCP token store: expected {SUBCOMMAND} store|delete <token id>");
        return 2;
    };
    let service = service(host::BUILD_CHANNEL);
    let stored = match command {
        Command::Store(id) => match read_token(std::io::stdin().lock()) {
            Some(token) => write(&service, &id, &token),
            None => {
                eprintln!("Macro Deck MCP token store: no token on stdin");
                return 2;
            }
        },
        Command::Delete(id) => delete(&service, &id),
    };
    if stored {
        0
    } else {
        1
    }
}

pub fn read_token(input: impl Read) -> Option<String> {
    let mut token = String::new();
    input
        .take(MAX_TOKEN_LENGTH)
        .read_to_string(&mut token)
        .ok()?;
    let token = token.trim();
    (!token.is_empty()).then(|| token.to_string())
}

pub fn read_stored(id: &str) -> Result<String, ReadError> {
    if !is_token_id(id) {
        return Err(ReadError::NotFound);
    }
    read(&service(host::BUILD_CHANNEL), &id.to_ascii_lowercase())
}

#[cfg(target_os = "macos")]
const ERR_SEC_ITEM_NOT_FOUND: i32 = -25300;

#[cfg(target_os = "macos")]
fn write(service: &str, id: &str, token: &str) -> bool {
    security_framework::passwords::set_generic_password(service, id, token.as_bytes()).is_ok()
}

#[cfg(target_os = "macos")]
fn read(service: &str, id: &str) -> Result<String, ReadError> {
    match security_framework::passwords::get_generic_password(service, id) {
        Ok(bytes) => String::from_utf8(bytes).map_err(|_| ReadError::Unavailable),
        Err(error) if error.code() == ERR_SEC_ITEM_NOT_FOUND => Err(ReadError::NotFound),
        Err(_) => Err(ReadError::Unavailable),
    }
}

#[cfg(target_os = "macos")]
fn delete(service: &str, id: &str) -> bool {
    match security_framework::passwords::delete_generic_password(service, id) {
        Ok(()) => true,
        Err(error) => error.code() == ERR_SEC_ITEM_NOT_FOUND,
    }
}

#[cfg(windows)]
mod windows_store {
    use windows::core::{HSTRING, PWSTR};
    use windows::Win32::Foundation::{LocalFree, ERROR_NOT_FOUND, HLOCAL};
    use windows::Win32::Security::Credentials::{
        CredDeleteW, CredFree, CredReadW, CredWriteW, CREDENTIALW, CRED_PERSIST_LOCAL_MACHINE,
        CRED_TYPE_GENERIC,
    };
    use windows::Win32::Security::Cryptography::{
        CryptProtectData, CryptUnprotectData, CRYPT_INTEGER_BLOB,
    };

    use super::ReadError;

    const ENTROPY: &[u8] = b"MacroDeck.McpToken.v1";

    fn target(service: &str, id: &str) -> HSTRING {
        HSTRING::from(format!("{service}/{id}"))
    }

    fn blob(bytes: &[u8]) -> CRYPT_INTEGER_BLOB {
        CRYPT_INTEGER_BLOB {
            cbData: bytes.len() as u32,
            pbData: bytes.as_ptr() as *mut u8,
        }
    }

    fn take(output: CRYPT_INTEGER_BLOB) -> Vec<u8> {
        unsafe {
            let bytes = std::slice::from_raw_parts(output.pbData, output.cbData as usize).to_vec();
            LocalFree(Some(HLOCAL(output.pbData as *mut _)));
            bytes
        }
    }

    // The credential store hands its blob to any process of the user; DPAPI keeps a copied dump
    // useless on another machine or account. Local-machine persistence never roams with the profile.
    pub fn write(service: &str, id: &str, token: &str) -> bool {
        let entropy = blob(ENTROPY);
        let mut protected = CRYPT_INTEGER_BLOB::default();
        let protected = unsafe {
            if CryptProtectData(
                &blob(token.as_bytes()),
                None,
                Some(&entropy),
                None,
                None,
                0,
                &mut protected,
            )
            .is_err()
            {
                return false;
            }
            take(protected)
        };
        let target = target(service, id);
        let account = HSTRING::from(id);
        let credential = CREDENTIALW {
            Type: CRED_TYPE_GENERIC,
            TargetName: PWSTR(target.as_ptr() as *mut u16),
            CredentialBlobSize: protected.len() as u32,
            CredentialBlob: protected.as_ptr() as *mut u8,
            Persist: CRED_PERSIST_LOCAL_MACHINE,
            UserName: PWSTR(account.as_ptr() as *mut u16),
            ..Default::default()
        };
        unsafe { CredWriteW(&credential, 0).is_ok() }
    }

    pub fn read(service: &str, id: &str) -> Result<String, ReadError> {
        let mut credential: *mut CREDENTIALW = std::ptr::null_mut();
        if let Err(error) = unsafe {
            CredReadW(
                &target(service, id),
                CRED_TYPE_GENERIC,
                None,
                &mut credential,
            )
        } {
            return Err(if error.code() == ERROR_NOT_FOUND.to_hresult() {
                ReadError::NotFound
            } else {
                ReadError::Unavailable
            });
        }
        let protected = unsafe {
            let stored = &*credential;
            let bytes = std::slice::from_raw_parts(
                stored.CredentialBlob,
                stored.CredentialBlobSize as usize,
            )
            .to_vec();
            CredFree(credential as *const _);
            bytes
        };
        let entropy = blob(ENTROPY);
        let mut plain = CRYPT_INTEGER_BLOB::default();
        let plain = unsafe {
            CryptUnprotectData(
                &blob(&protected),
                None,
                Some(&entropy),
                None,
                None,
                0,
                &mut plain,
            )
            .map_err(|_| ReadError::Unavailable)?;
            take(plain)
        };
        String::from_utf8(plain).map_err(|_| ReadError::Unavailable)
    }

    pub fn delete(service: &str, id: &str) -> bool {
        match unsafe { CredDeleteW(&target(service, id), CRED_TYPE_GENERIC, None) } {
            Ok(()) => true,
            Err(error) => error.code() == ERROR_NOT_FOUND.to_hresult(),
        }
    }
}

#[cfg(windows)]
use windows_store::{delete, read, write};

#[cfg(not(any(target_os = "macos", windows)))]
fn write(_service: &str, _id: &str, _token: &str) -> bool {
    false
}

#[cfg(not(any(target_os = "macos", windows)))]
fn read(_service: &str, _id: &str) -> Result<String, ReadError> {
    Err(ReadError::Unavailable)
}

#[cfg(not(any(target_os = "macos", windows)))]
fn delete(_service: &str, _id: &str) -> bool {
    false
}

#[cfg(test)]
mod tests {
    use super::*;

    const ID: &str = "0f8fad5b-d9cb-469f-a165-70867728950e";

    fn args(values: &[&str]) -> Vec<OsString> {
        values.iter().map(OsString::from).collect()
    }

    #[test]
    fn only_the_token_subcommand_is_an_invocation() {
        assert!(is_invocation(args(&["app", "mcp-token", "store", ID])));
        assert!(!is_invocation(args(&["app", "mcp"])));
        assert!(!is_invocation(args(&["app"])));
    }

    #[test]
    fn store_and_delete_take_exactly_one_token_id() {
        assert_eq!(
            parse_command(&args(&["app", "mcp-token", "store", ID])),
            Some(Command::Store(ID.to_string()))
        );
        assert_eq!(
            parse_command(&args(&["app", "mcp-token", "delete", &ID.to_uppercase()])),
            Some(Command::Delete(ID.to_string()))
        );
        assert_eq!(parse_command(&args(&["app", "mcp-token", "store"])), None);
        assert_eq!(
            parse_command(&args(&["app", "mcp-token", "read", ID])),
            None
        );
        assert_eq!(
            parse_command(&args(&["app", "mcp-token", "store", ID, "mdmcp_x"])),
            None
        );
    }

    #[test]
    fn a_token_id_is_a_guid_and_nothing_else() {
        assert!(is_token_id(ID));
        assert!(!is_token_id("mdmcp_0123"));
        assert!(!is_token_id("../0f8fad5b-d9cb-469f-a165-70867728950e"));
        assert!(!is_token_id("0f8fad5b-d9cb-469f-a165-70867728950"));
        assert!(!is_token_id("0f8fad5b-d9cb-469f-a165-70867728950g"));
    }

    #[test]
    fn the_token_comes_from_stdin_trimmed() {
        assert_eq!(
            read_token(&b"mdmcp_abc\n"[..]),
            Some("mdmcp_abc".to_string())
        );
        assert_eq!(read_token(&b"  \n"[..]), None);
    }

    #[test]
    fn each_build_channel_has_its_own_entries() {
        assert_ne!(service("Production"), service("Development"));
    }

    #[cfg(target_os = "macos")]
    #[test]
    #[ignore = "writes to the login keychain"]
    fn the_keychain_round_trip_overwrites_and_deletes() {
        let service = service("Development");
        let id = "00000000-0000-4000-8000-000000000001";
        assert!(write(&service, id, "mdmcp_first"));
        assert!(write(&service, id, "mdmcp_second"));
        assert_eq!(read(&service, id), Ok("mdmcp_second".to_string()));
        assert!(delete(&service, id));
        assert_eq!(read(&service, id), Err(ReadError::NotFound));
        assert!(delete(&service, id));
    }
}
