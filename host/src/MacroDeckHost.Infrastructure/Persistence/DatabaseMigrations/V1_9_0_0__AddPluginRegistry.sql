CREATE TABLE plugin_access_token
(
    pat_id            TEXT NOT NULL
        CONSTRAINT pk_plugin_access_token PRIMARY KEY,
    pat_name          TEXT NOT NULL,
    pat_token_hash    TEXT NOT NULL,
    pat_scopes        TEXT NOT NULL,
    pat_expires_at    TEXT NULL,
    pat_last_used_at  TEXT NULL,
    pat_revoked_at    TEXT NULL,
    pat_created_at    TEXT NOT NULL
);

CREATE UNIQUE INDEX ux_plugin_access_token_hash ON plugin_access_token (pat_token_hash);

CREATE TABLE plugin_registration
(
    pr_id               TEXT NOT NULL
        CONSTRAINT pk_plugin_registration PRIMARY KEY,
    pr_plugin_id        TEXT NOT NULL,
    pr_display_name     TEXT NOT NULL,
    pr_secret_hash      TEXT NOT NULL,
    pr_access_token_id  TEXT NOT NULL,
    pr_last_seen_at     TEXT NULL,
    pr_revoked_at       TEXT NULL,
    pr_created_at       TEXT NOT NULL
);

CREATE UNIQUE INDEX ux_plugin_registration_plugin_id ON plugin_registration (pr_plugin_id);
CREATE INDEX ix_plugin_registration_access_token ON plugin_registration (pr_access_token_id);
