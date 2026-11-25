import { afterEach, describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { catalogSite, renderApp, serve, site } from './trainingHelpers.jsx';

afterEach(() => {
  window.localStorage.clear();
});

const at = async (path) => waitFor(() => expect(screen.getByTestId('where')).toHaveTextContent(path), { timeout: 10000 });

describe('old library addresses move into the Training hub', () => {
  it.each([
    ['/app/library?search=guard&level=beginner', '/app/training/library/courses?search=guard&level=beginner'],
    ['/app/series/ser_1?from=mail', '/app/training/series/ser_1?from=mail'],
    ['/app/lessons/les_1?comment=cmt_9', '/app/training/lessons/les_1?comment=cmt_9']
  ])('%s redirects to %s keeping the query string', async (from, to) => {
    const { fetchImpl } = serve();
    renderApp(from, { fetchImpl });
    await at(to);
  }, 20000);

  it('redirects the catalog addresses when the catalog is on', async () => {
    const { fetchImpl } = serve();
    renderApp('/app/catalog?kind=Technique', { fetchImpl, siteBundle: catalogSite() });
    await at('/app/training/library/techniques?kind=Technique');
    expect(await screen.findByRole('link', { name: 'Hip escape' })).toHaveAttribute('href', '/app/training/catalog/hip-escape');
  }, 20000);

  it('redirects a catalog entry address', async () => {
    const { fetchImpl } = serve();
    renderApp('/app/catalog/hip-escape?tab=lessons', { fetchImpl, siteBundle: catalogSite() });
    await at('/app/training/catalog/hip-escape?tab=lessons');
  }, 20000);

  it('keeps the old addresses working when Training is off', async () => {
    const { fetchImpl, calls } = serve();
    renderApp('/app/library?from=menu', { fetchImpl, siteBundle: site({ features: { training: false } }) });
    expect((await screen.findAllByTestId('series-card-ser_1', {}, { timeout: 10000 })).length).toBeGreaterThan(0);
    expect(screen.getByTestId('where')).toHaveTextContent('/app/library?from=menu');
    expect(calls.some((call) => call.path === '/api/v1/me/training')).toBe(false);
  }, 20000);

  it('leaves the public lesson page alone', async () => {
    const { fetchImpl } = serve({ me: null });
    renderApp('/lessons/opening-lesson', { fetchImpl });
    // The public page is a marketing route: no redirect into the member app.
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(screen.getByTestId('where')).toHaveTextContent('/lessons/opening-lesson');
  }, 20000);
});
