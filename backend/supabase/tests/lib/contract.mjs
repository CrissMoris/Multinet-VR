// Shared RPC contract assertions for the MultiTravel Valiz Challenge backend.
//
// Both runners (pglite.test.mjs = in-process Postgres, rest.test.mjs = PostgREST
// over HTTP) build a `ctx` object with the same small transport interface and
// register the very same tests through `registerContractTests(ctx)`.
//
// ctx interface (filled by the runner inside its `before` hook):
//   ctx.event                 { slug, accessCode, name }   event the tests run against
//   ctx.rpc(name, args)       -> Promise<json>; rejects with RpcError on failure
//   ctx.anonSelect(table)     -> Promise<{ denied: boolean, reason: string }>
//   ctx.tryInternalCall(fn, args) -> Promise<{ denied: boolean, reason: string }>
//   ctx.admin                 null | { query(sql, params) -> Promise<rows[]> }
//                             (postgres-level access; tests that need it skip otherwise)
//   ctx.freshEvent            true when ctx.event was created for this run (exact ranks)

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';

export class RpcError extends Error {
  constructor({ code, detail, sqlState, httpStatus }) {
    super(`${code}${detail ? ` (${detail})` : ''}`);
    this.name = 'RpcError';
    this.code = code;          // contract error code, e.g. 'VALIDATION_FAILED:phone'
    this.detail = detail;      // human detail from DETAIL
    this.sqlState = sqlState;  // e.g. 'P0001', '42501'
    this.httpStatus = httpStatus;
  }
}

// Parameter types of the four public RPCs; used by the pglite runner to cast
// parameters exactly as PostgREST would, and by the REST runner for documentation.
export const SIGNATURES = {
  register_participant: {
    p_event_slug: 'text',
    p_access_code: 'text',
    p_station_id: 'text',
    p_client_session_id: 'uuid',
    p_first_name: 'text',
    p_last_name: 'text',
    p_title: 'text',
    p_company: 'text',
    p_location: 'text',
    p_phone: 'text',
    p_email: 'text',
    p_gender: 'text',
    p_consent_accepted: 'boolean',
    p_consent_version: 'text',
  },
  submit_result: {
    p_event_slug: 'text',
    p_access_code: 'text',
    p_station_id: 'text',
    p_submission_id: 'uuid',
    p_client_session_id: 'uuid',
    p_participant: 'jsonb',
    p_score: 'integer',
    p_completion_ms: 'integer',
    p_correct_count: 'integer',
    p_incorrect_count: 'integer',
    p_required_total: 'integer',
    p_placed_product_ids: 'text[]',
    p_gender: 'text',
    p_status: 'text',
    p_completion_reason: 'text',
    p_completed_at: 'timestamptz',
    p_client_version: 'text',
  },
  get_leaderboard: {
    p_event_slug: 'text',
    p_limit: 'integer',
  },
  ping_event: {
    p_event_slug: 'text',
    p_access_code: 'text',
  },
};

export const LEADERBOARD_KEYS = ['rank', 'display_name', 'score', 'completion_ms', 'gender', 'completed_at'];
export const TEST_STATION_ID = 'test-runner';

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function uniq() {
  return Math.random().toString(36).slice(2, 8);
}

function isoMinutesAgo(minutes) {
  return new Date(Date.now() - minutes * 60_000).toISOString();
}

// Builds a complete, valid register_participant payload (overrides win).
export function registerArgs(ctx, overrides = {}) {
  const tag = uniq();
  return {
    p_event_slug: ctx.event.slug,
    p_access_code: ctx.event.accessCode,
    p_station_id: TEST_STATION_ID,
    p_client_session_id: randomUUID(),
    p_first_name: `Test${tag}`,
    p_last_name: 'Runner',
    p_title: 'Müdür',
    p_company: 'Test A.Ş.',
    p_location: 'İstanbul / Şişli',
    p_phone: '+90 (532) 000-00-00',
    p_email: `test-${tag}@example.com`,
    p_gender: 'female',
    p_consent_accepted: true,
    p_consent_version: 'v1',
    ...overrides,
  };
}

