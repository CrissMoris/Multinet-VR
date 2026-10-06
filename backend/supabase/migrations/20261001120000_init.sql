-- =============================================================================
-- MultiTravel: Packing Challenge - backend schema, RPC contract and grants
-- -----------------------------------------------------------------------------
-- Contract: docs/ARCHITECTURE.md section 4 (Backend contract) and section 5
-- (Ranking). This file is applied with `supabase db push` (cloud) or
-- `supabase db reset` (local). It is written for Supabase Postgres 15 / 17 and
-- needs no extension (gen_random_uuid() is core since PostgreSQL 13).
--
-- Security model (summary - see backend/supabase/README.md):
--   * All tables have RLS enabled and NO policies -> no row is readable or
--     writable by anon/authenticated through PostgREST, even if a table grant
--     were ever added by mistake.
--   * anon/authenticated have NO privileges on tables, sequences or views.
--   * The only entry points are the four SECURITY DEFINER RPCs below, owned by
--     postgres, with a fixed search_path. Every RPC validates its input and
--     raises machine-readable error codes (MESSAGE = code, DETAIL = reason).
--   * get_leaderboard never exposes phone / email.
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 1. Tables
-- -----------------------------------------------------------------------------

create table public.events (
  id                uuid        primary key default gen_random_uuid(),
  slug              text        not null unique
                                check (slug ~ '^[a-z0-9][a-z0-9-]{1,62}$'),
  name              text        not null check (char_length(name) between 1 and 120),
  access_code       text        not null check (char_length(access_code) between 4 and 128),
  is_active         boolean     not null default true,
  name_display_mode text        not null default 'full'
                                check (name_display_mode in ('full', 'first_last_initial')),
  created_at        timestamptz not null default now()
);

comment on table  public.events is 'One row per event/activation. The access_code is the shared code the VR stations present; name_display_mode controls how names appear on the public leaderboard.';
comment on column public.events.access_code is 'Shared station access code (not a user secret). Change it before every event.';
comment on column public.events.name_display_mode is 'full => "Ad Soyad", first_last_initial => "Ad S."';

create table public.participants (
  id                uuid        primary key default gen_random_uuid(),
  event_id          uuid        not null references public.events (id) on delete restrict,
  client_session_id uuid        not null unique,
  station_id        text,
  first_name        text        not null check (char_length(first_name) between 1 and 60),
  last_name         text        not null check (char_length(last_name) between 1 and 60),
  title             text        check (title is null or char_length(title) between 1 and 100),
  company           text        check (company is null or char_length(company) between 1 and 100),
  location          text        check (location is null or char_length(location) between 1 and 100),
  phone             text        not null check (phone ~ '^\+?[0-9]{10,15}$'),
  email             text        not null check (char_length(email) between 3 and 254),
  gender            text        not null check (gender in ('female', 'male')),
  consent_accepted  boolean,
  consent_version   text,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now()
);

comment on table public.participants is 'Registered players. Contains PII (title, company, location, phone, email) - never exposed through anon-callable RPCs.';

create index participants_event_idx on public.participants (event_id);

create table public.results (
  id                 uuid        primary key default gen_random_uuid(),
  event_id           uuid        not null references public.events (id) on delete restrict,
  participant_id     uuid        not null references public.participants (id) on delete restrict,
  submission_id      uuid        not null unique,
  station_id         text,
  score              integer     not null,
  completion_ms      integer     not null check (completion_ms > 0),
  correct_count      integer     not null check (correct_count >= 0),
  incorrect_count    integer     not null check (incorrect_count >= 0),
  required_total     integer     not null check (required_total >= 0),
  placed_product_ids text[]      not null default '{}',
  gender             text        not null check (gender in ('female', 'male')),
  status             text        not null check (status in ('completed', 'abandoned')),
  completion_reason  text,
  completed_at       timestamptz not null,
  client_version     text,
  created_at         timestamptz not null default now()
);

comment on table public.results is 'One row per finished game. submission_id is the client-generated idempotency key.';

