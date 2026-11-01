-- 0001_initial.sql: Initial schema for IconChest

CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER PRIMARY KEY,
    applied_utc TEXT NOT NULL,
    description TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS library_folder (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    path TEXT NOT NULL UNIQUE,
    enabled INTEGER NOT NULL DEFAULT 1,
    last_scan_utc TEXT,
    include_patterns TEXT
);

CREATE TABLE IF NOT EXISTS image (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    folder_id INTEGER NOT NULL REFERENCES library_folder(id) ON DELETE CASCADE,
    path TEXT NOT NULL,
    path_key TEXT NOT NULL UNIQUE,
    file_name TEXT NOT NULL,
    extension TEXT NOT NULL,
    format TEXT NOT NULL,
    width INTEGER NOT NULL,
    height INTEGER NOT NULL,
    size_bytes INTEGER NOT NULL,
    hash TEXT NOT NULL,
    hash_algo TEXT NOT NULL,
    created_utc TEXT,
    modified_utc TEXT,
    exif_date_utc TEXT,
    taken_override_utc TEXT,
    added_utc TEXT NOT NULL,
    title TEXT NOT NULL,
    description TEXT NOT NULL DEFAULT '',
    rating INTEGER NOT NULL DEFAULT 0 CHECK(rating >= 0 AND rating <= 5),
    is_favourite INTEGER NOT NULL DEFAULT 0,
    is_hidden INTEGER NOT NULL DEFAULT 0,
    is_missing INTEGER NOT NULL DEFAULT 0,
    missing_since_utc TEXT,
    is_duplicate INTEGER NOT NULL DEFAULT 0,
    has_transparency INTEGER,
    status TEXT NOT NULL DEFAULT 'ok',
    exif_json TEXT,
    effective_date_utc TEXT GENERATED ALWAYS AS (
        COALESCE(taken_override_utc, exif_date_utc, created_utc, modified_utc)
    ) STORED
);

CREATE TABLE IF NOT EXISTS tag (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL COLLATE NOCASE UNIQUE,
    fg_color TEXT NOT NULL DEFAULT '#ffffff',
    bg_color TEXT NOT NULL DEFAULT '#3b82f6'
);

CREATE TABLE IF NOT EXISTS image_tag (
    image_id INTEGER NOT NULL REFERENCES image(id) ON DELETE CASCADE,
    tag_id INTEGER NOT NULL REFERENCES tag(id) ON DELETE CASCADE,
    PRIMARY KEY (image_id, tag_id)
);

CREATE TABLE IF NOT EXISTS thumbnail_job (
    image_id INTEGER NOT NULL REFERENCES image(id) ON DELETE CASCADE,
    size INTEGER NOT NULL,
    state TEXT NOT NULL DEFAULT 'pending',
    attempts INTEGER NOT NULL DEFAULT 0,
    error TEXT,
    updated_utc TEXT NOT NULL,
    PRIMARY KEY (image_id, size)
);

-- Indexes
CREATE INDEX IF NOT EXISTS idx_image_hash ON image(hash);
CREATE INDEX IF NOT EXISTS idx_image_folder_id ON image(folder_id);
CREATE INDEX IF NOT EXISTS idx_image_is_hidden ON image(is_hidden);
CREATE INDEX IF NOT EXISTS idx_image_is_missing ON image(is_missing);
CREATE INDEX IF NOT EXISTS idx_image_is_favourite ON image(is_favourite);
CREATE INDEX IF NOT EXISTS idx_image_is_duplicate ON image(is_duplicate);
CREATE INDEX IF NOT EXISTS idx_image_effective_date ON image(effective_date_utc);
CREATE INDEX IF NOT EXISTS idx_image_created_utc ON image(created_utc);
CREATE INDEX IF NOT EXISTS idx_image_modified_utc ON image(modified_utc);
CREATE INDEX IF NOT EXISTS idx_image_added_utc ON image(added_utc);
CREATE INDEX IF NOT EXISTS idx_image_tag_tag_id ON image_tag(tag_id);
CREATE INDEX IF NOT EXISTS idx_thumbnail_job_state ON thumbnail_job(state);
