-- 0002_library_scanning.sql: Add directory path and comparison keys to image

ALTER TABLE image ADD COLUMN directory_path TEXT NOT NULL DEFAULT '';
ALTER TABLE image ADD COLUMN directory_key TEXT NOT NULL DEFAULT '';

CREATE INDEX IF NOT EXISTS idx_image_directory_key ON image(directory_key);
CREATE INDEX IF NOT EXISTS idx_image_folder_missing ON image(folder_id, is_missing);
CREATE INDEX IF NOT EXISTS idx_image_hash_size ON image(hash, size_bytes);

