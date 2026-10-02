// Test configuration (data, not code): which event the runners talk to.
//
// The defaults mirror backend/supabase/seed.sql so a freshly seeded database
// works out of the box. Override with environment variables when the target
// project uses a different slug / access code:
//
//   MT_EVENT_SLUG          event slug (default: the seeded slug)
//   MT_EVENT_ACCESS_CODE   station access code of that event
//
// The pglite runner ignores these when the seed is present (it reads the seeded
// row straight from its in-memory database); the REST runner needs them unless
// SUPABASE_DB_URL is set (then it creates a throw-away event for the run).

export const SEEDED_EVENT = Object.freeze({
  slug: process.env.MT_EVENT_SLUG || 'multitravel-2026',
  accessCode: process.env.MT_EVENT_ACCESS_CODE || 'CHANGE-ME-BEFORE-EVENT',
  name: 'MultiTravel Valiz Challenge',
});

// Shape of the throw-away event the runners create when they have postgres access.
export function throwawayEvent(tag) {
  return {
    slug: `mt-test-${tag}`,
    accessCode: `test-code-${tag}`,
    name: `MultiTravel test ${tag}`,
  };
}
