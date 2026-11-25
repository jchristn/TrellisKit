import { useRef } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';

/**
 * Route tabs as an accessible tablist: each tab is a link to its route with `role="tab"`,
 * `aria-selected`, and a roving tab stop. Left and Right (and Home and End) move between tabs and
 * open the focused one. `keepSearch` keeps the query string (label and tag filters) on a switch.
 * @param {{ label: string, tabs: { key: string, label: string, to: string }[], current: string, controls?: string, id?: string, keepSearch?: boolean }} props
 */
function SectionTabs({ label, tabs, current, controls = null, id = 'section-tabs', keepSearch = false }) {
  const navigate = useNavigate();
  const location = useLocation();
  const refs = useRef([]);
  const target = (tab) => (keepSearch ? { pathname: tab.to, search: location.search } : tab.to);
  const move = (index) => {
    const next = (index + tabs.length) % tabs.length;
    const node = refs.current[next];
    if (node) node.focus();
    navigate(target(tabs[next]));
  };
  const onKeyDown = (event, index) => {
    if (event.key === 'ArrowRight') { event.preventDefault(); move(index + 1); }
    else if (event.key === 'ArrowLeft') { event.preventDefault(); move(index - 1); }
    else if (event.key === 'Home') { event.preventDefault(); move(0); }
    else if (event.key === 'End') { event.preventDefault(); move(tabs.length - 1); }
  };
  return (
    <div className="training-tabs" role="tablist" aria-label={label} id={id}>
      {tabs.map((tab, index) => {
        const selected = tab.key === current;
        return (
          <Link
            key={tab.key}
            ref={(node) => { refs.current[index] = node; }}
            to={target(tab)}
            role="tab"
            id={id + '-' + tab.key}
            aria-selected={selected}
            aria-controls={controls || undefined}
            tabIndex={selected ? 0 : -1}
            className={'training-tab' + (selected ? ' active' : '')}
            data-testid={id + '-' + tab.key}
            onKeyDown={(event) => onKeyDown(event, index)}
          >
            {tab.label}
          </Link>
        );
      })}
    </div>
  );
}

export default SectionTabs;
