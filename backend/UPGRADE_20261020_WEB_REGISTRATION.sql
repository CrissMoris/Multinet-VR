-- MultiTravel: web kayit kuyrugu (Ekim 2026). Supabase SQL Editor'de TEK SEFERDE calistirin.
-- Mevcut veri ve fonksiyonlar degismez; sadece kuyruk alanlari ve 5 yeni fonksiyon eklenir.
-- Tekrar calistirmak zararsizdir (if not exists / create or replace).
BEGIN;

-- =============================================================================
-- MultiTravel: Packing Challenge - web registration queue (October 2026)
-- -----------------------------------------------------------------------------
-- Participants register on the registration web page (kiosk / tablet) instead of
-- the operator's form in the VR app, so no personal data is shown on the screen
-- that is mirrored to the stand TV. The web page puts a participant in the queue;
-- the VR station lists the queue (first name + last initial only) and claims the
-- next participant, then plays exactly like before (same client_session_id, so
-- register_participant / submit_result keep working unchanged).
--
-- Every new RPC needs the event access code (the web page asks the staff for it
-- once per device), so a public visitor cannot fill the queue.
-- Additive: no existing column, function or grant is changed.
-- =============================================================================

alter table public.participants add column if not exists registered_via text not null default 'station';
alter table public.participants add column if not exists queue_status   text;
alter table public.participants add column if not exists queue_station  text;
alter table public.participants add column if not exists claimed_at     timestamptz;

alter table public.participants drop constraint if exists participants_registered_via_chk;
alter table public.participants add constraint participants_registered_via_chk check (registered_via in ('station', 'web'));
alter table public.participants drop constraint if exists participants_queue_status_chk;
alter table public.participants add constraint participants_queue_status_chk
  check (queue_status is null or queue_status in ('waiting', 'claimed', 'cancelled'));

create index if not exists participants_queue_idx on public.participants (event_id, queue_status, created_at);

-- First name + last initial ("Ayşe Y."): the queue is shown on a mirrored screen.
create or replace function public.mt_queue_name(p_first_name text, p_last_name text)
returns text
language sql
immutable
set search_path = public
as $$
  select public.mt_display_name(p_first_name, p_last_name, 'first_last_initial')
$$;

