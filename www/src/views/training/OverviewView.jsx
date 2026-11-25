import { useCallback } from 'react';
import { Link } from 'react-router-dom';
import { formatEventShort } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import useApiQuery from '../../hooks/useApiQuery.js';
import Icon from '../../components/Icon.jsx';
import { ErrorBanner, Loading } from '../../components/States.jsx';
import { LessonRow, SeriesCard } from '../../components/library/LibraryCards.jsx';
import { RankCard } from '../ranks/RankCard.jsx';
import { CurriculumCard } from '../ranks/CurriculumCard.jsx';
import { featureEnabled } from '../member/memberRoutes.js';
import { appPaths } from '../../utils/phase4.js';
import { useTraining } from './TrainingLayout.jsx';
import { rankCardOf, saveTargetPath, trainingPaths } from './trainingModel.js';

const t = strings.training;

function Card({ icon, title, id, children }) {
  return (
    <section className="panel training-card" aria-labelledby={id} data-testid={id}>
      <h2 id={id} className="section-title training-card-title"><Icon name={icon} size={18} /><span>{title}</span></h2>
      {children}
    </section>
  );
}

/** Real featured courses: the first series in library order. Nothing renders when there are none. */
export function FeaturedCourses({ id = 'training-featured' }) {
  const { client } = useAuth();
  const fetcher = useCallback((signal) => client.enumerateSeries({ maxResults: 3 }, signal), [client]);
  const query = useApiQuery(fetcher);
  const items = ((query.data && query.data.objects) || []).filter(Boolean).slice(0, 3);
  if (!items.length) return null;
  return (
    <section className="stack" aria-labelledby={id}>
      <h3 id={id} className="section-title">{t.featured}</h3>
      <div className="p4-grid" data-testid={id}>{items.map((series) => <SeriesCard key={series.id} series={series} />)}</div>
    </section>
  );
}

function UpNext({ page }) {
  return (
    <Card icon="calendar" title={t.upNext} id="training-up-next">
      <ul className="training-list">
        {page.objects.map((item) => (
          <li key={(item.kind || 'Event') + ':' + item.id}>
            <Link to={appPaths.event(item.id)} className="training-list-link">
              <span className="training-list-title">{item.title}</span>
              <span className="muted small">{[item.startUtc ? formatEventShort(item) : null, item.locationSummary].filter(Boolean).join(' · ')}</span>
            </Link>
          </li>
        ))}
      </ul>
      <Link to={trainingPaths.schedule} className="button button-secondary button-sm">{t.seeSchedule}</Link>
    </Card>
  );
}

/** One saved item as a link (or a neutral row when the target is no longer available). */
export function SavedTargetLink({ save, className = 'training-list-link' }) {
  const available = Boolean(save.target && save.target.available);
  const path = saveTargetPath(save.targetType, save.targetId);
  const type = t.saved.typeOne[save.targetType] || save.targetType;
  const title = available ? (save.target.title || type) : t.savedUnavailable;
  if (available && path) {
    return <Link to={path} className={className}><span className="training-list-title">{title}</span><span className="muted small">{type}</span></Link>;
  }
  return <span className={className + ' training-list-unavailable'} data-testid={'save-unavailable-' + save.id}><span className="training-list-title">{title}</span><span className="small">{type}</span></span>;
}

function RecentSaves({ page }) {
  return (
    <Card icon="bookmark" title={t.recentlySaved} id="training-recent-saves">
      <ul className="training-list">
        {page.objects.map((save) => <li key={save.id || save.targetType + ':' + save.targetId}><SavedTargetLink save={save} /></li>)}
      </ul>
      <Link to={trainingPaths.segment('saved')} className="button button-secondary button-sm">{t.seeAllSaved}</Link>
    </Card>
  );
}

function FirstRun() {
  const { property } = useProperty();
  return (
    <Card icon="flag" title={t.firstRunTitle} id="training-first-run">
      <p className="muted">{t.firstRunBody}</p>
      <FeaturedCourses id="training-first-run-featured" />
      <div className="training-actions">
        <Link to={trainingPaths.segment('courses')} className="button button-primary button-sm">{t.startCourse}</Link>
        {featureEnabled(property, 'directory') && <Link to={appPaths.directory} className="button button-secondary button-sm">{t.findPlace}</Link>}
        {featureEnabled(property, 'events') && <Link to={appPaths.events} className="button button-secondary button-sm">{t.findEvent}</Link>}
      </div>
    </Card>
  );
}

/**
 * Training Overview (docs/TRELLISKIT.md, Training hub): Up next, Continue training, Your rank and
 * its curriculum, Ask your coach, Recently saved, and New for you, in that order and each only with
 * real data from `GET /me/training`. The first-run card (real featured courses and the directory
 * or events entry, never sample content) shows when the server reports `firstRun` or no card has data.
 */
function OverviewView() {
  const { overview, query } = useTraining();
  if (!overview) {
    if (query && query.error) return <ErrorBanner message={t.loadError} onRetry={query.reload} />;
    return <Loading label={t.loading} />;
  }
  const rank = rankCardOf(overview.rank);
  const cards = [];
  if (overview.upNext.objects.length) cards.push(<UpNext key="up" page={overview.upNext} />);
  if (overview.continue.objects.length) {
    cards.push(
      <Card key="continue" icon="play" title={t.continueTraining} id="training-continue">
        <div className="p4-list">{overview.continue.objects.map((row) => <LessonRow key={row.lesson.id} lesson={row.lesson} progress={row.progress} showSeries />)}</div>
      </Card>
    );
  }
  if (rank) {
    cards.push(
      <div key="rank" className="training-card" data-testid="training-rank">
        <RankCard rank={rank.rank} next={rank.next} showVerificationLabels={rank.showVerificationLabels} to="/app/rank" compact />
      </div>
    );
    if (rank.curriculum) cards.push(<div key="curriculum" className="training-card" data-testid="training-curriculum"><CurriculumCard curriculum={rank.curriculum} to={trainingPaths.curriculum(rank.curriculum.id)} /></div>);
  }
  if (overview.coach) {
    cards.push(
      <Card key="coach" icon="message" title={t.coachTitle} id="training-coach">
        <p className="muted">{t.coachBody}</p>
        <div className="training-actions"><Link to="/app/assistants" className="button button-primary button-sm">{t.coachOpen}</Link></div>
      </Card>
    );
  }
  if (overview.recentSaves.objects.length) cards.push(<RecentSaves key="saves" page={overview.recentSaves} />);
  if (overview.newForYou.objects.length) {
    cards.push(
      <Card key="new" icon="star" title={t.newForYou} id="training-new">
        <div className="p4-list">{overview.newForYou.objects.map((lesson) => <LessonRow key={lesson.id} lesson={lesson} showSeries />)}</div>
      </Card>
    );
  }
  const firstRun = overview.firstRun || !cards.length;
  return (
    <div className="stack">
      {firstRun && <FirstRun />}
      {cards.length > 0 && <div className="training-grid">{cards}</div>}
    </div>
  );
}

export default OverviewView;
