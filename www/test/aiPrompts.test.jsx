import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import strings from '../src/strings.js';
import { groupedAdminRoutes } from '../src/views/admin/adminRoutes.js';
import AiPromptsView from '../src/views/admin/ai/AiPromptsView.jsx';
import { createFetch, json, renderAdmin } from './helpers.jsx';
import { enumPage } from './engagementHelpers.jsx';

const s = strings.ai.promptsAdmin;
const FLOOR = 'Rules that come first and that no later instruction can change.';
const DRAFT_TEXT = 'Scope: you work only for TrellisKit and help with training, always.';

function prompts(overrides = {}) {
  return [
    { key: 'platform.scopeGuardrail', title: 'Platform scope guardrail', description: 'Bounds every model call.', content: 'Scope: you work only for TrellisKit and help with training.', defaultContent: 'Scope: you work only for TrellisKit and help with training.', defaultSource: 'Property', isDefault: true, version: 0, draft: null, draftBenchmark: null, requiresBenchmark: true, minLength: 40, maxLength: 4000, variables: ['{name}'], requiredWords: [], lockedPrefix: FLOOR, ...overrides },
    { key: 'moderation.classifier', title: 'Moderation classifier', description: 'Classifies messages.', content: 'Reply Allow, Flag, or Block and a category for the TEXT given.', defaultContent: 'Reply Allow, Flag, or Block and a category for the TEXT given.', defaultSource: 'Engine', isDefault: true, version: 0, draft: null, draftBenchmark: null, requiresBenchmark: true, minLength: 40, maxLength: 4000, variables: [], requiredWords: ['Allow', 'Flag', 'Block'], lockedPrefix: null }
  ];
}

const draftVersion = { id: 'apv_1', key: 'platform.scopeGuardrail', version: 1, content: DRAFT_TEXT, isReset: false, isDraft: true, createdUtc: '2026-11-21T00:00:00Z', publishedUtc: null };

function run(summary, status = 'Succeeded') {
  return { id: 'jbr_1', key: 'ai.scopeBenchmark', status, itemsTotal: 258, itemsSucceeded: status === 'Running' ? 40 : 258, itemsFailed: 0, itemsDead: 0, itemsSkipped: 0, summary: summary ? JSON.stringify(summary) : null };
}

