// Megaphonk Mod co-op leaderboard: the host of a party submits each finished run, everyone reads the boards.
// A board is a map, a tier and a party size; each party (same players) keeps its best run (total kills).
// Every player proves its Steam id with a Steam session ticket (checked with the Steam Web API, key in the
// STEAM_API_KEY secret): a run can't be submitted for players who weren't in the game.
//
// The players' Steam names are taken from Steam (GetPlayerSummaries) at submission.
//
//   POST /v1/runs          { map, tier, modifiers, challenge, modVersion, players: [{ steamId, ticket, name, character, kills }] }
//   GET  /v1/top?map=Forest&tier=1&size=2[&offset=0&limit=50]                        -> { entries, total }
//   POST /v1/top/friends   { map, tier, size, steamIds: ["7656..."], offset, limit }  runs with any of these players

const MEGABONK_APP_ID = 3405340;
const MAPS = ["Forest", "Desert", "Hell", "Graveyard"];
const MAX_TIER = 5;
const MIN_PARTY = 2;
const MAX_PARTY = 6;
const MAX_KILLS_PER_PLAYER = 5_000_000;
const MAX_LIMIT = 100;
const MODIFIER_MASK = (1 << 14) - 1; // RunModifier flags of the mod
const MAX_FRIENDS = 3001;
const MAX_BODY_BYTES = 128 * 1024;
const STEAM_TIMEOUT_MS = 5000;
const SUBMISSIONS_PER_HOUR = 30;
const HOUR_MS = 3_600_000;

export default {
    async fetch(request, env) {
        const url = new URL(request.url);
        try {
            if (request.method === "POST" && url.pathname === "/v1/runs") {
                return await submitRun(request, env);
            }
            if (request.method === "GET" && url.pathname === "/v1/top") {
                return await top(env, {
                    map: url.searchParams.get("map"),
                    tier: Number(url.searchParams.get("tier")),
                    size: Number(url.searchParams.get("size")),
                    offset: Number(url.searchParams.get("offset") ?? 0),
                    limit: Number(url.searchParams.get("limit") ?? 50),
                });
            }
            if (request.method === "POST" && url.pathname === "/v1/top/friends") {
                const body = await readJson(request);
                if (!body || !Array.isArray(body.steamIds)) {
                    return error(400, "steamIds required");
                }
                const steamIds = body.steamIds.map(String).filter(isSteamId).slice(0, MAX_FRIENDS);
                return await top(env, { map: body.map, tier: Number(body.tier), size: Number(body.size), offset: Number(body.offset ?? 0), limit: Number(body.limit ?? 50), steamIds });
            }
            return error(404, "not found");
        } catch (e) {
            console.error(e);
            return error(500, "internal error");
        }
    },
};

async function submitRun(request, env) {
    const body = await readJson(request);
    const run = body && parseRun(body);
    if (!run) {
        return error(400, "invalid run");
    }

    // Every attempt counts, failed ones too: each one costs Steam Web API calls
    const now = Date.now();
    const ipHash = await sha256(env.STEAM_API_KEY + (request.headers.get("CF-Connecting-IP") ?? "unknown"));
    await env.DB.prepare("DELETE FROM submissions WHERE created_at < ?").bind(now - HOUR_MS).run();
    await env.DB.prepare("INSERT INTO submissions (ip_hash, created_at) VALUES (?, ?)").bind(ipHash, now).run();
    const { count } = await env.DB.prepare("SELECT COUNT(*) AS count FROM submissions WHERE ip_hash = ?").bind(ipHash).first();
    if (count > SUBMISSIONS_PER_HOUR) {
        return error(429, "too many submissions");
    }

    const verified = await Promise.all(run.players.map(p => verifyTicket(env, p.steamId, p.ticket)));
    if (verified.includes(null)) {
        return error(503, "steam unavailable");
    }
    if (verified.includes(false)) {
        return error(403, "steam verification failed");
    }

    const kills = run.players.reduce((sum, p) => sum + p.kills, 0);
    const partyKey = run.players.map(p => p.steamId).sort().join(",");
    const steamNames = await getSteamNames(env, run.players.map(p => p.steamId));
    const players = run.players.map(({ steamId, name, character, kills }) => ({ steamId, steamName: steamNames.get(steamId) ?? name, name, character, kills }));

    await env.DB.batch([
        env.DB.prepare(
            `INSERT INTO runs (map, tier, party_size, party_key, kills, players, modifiers, challenge, mod_version, created_at)
             VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
             ON CONFLICT (map, tier, party_size, party_key) DO UPDATE SET
                 kills = excluded.kills, players = excluded.players, modifiers = excluded.modifiers, challenge = excluded.challenge,
                 mod_version = excluded.mod_version, created_at = excluded.created_at
             WHERE excluded.kills > runs.kills`
        ).bind(run.map, run.tier, run.players.length, partyKey, kills, JSON.stringify(players), run.modifiers, run.challenge, run.modVersion, now),
    ]);

    // The party's best run (this one or a better one stored before) and its rank
    const best = await env.DB.prepare(
        `SELECT kills, (SELECT COUNT(*) + 1 FROM runs AS other
                        WHERE other.map = runs.map AND other.tier = runs.tier AND other.party_size = runs.party_size
                          AND other.kills > runs.kills) AS rank
         FROM runs WHERE map = ? AND tier = ? AND party_size = ? AND party_key = ?`
    ).bind(run.map, run.tier, run.players.length, partyKey).first();
    return json({ kills, bestKills: best.kills, rank: best.rank });
}

