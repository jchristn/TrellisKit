import strings from '../strings.js';
import { Badge } from './Badges.jsx';

const s = strings.metadataBadges;

/**
 * An item's labels and tags as small badges (feed posts, comments): labels as their text, tags as
 * `key: value` (a key alone when the value is empty). At most `max` badges; the rest collapse into
 * one "+N" badge whose title lists them.
 * @param {{ labels?: string[], tags?: Record<string, string>, max?: number }} props
 */
function MetadataBadges({ labels = [], tags = {}, max = 6 }) {
  const items = [
    ...(Array.isArray(labels) ? labels : []).filter(Boolean).map((l) => ({ key: 'l:' + l, text: l, title: s.label(l) })),
    ...Object.entries(tags && typeof tags === 'object' ? tags : {}).map(([k, v]) => {
      const text = v ? k + ': ' + v : k;
      return { key: 't:' + k, text, title: s.tag(text) };
    })
  ];
  if (!items.length) return null;
  const shown = items.slice(0, max);
  const rest = items.slice(max);
  return (
    <span className="metadata-badges" aria-label={s.listLabel}>
      {shown.map((item) => <Badge key={item.key} tone="neutral" title={item.title}>{item.text}</Badge>)}
      {rest.length > 0 && <Badge tone="neutral" title={rest.map((r) => r.text).join(', ')}>{s.more(rest.length)}</Badge>}
    </span>
  );
}

export default MetadataBadges;
