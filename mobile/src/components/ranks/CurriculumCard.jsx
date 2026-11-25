import strings from '../../strings';
import { useTheme } from '../../theme';
import AppText from '../AppText';
import Button from '../Button';
import Card from '../Card';
import ProgressBar from './ProgressBar';

/**
 * A curriculum summary card with a progress bar (Training Overview and Progress, Curricula).
 * Props: `curriculum` CurriculumView (`id`, `name`, `description`, `itemCount`, `progress`),
 * `onPress` (open it, for example `router.push('/curricula/' + id)`), `testID`.
 */
export default function CurriculumCard({ curriculum, onPress, testID }) {
  const theme = useTheme();
  if (!curriculum) return null;
  const progress = curriculum.progress || {};
  const total = progress.total ?? curriculum.itemCount ?? 0;
  const done = progress.completed || 0;
  const text = strings.ranks.curriculumProgress(done, total);
  const id = testID || 'curriculum-card-' + curriculum.id;
  return (
    <Card style={{ gap: theme.spacing.sm }} testID={id}>
      {onPress ? <Button label={curriculum.name} variant="ghost" onPress={onPress} testID={id + '-open'} /> : <AppText variant="label">{curriculum.name}</AppText>}
      {curriculum.description ? <AppText color="textMuted">{curriculum.description}</AppText> : null}
      <AppText variant="caption">{text}</AppText>
      <ProgressBar value={done} max={total} label={curriculum.name + ': ' + text} testID={id + '-progress'} />
    </Card>
  );
}