-- Ranking index: score desc, completion_ms asc, completed_at asc (section 5).
create index results_leaderboard_idx
  on public.results (event_id, status, score desc, completion_ms asc, completed_at asc);
create index results_participant_idx on public.results (participant_id);

-- -----------------------------------------------------------------------------
-- 2. updated_at trigger
-- -----------------------------------------------------------------------------

create or replace function public.mt_set_updated_at()
returns trigger
language plpgsql
set search_path = public
as $$
begin
  new.updated_at := now();
  return new;
end
$$;

create trigger participants_set_updated_at
  before update on public.participants
  for each row execute function public.mt_set_updated_at();

-- -----------------------------------------------------------------------------
-- 3. Row level security (defense in depth: no policies => nothing passes)
-- -----------------------------------------------------------------------------

alter table public.events       enable row level security;
alter table public.participants enable row level security;
alter table public.results      enable row level security;

-- -----------------------------------------------------------------------------
-- 4. Internal helpers (prefix mt_, NOT executable by anon/authenticated)
-- -----------------------------------------------------------------------------

-- Raises a machine-readable error. PostgREST turns it into HTTP 400 with
-- {"message": "<CODE>", "details": "<reason>", "code": "P0001"}.
create or replace function public.mt_fail(p_code text, p_detail text)
returns void
language plpgsql
set search_path = public
as $$
begin
  raise exception using message = p_code, detail = p_detail;
end
$$;

-- Trims all whitespace at both ends and collapses inner runs to one space.
create or replace function public.mt_squish(p_value text)
returns text
language sql
immutable
set search_path = public
as $$
  select regexp_replace(regexp_replace(coalesce(p_value, ''), '^\s+|\s+$', '', 'g'), '\s+', ' ', 'g')
$$;

create or replace function public.mt_clean_name(p_value text, p_field text)
returns text
language plpgsql
set search_path = public
as $$
declare
  v text;
begin
  v := public.mt_squish(p_value);
  if char_length(v) < 1 or char_length(v) > 60 or v ~ '[[:cntrl:]]' then
    perform public.mt_fail('VALIDATION_FAILED:' || p_field, p_field || ' must be 1..60 printable characters');
  end if;
  return v;
end
$$;

-- Strips spaces, dashes and parentheses; keeps an optional leading "+"; 10..15 digits.
create or replace function public.mt_clean_phone(p_value text)
returns text
language plpgsql
set search_path = public
as $$
declare
  v text;
begin
  v := regexp_replace(coalesce(p_value, ''), '[[:space:]()-]', '', 'g');
  if v !~ '^\+?[0-9]{10,15}$' then
    perform public.mt_fail('VALIDATION_FAILED:phone', 'phone must contain 10..15 digits with an optional leading +');
  end if;
  return v;
end
$$;

-- Required free text of 1..100 printable characters (title, company, location).
create or replace function public.mt_clean_profile_text(p_value text, p_field text)
returns text
language plpgsql
set search_path = public
as $$
declare
  v text;
begin
  v := public.mt_squish(p_value);
  if v is null or char_length(v) < 1 or char_length(v) > 100 or v ~ '[[:cntrl:]]' then
    perform public.mt_fail('VALIDATION_FAILED:' || p_field, p_field || ' must be 1..100 printable characters');
  end if;
  return v;
end
$$;

-- Lower-cases and applies a lightweight RFC-like pattern (local@domain.tld).
create or replace function public.mt_clean_email(p_value text)
returns text
language plpgsql
set search_path = public
as $$
declare
  v text;
begin
  v := lower(regexp_replace(coalesce(p_value, ''), '^\s+|\s+$', '', 'g'));
  if char_length(v) > 254
     or v !~ '^[a-z0-9._%+-]+@[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$' then
    perform public.mt_fail('VALIDATION_FAILED:email', 'email must look like name@domain.tld');
  end if;
  return v;
end
$$;

