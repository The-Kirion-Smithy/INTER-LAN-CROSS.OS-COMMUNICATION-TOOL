CREATE TABLE group_events (
    group_event_id TEXT PRIMARY KEY,
    group_id TEXT NOT NULL,
    actor_user_id TEXT NULL,
    subject_user_id TEXT NULL,
    event_type TEXT NOT NULL,
    payload_json TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    FOREIGN KEY (group_id) REFERENCES groups(group_id) ON DELETE CASCADE,
    FOREIGN KEY (actor_user_id) REFERENCES users(user_id),
    FOREIGN KEY (subject_user_id) REFERENCES users(user_id)
);

CREATE INDEX ix_group_events_group_created
    ON group_events(group_id, created_utc, group_event_id);

CREATE INDEX ix_group_members_user_active
    ON group_members(user_id, removed_utc, group_id);

CREATE INDEX ix_group_members_group_active
    ON group_members(group_id, removed_utc, group_role, user_id);
