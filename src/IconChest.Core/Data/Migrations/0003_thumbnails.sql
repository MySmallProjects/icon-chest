-- Migration 0003: Thumbnails and persistent work queue
-- Replaces initial image-keyed thumbnail_job with content-hash keyed thumbnail table

DROP TABLE IF EXISTS thumbnail_job;

CREATE TABLE thumbnail (
    hash TEXT NOT NULL,
    size INTEGER NOT NULL,
    state TEXT NOT NULL CHECK (state IN ('pending', 'running', 'done', 'failed', 'evicted', 'orphaned')),
    priority INTEGER NOT NULL DEFAULT 0,
    attempts INTEGER NOT NULL DEFAULT 0,
    next_attempt_utc TEXT,
    error TEXT,
    bytes INTEGER,
    width INTEGER,
    height INTEGER,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    last_accessed_utc TEXT,
    claimed_utc TEXT,
    PRIMARY KEY (hash, size)
);

-- Index for claiming jobs efficiently
CREATE INDEX idx_thumbnail_claim ON thumbnail (state, priority DESC, size ASC, next_attempt_utc ASC);

-- Index for LRU eviction of completed thumbnails
CREATE INDEX idx_thumbnail_evict ON thumbnail (state, last_accessed_utc ASC);

-- Index for hash lookups (e.g. orphan cleanup or state queries)
CREATE INDEX idx_thumbnail_hash ON thumbnail (hash);

