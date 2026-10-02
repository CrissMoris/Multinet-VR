// Runner (a): applies the migrations + seed into an in-memory PostgreSQL
// (@electric-sql/pglite, PG 17 compiled to WASM) and exercises every RPC as the
// `anon` role, exactly like PostgREST would. No Docker, no network.
//
//   npm test            (from backend/supabase/tests)
//
// The assertions live in lib/contract.mjs and are shared with rest.test.mjs.

import { test, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { readFile, readdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { PGlite } from '@electric-sql/pglite';
import { RpcError, SIGNATURES, registerContractTests, uniq } from './lib/contract.mjs';
import { SEEDED_EVENT, throwawayEvent } from './fixtures.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const supabaseDir = path.resolve(here, '..');
const migrationsDir = path.join(supabaseDir, 'migrations');
const seedFile = path.join(supabaseDir, 'seed.sql');

const SUPABASE_ROLES = ['anon', 'authenticated', 'service_role'];

let db;
let currentRole = 'postgres';

// Serialises a JS value the way PostgREST would hand it to the function parameter.
function encodeParam(value, pgType) {
  if (value === null || value === undefined) return null;
  if (pgType === 'jsonb') return JSON.stringify(value);
  if (pgType === 'text[]') {
    assert.ok(Array.isArray(value), 'text[] parameters must be arrays');
    const quote = (s) => `"${String(s).replace(/(["\\])/g, '\\$1')}"`;
    return `{${value.map(quote).join(',')}}`;
  }
  if (pgType === 'boolean') {
    // pglite's boolean serializer insists on a JS boolean (no 'true'/'false' strings).
    if (typeof value === 'boolean') return value;
    if (value === 'true' || value === 'false') return value === 'true';
    throw new TypeError(`boolean parameter received ${JSON.stringify(value)}`);
  }
  if (typeof value === 'object') return JSON.stringify(value);
  return String(value);
}

async function setRole(role) {
  if (role === currentRole) return;
  await db.exec(role === 'postgres' ? 'reset role' : `set role ${role}`);
  currentRole = role;
}

function toRpcError(err) {
  return new RpcError({ code: err.message, detail: err.detail, sqlState: err.code });
}

const ctx = {
  event: null,
  freshEvent: false,
  admin: null,

  async rpc(name, args) {
    const sig = SIGNATURES[name];
    assert.ok(sig, `unknown RPC ${name}`);
    const keys = Object.keys(args);
    const sql = `select public.${name}(${keys.map((k, i) => `${k} => $${i + 1}::${sig[k] || 'text'}`).join(', ')}) as result`;
    const values = keys.map((k) => encodeParam(args[k], sig[k] || 'text'));
    await setRole('anon');
    try {
      const res = await db.query(sql, values);
      return res.rows[0].result;
    } catch (err) {
      throw toRpcError(err);
    }
  },

  async anonSelect(table) {
    await setRole('anon');
    try {
      await db.query(`select * from public.${table} limit 1`);
      return { denied: false, reason: 'select succeeded' };
    } catch (err) {
      return { denied: err.code === '42501', reason: `${err.code} ${err.message}` };
    }
  },

  async tryInternalCall(fn, args) {
    await setRole('anon');
    const keys = Object.keys(args);
    const sql = `select public.${fn}(${keys.map((k, i) => `${k} => $${i + 1}::text`).join(', ')})`;
    try {
      await db.query(sql, keys.map((k) => args[k]));
      return { denied: false, reason: 'call succeeded' };
    } catch (err) {
      return { denied: err.code === '42501', reason: `${err.code} ${err.message}` };
    }
  },
};

before(async () => {
  db = new PGlite();

  // Supabase ships these roles; pglite does not. Create them if missing so the
  // migration's REVOKE/GRANT statements and SET ROLE behave like production.
  for (const role of SUPABASE_ROLES) {
    await db.exec(`do $$ begin
      if not exists (select 1 from pg_roles where rolname = '${role}') then
        create role ${role} nologin;
      end if;
    end $$;`);
  }

  const files = (await readdir(migrationsDir)).filter((f) => f.endsWith('.sql')).sort();
  assert.ok(files.length > 0, `no migrations found in ${migrationsDir}`);
  for (const file of files) {
    await db.exec(await readFile(path.join(migrationsDir, file), 'utf8'));
  }
  await db.exec(await readFile(seedFile, 'utf8'));

  ctx.admin = {
    async query(sql, params = []) {
      await setRole('postgres');
      return (await db.query(sql, params)).rows;
    },
  };

  // Run the contract against a throw-away event so ranks are exact.
  const ev = throwawayEvent(uniq());
  await ctx.admin.query(
    "insert into public.events (slug, name, access_code, is_active, name_display_mode) values ($1, $2, $3, true, 'full')",
    [ev.slug, ev.name, ev.accessCode],
  );
  ctx.event = ev;
  ctx.freshEvent = true;
});

after(async () => {
  if (db) await db.close();
});

// ---------------------------------------------------------------------------
// Runner-specific checks (schema / seed / privileges at the catalog level)
// ---------------------------------------------------------------------------

test('seed creates the default active event with name_display_mode = full', async () => {
  const rows = await ctx.admin.query(
    'select name, is_active, name_display_mode, char_length(access_code) as code_len from public.events where slug = $1',
    [SEEDED_EVENT.slug],
  );
  assert.equal(rows.length, 1, `seeded event ${SEEDED_EVENT.slug} must exist`);
  assert.equal(rows[0].name, SEEDED_EVENT.name);
  assert.equal(rows[0].is_active, true);
  assert.equal(rows[0].name_display_mode, 'full');
  assert.ok(rows[0].code_len >= 4);
});

test('seed is re-runnable (ON CONFLICT DO NOTHING)', async () => {
  await ctx.admin.query(await readFile(seedFile, 'utf8'));
  const rows = await ctx.admin.query('select count(*)::int as n from public.events where slug = $1', [SEEDED_EVENT.slug]);
  assert.equal(rows[0].n, 1);
});

test('the four RPCs are SECURITY DEFINER, owned by postgres, with a fixed search_path', async () => {
  const rows = await ctx.admin.query(`
    select p.proname, p.prosecdef, p.proowner::regrole::text as owner, p.proconfig
      from pg_proc p join pg_namespace n on n.oid = p.pronamespace
     where n.nspname = 'public'
       and p.proname in ('register_participant', 'submit_result', 'get_leaderboard', 'ping_event')
     order by p.proname`);
  assert.equal(rows.length, 4);
  for (const r of rows) {
    assert.equal(r.prosecdef, true, `${r.proname} must be SECURITY DEFINER`);
    assert.equal(r.owner, 'postgres', `${r.proname} must be owned by postgres`);
    assert.ok(Array.isArray(r.proconfig) && r.proconfig.includes('search_path=public'), `${r.proname} must pin search_path`);
  }
});

test('anon and authenticated may execute exactly the four RPCs and nothing else in public', async () => {
  const rows = await ctx.admin.query(`
    select p.proname,
           has_function_privilege('anon', p.oid, 'execute') as anon_exec,
           has_function_privilege('authenticated', p.oid, 'execute') as auth_exec
      from pg_proc p join pg_namespace n on n.oid = p.pronamespace
     where n.nspname = 'public'
     order by p.proname`);
  const allowed = new Set(['register_participant', 'submit_result', 'get_leaderboard', 'ping_event']);
  const anonAllowed = rows.filter((r) => r.anon_exec).map((r) => r.proname).sort();
  const authAllowed = rows.filter((r) => r.auth_exec).map((r) => r.proname).sort();
  assert.deepEqual(anonAllowed, [...allowed].sort());
  assert.deepEqual(authAllowed, [...allowed].sort());
});

test('anon and authenticated have no table, view or sequence privileges in public', async () => {
  const rows = await ctx.admin.query(`
    select grantee, table_name, privilege_type
      from information_schema.role_table_grants
     where table_schema = 'public' and grantee in ('anon', 'authenticated')`);
  assert.deepEqual(rows, []);
  const seq = await ctx.admin.query(`
    select c.relname
      from pg_class c join pg_namespace n on n.oid = c.relnamespace
     where n.nspname = 'public' and c.relkind = 'S'
       and (has_sequence_privilege('anon', c.oid, 'usage') or has_sequence_privilege('authenticated', c.oid, 'usage'))`);
  assert.deepEqual(seq, []);
});

test('row level security is enabled on every table and no policy exists', async () => {
  const rows = await ctx.admin.query(`
    select c.relname, c.relrowsecurity,
           (select count(*)::int from pg_policy pol where pol.polrelid = c.oid) as policies
      from pg_class c join pg_namespace n on n.oid = c.relnamespace
     where n.nspname = 'public' and c.relkind = 'r'
     order by c.relname`);
  assert.deepEqual(rows.map((r) => r.relname), ['events', 'participants', 'results']);
  for (const r of rows) {
    assert.equal(r.relrowsecurity, true, `${r.relname} must have RLS enabled`);
    assert.equal(r.policies, 0, `${r.relname} must have no policies`);
  }
});

test('leaderboard_public view exists, is owned by postgres and exposes no PII columns', async () => {
  const cols = await ctx.admin.query(`
    select column_name from information_schema.columns
     where table_schema = 'public' and table_name = 'leaderboard_public' order by ordinal_position`);
  const names = cols.map((c) => c.column_name);
  assert.ok(names.includes('rank') && names.includes('display_name') && names.includes('score'));
  for (const pii of ['phone', 'email', 'first_name', 'last_name']) {
    assert.ok(!names.includes(pii), `${pii} must not be in leaderboard_public`);
  }
  const owner = await ctx.admin.query("select relowner::regrole::text as owner, reloptions from pg_class where relname = 'leaderboard_public'");
  assert.equal(owner[0].owner, 'postgres');
  assert.ok(!(owner[0].reloptions || []).includes('security_invoker=true'), 'view must not be security_invoker');
});

test('results ranking index exists with the contract ordering', async () => {
  const rows = await ctx.admin.query("select indexdef from pg_indexes where schemaname = 'public' and indexname = 'results_leaderboard_idx'");
  assert.equal(rows.length, 1);
  assert.match(rows[0].indexdef, /\(event_id, status, score DESC, completion_ms, completed_at\)/);
});

// ---------------------------------------------------------------------------
// Shared RPC contract
// ---------------------------------------------------------------------------
registerContractTests(ctx);
