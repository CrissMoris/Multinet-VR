// Runner (b): the same contract assertions as pglite.test.mjs, but over
// PostgREST HTTP (fetch) against a running Supabase stack - local
// (`supabase start`) or a linked cloud project.
//
//   SUPABASE_URL=http://127.0.0.1:54321 SUPABASE_ANON_KEY=<anon key> npm run test:rest
//
// Optional:
//   SUPABASE_DB_URL        postgres connection string of the same stack. When set,
//                          the runner creates a throw-away event, verifies rows at
//                          the SQL level (counts, normalisation, inactive event,
//                          display mode) and deletes everything it created.
//   MT_EVENT_SLUG / MT_EVENT_ACCESS_CODE
//                          event to use when SUPABASE_DB_URL is NOT set (defaults
//                          mirror seed.sql). Rows written in that mode carry
//                          station_id = 'test-runner' so they are easy to delete.
//
// Skips with a clear message when SUPABASE_URL / SUPABASE_ANON_KEY are missing.

import { test, before, after } from 'node:test';
import assert from 'node:assert/strict';
import pg from 'pg';
import { RpcError, registerContractTests, uniq, TEST_STATION_ID, expectCode } from './lib/contract.mjs';
import { SEEDED_EVENT, throwawayEvent } from './fixtures.mjs';

const SUPABASE_URL = (process.env.SUPABASE_URL || '').trim().replace(/\/+$/, '');
const SUPABASE_ANON_KEY = (process.env.SUPABASE_ANON_KEY || '').trim();
const SUPABASE_DB_URL = (process.env.SUPABASE_DB_URL || '').trim();

const SKIP_MESSAGE = [
  'SUPABASE_URL and SUPABASE_ANON_KEY are not set - REST suite skipped.',
  'Local stack:  supabase start  then  supabase status -o env  and export',
  '  SUPABASE_URL=<API_URL> SUPABASE_ANON_KEY=<ANON_KEY> [SUPABASE_DB_URL=<DB_URL>]',
].join(' ');

if (!SUPABASE_URL || !SUPABASE_ANON_KEY) {
  test('REST contract (PostgREST over HTTP)', { skip: SKIP_MESSAGE }, () => {});
} else {
  const headers = {
    apikey: SUPABASE_ANON_KEY,
    Authorization: `Bearer ${SUPABASE_ANON_KEY}`,
    'Content-Type': 'application/json',
  };

  async function call(method, pathname, body) {
    const res = await fetch(`${SUPABASE_URL}${pathname}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    const text = await res.text();
    let json = null;
    try {
      json = text ? JSON.parse(text) : null;
    } catch {
      json = null;
    }
    return { res, json, text };
  }

  let pgClient = null;
  let eventId = null;

  const ctx = {
    event: null,
    freshEvent: false,
    admin: null,

    async rpc(name, args) {
      const { res, json, text } = await call('POST', `/rest/v1/rpc/${name}`, args);
      if (!res.ok) {
        throw new RpcError({
          code: (json && json.message) || `HTTP ${res.status}`,
          detail: (json && (json.details || json.hint)) || text,
          sqlState: json && json.code,
          httpStatus: res.status,
        });
      }
      return json;
    },

    async anonSelect(table) {
      const { res, json } = await call('GET', `/rest/v1/${table}?select=*&limit=1`);
      const denied = !res.ok && [401, 403, 404].includes(res.status);
      return { denied, reason: `HTTP ${res.status} ${json ? JSON.stringify(json) : ''}` };
    },

    async tryInternalCall(fn, args) {
      const { res, json } = await call('POST', `/rest/v1/rpc/${fn}`, args);
      const executed = json && json.message === args.p_code; // would mean mt_fail ran as anon
      const denied = !res.ok && [401, 403, 404].includes(res.status) && !executed;
      return { denied, reason: `HTTP ${res.status} ${json ? JSON.stringify(json) : ''}` };
    },
  };

  before(async () => {
    if (SUPABASE_DB_URL) {
      pgClient = new pg.Client({ connectionString: SUPABASE_DB_URL });
      await pgClient.connect();
      ctx.admin = {
        async query(sql, params = []) {
          return (await pgClient.query(sql, params)).rows;
        },
      };
      const ev = throwawayEvent(uniq());
      const rows = await ctx.admin.query(
        "insert into public.events (slug, name, access_code, is_active, name_display_mode) values ($1, $2, $3, true, 'full') returning id",
        [ev.slug, ev.name, ev.accessCode],
      );
      eventId = rows[0].id;
      ctx.event = ev;
      ctx.freshEvent = true;
      return;
    }

    ctx.event = { ...SEEDED_EVENT };
    ctx.freshEvent = false;
    // Fail fast with a useful message when the configured event does not match.
    try {
      const ping = await ctx.rpc('ping_event', { p_event_slug: ctx.event.slug, p_access_code: ctx.event.accessCode });
      ctx.event.name = ping.event_name;
    } catch (err) {
      throw new Error(
        `ping_event failed for slug "${ctx.event.slug}" (${err.message}). `
        + 'Set MT_EVENT_SLUG / MT_EVENT_ACCESS_CODE to an active event of the target project, '
        + 'or set SUPABASE_DB_URL so the runner can create its own event.',
      );
    }
  });

  after(async () => {
    if (pgClient) {
      try {
        if (eventId) {
          await pgClient.query('delete from public.results where event_id = $1', [eventId]);
          await pgClient.query('delete from public.participants where event_id = $1', [eventId]);
          await pgClient.query('delete from public.events where id = $1', [eventId]);
        }
      } finally {
        await pgClient.end();
      }
    }
  });

  // ---------------------------------------------------------------------------
  // HTTP-level checks that only make sense over PostgREST
  // ---------------------------------------------------------------------------
  test('PostgREST maps contract errors to HTTP 400 with message=<CODE> and details', async () => {
    const err = await expectCode(ctx, 'ping_event', { p_event_slug: ctx.event.slug, p_access_code: 'nope' }, 'EVENT_ACCESS_DENIED');
    assert.equal(err.httpStatus, 400);
    assert.equal(err.sqlState, 'P0001');
    assert.ok(typeof err.detail === 'string' && err.detail.length > 0, 'details must carry the human reason');
  });

  test('RPC responses are JSON with the expected content type', async () => {
    const res = await fetch(`${SUPABASE_URL}/rest/v1/rpc/get_leaderboard`, {
      method: 'POST',
      headers,
      body: JSON.stringify({ p_event_slug: ctx.event.slug, p_limit: 5 }),
    });
    assert.equal(res.status, 200);
    assert.match(res.headers.get('content-type') || '', /application\/json/);
    const body = await res.json();
    assert.ok(Array.isArray(body));
  });

  test('test rows are tagged with the test-runner station id', async (t) => {
    if (!ctx.admin) {
      t.skip('needs postgres-level access');
      return;
    }
    const rows = await ctx.admin.query('select count(*)::int as n from public.participants where event_id = $1 and station_id <> $2', [eventId, TEST_STATION_ID]);
    assert.equal(rows[0].n, 0);
  });

  // ---------------------------------------------------------------------------
  // Shared RPC contract
  // ---------------------------------------------------------------------------
  registerContractTests(ctx);
}
