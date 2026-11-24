import { useEffect } from 'react';
import { useProperty } from '../context/PropertyContext.jsx';

function setMeta(attr, key, content) {
  let el = document.head.querySelector('meta[' + attr + '="' + key + '"]');
  if (!el) {
    el = document.createElement('meta');
    el.setAttribute(attr, key);
    document.head.appendChild(el);
  }
  el.setAttribute('content', content);
}

/**
 * Set the document title, meta description, and Open Graph title/description for a page.
 * Property tokens such as `{name}` are filled from the active property.
 * @param {string} title
 * @param {string} [description]
 */
export function useDocumentMeta(title, description) {
  const { fill } = useProperty();
  const fullTitle = title ? fill(title) : '';
  const fullDescription = description ? fill(description) : '';
  useEffect(() => {
    if (fullTitle) {
      document.title = fullTitle;
      setMeta('property', 'og:title', fullTitle);
    }
    if (fullDescription) {
      setMeta('name', 'description', fullDescription);
      setMeta('property', 'og:description', fullDescription);
    }
  }, [fullTitle, fullDescription]);
}

export default useDocumentMeta;
