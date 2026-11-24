import Icon from '../Icon.jsx';
import { mediaUrl } from '../../utils/engagement.js';

/**
 * A forum's icon (PHASE3_CONTRACT.md section 2.1): its emoji, its image (`imageUrl`, the thumb
 * variant), or the generic forum icon, on the forum color when one is set. Decorative.
 * @param {{ forum: object, size?: number }} props
 */
function ForumIcon({ forum, size = 40 }) {
  const icon = (forum && forum.icon) || {};
  const style = { width: size, height: size, background: forum && forum.color ? forum.color : undefined };
  const image = mediaUrl(icon.imageUrl);
  return (
    <span className={'forum-icon' + (forum && forum.color ? ' forum-icon-colored' : '')} style={style} aria-hidden="true">
      {image ? <img src={image} alt="" width={size} height={size} /> : icon.emoji ? <span className="forum-icon-emoji">{icon.emoji}</span> : <Icon name="forum" size={Math.round(size / 2)} />}
    </span>
  );
}

export default ForumIcon;
