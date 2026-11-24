import { existsSync } from 'node:fs';
import path from 'node:path';
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import trellisKit from './scripts/vite-plugin-trelliskit.mjs';

const BACKEND = process.env.TK_BACKEND_URL || 'http://127.0.0.1:8000';
const DIST = path.resolve(import.meta.dirname, 'dist');
// Unit tests build against a test-local fixture property (a rank-enabled "dojo"), never a real one.
const TEST_PROPERTY_DIR = path.resolve(import.meta.dirname, 'test', 'fixtures', 'dojo');

/**
 * `vite preview` serves the build the way production nginx should: prerendered pages from
 * dist/<path>/index.html and dist/spa.html (the client-only shell) for every other route.
 */
function previewPrerendered() {
  return {
    name: 'trelliskit-preview-prerendered',
    configurePreviewServer(server) {
      server.middlewares.use((req, _res, next) => {
        const accept = req.headers.accept || '';
        if (req.method !== 'GET' || !accept.includes('text/html')) return next();
        const [pathname, query = ''] = req.url.split('?');
        if (path.extname(pathname)) return next();
        const clean = pathname.replace(/\/+$/, '') || '/';
        const prerendered = clean === '/' ? 'index.html' : path.join(clean.slice(1), 'index.html');
        const target = existsSync(path.join(DIST, prerendered)) ? '/' + prerendered.split(path.sep).join('/') : '/spa.html';
        req.url = target + (query ? '?' + query : '');
        next();
      });
    }
  };
}

export default defineConfig(({ isSsrBuild }) => ({
  plugins: [react(), trellisKit(process.env.VITEST ? { env: { ...process.env, TK_PROPERTY_DIR: TEST_PROPERTY_DIR } } : {}), previewPrerendered()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      // The realtime socket (PHASE5_CONTRACT.md section 4.14): upgrades are proxied like nginx does.
      '/api/v1/ws': { target: BACKEND, changeOrigin: false, ws: true },
      '/api': { target: BACKEND, changeOrigin: false, ws: true },
      '/openapi.json': { target: BACKEND, changeOrigin: false }
    }
  },
  preview: {
    port: 4173,
    proxy: {
      '/api': { target: BACKEND, changeOrigin: false, ws: true },
      '/openapi.json': { target: BACKEND, changeOrigin: false }
    }
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
    rollupOptions: isSsrBuild ? {} : {
      output: {
        manualChunks: {
          vendor: ['react', 'react-dom', 'react-dom/client', 'react-router-dom']
        }
      }
    }
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./test/setup.js'],
    include: ['test/**/*.test.{js,jsx}'],
    css: false,
    restoreMocks: true
  }
}));
