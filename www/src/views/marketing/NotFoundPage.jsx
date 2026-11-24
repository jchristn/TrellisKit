import { Link } from 'react-router-dom';
import strings from '../../strings.js';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';

/** 404 page. */
function NotFoundPage() {
  useDocumentMeta(strings.common.notFoundTitle + ' | {name}', strings.common.notFoundBody);
  return (
    <div className="site-container page center-text">
      <h1>{strings.common.notFoundTitle}</h1>
      <p className="muted">{strings.common.notFoundBody}</p>
      <p><Link to="/" className="button button-primary">{strings.common.backHome}</Link></p>
    </div>
  );
}

export default NotFoundPage;
