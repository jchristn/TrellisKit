import { useState } from 'react';
import strings from '../../strings.js';
import { useToast } from '../../context/ToastContext.jsx';
import { errorText } from '../../utils/engagement.js';
import { forumErrors } from '../../utils/forums.js';
import Icon from '../Icon.jsx';
import { useAccessPrompt } from '../AccessPrompt.jsx';

const t = strings.forums;

/**
 * Follow toggle for a forum or a thread (PHASE3_CONTRACT.md section 9.2). `onToggle(next)` runs the
 * request and returns the server's answer; the button shows the pressed state.
 * @param {{ followed: boolean, onToggle: (next: boolean) => Promise<any>, label: string, unfollowLabel: string, disabled?: boolean, compact?: boolean }} props
 */
function FollowButton({ followed, onToggle, label, unfollowLabel, disabled = false, compact = false }) {
  const toast = useToast();
  const accessPrompt = useAccessPrompt();
  const [busy, setBusy] = useState(false);
  const click = async () => {
    setBusy(true);
    try {
      await onToggle(!followed);
      toast.success(followed ? t.unfollowed : t.followed);
    } catch (err) {
      if (!accessPrompt.handle(err)) toast.error(errorText(err, forumErrors()));
    } finally {
      setBusy(false);
    }
  };
  return (
    <button type="button" className={'button button-sm ' + (followed ? 'button-secondary active' : 'button-secondary')} aria-pressed={followed} aria-label={followed ? unfollowLabel : label} onClick={click} disabled={busy || disabled}>
      <Icon name={followed ? 'check' : 'bell'} size={14} />{!compact && <span>{followed ? t.unfollow : t.follow}</span>}
    </button>
  );
}

export default FollowButton;
