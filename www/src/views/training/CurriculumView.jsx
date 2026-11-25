import { CurriculumDetailView } from '../ranks/CurriculaViews.jsx';

/**
 * `/app/training/curricula/:id`: one curriculum with the member's progress, reached from the
 * Training Overview's curriculum card and the Curricula section. Reuses the ranks curriculum view.
 */
function CurriculumView() {
  return <CurriculumDetailView />;
}

export default CurriculumView;
