import { describe, expect, it } from 'vitest';
import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route } from 'react-router-dom';
import { createBroadcastsFake, createFakeWebSocket, createIdeasFake, createSupportFake, welcomeData } from '@trelliskit/shared/testing';
import SupportView, { NewTicketView, validateTicket } from '../src/views/member/SupportView.jsx';
import TicketView from '../src/views/member/TicketView.jsx';
import IdeasView from '../src/views/member/IdeasView.jsx';
import IdeaView from '../src/views/member/IdeaView.jsx';
import AnnouncementsView, { AnnouncementView } from '../src/views/member/AnnouncementsView.jsx';
import { ideaSubmissionsOf } from '../src/utils/messaging.js';
import { webPathFor } from '../src/utils/engagement.js';
import { makeMe, renderSite } from './helpers.jsx';
import { fakeMemberFetch, renderPhase5, siteWith } from './phase5Helpers.jsx';

const supportRoutes = (
  <>
    <Route path="/app/support/new" element={<NewTicketView />} />
    <Route path="/app/support/tickets/:id" element={<TicketView />} />
  </>
);

describe('support', () => {
  it('validates a request before sending it', () => {
    expect(validateTicket({ type: '', title: 'ab', body: 'short' })).toEqual({ type: 'Choose a type.', title: 'Subjects are 3 to 140 characters.', body: 'Describe the problem in at least 10 characters.' });
    expect(validateTicket({ type: 'Bug', title: 'Crash on upload', body: 'The app closes when I upload.' })).toEqual({});
  });

  it('opens a request with type help text and diagnostics off by default, then shows its timeline', async () => {
    const fake = createSupportFake({ ticketTypes: [{ id: 'tty_bug', key: 'Bug', label: 'Bug', description: 'Something is broken', helpText: 'Tell us the steps.', staffRecipients: [], sortOrder: 10, isRetired: false, labels: [], tags: {} }] });
    const { fetchImpl, calls } = fakeMemberFetch(fake);
    await renderPhase5(<SupportView />, { fetchImpl, path: '/app/support', routePath: '/app/support', extraRoutes: supportRoutes });
    expect(await screen.findByText('You have not opened any support requests.')).toBeInTheDocument();
    await userEvent.click(screen.getAllByRole('link', { name: 'New request' })[0]);
    const form = await screen.findByRole('form', { name: 'New support request' });
    expect(within(form).getByRole('checkbox', { name: /Include app and browser details/ })).not.toBeChecked();
    await userEvent.click(within(form).getByRole('button', { name: 'Send request' }));
    expect(within(form).getByText('Choose a type.')).toBeInTheDocument();
    await waitFor(() => expect(within(form).getByRole('option', { name: 'Bug' })).toBeInTheDocument());
    await userEvent.selectOptions(within(form).getByLabelText('Type'), 'Bug');
    expect(within(form).getByRole('note')).toHaveTextContent('Tell us the steps.');
    expect(within(form).getByText('Something is broken')).toBeInTheDocument();
    await userEvent.type(within(form).getByLabelText('Subject'), 'Upload crashes');
    await userEvent.type(within(form).getByLabelText('Describe the problem'), 'The page closes when I upload a photo.');
    await userEvent.click(within(form).getByRole('button', { name: 'Send request' }));
    await waitFor(() => expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/tickets').body).toMatchObject({ type: 'Bug', title: 'Upload crashes', includeDiagnostics: false, mediaIds: [] }));
    expect(await screen.findByRole('heading', { name: 'Upload crashes' })).toBeInTheDocument();
    expect(screen.getByText('Request #1001')).toBeInTheDocument();
    expect(await screen.findAllByText('The page closes when I upload a photo.')).toHaveLength(1);

    await userEvent.type(screen.getByRole('textbox', { name: 'Your reply' }), 'More details here.');
    await userEvent.click(screen.getByRole('button', { name: 'Send reply' }));
    expect(await screen.findByText('More details here.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Close request' }));
    await waitFor(() => expect(calls.some((c) => c.path.endsWith('/close'))).toBe(true));
    expect(await screen.findByRole('button', { name: 'Reopen request' })).toBeInTheDocument();
    expect(screen.getByText('This request is closed. Reopen it to reply.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Reopen request' }));
    expect(await screen.findByRole('button', { name: 'Close request' })).toBeInTheDocument();
  });

  it('lists requests with status chips and refreshes a ticket on ticket.updated', async () => {
    const fake = createSupportFake();
    const sockets = createFakeWebSocket();
    const { fetchImpl, calls } = fakeMemberFetch(fake);
    const ticket = fake.handle({ method: 'POST', path: '/api/v1/tickets', body: { type: 'Billing', title: 'Charged twice', body: 'I was charged twice this month.' }, headers: {} }).body;
    fake.handle({ method: 'POST', path: '/api/v1/admin/tickets/' + ticket.id + '/messages', body: { kind: 'Note', body: 'Internal: check Stripe' }, headers: {} });
    await renderPhase5(<SupportView />, { fetchImpl, path: '/app/support', routePath: '/app/support', extraRoutes: supportRoutes, realtime: { WebSocketImpl: sockets.WebSocket } });
    const row = await screen.findByRole('link', { name: /Charged twice/ });
    expect(row).toHaveAttribute('href', '/app/support/tickets/' + ticket.id);
    await userEvent.click(screen.getByRole('button', { name: 'Waiting on you' }));
    await waitFor(() => expect(calls.filter((c) => c.path === '/api/v1/tickets/enumerate').at(-1).body.filters).toEqual({ status: 'WaitingOnMember' }));
    expect(await screen.findByText('No requests match these filters.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'All' }));
    await userEvent.click(await screen.findByRole('link', { name: /Charged twice/ }));
    expect(await screen.findByRole('heading', { name: 'Charged twice' })).toBeInTheDocument();
    // Internal notes never reach members.
    expect(screen.queryByText('Internal: check Stripe')).toBeNull();

    fake.handle({ method: 'POST', path: '/api/v1/admin/tickets/' + ticket.id + '/messages', body: { kind: 'Reply', body: 'We refunded the second charge.' }, headers: {} });
    const socket = sockets.last();
    await act(async () => { socket.accept(); });
    await act(async () => { socket.serverSend('welcome', welcomeData()); });
    await act(async () => { socket.serverSend('ticket.updated', { ticketId: ticket.id, status: 'WaitingOnMember', lastActivityUtc: '2026-11-12T12:00:00Z' }); });
    expect(await screen.findByText('We refunded the second charge.')).toBeInTheDocument();
    expect(screen.getByText(/Support team/)).toBeInTheDocument();
  });

  it('shows the support email when support is turned off', async () => {
    const fake = createSupportFake();
    const { fetchImpl, calls } = fakeMemberFetch(fake);
    await renderPhase5(<SupportView />, { fetchImpl, path: '/app/support', routePath: '/app/support', site: siteWith({ features: { support: false } }) });
    expect(await screen.findByRole('link', { name: 'help@dojo.test' })).toHaveAttribute('href', 'mailto:help@dojo.test');
    expect(calls.some((c) => c.path === '/api/v1/tickets/enumerate')).toBe(false);
  });

  it('is reachable from the paywall in a fully gated model without a membership', async () => {
    const fake = createSupportFake();
    const me = makeMe({ membership: { isActive: false }, membershipRequired: true, accessTier: 'SignedIn' });
    const { fetchImpl } = fakeMemberFetch(fake, me);
    window.localStorage.setItem('tk.hasSession', '1');
    renderSite('/app/support', { fetchImpl, site: siteWith({ access: { model: 'FullyGated', gatedFeatures: [], anonymousBrowsing: false } }) });
    expect(await screen.findByText('You have not opened any support requests.', {}, { timeout: 3000 })).toBeInTheDocument();
  });

  it('maps notification paths to the new member routes', () => {
    expect(webPathFor('/support/tickets/tkt_1')).toBe('/app/support/tickets/tkt_1');
    expect(webPathFor('/ideas/ida_1')).toBe('/app/ideas/ida_1');
    expect(webPathFor('/announcements/bct_1')).toBe('/app/announcements/bct_1');
    expect(webPathFor('/messages/requests')).toBe('/app/messages/requests');
    expect(webPathFor('/admin/support?ticket=tkt_1')).toBe('/admin/support?ticket=tkt_1');
    expect(webPathFor('/me/directory/submissions')).toBe('/app/directory/submissions');
    expect(webPathFor('/venues/ven_1')).toBe('/app/venues/ven_1');
  });
});

const ideaRoutes = <Route path="/app/ideas/:id" element={<IdeaView />} />;

describe('ideas', () => {
  it('reads the submission mode from the property', () => {
    expect(ideaSubmissionsOf({})).toBe('StaffOnly');
    expect(ideaSubmissionsOf({ ideas: { memberSubmissions: 'Direct' } })).toBe('Direct');
  });

  it('shows the board with orderings, status chips, search, and votes', async () => {
    const fake = createIdeasFake();
    fake.addIdea({ title: 'Dark mode', voters: ['usr_5', 'usr_6'], author: null });
    fake.addIdea({ title: 'Calendar export', status: 'Planned', voters: ['usr_5'] });
    const { fetchImpl, calls } = fakeMemberFetch(fake);
    await renderPhase5(<IdeasView />, { fetchImpl, path: '/app/ideas', routePath: '/app/ideas', extraRoutes: ideaRoutes });
    const list = await screen.findByRole('list', { name: 'Ideas' });
    expect(within(list).getAllByRole('link').map((a) => a.textContent)).toEqual(['Dark mode', 'Calendar export']);
    expect(screen.queryByRole('button', { name: 'Suggest an idea' })).toBeNull();
    expect(screen.getByText(/The team publishes ideas from support requests/)).toBeInTheDocument();
    const enumerations = () => calls.filter((c) => c.path === '/api/v1/ideas/enumerate');
    expect(enumerations()[0].body).toMatchObject({ ordering: 'Top', maxResults: 20 });

    await userEvent.click(await screen.findByRole('button', { name: 'Planned' }));
    await waitFor(() => expect(enumerations().at(-1).body.filters).toEqual({ status: ['Planned'] }));
    expect(await screen.findByRole('link', { name: 'Calendar export' })).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole('link', { name: 'Dark mode' })).toBeNull());
    await userEvent.click(screen.getByRole('button', { name: 'Planned' }));

    await userEvent.selectOptions(screen.getByRole('combobox', { name: /Order/ }), 'Trending');
    await waitFor(() => expect(enumerations().at(-1).body.ordering).toBe('Trending'));

    const vote = await screen.findByRole('button', { name: 'Vote for Dark mode' });
    await userEvent.click(vote);
    expect(await screen.findByRole('button', { name: 'Remove your vote for Dark mode' })).toHaveAttribute('aria-pressed', 'true');
    expect(calls.some((c) => c.method === 'PUT' && /\/ideas\/ida_[^/]+\/vote$/.test(c.path))).toBe(true);
  });

  it('offers the submit form when submissions are Direct', async () => {
    const fake = createIdeasFake();
    const { fetchImpl, calls } = fakeMemberFetch(fake);
    await renderPhase5(<IdeasView />, { fetchImpl, path: '/app/ideas', routePath: '/app/ideas', extraRoutes: ideaRoutes, site: siteWith({ ideas: { memberSubmissions: 'Direct' } }) });
    await userEvent.click(await screen.findByRole('button', { name: 'Suggest an idea' }));
    const dialog = await screen.findByRole('dialog', { name: 'Suggest an idea' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Post idea' }));
    expect(within(dialog).getByText('Titles are 3 to 140 characters.')).toBeInTheDocument();
    await userEvent.type(within(dialog).getByLabelText('Title'), 'Offline mode');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Post idea' }));
    await waitFor(() => expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/ideas').body).toMatchObject({ title: 'Offline mode' }));
    expect(await screen.findByRole('heading', { name: 'Offline mode' })).toBeInTheDocument();
  });

  it('shows an idea with the team note, follow, comments, and the merged banner', async () => {
    const fake = createIdeasFake();
    const target = fake.addIdea({ title: 'Dark theme', voters: ['usr_5'] });
    const idea = fake.addIdea({ title: 'Night mode', voters: ['usr_6'] });
    fake.handle({ method: 'PUT', path: '/api/v1/admin/ideas/' + idea.id + '/status', body: { status: 'Planned', statusNote: 'Coming next month.' } });
    const { fetchImpl, calls } = fakeMemberFetch(fake);
    const view = await renderPhase5(<IdeaView />, { fetchImpl, path: '/app/ideas/' + idea.id, routePath: '/app/ideas/:id' });
    expect(await screen.findByRole('heading', { name: 'Night mode' })).toBeInTheDocument();
    expect(screen.getByText('Coming next month.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Follow this idea' }));
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT' && c.path.endsWith('/follow'))).toBe(true));
    // Idea comments use the shared comment tree: sorting, the tree GET, and the generic composer.
    expect(await screen.findByRole('radiogroup', { name: /sort/i })).toBeInTheDocument();
    await waitFor(() => expect(calls.some((c) => c.method === 'GET' && c.path === '/api/v1/ideas/' + idea.id + '/comments')).toBe(true));
    await userEvent.type(screen.getByPlaceholderText('Add a comment'), 'I would use this daily');
    await userEvent.click(screen.getByRole('button', { name: 'Comment' }));
    expect(await screen.findByText('I would use this daily')).toBeInTheDocument();
    expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/ideas/' + idea.id + '/comments').body).toMatchObject({ body: 'I would use this daily' });
    await userEvent.click(screen.getByRole('button', { name: 'Reply' }));
    expect(screen.getAllByRole('button', { name: 'Reply' }).length).toBeGreaterThan(1);
    view.unmount();

    fake.handle({ method: 'POST', path: '/api/v1/admin/ideas/' + idea.id + '/merge', body: { intoIdeaId: target.id } });
    await renderPhase5(<IdeaView />, { fetchImpl, path: '/app/ideas/' + idea.id, routePath: '/app/ideas/:id' });
    expect(await screen.findByText('This idea was merged')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open the merged idea' })).toHaveAttribute('href', '/app/ideas/' + target.id);
    expect(screen.getByText('Comments are closed for this idea.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Vote for Night mode' })).toBeDisabled();
  });

  it('shows the upgrade prompt where ideas are gated', async () => {
    const fake = createIdeasFake();
    const { fetchImpl, calls } = fakeMemberFetch(fake, makeMe({ membership: { isActive: false }, accessTier: 'SignedIn' }));
    await renderPhase5(<IdeasView />, { fetchImpl, path: '/app/ideas', routePath: '/app/ideas', site: siteWith({ access: { model: 'PartiallyGated', gatedFeatures: ['ideas'], anonymousBrowsing: false } }) });
    expect(await screen.findByRole('region', { name: /Become a/ })).toBeInTheDocument();
    expect(calls.some((c) => c.path === '/api/v1/ideas/enumerate')).toBe(false);
  });
});

describe('announcements', () => {
  it('lists received announcements and opens one, including from the notification path', async () => {
    const fake = createBroadcastsFake({ audienceCount: 1 });
    const draft = fake.handle({ method: 'POST', path: '/api/v1/admin/broadcasts', body: { title: 'Holiday', channels: { inApp: true }, inApp: { title: 'Holiday hours', body: 'Closed **Monday**.' } } }).body;
    fake.handle({ method: 'POST', path: '/api/v1/admin/broadcasts/' + draft.id + '/send', body: { confirmCount: 1 } });
    const { fetchImpl } = fakeMemberFetch(fake);
    await renderPhase5(<AnnouncementsView />, { fetchImpl, path: '/app/announcements', routePath: '/app/announcements', extraRoutes: <Route path="/app/announcements/:id" element={<AnnouncementView />} /> });
    // The page summary names the property (its {name} placeholder is filled).
    await screen.findByRole('link', { name: 'Holiday hours' });
    expect(document.body.textContent).not.toContain('{name}');
    await userEvent.click(await screen.findByRole('link', { name: 'Holiday hours' }));
    expect(await screen.findByRole('heading', { name: 'Holiday hours' })).toBeInTheDocument();
    expect(screen.getByText('Monday', { selector: 'strong' })).toBeInTheDocument();
  });

  it('redirects /announcements/:id into the member app', async () => {
    const fake = createBroadcastsFake();
    const { fetchImpl, calls } = fakeMemberFetch(fake);
    window.localStorage.setItem('tk.hasSession', '1');
    renderSite('/announcements/bct_missing', { fetchImpl, site: siteWith() });
    expect(await screen.findByText('This announcement is not available.', {}, { timeout: 3000 })).toBeInTheDocument();
    expect(calls.some((c) => c.path === '/api/v1/announcements/bct_missing')).toBe(true);
  });
});
