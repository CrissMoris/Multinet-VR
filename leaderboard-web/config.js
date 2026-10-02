// MultiTravel Valiz Challenge - public leaderboard configuration.
//
// CLIENT-SAFE VALUES ONLY. This file is served to every browser that opens the
// leaderboard, so it may contain the Supabase project URL and the *anon*
// (public) key - never the service_role key, never a database password.
// The anon key can only call the four public RPCs (see backend/supabase/README.md).
//
//   SUPABASE_URL      e.g. "https://abcdefghijklmnop.supabase.co" (Dashboard > Project Settings > API)
//   SUPABASE_ANON_KEY the anon / publishable key of the same project
//   EVENT_SLUG        slug of the active event in the `events` table, e.g. "multitravel-2026"
//                     (can be overridden per screen with  index.html?event=<slug>)
//   REFRESH_SECONDS   auto-refresh interval (3..3600)
//   TOP_N             number of rows to show (1..1000)
//
// Tip: keep this file with empty strings in git and put the real values in
// config.local.js (same shape, git-ignored); index.html loads it when present:
//   window.MT_CONFIG = Object.assign(window.MT_CONFIG || {}, { SUPABASE_URL: "...", SUPABASE_ANON_KEY: "...", EVENT_SLUG: "..." });

window.MT_CONFIG = {
  SUPABASE_URL: "",
  SUPABASE_ANON_KEY: "",
  EVENT_SLUG: "",
  REFRESH_SECONDS: 15,
  TOP_N: 20
};
