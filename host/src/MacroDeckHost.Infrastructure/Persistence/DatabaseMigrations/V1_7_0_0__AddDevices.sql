CREATE TABLE device
(
    d_id             TEXT    NOT NULL
        CONSTRAINT pk_device PRIMARY KEY,
    d_secret_hash    TEXT    NOT NULL,
    d_name           TEXT    NOT NULL,
    d_name_is_custom INTEGER NOT NULL DEFAULT 0,
    d_proposed_name  TEXT    NULL,
    d_client_type    INTEGER NOT NULL DEFAULT 0,
    d_form_factor    INTEGER NOT NULL DEFAULT 0,
    d_platform       TEXT    NULL,
    d_browser        TEXT    NULL,
    d_app_version    TEXT    NULL,
    d_last_seen_at   TEXT    NOT NULL,
    d_created_at     TEXT    NOT NULL
);

CREATE INDEX ix_device_last_seen ON device (d_last_seen_at);

ALTER TABLE refresh_token
    ADD COLUMN rt_device_id TEXT NULL;

CREATE INDEX ix_refresh_token_device ON refresh_token (rt_device_id);
