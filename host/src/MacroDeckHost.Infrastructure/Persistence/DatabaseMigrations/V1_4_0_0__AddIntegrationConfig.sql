CREATE TABLE integration_config_entry
(
    ice_id             TEXT NOT NULL
        CONSTRAINT pk_integration_config_entry PRIMARY KEY,
    ice_integration_id TEXT NOT NULL,
    ice_title          TEXT NOT NULL DEFAULT '',
    ice_values_json    TEXT NOT NULL DEFAULT '{}',
    ice_created_at     TEXT NOT NULL,
    ice_updated_at     TEXT NOT NULL
);

CREATE INDEX ix_integration_config_entry_integration_id
    ON integration_config_entry (ice_integration_id);