create or replace function public.mt_clean_gender(p_value text)
returns text
language plpgsql
set search_path = public
as $$
declare
  v text;
begin
  v := lower(public.mt_squish(p_value));
  if v not in ('female', 'male') then
    perform public.mt_fail('VALIDATION_FAILED:gender', 'gender must be female or male');
  end if;
  return v;
end
$$;

create or replace function public.mt_clean_status(p_value text)
returns text
language plpgsql
set search_path = public
as $$
declare
  v text;
begin
  v := lower(public.mt_squish(p_value));
  if v not in ('completed', 'abandoned') then
    perform public.mt_fail('VALIDATION_FAILED:status', 'status must be completed or abandoned');
  end if;
  return v;
end
$$;

-- Optional short text: blank => null, otherwise trimmed, printable, max p_max chars.
create or replace function public.mt_clean_optional_text(p_value text, p_field text, p_max integer)
returns text
language plpgsql
set search_path = public
as $$
declare
  v text;
begin
  v := public.mt_squish(p_value);
  if v = '' then
    return null;
  end if;
  if char_length(v) > p_max or v ~ '[[:cntrl:]]' then
    perform public.mt_fail('VALIDATION_FAILED:' || p_field, p_field || ' must be at most ' || p_max || ' printable characters');
  end if;
  return v;
end
$$;

-- Normalised participant payload shared by register_participant and submit_result.
create type public.mt_participant_input as (
  station_id       text,
  first_name       text,
  last_name        text,
  title            text,
  company          text,
  location         text,
  phone            text,
  email            text,
  gender           text,
  consent_accepted boolean,
  consent_version  text
);

-- Accepts snake_case keys (contract) and camelCase aliases for robustness.
create or replace function public.mt_clean_participant(p jsonb)
returns public.mt_participant_input
language plpgsql
set search_path = public
as $$
declare
  r             public.mt_participant_input;
  v_consent_raw text;
begin
  if p is null or jsonb_typeof(p) <> 'object' then
    perform public.mt_fail('VALIDATION_FAILED:participant', 'participant must be a JSON object');
  end if;

  r.station_id := public.mt_clean_optional_text(coalesce(p ->> 'station_id', p ->> 'stationId'), 'station_id', 60);
  r.first_name := public.mt_clean_name(coalesce(p ->> 'first_name', p ->> 'firstName'), 'first_name');
  r.last_name  := public.mt_clean_name(coalesce(p ->> 'last_name', p ->> 'lastName'), 'last_name');
  r.title      := public.mt_clean_profile_text(p ->> 'title', 'title');
  r.company    := public.mt_clean_profile_text(p ->> 'company', 'company');
  r.location   := public.mt_clean_profile_text(p ->> 'location', 'location');
  r.phone      := public.mt_clean_phone(p ->> 'phone');
  r.email      := public.mt_clean_email(p ->> 'email');
  r.gender     := public.mt_clean_gender(p ->> 'gender');

  v_consent_raw := coalesce(p ->> 'consent_accepted', p ->> 'consentAccepted');
  if v_consent_raw is null then
    r.consent_accepted := null;
  else
    begin
      r.consent_accepted := v_consent_raw::boolean;
    exception when others then
      perform public.mt_fail('VALIDATION_FAILED:consent_accepted', 'consent_accepted must be true, false or null');
    end;
  end if;

  r.consent_version := public.mt_clean_optional_text(coalesce(p ->> 'consent_version', p ->> 'consentVersion'), 'consent_version', 64);
  return r;
end
$$;

-- Resolves an event by slug and checks the access code and the active flag.
create or replace function public.mt_require_event(p_event_slug text, p_access_code text)
returns public.events
language plpgsql
set search_path = public
as $$
declare
  v public.events;
