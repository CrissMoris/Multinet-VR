-- =============================================================================
-- Seed: default event row (local development and first deploy)
-- -----------------------------------------------------------------------------
-- !! PLACEHOLDER - the event team MUST change the access code before the event:
--
--   update public.events
--      set access_code = '<new-code>'
--    where slug = 'multitravel-2026';
--
-- The same code must be entered in the VR stations' AppConfig / runtime JSON
-- (EventAccessCode). See backend/supabase/README.md, "Set the event access code".
--
-- Applied automatically by `supabase db reset` (local). For a cloud project run
-- it once in the Studio SQL editor or with `supabase db query -f seed.sql`.
-- Safe to re-run: ON CONFLICT DO NOTHING.
-- =============================================================================

insert into public.events (slug, name, access_code, is_active, name_display_mode)
values ('multitravel-2026', 'MultiTravel Valiz Challenge', 'CHANGE-ME-BEFORE-EVENT', true, 'full')
on conflict (slug) do nothing;
