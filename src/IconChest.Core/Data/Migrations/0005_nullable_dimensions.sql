-- 0005_nullable_dimensions.sql: Make width and height nullable for unreadable images in IconChest

PRAGMA defer_foreign_keys = ON;

CREATE TABLE image_new (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    folder_id INTEGER NOT NULL REFERENCES library_folder(id) ON DELETE CASCADE,
    path TEXT NOT NULL,
    path_key TEXT NOT NULL UNIQUE,
    file_name TEXT NOT NULL,
    extension TEXT NOT NULL,
    format TEXT NOT NULL,
    width INTEGER,
    height INTEGER,
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
    ) STORED,
    directory_path TEXT NOT NULL DEFAULT '',
    directory_key TEXT NOT NULL DEFAULT ''
);

INSERT INTO image_new (
    id, folder_id, path, path_key, file_name, extension, format,
    width, height, size_bytes, hash, hash_algo, created_utc, modified_utc,
    exif_date_utc, taken_override_utc, added_utc, title, description,
    rating, is_favourite, is_hidden, is_missing, missing_since_utc,
    is_duplicate, has_transparency, status, exif_json,
    directory_path, directory_key
)
SELECT
    id, folder_id, path, path_key, file_name, extension, format,
    width, height, size_bytes, hash, hash_algo, created_utc, modified_utc,
    exif_date_utc, taken_override_utc, added_utc, title, description,
    rating, is_favourite, is_hidden, is_missing, missing_since_utc,
    is_duplicate, has_transparency, status, exif_json,
    directory_path, directory_key
FROM image;

DROP TABLE image;
ALTER TABLE image_new RENAME TO image;

-- Recreate image indexes
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
CREATE INDEX IF NOT EXISTS idx_image_directory_key ON image(directory_key);
CREATE INDEX IF NOT EXISTS idx_image_folder_missing ON image(folder_id, is_missing);
CREATE INDEX IF NOT EXISTS idx_image_hash_size ON image(hash, size_bytes);
CREATE INDEX IF NOT EXISTS idx_image_folder_dir ON image(is_hidden, is_missing, folder_id, directory_key, directory_path);
CREATE INDEX IF NOT EXISTS idx_image_effective_tree ON image(is_hidden, is_missing, effective_date_utc);
CREATE INDEX IF NOT EXISTS idx_image_year_month ON image(is_hidden, is_missing, SUBSTR(effective_date_utc, 1, 4), SUBSTR(effective_date_utc, 6, 2));
CREATE INDEX IF NOT EXISTS idx_image_filters ON image(is_hidden, is_missing, is_duplicate, rating, is_favourite);