// Builds a complete, valid submit_result payload (overrides win). By default it
// carries a participant payload so it works for an unknown client_session_id.
export function submitArgs(ctx, overrides = {}) {
  const tag = uniq();
  return {
    p_event_slug: ctx.event.slug,
    p_access_code: ctx.event.accessCode,
    p_station_id: TEST_STATION_ID,
    p_submission_id: randomUUID(),
    p_client_session_id: randomUUID(),
    p_participant: {
      first_name: `Test${tag}`,
      last_name: 'Runner',
      title: 'Müdür', company: 'Test A.Ş.', location: 'İstanbul / Şişli',
      phone: '+905320000000',
      email: `test-${tag}@example.com`,
      gender: 'male',
      consent_accepted: true,
      consent_version: 'v1',
    },
    p_score: 70,
    p_completion_ms: 90_000,
    p_correct_count: 7,
    p_incorrect_count: 0,
    p_required_total: 7,
    p_placed_product_ids: ['laptop', 'pen'],
    p_gender: 'male',
    p_status: 'completed',
    p_completion_reason: 'RequiredItemsPlaced',
    p_completed_at: isoMinutesAgo(5),
    p_client_version: '1.0.0',
    ...overrides,
  };
}

export async function expectCode(ctx, name, args, expectedCode) {
  let caught = null;
  try {
    await ctx.rpc(name, args);
  } catch (err) {
    caught = err;
  }
  assert.ok(caught, `${name} should have failed with ${expectedCode} but succeeded`);
  assert.ok(caught instanceof RpcError, `${name} failed with a non-RPC error: ${caught && caught.message}`);
  assert.equal(caught.code, expectedCode, `${name}: expected code ${expectedCode}, got ${caught.code} (${caught.detail || ''})`);
  return caught;
}

async function leaderboard(ctx, limit) {
  const args = { p_event_slug: ctx.event.slug };
  if (limit !== undefined) args.p_limit = limit;
  const rows = await ctx.rpc('get_leaderboard', args);
  assert.ok(Array.isArray(rows), 'get_leaderboard must return a JSON array');
  return rows;
}

function entriesFor(rows, firstName) {
  return rows.filter((r) => typeof r.display_name === 'string' && r.display_name.startsWith(`${firstName} `));
}

function requireAdmin(t, ctx) {
  if (!ctx.admin) {
    t.skip('needs postgres-level access (set SUPABASE_DB_URL for the REST runner)');
    return false;
  }
  return true;
}

