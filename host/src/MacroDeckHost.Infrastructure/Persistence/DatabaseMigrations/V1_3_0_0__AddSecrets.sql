CREATE TABLE secret
(
    s_id              TEXT    NOT NULL
        CONSTRAINT pk_secret PRIMARY KEY,
    s_kind            INTEGER NOT NULL,
    s_encrypted_value TEXT    NOT NULL DEFAULT '',
    s_created_at      TEXT    NOT NULL,
    s_updated_at      TEXT    NOT NULL
);
