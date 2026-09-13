ALTER TABLE refresh_token
    ADD COLUMN rt_rotated_by_grace INTEGER NOT NULL DEFAULT 0;
