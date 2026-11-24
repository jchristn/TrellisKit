import { useEffect } from 'react';
import { useProperty } from '../../context/PropertyContext.jsx';
import { siteMetaFor } from './siteMeta.js';

function selectorFor(t) {
  if (t.tag === 'title') return 'title';
  if (t.tag === 'script') return 'script[type="application/ld+json"]';
  if (t.tag === 'link') return 'link[rel="' + t.attrs.rel + '"]';
  if (t.attrs.property) return 'meta[property="' + t.attrs.property + '"]';
  return 'meta[name="' + t.attrs.name + '"]';
}

/**
 * Keep the document head in sync with the route metadata during client-side navigation: title,
 * description, canonical, Open Graph, Twitter, robots, and JSON-LD. The same tags are written
 * into the static HTML by the prerender script, so this only matters after navigation.
 * @param {string} path a marketing route path of the active property
 */
export function useRouteMeta(path) {
  const { site } = useProperty();
  useEffect(() => {
    const tags = siteMetaFor(site).buildHeadTags(path);
    if (!tags.length) return;
    for (const t of tags) {
      let el = document.head.querySelector(selectorFor(t));
      if (!el) {
        el = document.createElement(t.tag);
        document.head.appendChild(el);
      }
      for (const [key, value] of Object.entries(t.attrs || {})) el.setAttribute(key, value);
      if (t.text !== undefined) el.textContent = t.text;
    }
    if (!tags.some((t) => t.attrs && t.attrs.name === 'robots')) {
      const robots = document.head.querySelector('meta[name="robots"]');
      if (robots) robots.remove();
    }
  }, [path, site]);
}

export default useRouteMeta;
