CREATE TABLE app_user
(
    u_id            TEXT NOT NULL
        CONSTRAINT pk_app_user PRIMARY KEY,
    u_username      TEXT NOT NULL COLLATE NOCASE,
    u_password_hash TEXT NOT NULL,
    u_created_at    TEXT NOT NULL,
    u_updated_at    TEXT NOT NULL
);

CREATE UNIQUE INDEX ux_app_user_username ON app_user (u_username);

CREATE TABLE refresh_token
(
    rt_id             TEXT    NOT NULL
        CONSTRAINT pk_refresh_token PRIMARY KEY,
    rt_user_id        TEXT    NOT NULL,
    rt_token_hash     TEXT    NOT NULL,
    rt_scope          INTEGER NOT NULL,
    rt_persistent     INTEGER NOT NULL DEFAULT 0,
    rt_expires_at     TEXT    NOT NULL,
    rt_revoked_at     TEXT    NULL,
    rt_replaced_by_id TEXT    NULL,
    rt_created_at     TEXT    NOT NULL
);

CREATE UNIQUE INDEX ux_refresh_token_hash ON refresh_token (rt_token_hash);
CREATE INDEX ix_refresh_token_user ON refresh_token (rt_user_id);
