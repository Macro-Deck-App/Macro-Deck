(function () {
  'use strict';

  if (window.macroDeckShell) {
    return;
  }

  function invoke(command, args) {
    const internals = window.__TAURI_INTERNALS__;
    if (!internals || typeof internals.invoke !== 'function') {
      return Promise.reject(new Error('Tauri IPC is not available'));
    }
    return internals.invoke(command, args);
  }

  function listen(event, callback) {
    var internals = window.__TAURI_INTERNALS__;
    if (
      !internals ||
      typeof internals.invoke !== 'function' ||
      typeof internals.transformCallback !== 'function'
    ) {
      return Promise.resolve(function () {});
    }
    var handler = internals.transformCallback(function (message) {
      try {
        callback(message && message.payload);
      } catch (error) {
        // A throwing listener must not break the event pipeline.
      }
    });
    return internals
      .invoke('plugin:event|listen', {
        event: event,
        target: { kind: 'Any' },
        handler: handler,
      })
      .then(function (eventId) {
        return function () {
          return internals.invoke('plugin:event|unlisten', {
            event: event,
            eventId: eventId,
          });
        };
      });
  }

  window.macroDeckShell = {
    kind: 'tauri',
    getHostPort: function () {
      return invoke('get_host_port');
    },
    getShellInfo: function () {
      return invoke('get_shell_info');
    },
    getCursorPosition: function () {
      return invoke('get_cursor_position');
    },
    openExternal: function (url) {
      return invoke('open_external', { url: String(url) });
    },
    showOpenDialog: function (options) {
      // Normalize every field: WebView2 forwards a missing JS property as JSON
      // null, which the Rust command rejects outright (issue #122).
      var input = options || {};
      return invoke('show_open_dialog', {
        options: {
          directory: input.directory === true,
          extensions: Array.isArray(input.extensions) ? input.extensions : [],
        },
      });
    },
    onFileDrop: function (callback) {
      return listen('file-drop', callback);
    },
    onFileOpen: function (callback) {
      return listen('file-open', callback);
    },
    onMenuAction: function (callback) {
      return listen('menu-action', callback);
    },
    takeMenuAction: function () {
      return invoke('take_menu_action');
    },
    takeOpenedFiles: function () {
      return invoke('take_opened_files');
    },
    saveFile: function (options) {
      // The file content is the raw request body (an ArrayBuffer stays binary
      // over IPC); name and extension filter travel as headers, the name
      // percent-encoded because header values must stay ASCII.
      var internals = window.__TAURI_INTERNALS__;
      if (!internals || typeof internals.invoke !== 'function') {
        return Promise.reject(new Error('Tauri IPC is not available'));
      }
      var opts = options || {};
      var extensions = opts.extensions || [];
      return internals.invoke('save_file', opts.data, {
        headers: {
          'x-macrodeck-file-name': encodeURIComponent(opts.fileName || ''),
          'x-macrodeck-extensions': extensions.join(','),
        },
      });
    },
    // Suspends the application menu's key equivalents while a hotkey recorder
    // is armed, so combinations the menu owns reach the WebView (issue #423).
    // macOS only; a no-op everywhere else.
    setHotkeyCapture: function (active) {
      return invoke('set_hotkey_capture', { active: active === true });
    },
    // macOS only; reports supported:false everywhere else so the UI can hide
    // the setting without knowing the platform itself.
    getHideDockIcon: function () {
      return invoke('get_hide_dock_icon');
    },
    setHideDockIcon: function (enabled) {
      return invoke('set_hide_dock_icon', { enabled: enabled === true });
    },
    checkForUpdate: function () {
      return invoke('check_for_update');
    },
    getUpdateState: function () {
      return invoke('get_update_state');
    },
    requestUpdateCheck: function () {
      return invoke('request_update_check');
    },
    cancelUpdateDownload: function () {
      return invoke('cancel_update_download');
    },
    onUpdateState: function (callback) {
      return listen('update-state', callback);
    },
    getUpdateChannel: function () {
      return invoke('get_update_channel');
    },
    setUpdateChannel: function (channel) {
      return invoke('set_update_channel', { channel: channel === 'beta' ? 'beta' : 'stable' });
    },
    getUpdateMode: function () {
      return invoke('get_update_mode');
    },
    setUpdateMode: function (mode) {
      return invoke('set_update_mode', { mode: mode });
    },
    // Not granted on Linux (issue #271): callers must branch on
    // checkForUpdate().installStrategy, never on typeof bridge.installUpdate.
    installUpdate: function () {
      return invoke('install_update');
    },
    onUpdateProgress: function (callback) {
      return listen('update-progress', callback);
    },
  };
})();