-- -----------------------------------------------------------------------------
-- enqueue_participant -> { participant_id, queue_number, waiting_ahead }
-- Web registration. Consent is mandatory. client_session_id is generated here and
-- handed to the VR station when it claims the participant.
-- -----------------------------------------------------------------------------
create or replace function public.enqueue_participant(
  p_event_slug       text,
  p_access_code      text,
  p_first_name       text,
  p_last_name        text,
  p_title            text,
  p_company          text,
  p_location         text,
  p_phone            text,
  p_email            text,
  p_gender           text,
  p_consent_accepted boolean,
  p_consent_version  text
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
  v_created timestamptz;
  v_number  integer;
  v_ahead   integer;
begin
  v_event := public.mt_require_event(p_event_slug, p_access_code);

  v_input := public.mt_clean_participant(jsonb_build_object(
    'station_id',       'web',
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

  if v_input.consent_accepted is distinct from true then
    perform public.mt_fail('VALIDATION_FAILED:consent_accepted', 'consent must be accepted');
  end if;

  insert into public.participants (
    event_id, client_session_id, station_id, first_name, last_name, title, company, location, phone, email, gender,
    consent_accepted, consent_version, registered_via, queue_status)
  values (
    v_event.id, gen_random_uuid(), 'web', v_input.first_name, v_input.last_name, v_input.title, v_input.company,
    v_input.location, v_input.phone, v_input.email, v_input.gender, true, v_input.consent_version, 'web', 'waiting')
  returning id, created_at into v_id, v_created;

  select count(*) into v_number
    from public.participants p
   where p.event_id = v_event.id and p.registered_via = 'web';

  select count(*) into v_ahead
    from public.participants p
   where p.event_id = v_event.id and p.queue_status = 'waiting' and p.created_at < v_created;

  return jsonb_build_object('participant_id', v_id, 'queue_number', v_number, 'waiting_ahead', v_ahead);
end
$$;

-- -----------------------------------------------------------------------------
-- list_waiting_participants -> [{ participant_id, display_name, gender, waiting_since }]
-- Oldest first. Never returns phone / e-mail / full surname.
-- -----------------------------------------------------------------------------
create or replace function public.list_waiting_participants(
  p_event_slug  text,
  p_access_code text,
  p_limit       integer default 20
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_event public.events;
  v_limit integer;
begin
  v_event := public.mt_require_event(p_event_slug, p_access_code);
  v_limit := least(greatest(coalesce(p_limit, 20), 1), 100);

  return coalesce((
    select jsonb_agg(jsonb_build_object(
             'participant_id', q.id,
             'display_name',   public.mt_queue_name(q.first_name, q.last_name),
             'gender',         q.gender,
             'waiting_since',  q.created_at) order by q.created_at)
      from (
        select p.id, p.first_name, p.last_name, p.gender, p.created_at
          from public.participants p
         where p.event_id = v_event.id and p.queue_status = 'waiting'
         order by p.created_at
         limit v_limit) q), '[]'::jsonb);
end
$$;

-- -----------------------------------------------------------------------------
-- claim_participant -> full participant record for the VR station.
-- Only a waiting participant can be claimed (two stations never get the same one).
-- -----------------------------------------------------------------------------
create or replace function public.claim_participant(
  p_event_slug     text,
  p_access_code    text,
  p_participant_id uuid,
  p_station_id     text
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
  v_event public.events;
  v       public.participants;
begin
  v_event := public.mt_require_event(p_event_slug, p_access_code);

  update public.participants p
     set queue_status  = 'claimed',
         queue_station = public.mt_clean_optional_text(p_station_id, 'station_id', 60),
         claimed_at    = now()
   where p.id = p_participant_id
     and p.event_id = v_event.id
     and p.queue_status = 'waiting'
  returning p.* into v;

  if v.id is null then
    perform public.mt_fail('QUEUE_NOT_WAITING', 'Participant is not waiting in the queue (already taken or removed)');
  end if;

  return jsonb_build_object(
    'participant_id',    v.id,
    'client_session_id', v.client_session_id,
    'first_name',        v.first_name,
    'last_name',         v.last_name,
    'title',             v.title,
    'company',           v.company,
    'location',          v.location,
    'phone',             v.phone,
    'email',             v.email,
    'gender',            v.gender,
    'consent_accepted',  v.consent_accepted,
    'consent_version',   v.consent_version);
end
$$;

-- -----------------------------------------------------------------------------
-- release_participant -> { ok } : a claimed participant goes back to the front of
-- the queue (the operator cancelled before the game started).
-- -----------------------------------------------------------------------------
create or replace function public.release_participant(
  p_event_slug     text,
  p_access_code    text,
  p_participant_id uuid
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
  update public.participants p
     set queue_status = 'waiting', queue_station = null, claimed_at = null
   where p.id = p_participant_id and p.event_id = v_event.id and p.queue_status = 'claimed';
  return jsonb_build_object('ok', found);
end
$$;

-- -----------------------------------------------------------------------------
-- remove_from_queue -> { ok } : the participant left / registered twice.
-- The registration itself is kept (consent, contact data for the customer).
-- -----------------------------------------------------------------------------
create or replace function public.remove_from_queue(
  p_event_slug     text,
  p_access_code    text,
  p_participant_id uuid
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
  update public.participants p
     set queue_status = 'cancelled'
   where p.id = p_participant_id and p.event_id = v_event.id and p.queue_status = 'waiting';
  return jsonb_build_object('ok', found);
end
$$;

alter function public.mt_queue_name(text, text) owner to postgres;
alter function public.enqueue_participant(text, text, text, text, text, text, text, text, text, text, boolean, text) owner to postgres;
alter function public.list_waiting_participants(text, text, integer) owner to postgres;
alter function public.claim_participant(text, text, uuid, text) owner to postgres;
alter function public.release_participant(text, text, uuid) owner to postgres;
alter function public.remove_from_queue(text, text, uuid) owner to postgres;

revoke all on function public.mt_queue_name(text, text) from public, anon, authenticated;
revoke all on function public.enqueue_participant(text, text, text, text, text, text, text, text, text, text, boolean, text) from public, anon, authenticated;
revoke all on function public.list_waiting_participants(text, text, integer) from public, anon, authenticated;
revoke all on function public.claim_participant(text, text, uuid, text) from public, anon, authenticated;
revoke all on function public.release_participant(text, text, uuid) from public, anon, authenticated;
revoke all on function public.remove_from_queue(text, text, uuid) from public, anon, authenticated;

grant execute on function public.enqueue_participant(text, text, text, text, text, text, text, text, text, text, boolean, text) to anon, authenticated;
grant execute on function public.list_waiting_participants(text, text, integer) to anon, authenticated;
grant execute on function public.claim_participant(text, text, uuid, text) to anon, authenticated;
grant execute on function public.release_participant(text, text, uuid) to anon, authenticated;
grant execute on function public.remove_from_queue(text, text, uuid) to anon, authenticated;

-- PostgREST picks the new functions up after a schema reload.
notify pgrst, 'reload schema';

COMMIT;
