ALTER TABLE refresh_token
    ADD COLUMN rt_family_id TEXT NOT NULL DEFAULT '';

-- Every existing rotation chain becomes one family, walked from the head that nothing replaced, so a
-- predecessor coming back still reaches the live token at the tail of its own chain.
WITH RECURSIVE chain(family_id, id) AS (SELECT rt_id, rt_id
                                        FROM refresh_token
                                        WHERE rt_id NOT IN (SELECT rt_replaced_by_id
                                                            FROM refresh_token
                                                            WHERE rt_replaced_by_id IS NOT NULL)
                                        UNION ALL
                                        SELECT chain.family_id, t.rt_replaced_by_id
                                        FROM refresh_token t
                                                 JOIN chain ON t.rt_id = chain.id
                                        WHERE t.rt_replaced_by_id IS NOT NULL)
UPDATE refresh_token
SET rt_family_id = COALESCE((SELECT family_id FROM chain WHERE chain.id = refresh_token.rt_id), rt_id);

CREATE INDEX ix_refresh_token_family ON refresh_token (rt_family_id);
