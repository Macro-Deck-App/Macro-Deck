use std::collections::BTreeSet;
use std::path::PathBuf;

use serde_json::Value;

const IN_APP_ONLY: [&str; 1] = ["install_update"];

fn read(rel: &str) -> String {
    let path = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join(rel);
    std::fs::read_to_string(&path)
        .unwrap_or_else(|e| panic!("failed to read {}: {e}", path.display()))
}

fn quoted_between(source: &str, start: &str, end: &str) -> Vec<String> {
    let from = source.find(start).expect("start marker") + start.len();
    let to = from + source[from..].find(end).expect("end marker");
    source[from..to]
        .split('"')
        .skip(1)
        .step_by(2)
        .map(str::to_string)
        .collect()
}

fn permission(command: &str) -> String {
    format!("allow-{}", command.replace('_', "-"))
}

fn manifest_commands() -> Vec<String> {
    quoted_between(&read("build.rs"), "commands(&[", "])")
}

fn capability_permissions(rel: &str) -> BTreeSet<String> {
    let value: Value = serde_json::from_str(&read(rel)).expect("capability JSON");
    value["permissions"]
        .as_array()
        .expect("permissions array")
        .iter()
        .filter_map(|p| p.as_str().map(str::to_string))
        .collect()
}

fn runtime_permissions(capability: &str) -> BTreeSet<String> {
    let source = read("src/window.rs");
    let start = format!("CapabilityBuilder::new(\"{capability}\")");
    quoted_between(&source, &start, ";")
        .into_iter()
        .filter(|p| p.starts_with("allow-"))
        .collect()
}

#[test]
fn every_app_command_is_granted_in_development_and_in_packaged_builds() {
    let shared_dev = capability_permissions("capabilities/main.json");
    let in_app_dev = capability_permissions("capabilities/in-app-update.json");
    let shared_runtime = runtime_permissions("main-window-runtime");
    let in_app_runtime = runtime_permissions("main-window-runtime-in-app-update");

    for command in manifest_commands() {
        let permission = permission(&command);
        if IN_APP_ONLY.contains(&command.as_str()) {
            assert!(
                in_app_dev.contains(&permission),
                "{permission} missing in in-app-update.json"
            );
            assert!(
                in_app_runtime.contains(&permission),
                "{permission} missing in the in-app runtime grant"
            );
            assert!(
                !shared_dev.contains(&permission),
                "{permission} must stay Windows/macOS only"
            );
            assert!(
                !shared_runtime.contains(&permission),
                "{permission} must stay Windows/macOS only"
            );
        } else {
            assert!(
                shared_dev.contains(&permission),
                "{permission} missing in main.json"
            );
            assert!(
                shared_runtime.contains(&permission),
                "{permission} missing in the runtime grant"
            );
        }
    }
}

#[test]
fn no_capability_reaches_the_host_error_window() {
    for rel in ["capabilities/main.json", "capabilities/in-app-update.json"] {
        let value: Value = serde_json::from_str(&read(rel)).expect("capability JSON");
        let windows: Vec<&str> = value["windows"]
            .as_array()
            .expect("windows array")
            .iter()
            .filter_map(Value::as_str)
            .collect();
        assert_eq!(windows, ["main"], "{rel} must only grant the main window");
    }

    let source = read("src/window.rs");
    let builders = source.matches("CapabilityBuilder::new(").count();
    let main_scoped = source.matches(".window(MAIN_WINDOW)").count();
    assert_eq!(
        builders, main_scoped,
        "every runtime grant must be scoped to the main window"
    );
    assert!(!source.contains(".windows("));
    assert!(!read("src/host_error_window.rs").contains("CapabilityBuilder"));
}
