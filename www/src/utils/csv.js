function cell(value) {
  if (value === null || value === undefined) return '';
  const text = Array.isArray(value) ? value.join(';') : String(value);
  return /[",\r\n]/.test(text) ? '"' + text.replace(/"/g, '""') + '"' : text;
}

/**
 * Build CSV text from rows.
 * @param {Array<object>} rows
 * @param {Array<{ key: string, label: string }>} columns
 * @returns {string}
 */
export function toCsv(rows, columns) {
  const lines = [columns.map((c) => cell(c.label)).join(',')];
  for (const row of rows) lines.push(columns.map((c) => cell(row[c.key])).join(','));
  return lines.join('\r\n') + '\r\n';
}

/** Most pages one CSV export follows (each page is at most `enumeration.maxResultsAdmin` rows). */
export const CSV_EXPORT_MAX_PAGES = 200;

/**
 * Read a bounded CSV export page by page (REST_API.md "CSV exports are bounded"): each response
 * holds one page, and the `X-Continuation-Token` header, when present, is sent back as
 * `continuationToken` with the same filters for the next page. Header rows after the first page
 * are dropped.
 * @param {(query: object) => Promise<{ text: string, headers: Headers }>} fetchPage
 * @param {object} query The filters of the first page.
 * @param {{ maxPages?: number }} [options]
 * @returns {Promise<{ text: string, pages: number, complete: boolean }>}
 */
export async function fetchCsvPages(fetchPage, query, { maxPages = CSV_EXPORT_MAX_PAGES } = {}) {
  let text = '';
  let token = null;
  let pages = 0;
  do {
    const page = await fetchPage(token ? { ...query, continuationToken: token } : query);
    let body = (page && page.text) || '';
    if (pages > 0) {
      const firstBreak = body.indexOf('\n');
      body = firstBreak >= 0 ? body.slice(firstBreak + 1) : '';
    }
    if (text && !/\n$/.test(text) && body) text += '\r\n';
    text += body;
    pages += 1;
    token = (page && page.headers && page.headers.get && page.headers.get('x-continuation-token')) || null;
  } while (token && pages < maxPages);
  return { text, pages, complete: !token };
}

/**
 * Parse CSV text (RFC 4180: quoted fields, doubled quotes, CRLF or LF) into rows of cells.
 * Blank lines are skipped. Bounded by `maxRows` (default 1001: a header plus 1000 rows).
 * @param {string} text
 * @param {{ maxRows?: number }} [options]
 * @returns {string[][]}
 */
export function parseCsv(text, { maxRows = 1001 } = {}) {
  const rows = [];
  let row = [];
  let cell = '';
  let quoted = false;
  const source = String(text || '');
  const push = () => {
    row.push(cell);
    cell = '';
  };
  const end = () => {
    push();
    if (row.some((x) => x.trim() !== '')) rows.push(row);
    row = [];
  };
  for (let i = 0; i < source.length && rows.length < maxRows; i += 1) {
    const ch = source[i];
    if (quoted) {
      if (ch === '"' && source[i + 1] === '"') {
        cell += '"';
        i += 1;
      } else if (ch === '"') quoted = false;
      else cell += ch;
    } else if (ch === '"') quoted = true;
    else if (ch === ',') push();
    else if (ch === '\n') end();
    else if (ch !== '\r') cell += ch;
  }
  if ((cell || row.length) && rows.length < maxRows) end();
  return rows;
}