begin
  select e.* into v
  from public.events e
  where e.slug = lower(public.mt_squish(p_event_slug));

  if not found then
    perform public.mt_fail('EVENT_ACCESS_DENIED', 'Unknown event or wrong access code');
  end if;
  if p_access_code is null
     or public.mt_squish(p_access_code) = ''
     or public.mt_squish(p_access_code) <> v.access_code then
    perform public.mt_fail('EVENT_ACCESS_DENIED', 'Unknown event or wrong access code');
  end if;
  if not v.is_active then
    perform public.mt_fail('EVENT_INACTIVE', 'Event is not active');
  end if;
  return v;
end
$$;

-- Resolves an active event by slug only (public leaderboard, no access code).
create or replace function public.mt_require_public_event(p_event_slug text)
returns public.events
language plpgsql
set search_path = public
as $$
declare
  v public.events;
begin
  select e.* into v
  from public.events e
  where e.slug = lower(public.mt_squish(p_event_slug));

  if not found then
    perform public.mt_fail('EVENT_ACCESS_DENIED', 'Unknown event');
  end if;
  if not v.is_active then
    perform public.mt_fail('EVENT_INACTIVE', 'Event is not active');
  end if;
  return v;
end
$$;

-- Display name according to events.name_display_mode.
-- 'full' => "Ad Soyad"; 'first_last_initial' => "Ad S." (Turkish-aware initial).
create or replace function public.mt_display_name(p_first_name text, p_last_name text, p_mode text)
returns text
language sql
immutable
set search_path = public
as $$
  select case
    when p_mode = 'first_last_initial'
      then p_first_name || ' ' || upper(translate(left(p_last_name, 1), 'iışğüöçâîû', 'İIŞĞÜÖÇÂÎÛ')) || '.'
    else p_first_name || ' ' || p_last_name
  end
$$;

-- Rank of one completed result inside its event (1-based). The ordering is the
-- single source of truth for ranking: score desc, completion_ms asc,
-- completed_at asc (id asc as a final deterministic tie-break).
create or replace function public.mt_rank_of(p_event_id uuid, p_result_id uuid)
returns integer
language sql
stable
set search_path = public
as $$
  select t.rn::integer
  from (
    select r.id,
           row_number() over (order by r.score desc, r.completion_ms asc, r.completed_at asc, r.id asc) as rn
    from public.results r
    where r.event_id = p_event_id
      and r.status = 'completed'
  ) t
  where t.id = p_result_id
$$;

-- -----------------------------------------------------------------------------
-- 5. Public RPCs (the only thing anon may call)
-- -----------------------------------------------------------------------------

