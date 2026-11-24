import { useCallback, useState } from 'react';
import { normalizeEnumerationResult } from '@trelliskit/shared';
import strings from '../../strings.js';
import useApiQuery from '../../hooks/useApiQuery.js';
import { errorMessage } from '../../utils/api.js';
import { formatDateTime, formatNumber } from '../../utils/format.js';
import ConfirmModal from '../ConfirmModal.jsx';
import Icon from '../Icon.jsx';
import { Badge } from '../Badges.jsx';
import { ErrorBanner, Loading } from '../States.jsx';
import TableColumns from '../TableColumns.jsx';

const s = strings.admin.branding;
const v = s.versions;

/** Page size of the palette versions list (a bounded enumeration; `property.maxPaletteVersions` caps the total). */
export const PALETTE_VERSIONS_PAGE = 10;

const SWATCH_KEYS = Object.freeze(['primary', 'accent', 'accentSoft']);

function Swatches({ palette, scheme }) {
  const colors = palette || {};
  return (
    <span className="brand-version-swatches" aria-label={v.swatchesLabel(s[scheme])}>
      {SWATCH_KEYS.map((key) => (colors[key] ? <span key={key} className="brand-version-swatch" style={{ background: colors[key] }} title={s.keys[key].label + ' ' + colors[key]} /> : null))}
    </span>
  );
}

/**
 * Every kept palette version (newest first, paged with continuation; at most
 * `property.maxPaletteVersions` exist) with the source, note, time, and swatches of each, and
 * "Restore" on any version but the current one. Restoring confirms first and creates a new
 * version with that version's colors (`POST .../branding/revert` `{ to: 'Version', version }`).
 *
 * @param {{ list: (query: object, signal?: AbortSignal) => Promise<object>, restore: (version: number) => Promise<object>,
 *   currentVersion: number, onRestored: (view: object) => void, disabled?: boolean }} props
 */
function PaletteVersions({ list, restore, currentVersion, onRestored, disabled = false }) {
  const [tokens, setTokens] = useState([null]);
  const token = tokens[tokens.length - 1];
  const fetcher = useCallback((signal) => list({ maxResults: PALETTE_VERSIONS_PAGE, continuationToken: token || undefined }, signal), [list, token]);
  const query = useApiQuery(fetcher);
  const page = query.data ? normalizeEnumerationResult(query.data) : null;
  const [confirm, setConfirm] = useState(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  const doRestore = async () => {
    const version = confirm;
    setConfirm(null);
    setBusy(true);
    setError('');
    try {
      const next = await restore(version);
      setTokens([null]);
      query.reload();
      onRestored(next, version);
    } catch (err) {
      setError(errorMessage(err, strings.common.errorGeneric));
    } finally {
      setBusy(false);
    }
  };

  const rows = page ? page.objects : [];
  return (
    <section className="panel brand-versions" aria-labelledby="brand-versions-title">
      <h2 id="brand-versions-title">{v.title}</h2>
      <p className="muted small">{v.hint}</p>
      <ErrorBanner message={error || (query.error ? errorMessage(query.error, v.loadError) : '')} onRetry={query.error ? query.reload : undefined} onDismiss={error ? () => setError('') : undefined} />
      {query.loading && !page && <Loading inline />}
      {page && (
        <div className="table-scroll">
          <TableColumns id="tk.branding.PaletteVersions"><table className="data-table">
            <caption className="visually-hidden">{v.title}</caption>
            <thead>
              <tr>
                <th scope="col" className="num">{s.version}</th>
                <th scope="col">{s.source}</th>
                <th scope="col">{v.note}</th>
                <th scope="col">{s.updated}</th>
                <th scope="col">{s.light}</th>
                <th scope="col">{s.dark}</th>
                <th scope="col"><span className="visually-hidden">{v.actions}</span></th>
              </tr>
            </thead>
            <tbody>
              {rows.length === 0 && <tr><td colSpan={7} className="muted">{v.empty}</td></tr>}
              {rows.map((row) => {
                const current = row.isCurrent || row.version === currentVersion;
                return (
                  <tr key={row.version}>
                    <th scope="row" className="num mono">{formatNumber(row.version)}</th>
                    <td>
                      <Badge tone="info">{s.sources[row.source] || row.source}</Badge>
                      {current && <> <Badge tone="success">{v.current}</Badge></>}
                    </td>
                    <td className="cell-wrap">{row.note || <span className="muted">-</span>}</td>
                    <td className="nowrap">{formatDateTime(row.createdUtc)}</td>
                    <td><Swatches palette={row.light} scheme="light" /></td>
                    <td><Swatches palette={row.dark} scheme="dark" /></td>
                    <td>
                      {!current && (
                        <button type="button" className="button button-secondary button-sm" onClick={() => setConfirm(row.version)} disabled={disabled || busy} aria-label={v.restoreLabel(row.version)}>
                          <Icon name="refresh" size={14} /><span>{v.restore}</span>
                        </button>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table></TableColumns>
        </div>
      )}
      {page && (tokens.length > 1 || !page.endOfResults) && (
        <div className="form-actions">
          <button type="button" className="button button-secondary button-sm" onClick={() => setTokens((list) => list.slice(0, -1))} disabled={tokens.length <= 1 || query.loading}>{v.newer}</button>
          <button type="button" className="button button-secondary button-sm" onClick={() => setTokens((list) => [...list, page.continuationToken])} disabled={page.endOfResults || !page.continuationToken || query.loading}>{v.older}</button>
        </div>
      )}
      <ConfirmModal
        open={confirm !== null}
        title={v.confirmTitle(confirm)}
        message={v.confirmBody(confirm)}
        confirmLabel={v.restore}
        danger={false}
        onCancel={() => setConfirm(null)}
        onConfirm={doRestore}
      />
    </section>
  );
}

export default PaletteVersions;
