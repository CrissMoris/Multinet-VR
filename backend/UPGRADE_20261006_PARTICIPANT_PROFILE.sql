-- MultiTravel: Packing Challenge - mevcut (kurulu) veritabanini yeni katilimci alanlariyla gunceller.
-- Eklenenler: Unvan (title), Kurum (company), Sirket Lokasyonu Il/Ilce (location).
-- Supabase SQL Editor'de TEK SEFERDE calistirin. Mevcut veri silinmez; eski kayitlarda yeni alanlar bos kalir.
BEGIN;

alter table public.participants add column if not exists title    text;
alter table public.participants add column if not exists company  text;
alter table public.participants add column if not exists location text;
alter table public.participants drop constraint if exists participants_title_len;
alter table public.participants drop constraint if exists participants_company_len;
alter table public.participants drop constraint if exists participants_location_len;
alter table public.participants add constraint participants_title_len    check (title    is null or char_length(title)    between 1 and 100);
alter table public.participants add constraint participants_company_len  check (company  is null or char_length(company)  between 1 and 100);
alter table public.participants add constraint participants_location_len check (location is null or char_length(location) between 1 and 100);

-- Composite type used by the participant parser (attributes are added once).
do $$
begin
  if not exists (
    select 1 from pg_attribute a
    join pg_class c on c.oid = a.attrelid
    join pg_namespace n on n.oid = c.relnamespace
    where n.nspname = 'public' and c.relname = 'mt_participant_input' and a.attname = 'title' and not a.attisdropped) then
    alter type public.mt_participant_input add attribute title text, add attribute company text, add attribute location text;
  end if;
end
$$;

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

-- Eski imza (telefon/e-posta oncesi alan yok) kaldirilir; yenisi asagida olusturulur.
drop function if exists public.register_participant(text, text, text, uuid, text, text, text, text, text, boolean, text);

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

alter function public.mt_clean_profile_text(text, text) owner to postgres;
alter function public.register_participant(text, text, text, uuid, text, text, text, text, text, text, text, text, boolean, text) owner to postgres;
revoke all on function public.mt_clean_profile_text(text, text) from public, anon, authenticated;
revoke all on function public.register_participant(text, text, text, uuid, text, text, text, text, text, text, text, text, boolean, text) from public, anon, authenticated;
grant execute on function public.register_participant(text, text, text, uuid, text, text, text, text, text, text, text, text, boolean, text) to anon, authenticated;

NOTIFY pgrst, 'reload schema';
COMMIT;
