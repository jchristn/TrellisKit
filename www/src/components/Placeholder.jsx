import strings from '../strings.js';

/** Visibly marked placeholder copy for marketing pages. */
function Placeholder({ children, as: Tag = 'p' }) {
  return (
    <Tag className="placeholder-copy">
      <span className="placeholder-tag">{strings.common.placeholderTag}</span>
      {children}
    </Tag>
  );
}

export default Placeholder;
