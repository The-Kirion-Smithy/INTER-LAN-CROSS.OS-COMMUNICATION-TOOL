-- Existing attachment rows predate scope authority. Preserve them as
-- unscoped/unauthorized until they can be reconciled explicitly.
ALTER TABLE attachments ADD COLUMN scope_type TEXT NULL
    CHECK (scope_type IN ('DIRECT', 'GROUP'));
ALTER TABLE attachments ADD COLUMN scope_id TEXT NULL;

CREATE INDEX ix_attachments_authorized_scope
    ON attachments(scope_type, scope_id, created_utc, attachment_id);