-- 5.1 register_participant -> { "participant_id": uuid, "created": bool }
-- Idempotent on client_session_id: a repeated call updates the mutable fields
-- (typo corrections) and returns created=false. INSERT ... ON CONFLICT DO NOTHING
-- followed by UPDATE is race-safe under concurrent identical requests.
create or replace function public.register_participant(
  p_event_slug        text,
  p_access_code       text,
  p_station_id        text,
  p_client_session_id uuid,
  p_first_name        text,
  p_last_name         text,
  p_title             text,
  p_company           text,
  p_location          text,
  p_phone             text,
  p_email             text,
  p_gender            text,
  p_consent_accepted  boolean default null,
  p_consent_version   text    default null
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_event   public.events;
  v_input   public.mt_participant_input;
  v_id      uuid;
  v_created boolean := false;
begin
  v_event := public.mt_require_event(p_event_slug, p_access_code);

  if p_client_session_id is null then
    perform public.mt_fail('VALIDATION_FAILED:client_session_id', 'client_session_id is required');
  end if;

  v_input := public.mt_clean_participant(jsonb_build_object(
    'station_id',       p_station_id,
    'first_name',       p_first_name,
    'last_name',        p_last_name,
    'title',            p_title,
    'company',          p_company,
    'location',         p_location,
    'phone',            p_phone,
    'email',            p_email,
    'gender',           p_gender,
    'consent_accepted', p_consent_accepted,
    'consent_version',  p_consent_version));

  insert into public.participants (
    event_id, client_session_id, station_id, first_name, last_name, title, company, location, phone, email, gender,
    consent_accepted, consent_version)
  values (
    v_event.id, p_client_session_id, v_input.station_id, v_input.first_name, v_input.last_name,
    v_input.title, v_input.company, v_input.location,
    v_input.phone, v_input.email, v_input.gender, v_input.consent_accepted, v_input.consent_version)
  on conflict (client_session_id) do nothing
  returning id into v_id;

  if v_id is not null then
    v_created := true;
  else
    update public.participants p
       set station_id       = v_input.station_id,
           first_name       = v_input.first_name,
           last_name        = v_input.last_name,
           title            = v_input.title,
           company          = v_input.company,
           location         = v_input.location,
           phone            = v_input.phone,
           email            = v_input.email,
           gender           = v_input.gender,
           consent_accepted = v_input.consent_accepted,
           consent_version  = v_input.consent_version
     where p.client_session_id = p_client_session_id
       and p.event_id = v_event.id
    returning p.id into v_id;

    if v_id is null then
      perform public.mt_fail('VALIDATION_FAILED:client_session_id', 'client_session_id is already registered under another event');
    end if;
  end if;

  return jsonb_build_object('participant_id', v_id, 'created', v_created);
end
$$;

-- 5.2 submit_result -> { "result_id", "participant_id", "created", "rank" }
-- Idempotent on submission_id. A duplicate (retry from the outbox) returns the
-- existing row with created=false and never inserts twice, also under concurrent
-- identical requests (INSERT ... ON CONFLICT DO NOTHING + re-select).
create or replace function public.submit_result(
  p_event_slug         text,
  p_access_code        text,
  p_station_id         text,
  p_submission_id      uuid,
  p_client_session_id  uuid,
  p_participant        jsonb,
  p_score              integer,
  p_completion_ms      integer,
  p_correct_count      integer,
  p_incorrect_count    integer,
  p_required_total     integer,
  p_placed_product_ids text[],
  p_gender             text,
  p_status             text,
  p_completion_reason  text,
  p_completed_at       timestamptz,
  p_client_version     text
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_event            public.events;
  v_input            public.mt_participant_input;
  v_station          text;
  v_gender           text;
  v_status           text;
  v_reason           text;
  v_version          text;
  v_ids              text[];
  v_participant_id   uuid;
  v_participant_evt  uuid;
  v_result_id        uuid;
  v_existing         record;
  v_created          boolean := false;
  v_rank             integer;
begin
  v_event := public.mt_require_event(p_event_slug, p_access_code);

  -- ---- input validation -----------------------------------------------------
  if p_submission_id is null then
    perform public.mt_fail('VALIDATION_FAILED:submission_id', 'submission_id is required');
  end if;
  if p_client_session_id is null then
    perform public.mt_fail('VALIDATION_FAILED:client_session_id', 'client_session_id is required');
  end if;

  v_station := public.mt_clean_optional_text(p_station_id, 'station_id', 60);
  v_gender  := public.mt_clean_gender(p_gender);
  v_status  := public.mt_clean_status(p_status);

  if p_score is null or p_score < -1000000 or p_score > 1000000 then
    perform public.mt_fail('VALIDATION_FAILED:score', 'score must be between -1000000 and 1000000');
  end if;
  if p_completion_ms is null or p_completion_ms < 1 or p_completion_ms > 86400000 then
    perform public.mt_fail('VALIDATION_FAILED:completion_ms', 'completion_ms must be between 1 and 86400000 (24h)');
  end if;
  if p_correct_count is null or p_correct_count < 0 or p_correct_count > 100000 then
    perform public.mt_fail('VALIDATION_FAILED:correct_count', 'correct_count must be between 0 and 100000');
  end if;
  if p_incorrect_count is null or p_incorrect_count < 0 or p_incorrect_count > 100000 then
    perform public.mt_fail('VALIDATION_FAILED:incorrect_count', 'incorrect_count must be between 0 and 100000');
  end if;
  if p_required_total is null or p_required_total < 0 or p_required_total > 100000 then
    perform public.mt_fail('VALIDATION_FAILED:required_total', 'required_total must be between 0 and 100000');
  end if;

  v_ids := coalesce(p_placed_product_ids, '{}'::text[]);
  if coalesce(array_ndims(v_ids), 1) <> 1
     or cardinality(v_ids) > 1000
     or exists (
       select 1 from unnest(v_ids) as u(product_id)
       where u.product_id is null
          or char_length(u.product_id) < 1
          or char_length(u.product_id) > 64
          or u.product_id ~ '[[:cntrl:]]') then
    perform public.mt_fail('VALIDATION_FAILED:placed_product_ids', 'placed_product_ids must be a flat array of at most 1000 ids (1..64 chars each)');
  end if;

  v_reason  := public.mt_clean_optional_text(p_completion_reason, 'completion_reason', 64);
  v_version := public.mt_clean_optional_text(p_client_version, 'client_version', 64);

  if p_completed_at is null
     or p_completed_at < timestamptz '2020-01-01 00:00:00+00'
     or p_completed_at > now() + interval '7 days' then
    perform public.mt_fail('VALIDATION_FAILED:completed_at', 'completed_at must be a plausible timestamp (not null, not far in the future)');
  end if;

  -- ---- fast path: duplicate submission -------------------------------------
  select r.id, r.participant_id, r.event_id, r.status
    into v_existing
    from public.results r
   where r.submission_id = p_submission_id;

  if found then
    if v_existing.event_id <> v_event.id then
      perform public.mt_fail('VALIDATION_FAILED:submission_id', 'submission_id belongs to another event');
    end if;
    v_rank := case when v_existing.status = 'completed'
                   then public.mt_rank_of(v_event.id, v_existing.id) else null end;
    return jsonb_build_object(
      'result_id',      v_existing.id,
      'participant_id', v_existing.participant_id,
      'created',        false,
      'rank',           v_rank);
  end if;

  -- ---- participant: find or upsert from p_participant ------------------------
  select p.id, p.event_id
    into v_participant_id, v_participant_evt
    from public.participants p
   where p.client_session_id = p_client_session_id;

  if v_participant_id is null then
    if p_participant is null then
      perform public.mt_fail('PARTICIPANT_NOT_FOUND', 'No participant for client_session_id and no participant payload was supplied');
    end if;

    v_input := public.mt_clean_participant(
      p_participant
      || jsonb_build_object(
           'station_id', coalesce(p_participant ->> 'station_id', p_participant ->> 'stationId', v_station),
           'gender',     coalesce(p_participant ->> 'gender', v_gender)));

    insert into public.participants (
      event_id, client_session_id, station_id, first_name, last_name, title, company, location, phone, email, gender,
      consent_accepted, consent_version)
    values (
      v_event.id, p_client_session_id, v_input.station_id, v_input.first_name, v_input.last_name,
      v_input.title, v_input.company, v_input.location,
      v_input.phone, v_input.email, v_input.gender, v_input.consent_accepted, v_input.consent_version)
    on conflict (client_session_id) do nothing
    returning id into v_participant_id;

    if v_participant_id is null then
      -- Lost a race against an identical request: re-select the winner's row.
      select p.id, p.event_id
        into v_participant_id, v_participant_evt
        from public.participants p
       where p.client_session_id = p_client_session_id;
    else
      v_participant_evt := v_event.id;
    end if;
  end if;

  if v_participant_evt <> v_event.id then
    perform public.mt_fail('VALIDATION_FAILED:client_session_id', 'client_session_id belongs to another event');
  end if;

  -- ---- result: insert once ---------------------------------------------------
  insert into public.results (
    event_id, participant_id, submission_id, station_id, score, completion_ms,
    correct_count, incorrect_count, required_total, placed_product_ids, gender, status,
    completion_reason, completed_at, client_version)
  values (
    v_event.id, v_participant_id, p_submission_id, v_station, p_score, p_completion_ms,
    p_correct_count, p_incorrect_count, p_required_total, v_ids, v_gender, v_status,
    v_reason, p_completed_at, v_version)
  on conflict (submission_id) do nothing
  returning id into v_result_id;

  if v_result_id is not null then
    v_created := true;
  else
    -- Lost a race against an identical request: return the winner's row.
    select r.id, r.participant_id, r.event_id, r.status
      into v_existing
      from public.results r
     where r.submission_id = p_submission_id;
    if v_existing.event_id <> v_event.id then
      perform public.mt_fail('VALIDATION_FAILED:submission_id', 'submission_id belongs to another event');
    end if;
    v_result_id      := v_existing.id;
    v_participant_id := v_existing.participant_id;
    v_status         := v_existing.status;
  end if;

  v_rank := case when v_status = 'completed'
                 then public.mt_rank_of(v_event.id, v_result_id) else null end;

  return jsonb_build_object(
    'result_id',      v_result_id,
    'participant_id', v_participant_id,
    'created',        v_created,
    'rank',           v_rank);
end
$$;

-- 5.3 get_leaderboard -> [ { rank, display_name, score, completion_ms, gender, completed_at } ]
-- Public (no access code), active events only, completed results only, never PII.
create or replace function public.get_leaderboard(
  p_event_slug text,
  p_limit      integer default 100
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_event public.events;
  v_limit integer;
  v_rows  jsonb;
begin
  v_event := public.mt_require_public_event(p_event_slug);

  v_limit := coalesce(p_limit, 100);
  if v_limit < 1 or v_limit > 1000 then
    perform public.mt_fail('VALIDATION_FAILED:limit', 'limit must be between 1 and 1000');
  end if;

  select coalesce(jsonb_agg(jsonb_build_object(
           'rank',          t.rn,
           'display_name',  public.mt_display_name(t.first_name, t.last_name, v_event.name_display_mode),
           'score',         t.score,
           'completion_ms', t.completion_ms,
           'gender',        t.gender,
           'completed_at',  t.completed_at) order by t.rn), '[]'::jsonb)
    into v_rows
    from (
      select row_number() over (order by r.score desc, r.completion_ms asc, r.completed_at asc, r.id asc)::integer as rn,
             p.first_name, p.last_name, r.score, r.completion_ms, r.gender, r.completed_at
        from public.results r
        join public.participants p on p.id = r.participant_id
       where r.event_id = v_event.id
         and r.status = 'completed'
       order by r.score desc, r.completion_ms asc, r.completed_at asc, r.id asc
       limit v_limit
    ) t;

  return v_rows;
end
$$;

-- 5.4 ping_event -> { "ok": true, "event_name": text, "server_time": timestamptz }
create or replace function public.ping_event(
  p_event_slug  text,
  p_access_code text
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_event public.events;
begin
  v_event := public.mt_require_event(p_event_slug, p_access_code);
  return jsonb_build_object(
    'ok',          true,
    'event_name',  v_event.name,
    'server_time', now());
end
$$;

comment on function public.register_participant(text, text, text, uuid, text, text, text, text, text, text, text, text, boolean, text)
  is 'RPC: registers a participant (idempotent on client_session_id). Requires event slug + access code.';
comment on function public.submit_result(text, text, text, uuid, uuid, jsonb, integer, integer, integer, integer, integer, text[], text, text, text, timestamptz, text)
  is 'RPC: stores a game result (idempotent on submission_id) and returns the current rank.';
comment on function public.get_leaderboard(text, integer)
  is 'RPC: public leaderboard for an active event. Never returns phone/email.';
comment on function public.ping_event(text, text)
  is 'RPC: health/credential check for the operator status bar.';

-- -----------------------------------------------------------------------------
-- 6. Studio convenience view (PII-free). Owner = postgres, security_invoker=false
--    so Studio users see every event; anon/authenticated get NO select on it.
-- -----------------------------------------------------------------------------

create view public.leaderboard_public
with (security_invoker = false)
as
select e.slug                                   as event_slug,
       e.name                                   as event_name,
       row_number() over (
         partition by r.event_id
         order by r.score desc, r.completion_ms asc, r.completed_at asc, r.id asc)::integer as rank,
       public.mt_display_name(p.first_name, p.last_name, e.name_display_mode) as display_name,
       r.score,
       r.completion_ms,
       r.gender,
       r.completed_at,
       r.station_id,
       r.id                                     as result_id,
       r.participant_id
  from public.results r
  join public.participants p on p.id = r.participant_id
  join public.events       e on e.id = r.event_id
 where r.status = 'completed';

comment on view public.leaderboard_public is 'PII-free leaderboard for Supabase Studio dashboards. Not accessible to anon (RPC only).';

-- -----------------------------------------------------------------------------
-- 7. Ownership and privileges
-- -----------------------------------------------------------------------------

alter function public.mt_set_updated_at()                                   owner to postgres;
alter function public.mt_fail(text, text)                                   owner to postgres;
alter function public.mt_squish(text)                                       owner to postgres;
alter function public.mt_clean_name(text, text)                             owner to postgres;
alter function public.mt_clean_phone(text)                                  owner to postgres;
alter function public.mt_clean_profile_text(text, text)                     owner to postgres;
alter function public.mt_clean_email(text)                                  owner to postgres;
alter function public.mt_clean_gender(text)                                 owner to postgres;
alter function public.mt_clean_status(text)                                 owner to postgres;
alter function public.mt_clean_optional_text(text, text, integer)           owner to postgres;
alter function public.mt_clean_participant(jsonb)                           owner to postgres;
alter function public.mt_require_event(text, text)                          owner to postgres;
alter function public.mt_require_public_event(text)                         owner to postgres;
alter function public.mt_display_name(text, text, text)                     owner to postgres;
alter function public.mt_rank_of(uuid, uuid)                                owner to postgres;
alter function public.register_participant(text, text, text, uuid, text, text, text, text, text, text, text, text, boolean, text) owner to postgres;
alter function public.submit_result(text, text, text, uuid, uuid, jsonb, integer, integer, integer, integer, integer, text[], text, text, text, timestamptz, text) owner to postgres;
alter function public.get_leaderboard(text, integer)                        owner to postgres;
alter function public.ping_event(text, text)                                owner to postgres;
alter view     public.leaderboard_public                                    owner to postgres;

-- Supabase default privileges grant anon/authenticated access to new objects in
-- public; revoke everything created above, then grant exactly the four RPCs.
revoke all on public.events, public.participants, public.results, public.leaderboard_public from public, anon, authenticated;
-- Limit revocation to this application's functions; preserve other applications
-- that may share the public schema. UUID keys do not create sequences.
do $$
declare f regprocedure;
begin
  for f in
    select p.oid::regprocedure from pg_proc p
    join pg_namespace n on n.oid = p.pronamespace
    where n.nspname = 'public' and p.proname = any(array[
      'mt_set_updated_at','mt_fail','mt_squish','mt_clean_name','mt_clean_phone','mt_clean_profile_text',
      'mt_clean_email','mt_clean_gender','mt_clean_status','mt_clean_optional_text',
      'mt_clean_participant','mt_require_event','mt_require_public_event',
      'mt_display_name','mt_rank_of','register_participant','submit_result',
      'get_leaderboard','ping_event'])
  loop
    execute format('revoke all on function %s from public, anon, authenticated', f);
  end loop;
end;
$$;

grant usage on schema public to anon, authenticated;

grant execute on function public.register_participant(text, text, text, uuid, text, text, text, text, text, text, text, text, boolean, text) to anon, authenticated;
grant execute on function public.submit_result(text, text, text, uuid, uuid, jsonb, integer, integer, integer, integer, integer, text[], text, text, text, timestamptz, text) to anon, authenticated;
grant execute on function public.get_leaderboard(text, integer) to anon, authenticated;
grant execute on function public.ping_event(text, text) to anon, authenticated;
