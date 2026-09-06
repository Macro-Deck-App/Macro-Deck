ALTER TABLE device
    ADD COLUMN d_provider_id TEXT NULL;

ALTER TABLE device
    ADD COLUMN d_provider_device_id TEXT NULL;

ALTER TABLE device
    ADD COLUMN d_model TEXT NULL;

ALTER TABLE device
    ADD COLUMN d_manufacturer TEXT NULL;

ALTER TABLE device
    ADD COLUMN d_layout_reference TEXT NULL;

ALTER TABLE device
    ADD COLUMN d_capabilities TEXT NULL;

CREATE UNIQUE INDEX ux_device_provider_identity ON device (d_provider_id, d_provider_device_id);