async function top(env, { map, tier, size, offset, limit, steamIds }) {
    if (!MAPS.includes(map) || !isTier(tier) || !isPartySize(size)) {
        return error(400, "invalid board");
    }
    limit = Number.isInteger(limit) ? Math.min(Math.max(limit, 1), MAX_LIMIT) : 50;
    offset = Number.isInteger(offset) && offset > 0 ? offset : 0;

    // Same filter for the page and the total
    let where = "map = ? AND tier = ? AND party_size = ?";
    const params = [map, tier, size];
    if (steamIds) {
        where += ` AND EXISTS (SELECT 1 FROM json_each(runs.players) p
                               WHERE json_extract(p.value, '$.steamId') IN (SELECT value FROM json_each(?)))`;
        params.push(JSON.stringify(steamIds));
    }

    const [page, total] = await env.DB.batch([
        env.DB.prepare(`SELECT kills, players, modifiers, challenge, created_at FROM runs WHERE ${where}
                        ORDER BY kills DESC, created_at ASC LIMIT ? OFFSET ?`).bind(...params, limit, offset),
        env.DB.prepare(`SELECT COUNT(*) AS count FROM runs WHERE ${where}`).bind(...params),
    ]);

    const entries = page.results.map((row, i) => ({
        rank: offset + i + 1,
        kills: row.kills,
        modifiers: row.modifiers,
        challenge: row.challenge,
        at: row.created_at,
        players: JSON.parse(row.players).map(p => ({ name: p.name, steamName: p.steamName ?? p.name, character: p.character, kills: p.kills })),
    }));
    return json({ entries, total: total.results[0].count });
}

function parseRun(body) {
    const tier = Number(body.tier);
    if (!MAPS.includes(body.map) || !isTier(tier) || !Array.isArray(body.players) || !isPartySize(body.players.length)) {
        return null;
    }

    const players = [];
    for (const p of body.players) {
        const steamId = String(p?.steamId ?? "");
        const ticket = String(p?.ticket ?? "");
        const name = cleanText(p?.name, 32);
        const character = cleanText(p?.character, 32);
        const kills = p?.kills;
        if (!isSteamId(steamId) || steamId === "0" || !/^[0-9A-Fa-f]{20,4096}$/.test(ticket) || !name || !character || !Number.isInteger(kills) || kills < 0 || kills > MAX_KILLS_PER_PLAYER) {
            return null;
        }
        players.push({ steamId, ticket, name, character, kills });
    }

    if (new Set(players.map(p => p.steamId)).size !== players.length) {
        return null;
    }

    const modifiers = body.modifiers ?? 0;
    if (!Number.isInteger(modifiers) || (modifiers & ~MODIFIER_MASK) !== 0) {
        return null;
    }

    return { map: body.map, tier, players, modifiers, challenge: cleanText(body.challenge, 64) ?? "", modVersion: cleanText(body.modVersion, 16) ?? "" };
}

/**
 * The ticket is a live Steam session ticket of Megabonk for this Steam id: true, false, or null when Steam could not
 * answer (the client retries). A player's ticket is replaced (and the old one cancelled) when it joins another game.
 */
async function verifyTicket(env, steamId, ticket) {
    try {
        const url = `https://api.steampowered.com/ISteamUserAuth/AuthenticateUserTicket/v1/?key=${env.STEAM_API_KEY}&appid=${MEGABONK_APP_ID}&ticket=${ticket}`;
        const response = await fetch(url, { signal: AbortSignal.timeout(STEAM_TIMEOUT_MS) });
        if (!response.ok) {
            console.warn(`Steam ticket check: HTTP ${response.status}`);
            return response.status === 429 || response.status >= 500 ? null : false;
        }
        const params = (await response.json())?.response?.params;
        return params?.result === "OK" && params.steamid === steamId;
    } catch (e) {
        console.warn(`Steam ticket check failed: ${e}`);
        return null;
    }
}

/** Steam names (persona names) of these players, by Steam id: empty when Steam could not answer */
async function getSteamNames(env, steamIds) {
    const names = new Map();
    try {
        const url = `https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key=${env.STEAM_API_KEY}&steamids=${steamIds.join(",")}`;
        const response = await fetch(url, { signal: AbortSignal.timeout(STEAM_TIMEOUT_MS) });
        if (response.ok) {
            for (const player of (await response.json())?.response?.players ?? []) {
                const name = cleanText(player.personaname, 32);
                if (name) {
                    names.set(String(player.steamid), name);
                }
            }
        }
    } catch (e) {
        console.warn(`Steam names failed: ${e}`);
    }
    return names;
}

function isTier(tier) {
    return Number.isInteger(tier) && tier >= 1 && tier <= MAX_TIER;
}

function isPartySize(size) {
    return Number.isInteger(size) && size >= MIN_PARTY && size <= MAX_PARTY;
}

function isSteamId(id) {
    return /^\d{1,20}$/.test(id);
}

/** Trimmed text without control, bidi or zero-width characters nor rich text tags (the game renders names with TextMeshPro) */
function cleanText(value, maxLength) {
    if (typeof value !== "string") {
        return null;
    }
    const text = [...value.replace(/[\u0000-\u001f\u007f-\u009f​-‏‪-‮⁦-⁩<>]/g, "").trim()].slice(0, maxLength).join("");
    return text.length > 0 ? text : null;
}

async function readJson(request) {
    if (Number(request.headers.get("Content-Length") ?? 0) > MAX_BODY_BYTES) {
        return null;
    }
    try {
        return await request.json();
    } catch {
        return null;
    }
}

async function sha256(text) {
    const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
    return [...new Uint8Array(digest)].map(b => b.toString(16).padStart(2, "0")).join("");
}

function json(data, status = 200) {
    return new Response(JSON.stringify(data), { status, headers: { "Content-Type": "application/json" } });
}

function error(status, message) {
    return json({ error: message }, status);
}
