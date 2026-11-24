/** Route header with title, summary, and optional actions. */
function PageHeader({ title, summary, actions, children }) {
  return (
    <header className="page-header">
      <div className="page-header-text">
        <h1>{title}</h1>
        {summary && <p className="page-summary">{summary}</p>}
        {children}
      </div>
      {actions && <div className="page-header-actions">{actions}</div>}
    </header>
  );
}

export default PageHeader;
