/** The last camera and searched area per map search, kept for the app session (no imports, so sign-out can clear it cheaply). */
export const mapSearchSessions = new Map();

/** Forget every map search's camera and area (sign-out and tests). */
export function resetMapSearchSessions() {
  mapSearchSessions.clear();
}
