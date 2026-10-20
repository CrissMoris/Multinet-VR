// MultiTravel: Packing Challenge - registration page configuration.
//
// CLIENT-SAFE VALUES ONLY (served to every browser): Supabase project URL, the anon / publishable key and the event
// slug. Never the service_role key. The event ACCESS CODE is NOT stored here: the staff enters it once per device on
// the page (kept in that browser's localStorage) or opens the page once with ?kod=<code>.
// Keep this file empty in git; the deployed copy is filled from Deployment/registration.config.js (git-ignored).
window.MT_REG_CONFIG = {
  SUPABASE_URL: "",
  SUPABASE_ANON_KEY: "",
  EVENT_SLUG: "multitravel-2026",
  CONSENT_VERSION: "2026-10-kvkk-1"
};
