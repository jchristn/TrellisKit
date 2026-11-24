import { useSearchParams } from 'react-router-dom';
import strings from '../../strings.js';
import { useAdminAuth } from '../../context/AdminAuthContext.jsx';
import PageHeader from '../../components/PageHeader.jsx';
import ModerationQueue from './ModerationQueue.jsx';
import ModerationActionsTab from './ModerationActionsTab.jsx';
import ModeratorsTab from './ModeratorsTab.jsx';
import WordFilterTab from './WordFilterTab.jsx';
import ForumMutesTab from './ForumMutesTab.jsx';
import ReviewRequestsTab from './ReviewRequestsTab.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import { capabilityAvailable } from '../member/memberRoutes.js';
import { isForumModerator } from '../../utils/engagement.js';

const s = strings.admin.moderation;

/**
 * /admin/moderation: the report queue (with the Feed or Forum scope filter), action history, forum
 * mutes, and review requests for moderators; administrators also get the Moderators and Word
 * filter tabs. The tab lives in the URL (`?tab=`).
 */
function ModerationView() {
  const { isSystemAdmin, me } = useAdminAuth();
  const [searchParams, setSearchParams] = useSearchParams();
  const forumsOn = capabilityAvailable(useProperty().property, 'forums') && (isSystemAdmin || isForumModerator(me));
  const tabs = ['queue', 'actions', ...(forumsOn ? ['forumMutes', 'reviewRequests'] : []), ...(isSystemAdmin ? ['moderators', 'wordFilter'] : [])];
  const tab = tabs.includes(searchParams.get('tab')) ? searchParams.get('tab') : 'queue';
  const select = (id) => setSearchParams(id === 'queue' ? {} : { tab: id });
  return (
    <div className="view">
      <PageHeader title={strings.admin.routes.moderation.label} summary={strings.admin.routes.moderation.summary} />
      <div className="segmented tab-bar" role="tablist" aria-label={s.tabsLabel}>
        {tabs.map((id) => (
          <button key={id} type="button" role="tab" id={'mod-tab-' + id} aria-selected={tab === id} aria-controls={'mod-panel-' + id} className={'segment' + (tab === id ? ' active' : '')} onClick={() => select(id)}>{s.tabs[id]}</button>
        ))}
      </div>
      <div role="tabpanel" id={'mod-panel-' + tab} aria-labelledby={'mod-tab-' + tab} className="tab-panel">
        {tab === 'queue' && <ModerationQueue />}
        {tab === 'actions' && <ModerationActionsTab />}
        {tab === 'forumMutes' && <ForumMutesTab />}
        {tab === 'reviewRequests' && <ReviewRequestsTab />}
        {tab === 'moderators' && <ModeratorsTab />}
        {tab === 'wordFilter' && <WordFilterTab />}
      </div>
    </div>
  );
}

export default ModerationView;
