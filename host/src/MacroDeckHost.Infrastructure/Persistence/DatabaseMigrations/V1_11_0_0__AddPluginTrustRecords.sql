CREATE TABLE plugin_trust_record
(
    ptr_id               TEXT NOT NULL
        CONSTRAINT pk_plugin_trust_record PRIMARY KEY,
    ptr_plugin_id        TEXT NOT NULL,
    ptr_version          TEXT NOT NULL,
    ptr_admitted_verdict TEXT NOT NULL,
    ptr_certificate_id   TEXT NULL,
    ptr_installed_at     TEXT NOT NULL,
    ptr_created_at       TEXT NOT NULL
);

CREATE UNIQUE INDEX ux_plugin_trust_record_plugin_version ON plugin_trust_record (ptr_plugin_id, ptr_version);
