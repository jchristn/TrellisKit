/** Round avatar image with an initials fallback. */
function Avatar({ src, name, size = 40, alt }) {
  const initials = String(name || '?').trim().split(/\s+/).slice(0, 2).map((part) => part[0] || '').join('').toUpperCase() || '?';
  const style = { width: size, height: size, fontSize: Math.max(11, Math.round(size * 0.38)) };
  if (src) return <img className="avatar" src={src} alt={alt === undefined ? '' : alt} width={size} height={size} style={style} />;
  return <span className="avatar avatar-fallback" style={style} aria-hidden={alt === undefined ? 'true' : undefined} role={alt ? 'img' : undefined} aria-label={alt || undefined}>{initials}</span>;
}

export default Avatar;
