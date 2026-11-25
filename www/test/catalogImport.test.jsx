import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import CatalogImportPanel from '../src/views/admin/catalog/CatalogImportPanel.jsx';

const source = { id: 'cis_one', name: 'Licensed source', url: 'https://example.test/catalog.json', license: 'CC0-1.0', attribution: null, revision: 3, enabled: true, maxEntries: 20, labels: [], tags: {}, reviewedUtc: null };
function clientFor(row = source) {
  return { adminEnumerateCatalogImportSources: vi.fn().mockResolvedValue({ objects: [row], endOfResults: true }), adminGetCatalogImportSource: vi.fn().mockResolvedValue(row), adminReviewCatalogImportSource: vi.fn().mockResolvedValue({ ...row, revision: 4, reviewedUtc: '2026-11-23T00:00:00Z' }), adminRunJob: vi.fn().mockResolvedValue({ id: 'run' }) };
}
async function open(client) { render(<MemoryRouter><CatalogImportPanel client={client} /></MemoryRouter>); await userEvent.click(screen.getByRole('button', { name: 'Manage import sources' })); await screen.findByRole('heading', { name: 'Licensed source' }); }

it('requires a fresh source revision and explicit license acknowledgement before authorizing imports', async () => {
  const client = clientFor(); await open(client);
  expect(screen.queryByRole('button', { name: 'Import drafts' })).not.toBeInTheDocument();
  await userEvent.click(screen.getByRole('button', { name: 'Review source license' }));
  const dialog = await screen.findByRole('dialog');
  expect(client.adminGetCatalogImportSource).toHaveBeenCalledWith('cis_one');
  const confirm = within(dialog).getByRole('button', { name: 'Authorize draft imports' }); expect(confirm).toBeDisabled();
  await userEvent.click(within(dialog).getByRole('checkbox')); await userEvent.click(confirm);
  await waitFor(() => expect(client.adminReviewCatalogImportSource).toHaveBeenCalledWith('cis_one', { expectedRevision: 3, licenseReviewed: true }));
});

it('queues only the chosen reviewed source and explicitly separates fetched preview from draft import', async () => {
  const client = clientFor({ ...source, reviewedUtc: '2026-11-23T00:00:00Z' }); await open(client);
  await userEvent.click(screen.getByRole('button', { name: 'Preview import' }));
  await waitFor(() => expect(client.adminRunJob).toHaveBeenCalledWith('catalog.importSources', true, JSON.stringify({ sourceIds: ['cis_one'] })));
  await userEvent.click(screen.getByRole('button', { name: 'Import drafts' }));
  await waitFor(() => expect(client.adminRunJob).toHaveBeenCalledWith('catalog.importSources', false, JSON.stringify({ sourceIds: ['cis_one'] })));
  expect(screen.getByRole('status')).toHaveTextContent('Import queued');
});

it('keeps the editor open on a stale review conflict without silently reapproving changed content', async () => {
  const client = clientFor(); client.adminReviewCatalogImportSource.mockRejectedValue({ code: 'CatalogSourceChanged' }); await open(client);
  await userEvent.click(screen.getByRole('button', { name: 'Review source license' })); const dialog = await screen.findByRole('dialog');
  fireEvent.click(within(dialog).getByRole('checkbox')); await userEvent.click(within(dialog).getByRole('button', { name: 'Authorize draft imports' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('This source changed'); expect(screen.getByRole('dialog')).toBeInTheDocument();
});
