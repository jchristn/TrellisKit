// Offline checks of a seed file: unique keys, member, asset, and object references.

const MEMBER_FIELDS = new Set(['author', 'from', 'to', 'member', 'by', 'requester', 'assignee', 'reviewer', 'submitter', 'sender', 'proposer']);
const MEMBER_LIST_FIELDS = new Set(['hosts', 'going', 'interested', 'voters', 'members', 'participants', 'followers']);

export function validateSeed(seed) {
  const problems = [];
  if (seed.schemaVersion !== 1) problems.push('schemaVersion must be 1');
  const memberKeys = new Set();
  for (const m of seed.members ?? []) {
    if (!m.key) problems.push('a member has no key');
    if (memberKeys.has(m.key)) problems.push(`duplicate member key ${m.key}`);
    memberKeys.add(m.key);
    if (!m.displayName || !m.handle) problems.push(`member ${m.key}: displayName and handle are required`);
    if (m.membership?.type === 'code' && !(seed.signupCodes ?? []).some((c) => c.key === m.membership.code)) problems.push(`member ${m.key}: unknown sign-up code ${m.membership.code}`);
  }
  const assetKeys = new Set((seed.assets ?? []).map((a) => a.key));
  const keysBySection = {};

  function walk(node, where, section) {
    if (Array.isArray(node)) {
      node.forEach((n, i) => walk(n, `${where}[${i}]`, section));
      return;
    }
    if (!node || typeof node !== 'object') return;
    for (const [k, v] of Object.entries(node)) {
      if (MEMBER_FIELDS.has(k) && typeof v === 'string' && !memberKeys.has(v)) problems.push(`${where}.${k}: unknown member ${v}`);
      if (MEMBER_LIST_FIELDS.has(k) && Array.isArray(v)) for (const x of v) if (typeof x === 'string' && !memberKeys.has(x)) problems.push(`${where}.${k}: unknown member ${x}`);
      if ((k === 'media' || k === 'photos') && Array.isArray(v)) for (const x of v) if (typeof x === 'string' && !assetKeys.has(x)) problems.push(`${where}.${k}: unknown asset ${x}`);
      if (['cover', 'video', 'avatar', 'photo', 'asset'].includes(k) && typeof v === 'string' && !assetKeys.has(v)) problems.push(`${where}.${k}: unknown asset ${v}`);
      if (k === 'reactions' && v && !Array.isArray(v) && typeof v === 'object') for (const who of Object.keys(v)) if (!memberKeys.has(who)) problems.push(`${where}.reactions: unknown member ${who}`);
      if (k === 'key' && typeof v === 'string' && section && !/(comments|replies|messages|options)\[/.test(where)) {
        keysBySection[section] ??= new Set();
        if (keysBySection[section].has(v)) problems.push(`${where}: duplicate key ${v} in ${section}`);
        keysBySection[section].add(v);
      }
      if (v && typeof v === 'object') walk(v, `${where}.${k}`, section);
    }
  }

  for (const section of ['signupCodes', 'forums', 'feed', 'threads', 'venues', 'instructors', 'directorySubmissions', 'venueReviews', 'venueMedia', 'events', 'library', 'conversations', 'tickets', 'ideas', 'broadcasts', 'ai']) {
    if (seed[section] !== undefined) walk(seed[section], section, section);
  }
  const venueKeys = new Set((seed.venues ?? []).map((v) => v.key));
  for (const [i, r] of (seed.venueReviews ?? []).entries()) {
    if (!venueKeys.has(r.venue)) problems.push(`venueReviews[${i}].venue: unknown venue ${r.venue}`);
    if (!Number.isInteger(r.stars) || r.stars < 1 || r.stars > 5) problems.push(`venueReviews[${i}].stars: use a whole number 1 to 5`);
    if (typeof r.text !== 'string' || !r.text.trim()) problems.push(`venueReviews[${i}].text is required`);
    if (r.reply && (typeof r.reply.text !== 'string' || !r.reply.text.trim())) problems.push(`venueReviews[${i}].reply.text is required`);
  }
  for (const [i, m] of (seed.venueMedia ?? []).entries()) {
    if (!venueKeys.has(m.venue)) problems.push(`venueMedia[${i}].venue: unknown venue ${m.venue}`);
    if (!m.asset) problems.push(`venueMedia[${i}].asset is required`);
  }
  for (const a of seed.assets ?? []) {
    if (!a.key || !a.file) problems.push('every asset needs key and file');
    if (!['image', 'video'].includes(a.kind)) problems.push(`asset ${a.key}: kind must be image or video`);
  }
  return problems;
}
