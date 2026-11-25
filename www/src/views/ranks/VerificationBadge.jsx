/**
 * The "Verified" or "Self-reported" label shown next to a Rank field value.
 * Props: `badge` ('verified' | 'selfReported' | null; null renders nothing), `fieldLabel` (the
 * Rank field's label, used in the tooltip).
 */
import strings from '../../strings.js';
import { Badge } from '../../components/Badges.jsx';

const t = strings.ranks;

export function VerificationBadge({ badge, fieldLabel = '' }) {
  if (badge === 'verified') return <Badge tone="success" title={t.verifiedTitle(fieldLabel)}>{t.verified}</Badge>;
  if (badge === 'selfReported') return <Badge tone="neutral" title={t.selfReportedTitle(fieldLabel)}>{t.selfReported}</Badge>;
  return null;
}

export default VerificationBadge;
