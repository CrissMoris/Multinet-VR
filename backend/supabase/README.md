# MultiTravel Valiz Challenge — backend (Supabase)

The backend is a Postgres schema plus four PostgreSQL functions exposed through
Supabase's PostgREST as RPCs. The VR stations (Unity) and the public leaderboard
page only ever talk to those four functions with the **anon** key. There is no
Edge Function, no auth user, no direct table access.

```
backend/supabase/
  config.toml                 local stack settings (supabase init defaults, Postgres 17)
  migrations/
    20261001120000_init.sql   schema, helpers, RPCs, view, RLS, grants
  seed.sql                    default event row (placeholder access code - change it!)
  tests/
    package.json              npm test (in-memory Postgres) / npm run test:rest (HTTP)
    pglite.test.mjs           runner (a): @electric-sql/pglite, no Docker needed
    rest.test.mjs             runner (b): PostgREST over HTTP against a running stack
    run.mjs                   runs (a) then (b) when SUPABASE_URL/ANON_KEY are set
    lib/contract.mjs          the shared assertions
    fixtures.mjs              test configuration (event slug / access code defaults)
```

Contract reference: `docs/ARCHITECTURE.md` §4 (backend) and §5 (ranking).

---

## 1. Prerequisites

- [Supabase CLI](https://supabase.com/docs/guides/cli) ≥ 2.75 (`supabase --version`)
- Node.js ≥ 20 (for the tests)
- Docker Desktop only for the *local* stack (`supabase start`); not needed to deploy

All CLI commands below are run from `backend/` (the folder that contains
`supabase/`), or pass `--workdir backend` from the repository root.

## 2. Local development (optional, needs Docker)

```sh
cd backend
supabase start                 # first run pulls images (several minutes)
supabase db reset              # (re)applies migrations + seed.sql
supabase status                # prints API URL, DB URL and the local keys
```

Studio: <http://127.0.0.1:54323>. The local API is <http://127.0.0.1:54321>.

Run the tests:

```sh
cd backend/supabase/tests
npm install
npm test                                   # (a) in-memory Postgres, ~1 s, no Docker
# (b) over HTTP - copy the values printed by `supabase status -o env`:
SUPABASE_URL=http://127.0.0.1:54321 SUPABASE_ANON_KEY=<anon key> SUPABASE_DB_URL=<db url> npm run test:rest
```

On PowerShell set the variables with `$env:SUPABASE_URL = "..."` before `npm run test:rest`.

`SUPABASE_DB_URL` is optional: with it the REST runner creates a throw-away
event, checks rows at the SQL level and deletes everything it wrote. Without it
the runner uses the event from `MT_EVENT_SLUG` / `MT_EVENT_ACCESS_CODE`
(defaults mirror `seed.sql`) and leaves rows tagged `station_id = 'test-runner'`
behind — see §7 for the clean-up query. Never point the REST runner at the
production project during the event.

## 3. Deploy to a Supabase project

1. Create the project in the Supabase dashboard (region: Frankfurt `eu-central-1`
   is closest to Turkey). Note the **Project ref** (Project Settings → General).
2. Log in and link the local folder to the project (one time):

   ```sh
   cd backend
   supabase login
   supabase link --project-ref <project-ref>
   ```

   `link` asks for the database password you chose when creating the project.
   It is stored by the CLI only; never write it into the repository.
3. Push the migrations:

   ```sh
   supabase db push
   supabase migration list        # the init migration must show as applied remotely
   ```

4. Seed the default event: open **Studio → SQL Editor**, paste the content of
   `seed.sql` and run it (it is idempotent). Alternatively, with `psql`
   installed: `psql "<connection string from Dashboard → Connect>" -f supabase/seed.sql`.
5. Change the access code (§5) and verify with the REST runner against the new
   project, using a throw-away event (set `SUPABASE_DB_URL` to the project's
   connection string, run the tests, the runner deletes its event afterwards).

Re-deploying after a schema change: add a new file to `migrations/` (never edit
an applied one) and run `supabase db push` again.

## 4. Where to find the keys

Dashboard → **Project Settings → API**:

- **Project URL** → `SupabaseUrl` (Unity `AppConfig`) and `SUPABASE_URL` (leaderboard `config.js`).
- **anon / public key** (or the newer `sb_publishable_…` key) → `SupabaseAnonKey` / `SUPABASE_ANON_KEY`.
  This key is meant to be shipped to clients; with this schema it can only call
  the four RPCs.
- **service_role key**: never copy it anywhere in this repository, in the Unity
  project or on the leaderboard machine. It bypasses every protection and is
  not needed for anything the event team does (Studio is enough).

## 5. Configure the event

All event settings live in the `events` table; nothing is hard-coded in the
clients. Run these in **Studio → SQL Editor**.

Set the access code (do this before every event; the same code goes into the
stations' `AppConfig.EventAccessCode` / runtime JSON):

```sql
update public.events
   set access_code = 'MT26-VALIZ-7731'      -- choose your own; 4..128 characters
 where slug = 'multitravel-2026';
```

Create an additional event (e.g. a second city):

```sql
insert into public.events (slug, name, access_code, is_active, name_display_mode)
values ('multitravel-2026-ankara', 'MultiTravel Valiz Challenge - Ankara', 'ANK-2026-XYZ', true, 'full');
```

Name display on the public leaderboard (`full` = "Ad Soyad", `first_last_initial` = "Ad S."):

```sql
update public.events set name_display_mode = 'first_last_initial' where slug = 'multitravel-2026';
```

Pause / disable an event (stations get `EVENT_INACTIVE`, the leaderboard shows
"Etkinlik şu anda aktif değil", all data stays):

```sql
update public.events set is_active = false where slug = 'multitravel-2026';
-- and back:
update public.events set is_active = true  where slug = 'multitravel-2026';
```

Check what is configured:

```sql
select slug, name, is_active, name_display_mode, access_code from public.events;
```

## 6. Export the results for the customer (CSV with PII)

Studio → SQL Editor → run the query → **Download CSV** (button under the result grid).

```sql
-- Ranked, completed games with contact details (one row per game)
select r.rank,
       p.first_name, p.last_name, p.phone, p.email,
       res.gender, res.score, res.completion_ms,
       to_char((res.completion_ms || ' milliseconds')::interval, 'MI:SS.MS') as completion_time,
       res.correct_count, res.incorrect_count, res.required_total,
       array_to_string(res.placed_product_ids, ';') as placed_product_ids,
       res.completion_reason, res.station_id,
       res.completed_at at time zone 'Europe/Istanbul' as completed_at_istanbul,
       p.consent_accepted, p.consent_version,
       res.id as result_id, p.client_session_id
  from public.leaderboard_public r
  join public.results      res on res.id = r.result_id
  join public.participants p   on p.id  = r.participant_id
 where r.event_slug = 'multitravel-2026'
 order by r.rank;
```

Everyone who registered (including people who never finished a game):

```sql
select p.first_name, p.last_name, p.phone, p.email, p.gender,
       p.consent_accepted, p.consent_version, p.station_id,
       p.created_at at time zone 'Europe/Istanbul' as registered_at_istanbul,
       (select count(*) from public.results r where r.participant_id = p.id and r.status = 'completed') as completed_games
  from public.participants p
  join public.events e on e.id = p.event_id
 where e.slug = 'multitravel-2026'
 order by p.created_at;
```

`leaderboard_public` is a PII-free view for dashboards (Studio → Table Editor →
Views). It is only readable by database users, not by the anon key.

## 7. Operations cheat sheet

```sql
-- Live counts for an event
select count(*) filter (where status = 'completed') as completed,
       count(*) filter (where status = 'abandoned') as abandoned
  from public.results r join public.events e on e.id = r.event_id
 where e.slug = 'multitravel-2026';

-- Remove rows written by the REST test runner (never run against live event data)
delete from public.results      where station_id = 'test-runner';
delete from public.participants where station_id = 'test-runner';

-- Delete a single wrong result (keeps the participant)
delete from public.results where id = '<result uuid>';
```

Backups: Supabase keeps daily backups on paid plans; before deleting anything,
export the CSVs from §6.

## 8. RPC reference

Base URL `{SUPABASE_URL}/rest/v1/rpc/{function}`, `POST`, headers
`apikey: <anon key>`, `Authorization: Bearer <anon key>`,
`Content-Type: application/json`. The body is a JSON object whose keys are the
parameter names below (unknown keys make PostgREST answer 404 "function not found").

| Function | Parameters | Returns |
|---|---|---|
| `register_participant` | `p_event_slug`, `p_access_code`, `p_station_id`, `p_client_session_id` (uuid), `p_first_name`, `p_last_name`, `p_phone`, `p_email`, `p_gender` (`female`/`male`), `p_consent_accepted` (bool/null), `p_consent_version` (text/null) | `{ "participant_id": uuid, "created": bool }` — idempotent on `p_client_session_id` (a repeat updates the fields and returns `created=false`) |
| `submit_result` | `p_event_slug`, `p_access_code`, `p_station_id`, `p_submission_id` (uuid), `p_client_session_id` (uuid), `p_participant` (object with the register fields, or null), `p_score`, `p_completion_ms`, `p_correct_count`, `p_incorrect_count`, `p_required_total`, `p_placed_product_ids` (string[]), `p_gender`, `p_status` (`completed`/`abandoned`), `p_completion_reason`, `p_completed_at` (ISO 8601), `p_client_version` | `{ "result_id": uuid, "participant_id": uuid, "created": bool, "rank": int\|null }` — idempotent on `p_submission_id` (duplicates return the existing row with `created=false`, never a second insert, also under concurrent retries) |
| `get_leaderboard` | `p_event_slug`, `p_limit` (1..1000, default 100) | `[{ "rank", "display_name", "score", "completion_ms", "gender", "completed_at" }]` — no access code needed, active events only, `completed` results only, never phone/e-mail |
| `ping_event` | `p_event_slug`, `p_access_code` | `{ "ok": true, "event_name": text, "server_time": timestamptz }` |

Ranking (one place, SQL): `score desc, completion_ms asc, completed_at asc`
(`id` as the final deterministic tie-break). `rank` is the row number in that order.

Validation / normalisation performed server-side:

- names: trimmed, inner whitespace collapsed, 1..60 printable characters
- phone: spaces, dashes and parentheses removed, optional leading `+`, 10..15 digits
- e-mail: trimmed, lower-cased, `local@domain.tld` pattern, ≤ 254 characters
- gender `female`/`male`; status `completed`/`abandoned`
- `score` −1 000 000..1 000 000, `completion_ms` 1..86 400 000, counts 0..100 000,
  `placed_product_ids` ≤ 1000 ids of 1..64 chars, `completed_at` plausible (2020 .. now+7 days)
- `station_id`, `consent_version`, `completion_reason`, `client_version`: optional, ≤ 60/64 chars

Errors come back as HTTP 400 with `{"code":"P0001","message":"<CODE>","details":"<reason>"}`:

| `message` | Meaning |
|---|---|
| `EVENT_ACCESS_DENIED` | unknown slug or wrong/missing access code (also used by `get_leaderboard` for an unknown slug) |
| `EVENT_INACTIVE` | the event exists and the code is right, but `is_active = false` |
| `VALIDATION_FAILED:<field>` | the named parameter failed validation (`details` says why) |
| `PARTICIPANT_NOT_FOUND` | `submit_result` without `p_participant` for an unknown `p_client_session_id` |

Clients map these to Turkish messages; anything else (HTTP 5xx, timeouts, 401 for a wrong key) is "Sunucuya ulaşılamadı".

## 9. Security model

**Why anon has only RPC access.** The anon key is public by design (it ships in
the Unity build and in the leaderboard page). With this migration:

- `REVOKE ALL` on every table, sequence and view in `public` from `anon` and
  `authenticated`; they can `SELECT`/`INSERT` nothing directly, and the PII-free
  `leaderboard_public` view is not readable by them either.
- Row Level Security is enabled on all tables with **no** policies — defense in
  depth: even if someone later grants a table privilege by mistake, RLS still
  blocks every row for anon.
- The four RPCs are `SECURITY DEFINER`, owned by `postgres`, with
  `SET search_path = public` pinned and every object reference schema-qualified,
  so they cannot be hijacked through the search path. Internal helpers (`mt_*`)
  are not executable by anon.
- Every write is validated and idempotent (`client_session_id`,
  `submission_id`), so retries from the station's outbox cannot duplicate rows,
  and malformed data cannot enter the tables.
- `get_leaderboard` is the only read path for anon and it exposes exactly
  rank, display name (according to `name_display_mode`), score, time, gender and
  completion time. Phone and e-mail never leave the database through the API.

**What the access code is.** A shared event code (`events.access_code`) that
every station presents on each write/ping. It stops someone who merely knows
the project URL and anon key from writing fake results or probing `ping_event`.
Change it before every event and keep it out of public channels.

**What the access code is not.** It is not a secret in the cryptographic sense:
it is stored in plain text in the stations' configuration and compared in the
function. It does not protect reads (the leaderboard is public on purpose), and
it does not identify users. Treat the combination "anon key + access code" as
"can submit a score", nothing more; PII protection does not depend on it.

Studio's database linter flags `leaderboard_public` as a "security definer
view": that is intentional (it is for Studio users, and anon has no privilege
on it). The `postgres` role in Supabase is not a superuser but owns the schema
and bypasses RLS, which is what the definer functions rely on.
