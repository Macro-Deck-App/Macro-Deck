ALTER TABLE device
    ADD COLUMN d_screensaver_enabled INTEGER NOT NULL DEFAULT 0;
ALTER TABLE device
    ADD COLUMN d_screensaver_idle_seconds INTEGER NOT NULL DEFAULT 300;
ALTER TABLE device
    ADD COLUMN d_screensaver_id TEXT NULL;
ALTER TABLE device
    ADD COLUMN d_screensaver_configuration TEXT NULL;
