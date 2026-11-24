import { describe, it, expect, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import DataTable from '../src/components/DataTable.jsx';
import TableColumns from '../src/components/TableColumns.jsx';
import { defaultHiddenColumns, isPinnedColumn } from '../src/hooks/useTableColumns.js';

const COLUMNS = [
  { key: 'name', label: 'Name' },
  { key: 'email', label: 'Email' },
  { key: 'status', label: 'Status' },
  { key: 'actions', label: '', className: 'cell-actions', render: () => 'x' }
];
const ROWS = [{ id: '1', name: 'Sam', email: 'sam@example.test', status: 'Active' }];

function headers() {
  return screen.getAllByRole('columnheader').map((th) => th.textContent);
}

describe('table columns', () => {
  beforeEach(() => window.localStorage.clear());

  it('every data table has a Columns control; a hidden column stays hidden on the next visit', async () => {
    const view = render(<DataTable tableId="test.people" columns={COLUMNS} rows={ROWS} rowKey={(r) => r.id} />);
    expect(headers()).toEqual(['Name', 'Email', 'Status', '']);
    fireEvent.click(screen.getByRole('button', { name: 'Columns' }));
    // The first column and the row actions cannot be hidden; the actions column is not listed.
    expect(screen.getByRole('checkbox', { name: /Name/ })).toBeDisabled();
    expect(screen.queryAllByRole('checkbox')).toHaveLength(3);
    fireEvent.click(screen.getByRole('checkbox', { name: 'Email' }));
    await waitFor(() => expect(headers()).toEqual(['Name', 'Status', '']));
    expect(JSON.parse(window.localStorage.getItem('tk.table.test.people.hiddenColumns'))).toEqual(['email']);
    view.unmount();
    render(<DataTable tableId="test.people" columns={COLUMNS} rows={ROWS} rowKey={(r) => r.id} />);
    expect(headers()).toEqual(['Name', 'Status', '']);
  });

  it('a page with its own chooser turns the built-in control off', () => {
    render(<DataTable columns={COLUMNS} rows={ROWS} rowKey={(r) => r.id} columnsControl={false} />);
    expect(screen.queryByRole('button', { name: 'Columns' })).toBeNull();
    expect(headers()).toHaveLength(4);
  });

  it('a wide table starts with its secondary columns hidden, a narrow one with none', () => {
    const wide = ['name', 'status', 'kind', 'city', 'region', 'rating', 'createdUtc', 'updatedUtc', 'id', 'tags'].map((key) => ({ key, label: key }));
    expect(defaultHiddenColumns(wide)).toEqual(['updatedUtc', 'id', 'tags']);
    expect(defaultHiddenColumns(wide.slice(0, 5).concat({ key: 'id', label: 'Id' }))).toEqual([]);
    expect(defaultHiddenColumns([{ key: 'name', label: 'Name' }, { key: 'notes', label: 'Notes', defaultHidden: true }])).toEqual(['notes']);
    expect(isPinnedColumn({ key: 'updatedUtc', label: 'Updated', pinned: true }, 3)).toBe(true);
  });

  it('a hand-written table gets the same control, with the choice saved', async () => {
    const table = (
      <TableColumns id="test.raw">
        <table>
          <thead><tr><th>Title</th><th>Owner</th><th>Created</th><th></th></tr></thead>
          <tbody><tr><td>A</td><td>B</td><td>C</td><td>menu</td></tr></tbody>
        </table>
      </TableColumns>
    );
    const view = render(table);
    fireEvent.click(await screen.findByRole('button', { name: 'Columns' }));
    expect(screen.getByRole('checkbox', { name: /Title/ })).toBeDisabled();
    fireEvent.click(screen.getByRole('checkbox', { name: 'Owner' }));
    await waitFor(() => expect(view.container.querySelector('style').textContent).toContain(':nth-child(2)'));
    expect(JSON.parse(window.localStorage.getItem('tk.table.test.raw.hiddenColumns'))).toEqual(['1']);
    view.unmount();
    const again = render(table);
    await waitFor(() => expect(again.container.querySelector('style').textContent).toContain(':nth-child(2)'));
  });
});
