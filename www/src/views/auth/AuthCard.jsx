/** Centered card used by every public authentication page. */
function AuthCard({ title, subtitle, children, footer, wide = false }) {
  return (
    <div className="site-container auth-page">
      <section className={'auth-card' + (wide ? ' auth-card-wide' : '')} aria-labelledby="auth-title">
        <header className="auth-card-header">
          <h1 id="auth-title">{title}</h1>
          {subtitle && <p className="muted">{subtitle}</p>}
        </header>
        {children}
        {footer && <footer className="auth-card-footer">{footer}</footer>}
      </section>
    </div>
  );
}

export default AuthCard;
