import { useLocalSearchParams } from 'expo-router';
import MemberGate from '../../../src/components/MemberGate';
import { CurriculumDetail } from '../../../src/components/ranks/Curricula';

/** A curriculum inside Training (`/training/curricula/{id}`), the same screen as `/curricula/{id}`. */
export default function TrainingCurriculumScreen() {
  const { id } = useLocalSearchParams();
  const value = Array.isArray(id) ? id[0] : id;
  return (
    <MemberGate>
      <CurriculumDetail key={value} curriculumId={value} />
    </MemberGate>
  );
}
