-- Best run of each party (same players) on each board: map, tier and party size
CREATE TABLE IF NOT EXISTS runs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    map TEXT NOT NULL,
    tier INTEGER NOT NULL,
    party_size INTEGER NOT NULL,
    party_key TEXT NOT NULL,
    kills INTEGER NOT NULL,
    players TEXT NOT NULL, -- JSON: [{ steamId, steamName, name, character, kills }]
    modifiers INTEGER NOT NULL DEFAULT 0, -- RunModifier flags of the mod
    challenge TEXT NOT NULL DEFAULT '',
    mod_version TEXT NOT NULL DEFAULT '',
    created_at INTEGER NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS runs_party ON runs (map, tier, party_size, party_key);
CREATE INDEX IF NOT EXISTS runs_board ON runs (map, tier, party_size, kills DESC);

-- Submissions per client (hashed IP) for the rate limit
CREATE TABLE IF NOT EXISTS submissions (
    ip_hash TEXT NOT NULL,
    created_at INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS submissions_ip ON submissions (ip_hash, created_at);
CREATE INDEX IF NOT EXISTS submissions_time ON submissions (created_at);
