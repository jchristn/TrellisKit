import { StrictMode } from 'react';
import { createRoot, hydrateRoot } from 'react-dom/client';
import App from './App.jsx';
import './index.css';
// Property theme (generated from property.json at build time); loaded after the neutral defaults so it wins.
import 'virtual:trelliskit/theme.css';

const container = document.getElementById('root');
const app = (
  <StrictMode>
    <App />
  </StrictMode>
);

// Marketing pages are prerendered at build time and marked with the path they were rendered for.
// Hydrate those; everything else (member app, admin, auth pages) renders on the client.
if (container.dataset.prerendered && container.dataset.prerendered === window.location.pathname) {
  hydrateRoot(container, app);
} else {
  createRoot(container).render(app);
}