export function registerContractTests(ctx) {
  // ---------------------------------------------------------------------------
  // ping_event
  // ---------------------------------------------------------------------------
  test('ping_event returns ok, event_name and server_time', async () => {
    const res = await ctx.rpc('ping_event', { p_event_slug: ctx.event.slug, p_access_code: ctx.event.accessCode });
    assert.equal(res.ok, true);
    assert.equal(res.event_name, ctx.event.name);
    assert.ok(typeof res.server_time === 'string' && !Number.isNaN(Date.parse(res.server_time)), 'server_time must be an ISO timestamp');
    assert.deepEqual(Object.keys(res).sort(), ['event_name', 'ok', 'server_time']);
  });

  test('ping_event rejects a wrong access code with EVENT_ACCESS_DENIED', async () => {
    await expectCode(ctx, 'ping_event', { p_event_slug: ctx.event.slug, p_access_code: `${ctx.event.accessCode}-wrong` }, 'EVENT_ACCESS_DENIED');
    await expectCode(ctx, 'ping_event', { p_event_slug: ctx.event.slug, p_access_code: '' }, 'EVENT_ACCESS_DENIED');
    await expectCode(ctx, 'ping_event', { p_event_slug: ctx.event.slug, p_access_code: null }, 'EVENT_ACCESS_DENIED');
  });

  test('ping_event rejects an unknown event slug with EVENT_ACCESS_DENIED', async () => {
    await expectCode(ctx, 'ping_event', { p_event_slug: `no-such-event-${uniq()}`, p_access_code: ctx.event.accessCode }, 'EVENT_ACCESS_DENIED');
  });

  // ---------------------------------------------------------------------------
  // register_participant
  // ---------------------------------------------------------------------------
  test('register_participant creates a participant and normalises phone, email and names', async (t) => {
    const args = registerArgs(ctx, {
      p_first_name: '  Ayşe   Nur ',
      p_last_name: ' Yılmaz ',
      p_phone: '0 (532) 123-45 67',
      p_email: '  Ayse.Nur@Example.COM ',
    });
    const res = await ctx.rpc('register_participant', args);
    assert.equal(res.created, true);
    assert.match(res.participant_id, UUID_RE);
    assert.deepEqual(Object.keys(res).sort(), ['created', 'participant_id']);

    if (!ctx.admin) {
      t.diagnostic('normalisation of stored values not verified (no postgres access)');
      return;
    }
    const rows = await ctx.admin.query(
      'select first_name, last_name, phone, email, gender, station_id, consent_accepted, consent_version from public.participants where id = $1',
      [res.participant_id],
    );
    assert.equal(rows.length, 1);
    assert.equal(rows[0].first_name, 'Ayşe Nur');
    assert.equal(rows[0].last_name, 'Yılmaz');
    assert.equal(rows[0].phone, '05321234567');
    assert.equal(rows[0].email, 'ayse.nur@example.com');
    assert.equal(rows[0].gender, 'female');
    assert.equal(rows[0].station_id, TEST_STATION_ID);
    assert.equal(rows[0].consent_accepted, true);
    assert.equal(rows[0].consent_version, 'v1');
  });

  test('register_participant keeps a leading + in phone numbers', async (t) => {
    const res = await ctx.rpc('register_participant', registerArgs(ctx, { p_phone: '+90 532 987 65 43' }));
    assert.equal(res.created, true);
    if (!requireAdmin(t, ctx)) return;
    const rows = await ctx.admin.query('select phone from public.participants where id = $1', [res.participant_id]);
    assert.equal(rows[0].phone, '+905329876543');
  });

  test('register_participant is idempotent on client_session_id', async (t) => {
    const args = registerArgs(ctx);
    const first = await ctx.rpc('register_participant', args);
    assert.equal(first.created, true);

    const again = await ctx.rpc('register_participant', args);
    assert.equal(again.created, false);
    assert.equal(again.participant_id, first.participant_id);

    // A correction with the same session id updates in place (still one row).
    const corrected = await ctx.rpc('register_participant', { ...args, p_last_name: 'Corrected' });
    assert.equal(corrected.created, false);
    assert.equal(corrected.participant_id, first.participant_id);

    if (!ctx.admin) return;
    const rows = await ctx.admin.query(
      'select count(*)::int as n, min(last_name) as last_name, bool_and(updated_at > created_at) as touched from public.participants where client_session_id = $1',
      [args.p_client_session_id],
    );
    assert.equal(rows[0].n, 1, 'exactly one participant row per client_session_id');
    assert.equal(rows[0].last_name, 'Corrected');
    assert.equal(rows[0].touched, true, 'updated_at trigger must bump updated_at on update');
  });

  const REGISTER_CASES = [
    ['first_name', { p_first_name: '   ' }],
    ['first_name', { p_first_name: null }],
    ['first_name', { p_first_name: 'x'.repeat(61) }],
    ['last_name', { p_last_name: '' }],
    ['last_name', { p_last_name: 'y'.repeat(61) }],
    ['last_name', { p_last_name: 'Bad\u0001Name' }],
    ['phone', { p_phone: '12345' }],
    ['phone', { p_phone: '+90 532 ABC 45 67' }],
    ['phone', { p_phone: '1234567890123456' }],
    ['phone', { p_phone: null }],
    ['email', { p_email: 'not-an-email' }],
    ['email', { p_email: 'a@b' }],
    ['email', { p_email: 'a@@b.com' }],
    ['email', { p_email: null }],
    ['gender', { p_gender: 'other' }],
    ['gender', { p_gender: null }],
    ['client_session_id', { p_client_session_id: null }],
    ['station_id', { p_station_id: 's'.repeat(61) }],
    ['consent_version', { p_consent_version: 'v'.repeat(65) }],
  ];

  for (const [field, overrides] of REGISTER_CASES) {
    test(`register_participant validation: ${field} (${JSON.stringify(overrides)})`, async () => {
      await expectCode(ctx, 'register_participant', registerArgs(ctx, overrides), `VALIDATION_FAILED:${field}`);
    });
  }

  test('register_participant rejects a wrong access code before validating anything', async () => {
    await expectCode(ctx, 'register_participant', registerArgs(ctx, { p_access_code: 'nope', p_phone: 'bad' }), 'EVENT_ACCESS_DENIED');
  });

  test('inactive event is rejected by every RPC with EVENT_INACTIVE', async (t) => {
    if (!requireAdmin(t, ctx)) return;
    await ctx.admin.query('update public.events set is_active = false where slug = $1', [ctx.event.slug]);
    try {
      await expectCode(ctx, 'ping_event', { p_event_slug: ctx.event.slug, p_access_code: ctx.event.accessCode }, 'EVENT_INACTIVE');
      await expectCode(ctx, 'register_participant', registerArgs(ctx), 'EVENT_INACTIVE');
      await expectCode(ctx, 'submit_result', submitArgs(ctx), 'EVENT_INACTIVE');
      await expectCode(ctx, 'get_leaderboard', { p_event_slug: ctx.event.slug }, 'EVENT_INACTIVE');
      // A wrong code on an inactive event must still look like access denied (no leak).
      await expectCode(ctx, 'ping_event', { p_event_slug: ctx.event.slug, p_access_code: 'nope' }, 'EVENT_ACCESS_DENIED');
    } finally {
      await ctx.admin.query('update public.events set is_active = true where slug = $1', [ctx.event.slug]);
    }
  });

  // ---------------------------------------------------------------------------
  // submit_result
  // ---------------------------------------------------------------------------
  test('submit_result creates a result for a registered participant and returns its rank', async () => {
    const reg = registerArgs(ctx);
    const registered = await ctx.rpc('register_participant', reg);

    const args = submitArgs(ctx, { p_client_session_id: reg.p_client_session_id, p_participant: null, p_gender: reg.p_gender, p_score: 60, p_completion_ms: 120_000 });
    const res = await ctx.rpc('submit_result', args);
    assert.deepEqual(Object.keys(res).sort(), ['created', 'participant_id', 'rank', 'result_id']);
    assert.equal(res.created, true);
    assert.equal(res.participant_id, registered.participant_id);
    assert.match(res.result_id, UUID_RE);
    assert.ok(Number.isInteger(res.rank) && res.rank >= 1, `rank must be a positive integer, got ${res.rank}`);

    const rows = await leaderboard(ctx, 1000);
    const mine = entriesFor(rows, reg.p_first_name);
    assert.equal(mine.length, 1, 'the new result must appear exactly once on the leaderboard');
    assert.equal(mine[0].score, 60);
    assert.equal(mine[0].completion_ms, 120_000);
    assert.equal(mine[0].gender, reg.p_gender);
    assert.equal(mine[0].rank, res.rank, 'rank returned by submit_result must match the leaderboard');
  });

  test('submit_result duplicate returns created=false with the same ids and never inserts twice', async (t) => {
    const args = submitArgs(ctx);
    const first = await ctx.rpc('submit_result', args);
    assert.equal(first.created, true);

    const dup = await ctx.rpc('submit_result', args);
    assert.equal(dup.created, false);
    assert.equal(dup.result_id, first.result_id);
    assert.equal(dup.participant_id, first.participant_id);
    assert.ok(Number.isInteger(dup.rank) && dup.rank >= 1);

    // A retry with a changed score but the same submission_id must still be ignored.
    const dup2 = await ctx.rpc('submit_result', { ...args, p_score: 999 });
    assert.equal(dup2.created, false);
    assert.equal(dup2.result_id, first.result_id);

    const rows = await leaderboard(ctx, 1000);
    const mine = entriesFor(rows, args.p_participant.first_name);
    assert.equal(mine.length, 1, 'the leaderboard must list the submission once');
    assert.equal(mine[0].score, args.p_score, 'the original score must be kept');

    if (!ctx.admin) {
      t.diagnostic('row count not verified directly (no postgres access)');
      return;
    }
    const count = await ctx.admin.query('select count(*)::int as n from public.results where submission_id = $1', [args.p_submission_id]);
    assert.equal(count[0].n, 1, 'results count for the submission_id must stay 1');
  });

  test('submit_result upserts the participant from p_participant when the session is unknown', async (t) => {
    const args = submitArgs(ctx, {
      p_participant: {
        firstName: 'Camel',
        lastName: 'Case',
        title: 'Mühendis',
        company: 'Camel A.Ş.',
        location: 'Ankara / Çankaya',
        phone: '0533 111 22 33',
        email: 'CAMEL@EXAMPLE.COM',
        gender: 'female',
        consentAccepted: false,
        consentVersion: 'kvkk-2026',
      },
      p_gender: 'female',
    });
    const res = await ctx.rpc('submit_result', args);
    assert.equal(res.created, true);
    assert.match(res.participant_id, UUID_RE);

    if (ctx.admin) {
      const rows = await ctx.admin.query(
        'select first_name, last_name, title, company, location, phone, email, gender, consent_accepted, consent_version, station_id from public.participants where id = $1',
        [res.participant_id],
      );
      assert.deepEqual(rows[0], {
        first_name: 'Camel',
        last_name: 'Case',
        title: 'Mühendis',
        company: 'Camel A.Ş.',
        location: 'Ankara / Çankaya',
        phone: '05331112233',
        email: 'camel@example.com',
        gender: 'female',
        consent_accepted: false,
        consent_version: 'kvkk-2026',
        station_id: TEST_STATION_ID,
      });
    }

    // Registering afterwards with the same session id is idempotent (created=false).
    const reg = await ctx.rpc('register_participant', registerArgs(ctx, {
      p_client_session_id: args.p_client_session_id,
      p_first_name: 'Camel',
      p_last_name: 'Case',
      p_phone: '05331112233',
      p_email: 'camel@example.com',
      p_gender: 'female',
      p_consent_accepted: false,
      p_consent_version: 'kvkk-2026',
    }));
    assert.equal(reg.created, false);
    assert.equal(reg.participant_id, res.participant_id);

    if (!requireAdmin(t, ctx)) return;
    const count = await ctx.admin.query('select count(*)::int as n from public.participants where client_session_id = $1', [args.p_client_session_id]);
    assert.equal(count[0].n, 1, 'still exactly one participant row');
  });

  test('submit_result without p_participant for an unknown session raises PARTICIPANT_NOT_FOUND', async () => {
    await expectCode(ctx, 'submit_result', submitArgs(ctx, { p_participant: null }), 'PARTICIPANT_NOT_FOUND');
  });

  const SUBMIT_CASES = [
    ['submission_id', { p_submission_id: null }],
    ['client_session_id', { p_client_session_id: null }],
    ['station_id', { p_station_id: 's'.repeat(61) }],
    ['gender', { p_gender: 'unknown' }],
    ['status', { p_status: 'pending' }],
    ['score', { p_score: null }],
    ['score', { p_score: 1_000_001 }],
    ['score', { p_score: -1_000_001 }],
    ['completion_ms', { p_completion_ms: 0 }],
    ['completion_ms', { p_completion_ms: -5 }],
    ['completion_ms', { p_completion_ms: 86_400_001 }],
    ['correct_count', { p_correct_count: -1 }],
    ['incorrect_count', { p_incorrect_count: -1 }],
    ['required_total', { p_required_total: 100_001 }],
    ['placed_product_ids', { p_placed_product_ids: ['laptop', ''] }],
    ['placed_product_ids', { p_placed_product_ids: ['x'.repeat(65)] }],
    ['completion_reason', { p_completion_reason: 'r'.repeat(65) }],
    ['client_version', { p_client_version: 'v'.repeat(65) }],
    ['completed_at', { p_completed_at: null }],
    ['completed_at', { p_completed_at: new Date(Date.now() + 30 * 24 * 3_600_000).toISOString() }],
    ['completed_at', { p_completed_at: '2001-01-01T00:00:00Z' }],
    ['participant', { p_participant: [] }],
    ['participant', { p_participant: 'text' }],
    ['first_name', { p_participant: { first_name: '', last_name: 'X', title: 'Müdür', company: 'Test A.Ş.', location: 'İstanbul / Şişli', phone: '05320000000', email: 'a@b.co', gender: 'male' } }],
    ['phone', { p_participant: { first_name: 'A', last_name: 'X', title: 'Müdür', company: 'Test A.Ş.', location: 'İstanbul / Şişli', phone: '1', email: 'a@b.co', gender: 'male' } }],
    ['email', { p_participant: { first_name: 'A', last_name: 'X', title: 'Müdür', company: 'Test A.Ş.', location: 'İstanbul / Şişli', phone: '05320000000', email: 'nope', gender: 'male' } }],
    ['consent_accepted', { p_participant: { first_name: 'A', last_name: 'X', title: 'Müdür', company: 'Test A.Ş.', location: 'İstanbul / Şişli', phone: '05320000000', email: 'a@b.co', gender: 'male', consent_accepted: 'maybe' } }],
  ];

  for (const [field, overrides] of SUBMIT_CASES) {
    test(`submit_result validation: ${field} (${JSON.stringify(overrides).slice(0, 80)})`, async () => {
      await expectCode(ctx, 'submit_result', submitArgs(ctx, overrides), `VALIDATION_FAILED:${field}`);
    });
  }

  test('submit_result rejects a wrong access code', async () => {
    await expectCode(ctx, 'submit_result', submitArgs(ctx, { p_access_code: 'nope' }), 'EVENT_ACCESS_DENIED');
  });

  test('abandoned results get rank=null and stay off the leaderboard', async () => {
    const args = submitArgs(ctx, { p_status: 'abandoned', p_completion_reason: 'OperatorForced', p_score: 5000 });
    const res = await ctx.rpc('submit_result', args);
    assert.equal(res.created, true);
    assert.equal(res.rank, null);

    const rows = await leaderboard(ctx, 1000);
    assert.equal(entriesFor(rows, args.p_participant.first_name).length, 0);
    assert.ok(rows.every((r) => r.score !== 5000), 'abandoned score must not leak into the leaderboard');
  });

  // ---------------------------------------------------------------------------
  // get_leaderboard
  // ---------------------------------------------------------------------------
  test('get_leaderboard orders by score desc, completion_ms asc, completed_at asc and numbers ranks 1..n', async () => {
    const base = Date.now() - 10 * 60_000;
    const tag = uniq();
    const names = { a: `Alpha${tag}`, b: `Bravo${tag}`, c: `Charlie${tag}`, d: `Delta${tag}` };
    const make = (name, score, ms, at) => submitArgs(ctx, {
      p_participant: { first_name: name, last_name: 'Order', title: 'Müdür', company: 'Test A.Ş.', location: 'İstanbul / Şişli', phone: '05320000000', email: `${name.toLowerCase()}@example.com`, gender: 'male' },
      p_score: score,
      p_completion_ms: ms,
      p_completed_at: new Date(at).toISOString(),
    });
    // Submit out of order on purpose.
    await ctx.rpc('submit_result', make(names.a, 100, 60_000, base));
    await ctx.rpc('submit_result', make(names.d, 100, 50_000, base + 2_000));
    await ctx.rpc('submit_result', make(names.c, 90, 10_000, base));
    await ctx.rpc('submit_result', make(names.b, 100, 50_000, base + 1_000));

    const rows = await leaderboard(ctx, 1000);
    rows.forEach((r, i) => assert.equal(r.rank, i + 1, 'rank must equal the array position + 1'));
    for (let i = 1; i < rows.length; i += 1) {
      const prev = rows[i - 1];
      const cur = rows[i];
      const ordered = prev.score > cur.score
        || (prev.score === cur.score && prev.completion_ms < cur.completion_ms)
        || (prev.score === cur.score && prev.completion_ms === cur.completion_ms && Date.parse(prev.completed_at) <= Date.parse(cur.completed_at));
      assert.ok(ordered, `rows ${i - 1} and ${i} violate the ranking rule`);
    }
    const idx = (name) => rows.findIndex((r) => r.display_name.startsWith(`${name} `));
    const order = [idx(names.b), idx(names.d), idx(names.a), idx(names.c)];
    assert.ok(order.every((i) => i >= 0), `all four entries must be listed: ${JSON.stringify(order)}`);
    assert.ok(order[0] < order[1] && order[1] < order[2] && order[2] < order[3], `expected B < D < A < C, got ${JSON.stringify(order)}`);
    if (ctx.freshEvent) {
      assert.deepEqual(rows.slice(order[0], order[0] + 4).map((r) => r.display_name.split(' ')[0]), [names.b, names.d, names.a, names.c]);
    }
  });

  test('get_leaderboard honours name_display_mode = first_last_initial', async (t) => {
    if (!requireAdmin(t, ctx)) return;
    const reg = registerArgs(ctx, { p_first_name: `Initial${uniq()}`, p_last_name: 'şahin' });
    await ctx.rpc('register_participant', reg);
    await ctx.rpc('submit_result', submitArgs(ctx, { p_client_session_id: reg.p_client_session_id, p_participant: null, p_gender: reg.p_gender }));

    await ctx.admin.query("update public.events set name_display_mode = 'first_last_initial' where slug = $1", [ctx.event.slug]);
    try {
      const rows = await leaderboard(ctx, 1000);
      assert.ok(rows.length > 0);
      for (const r of rows) {
        assert.match(r.display_name, /^.+ \S\.$/, `display_name "${r.display_name}" must look like "Ad S."`);
      }
      const mine = entriesFor(rows, reg.p_first_name);
      assert.equal(mine.length, 1);
      assert.equal(mine[0].display_name, `${reg.p_first_name} Ş.`);
    } finally {
      await ctx.admin.query("update public.events set name_display_mode = 'full' where slug = $1", [ctx.event.slug]);
    }
    const full = await leaderboard(ctx, 1000);
    assert.equal(entriesFor(full, reg.p_first_name)[0].display_name, `${reg.p_first_name} şahin`);
  });

  test('get_leaderboard never exposes phone or email', async () => {
    const tag = uniq();
    const phone = `+90555${tag.replace(/[^0-9]/g, '1').padEnd(7, '7')}`;
    const email = `pii-${tag}@example.com`;
    const reg = registerArgs(ctx, { p_phone: phone, p_email: email });
    await ctx.rpc('register_participant', reg);
    await ctx.rpc('submit_result', submitArgs(ctx, { p_client_session_id: reg.p_client_session_id, p_participant: null, p_gender: reg.p_gender }));

    const rows = await leaderboard(ctx, 1000);
    assert.ok(rows.length > 0);
    for (const r of rows) {
      const keys = Object.keys(r).sort();
      assert.deepEqual(keys, [...LEADERBOARD_KEYS].sort(), `unexpected keys in leaderboard entry: ${keys}`);
    }
    const serialised = JSON.stringify(rows);
    assert.ok(!serialised.includes(phone.replace('+', '')), 'phone digits must not appear in the leaderboard');
    assert.ok(!serialised.includes(email), 'email must not appear in the leaderboard');
    assert.ok(!/"(phone|email|first_name|last_name)"/.test(serialised), 'no PII keys in the leaderboard');
  });

  test('get_leaderboard validates p_limit and applies it', async () => {
    await expectCode(ctx, 'get_leaderboard', { p_event_slug: ctx.event.slug, p_limit: 0 }, 'VALIDATION_FAILED:limit');
    await expectCode(ctx, 'get_leaderboard', { p_event_slug: ctx.event.slug, p_limit: 1001 }, 'VALIDATION_FAILED:limit');
    const two = await leaderboard(ctx, 2);
    assert.ok(two.length <= 2);
    assert.deepEqual(two.map((r) => r.rank), two.map((_, i) => i + 1));
    const defaulted = await leaderboard(ctx);
    assert.ok(defaulted.length <= 100);
  });

  test('get_leaderboard rejects an unknown slug with EVENT_ACCESS_DENIED', async () => {
    await expectCode(ctx, 'get_leaderboard', { p_event_slug: `no-such-event-${uniq()}` }, 'EVENT_ACCESS_DENIED');
  });

  test('concurrent identical submits insert exactly once', async (t) => {
    const args = submitArgs(ctx);
    const results = await Promise.all(Array.from({ length: 6 }, () => ctx.rpc('submit_result', args)));
    const created = results.filter((r) => r.created === true).length;
    assert.equal(created, 1, `exactly one request must create the row, got ${created}`);
    assert.equal(new Set(results.map((r) => r.result_id)).size, 1, 'every response must carry the same result_id');
    assert.equal(new Set(results.map((r) => r.participant_id)).size, 1, 'every response must carry the same participant_id');
    if (!ctx.admin) {
      t.diagnostic('row count not verified directly (no postgres access)');
      return;
    }
    const count = await ctx.admin.query('select count(*)::int as n from public.results where submission_id = $1', [args.p_submission_id]);
    assert.equal(count[0].n, 1);
    const participants = await ctx.admin.query('select count(*)::int as n from public.participants where client_session_id = $1', [args.p_client_session_id]);
    assert.equal(participants[0].n, 1);
  });

  // ---------------------------------------------------------------------------
  // privileges
  // ---------------------------------------------------------------------------
  for (const table of ['participants', 'results', 'events', 'leaderboard_public']) {
    test(`anon cannot read ${table} directly`, async () => {
      const res = await ctx.anonSelect(table);
      assert.equal(res.denied, true, `anon must not read ${table}: ${res.reason}`);
    });
  }

  test('anon cannot execute internal helper functions', async () => {
    const res = await ctx.tryInternalCall('mt_fail', { p_code: 'X', p_detail: 'Y' });
    assert.equal(res.denied, true, `anon must not execute mt_fail: ${res.reason}`);
  });
}