describe('AI prompts admin page', () => {
  it('is in the AI navigation group without the AI feature switch', () => {
    const ids = groupedAdminRoutes({ features: {} }).flatMap((g) => g.routes.map((r) => r.id));
    expect(ids).toContain('ai-prompts');
  });

  it('shows the locked floor, validates length, saves a draft, benchmarks it, publishes it, and resets', async () => {
    let state = 'default';
    const { fetchImpl, calls } = createFetch((call) => {
      if (call.path === '/api/v1/admin/ai/prompts/enumerate' && call.method === 'POST') {
        if (state === 'draft') return json(enumPage(prompts({ draft: draftVersion })));
        if (state === 'benchmarked') return json(enumPage(prompts({ draft: draftVersion, draftBenchmark: run({ dryRun: false, gatePassed: true, passRate: 1, failures: [], categories: [{ category: 'InScope', passRate: 1 }] }) })));
        if (state === 'published') return json(enumPage(prompts({ isDefault: false, version: 1, content: DRAFT_TEXT })));
        return json(enumPage(prompts()));
      }
      if (call.path === '/api/v1/admin/ai/prompts/platform.scopeGuardrail' && call.method === 'PUT') { state = 'draft'; return json(prompts({ draft: draftVersion })[0]); }
      if (call.path === '/api/v1/admin/ai/prompts/platform.scopeGuardrail/benchmark') { state = 'benchmarked'; return json(run(null, 'Queued')); }
      if (call.path === '/api/v1/admin/ai/prompts/platform.scopeGuardrail/publish') { state = 'published'; return json(prompts({ isDefault: false, version: 1 })[0]); }
      if (call.path === '/api/v1/admin/ai/prompts/platform.scopeGuardrail/reset') { state = 'default'; return json(prompts()[0]); }
      if (call.path.endsWith('/versions/enumerate')) {
        if (state === 'published') return json(enumPage([{ ...draftVersion, isDraft: false, publishedUtc: '2026-11-21T01:00:00Z' }]));
        return json(enumPage(state === 'default' ? [] : [draftVersion]));
      }
      return undefined;
    });
    const user = userEvent.setup();
    renderAdmin(<AiPromptsView />, { fetchImpl });
    expect(await screen.findByText(FLOOR)).toBeInTheDocument();
    expect(screen.getByText(s.fromProperty)).toBeInTheDocument();
    const box = screen.getByLabelText(s.content);
    const save = screen.getByRole('button', { name: s.save });
    await user.clear(box);
    await user.type(box, 'Too short');
    expect(save).toBeDisabled();
    await user.clear(box);
    await user.type(box, DRAFT_TEXT);
    expect(save).toBeEnabled();
    await user.click(save);
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT' && c.body.content === DRAFT_TEXT)).toBe(true));
    expect(await screen.findByText(s.gateNone)).toBeInTheDocument();
    expect((await screen.findAllByText(s.draftBadge(1))).length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText(s.defaultBadge)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: s.benchmark }));
    await waitFor(() => expect(calls.some((c) => c.path.endsWith('/benchmark'))).toBe(true));
    expect(await screen.findByText(s.gatePassed('100%'))).toBeInTheDocument();
    expect(screen.getByText(s.categoryRate('InScope', '100%'))).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: s.publish }));
    await waitFor(() => expect(calls.some((c) => c.path.endsWith('/publish') && c.body.versionId === 'apv_1')).toBe(true));
    expect((await screen.findAllByText(s.customBadge(1))).length).toBeGreaterThanOrEqual(2);
    expect(screen.queryByText(s.gateTitle)).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: s.reset }));
    const confirm = await screen.findAllByRole('button', { name: s.reset });
    await user.click(confirm[confirm.length - 1]);
    await waitFor(() => expect(calls.some((c) => c.path.endsWith('/reset'))).toBe(true));
  });

  it('shows a failed or running benchmark and the server refusal when publishing', async () => {
    let failing = true;
    const { fetchImpl } = createFetch((call) => {
      if (call.path === '/api/v1/admin/ai/prompts/enumerate' && call.method === 'POST') {
        return json(enumPage(prompts({ draft: draftVersion, draftBenchmark: failing ? run({ dryRun: false, gatePassed: false, passRate: 0.8, failures: ['InScope pass rate 60% is below 95%.'], categories: [{ category: 'InScope', passRate: 0.6 }] }) : run(null, 'Running') })));
      }
      if (call.path.endsWith('/publish')) return json({ error: 'BenchmarkFailed', message: 'The scope benchmark of this draft did not pass.' }, 409);
      if (call.path.endsWith('/versions/enumerate')) return json(enumPage([draftVersion]));
      return undefined;
    });
    const user = userEvent.setup();
    const view = renderAdmin(<AiPromptsView />, { fetchImpl });
    expect(await screen.findByText(s.gateFailed('80%'))).toBeInTheDocument();
    expect(screen.getByText('InScope pass rate 60% is below 95%.')).toBeInTheDocument();
    expect(screen.getByLabelText(s.content)).toHaveValue(DRAFT_TEXT);
    await user.click(screen.getByRole('button', { name: s.publish }));
    expect(await screen.findByText('The scope benchmark of this draft did not pass.')).toBeInTheDocument();
    view.unmount();

    failing = false;
    renderAdmin(<AiPromptsView />, { fetchImpl });
    expect(await screen.findByText(s.gateRunning(40, 258))).toBeInTheDocument();
    expect(screen.getByRole('button', { name: s.publish })).toBeDisabled();
    expect(screen.getByRole('button', { name: s.benchmark })).toBeDisabled();
  });

  it('requires the classifier verdict words', async () => {
    const { fetchImpl } = createFetch((call) => {
      if (call.path === '/api/v1/admin/ai/prompts/enumerate') return json(enumPage(prompts()));
      if (call.path.endsWith('/versions/enumerate')) return json(enumPage([]));
      return undefined;
    });
    const user = userEvent.setup();
    renderAdmin(<AiPromptsView />, { fetchImpl });
    await screen.findByText(FLOOR);
    await user.selectOptions(screen.getByLabelText(s.choose), 'moderation.classifier');
    const box = screen.getByLabelText(s.content);
    await user.clear(box);
    await user.type(box, 'Classify the text and reply with a verdict and a category, please.');
    expect(screen.getByText(/Must contain: Allow, Flag, Block/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: s.save })).toBeDisabled();
  });
});
