CREATE TABLE plugin_registration_new
(
    pr_id               TEXT NOT NULL
        CONSTRAINT pk_plugin_registration PRIMARY KEY,
    pr_plugin_id        TEXT NOT NULL,
    pr_display_name     TEXT NOT NULL,
    pr_secret_hash      TEXT NOT NULL,
    pr_access_token_id  TEXT NULL,
    pr_origin           TEXT NOT NULL,
    pr_last_seen_at     TEXT NULL,
    pr_revoked_at       TEXT NULL,
    pr_created_at       TEXT NOT NULL
);

INSERT INTO plugin_registration_new
    (pr_id, pr_plugin_id, pr_display_name, pr_secret_hash, pr_access_token_id,
     pr_origin, pr_last_seen_at, pr_revoked_at, pr_created_at)
SELECT pr_id, pr_plugin_id, pr_display_name, pr_secret_hash, pr_access_token_id,
       'developer-token', pr_last_seen_at, pr_revoked_at, pr_created_at
FROM plugin_registration;

DROP TABLE plugin_registration;

ALTER TABLE plugin_registration_new RENAME TO plugin_registration;

CREATE UNIQUE INDEX ux_plugin_registration_plugin_id ON plugin_registration (pr_plugin_id);
CREATE INDEX ix_plugin_registration_access_token ON plugin_registration (pr_access_token_id);
