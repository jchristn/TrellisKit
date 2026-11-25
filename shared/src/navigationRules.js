/**
 * Navigation validation rules shared by every navigation surface (mobile and web), so the two
 * cannot drift. Callers pass the surface's configuration path, its lists with their maximums, the
 * known destination identifiers, and the label length limit. Used by `validateNavigation`
 * (navigation.js) and `validateMobileNavigation` (mobileNavigation.js); the server applies the same
 * rules in `PropertyValidator`.
 */

const object = (value) => value !== null && typeof value === 'object' && !Array.isArray(value);

/**
 * Validate one navigation section.
 * @param {string} prefix Configuration path, such as `mobile.navigation`.
 * @param {any} navigation The section value (missing or null is valid).
 * @param {Array<[string, number]>} lists List keys and their maximum lengths.
 * @param {readonly string[]} known Known destination identifiers.
 * @param {number} maxLabelLength Longest label override, after trimming.
 * @returns {string[]} Problems naming the invalid paths.
 */
export function validateNavigationSection(prefix, navigation, lists, known, maxLabelLength) {
  if (navigation === undefined || navigation === null) return [];
  if (!object(navigation)) return [prefix + ' must be an object'];
  const isKnown = (value) => typeof value === 'string' && known.includes(value);
  const problems = [];
  for (const [key, max] of lists) {
    const values = navigation[key];
    if (values === undefined || values === null) continue;
    const field = prefix + '.' + key;
    if (!Array.isArray(values)) { problems.push(field + ' must be an array'); continue; }
    if (values.length > max) problems.push(field + ' must contain at most ' + max + ' destinations');
    const seen = new Set();
    values.forEach((id, i) => {
      if (!isKnown(id)) problems.push(field + '[' + i + '] must be a known destination');
      else if (seen.has(id)) problems.push(field + '[' + i + '] duplicates ' + id);
      seen.add(id);
    });
  }
  const labels = navigation.labels;
  if (labels !== undefined && labels !== null) {
    if (!object(labels)) problems.push(prefix + '.labels must be an object');
    else for (const [id, text] of Object.entries(labels)) {
      const field = prefix + '.labels.' + id;
      if (!isKnown(id)) problems.push(field + ' must name a known destination');
      else if (typeof text !== 'string' || !text.trim() || text.trim().length > maxLabelLength) problems.push(field + ' must be 1 to ' + maxLabelLength + ' characters');
    }
  }
  return problems;
}

/**
 * Clean label overrides defensively (cached or older configuration): known ids only, trimmed,
 * nonempty, and cut to the limit.
 * @param {any} labels
 * @param {readonly string[]} known
 * @param {number} maxLabelLength
 * @returns {Record<string,string>}
 */
export function cleanNavigationLabels(labels, known, maxLabelLength) {
  const result = {};
  if (!object(labels)) return result;
  for (const [id, text] of Object.entries(labels)) {
    if (known.includes(id) && typeof text === 'string' && text.trim()) result[id] = text.trim().slice(0, maxLabelLength);
  }
  return result;
}
