import { useLocalSearchParams } from 'expo-router';
import { CurriculumDetail } from '../../src/components/ranks/Curricula';

export default function CurriculumScreen() {
  const { id } = useLocalSearchParams();
  const value = Array.isArray(id) ? id[0] : id;
  return <CurriculumDetail key={value} curriculumId={value} />;
}
