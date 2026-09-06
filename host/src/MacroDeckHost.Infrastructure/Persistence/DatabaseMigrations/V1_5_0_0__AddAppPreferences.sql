CREATE TABLE app_preference
(
    ap_key        TEXT NOT NULL
        CONSTRAINT pk_app_preference PRIMARY KEY,
    ap_value      TEXT NOT NULL DEFAULT '',
    ap_created_at TEXT NOT NULL,
    ap_updated_at TEXT NOT NULL
);
