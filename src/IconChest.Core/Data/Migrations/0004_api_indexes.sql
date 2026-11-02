-- 0004_api_indexes.sql: Additional query and filtering indexes for IconChest API

-- Date sorting and filtering indexes
CREATE INDEX IF NOT EXISTS idx_image_effective_date ON image(effective_date_utc);
CREATE INDEX IF NOT EXISTS idx_image_created_utc ON image(created_utc);
CREATE INDEX IF NOT EXISTS idx_image_modified_utc ON image(modified_utc);
CREATE INDEX IF NOT EXISTS idx_image_added_utc ON image(added_utc);

-- Directory tree and filter lookup (covering index for folder tree counts)
CREATE INDEX IF NOT EXISTS idx_image_folder_dir ON image(is_hidden, is_missing, folder_id, directory_key, directory_path);

-- Date tree index & expression index for ultra-fast year/month hierarchy tree calculation
CREATE INDEX IF NOT EXISTS idx_image_effective_tree ON image(is_hidden, is_missing, effective_date_utc);
CREATE INDEX IF NOT EXISTS idx_image_year_month ON image(is_hidden, is_missing, SUBSTR(effective_date_utc, 1, 4), SUBSTR(effective_date_utc, 6, 2));

-- Common filter flags composite index
CREATE INDEX IF NOT EXISTS idx_image_filters ON image(is_hidden, is_missing, is_duplicate, rating, is_favourite);

-- Efficient reverse tag lookup and tag filtering
CREATE INDEX IF NOT EXISTS idx_image_tag_reverse ON image_tag(tag_id, image_id);
