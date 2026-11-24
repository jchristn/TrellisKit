import { useState } from 'react';
import { safety } from '../../api/community';
import { useAuth } from '../../auth/AuthProvider';
import { describeContentError } from '../../content/errors';
import { ComposerLimits } from '../../content/permissions';
import strings from '../../strings';
import AppText from '../AppText';
import Button from '../Button';
import ChipGroup from '../ChipGroup';
import Notice from '../Notice';
import Sheet from '../Sheet';
import TextField from '../TextField';
import { useBlock } from './useBlock';

/** Report reasons in contract order (9.1). */
export const ReportReasons = Object.freeze(['Spam', 'Harassment', 'HateSpeech', 'SexualContent', 'Violence', 'SelfHarm', 'Impersonation', 'IntellectualProperty', 'Other']);

/** Reasons for events and lessons (docs/PHASE4_CONTRACT.md section 3.10 adds Inaccurate). */
export const Phase4ReportReasons = Object.freeze(['Spam', 'Harassment', 'HateSpeech', 'SexualContent', 'Violence', 'SelfHarm', 'Impersonation', 'IntellectualProperty', 'Inaccurate', 'Other']);

/** Reasons for venues and instructors: the Phase 2 reasons plus Inaccurate and PermanentlyClosed (section 3.10). */
export const DirectoryReportReasons = Object.freeze(['Spam', 'Harassment', 'HateSpeech', 'SexualContent', 'Violence', 'SelfHarm', 'Impersonation', 'IntellectualProperty', 'Inaccurate', 'PermanentlyClosed', 'Other']);

/**
 * Report a post, comment, or profile (POST /reports). After sending, thanks the member and
 * offers to block the author (contract 9.1), or explains that staff cannot be blocked.
 * @param {object} props
 * @param {{ targetType: 'FeedPost'|'Comment'|'User', targetId: string }} props.target
 * @param {object|null} [props.author] UserSummary of the content's author (block offer).
 * @param {(user: object) => void} [props.onBlocked]
 */
export default function ReportSheet({ target, author, onClose, onBlocked, reasons = ReportReasons }) {
  const { api, user } = useAuth();
  const [reason, setReason] = useState(null);
  const [details, setDetails] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const [sent, setSent] = useState(false);
  const [blockedMessage, setBlockedMessage] = useState(null);
  const blocker = useBlock(api);
  const t = strings.report;

  async function submit() {
    setError(null);
    if (!reason) return setError(t.chooseReason);
    const trimmed = details.trim();
    if (reason === 'Other' && trimmed.length < ComposerLimits.reportOtherMinimum) return setError(t.detailsRequired);
    setBusy(true);
    try {
      await safety.report(api, { targetType: target.targetType, targetId: target.targetId, reason, details: trimmed });
      setSent(true);
    } catch (e) {
      setError(describeContentError(e));
    } finally {
      setBusy(false);
    }
  }

  async function block() {
    const result = await blocker.block(author);
    if (result.ok) {
      setBlockedMessage(strings.block.blocked(author.handle || author.displayName));
      if (onBlocked) onBlocked(author);
    }
  }

  const canOfferBlock = Boolean(author && author.id && !author.isDeleted && (!user || author.id !== user.id));

  return (
    <Sheet visible title={t.title} onClose={onClose} testID="report">
      {sent ? (
        <>
          <Notice tone="success" testID="report-thanks">
            {t.thanks}
          </Notice>
          {canOfferBlock && author.isStaff ? <AppText color="textMuted">{strings.block.staff}</AppText> : null}
          {canOfferBlock && !author.isStaff && !blockedMessage ? (
            <>
              <AppText color="textMuted">{strings.block.confirmBody}</AppText>
              <Button label={strings.postMenu.block(author.handle || author.displayName)} variant="secondary" onPress={block} disabled={blocker.busy} testID="report-block" />
            </>
          ) : null}
          <Notice tone={blocker.error ? 'error' : 'success'} testID="report-block-result">
            {blocker.error || blockedMessage}
          </Notice>
          <Button label={t.done} onPress={onClose} testID="report-done" />
        </>
      ) : (
        <>
          <ChipGroup label={t.heading} options={reasons.map((r) => ({ value: r, label: t.reasons[r] }))} value={reason} onToggle={setReason} testID="report-reason" />
          <TextField label={t.details} hint={reason === 'Other' ? t.detailsRequired : t.detailsHint} value={details} onChangeText={setDetails} multiline maxLength={ComposerLimits.reportDetails} testID="report-details" />
          <Notice tone="error" testID="report-error">
            {error}
          </Notice>
          <Button label={busy ? t.sending : t.submit} onPress={submit} disabled={busy} testID="report-submit" />
        </>
      )}
    </Sheet>
  );
}
