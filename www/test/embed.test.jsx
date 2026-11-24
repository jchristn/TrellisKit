import { afterEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import strings from '../src/strings.js';
import { BUILD_SITE } from '../src/property/buildSite.js';
import { embedTargetOrigin, embeddingOrigin } from '../src/views/ai/PublicAssistantView.jsx';
import { createFetch, json, renderSite } from './helpers.jsx';

const site = { ...BUILD_SITE, property: { ...BUILD_SITE.property, features: { ...(BUILD_SITE.property.features || {}), ai: true } } };

function summary(embed) {
  return {
    id: 'ast_1', name: 'Helper', title: 'Community helper', description: null, avatar: { emoji: null }, starterPrompts: [], disclaimer: null,
    streaming: false, showReasoning: false, audience: 'Public', toolsEnabled: false, knowledgeEnabled: false,
    limits: { maxMessageCharacters: 4000, maxMessagesPerConversation: 100, messagesPerDay: 10, remainingToday: 10 }, privacy: { adminsCanRead: true },
    embed, labels: [], tags: {}
  };
}

function frameFetch(embed) {
  return createFetch((call) => {
    if (call.path === '/api/v1/assistant-visitors') return json({ visitorId: 'avs_1', visitorToken: 'vt-1' }, 201);
    if (call.path === '/api/v1/assistants/ast_1') return json(summary(embed));
    if (call.path === '/api/v1/assistants/ast_1/conversations/enumerate') return json({ objects: [], totalRecords: 0, endOfResults: true, continuationToken: null });
    return undefined;
  });
}

const originalParent = Object.getOwnPropertyDescriptor(window, 'parent');
const originalReferrer = Object.getOwnPropertyDescriptor(Document.prototype, 'referrer');

function embedIn(referrer) {
  const postMessage = vi.fn();
  Object.defineProperty(window, 'parent', { value: { postMessage }, configurable: true });
  Object.defineProperty(document, 'referrer', { value: referrer, configurable: true });
  return postMessage;
}

afterEach(() => {
  if (originalParent) Object.defineProperty(window, 'parent', originalParent);
  delete document.referrer;
  if (originalReferrer) Object.defineProperty(Document.prototype, 'referrer', originalReferrer);
});

describe('embed origin checks', () => {
  it('posts only to an allowed embedding origin, never to "*"', () => {
    const embed = { allowedOrigins: ['https://partner.example', 'https://www.example.test:8443'] };
    expect(embedTargetOrigin(embed, 'https://partner.example')).toBe('https://partner.example');
    expect(embedTargetOrigin(embed, 'HTTPS://Partner.Example/')).toBe('https://partner.example');
    expect(embedTargetOrigin(embed, 'https://www.example.test:8443')).toBe('https://www.example.test:8443');
    expect(embedTargetOrigin(embed, 'https://evil.example')).toBeNull();
    expect(embedTargetOrigin(embed, null)).toBeNull();
    expect(embedTargetOrigin(null, 'https://partner.example')).toBeNull();
    expect(embedTargetOrigin({ allowedOrigins: ['*'] }, 'https://partner.example')).toBeNull();
  });

  it('reads the embedding origin from ancestorOrigins, then the referrer', () => {
    expect(embeddingOrigin({ location: { ancestorOrigins: ['https://a.example'] }, document: { referrer: 'https://b.example/x' } })).toBe('https://a.example');
    expect(embeddingOrigin({ location: {}, document: { referrer: 'https://b.example/page?q=1' } })).toBe('https://b.example');
    expect(embeddingOrigin({ location: {}, document: { referrer: '' } })).toBeNull();
    expect(embeddingOrigin({ location: {}, document: { referrer: 'javascript:alert(1)' } })).toBeNull();
  });

  it('the embed page posts ready and close to the allowed parent origin only', async () => {
    const postMessage = embedIn('https://partner.example/help');
    renderSite('/embed/assistants/ast_1', { site, fetchImpl: frameFetch({ launcherLabel: null, position: 'BottomRight', allowedOrigins: ['https://partner.example'] }).fetchImpl });
    expect(await screen.findByRole('heading', { name: /Community helper/ })).toBeInTheDocument();
    await waitFor(() => expect(postMessage).toHaveBeenCalledWith({ type: 'tk-assistant:ready' }, 'https://partner.example'));
    screen.getByRole('button', { name: strings.ai.embedClose }).click();
    expect(postMessage).toHaveBeenCalledWith({ type: 'tk-assistant:close' }, 'https://partner.example');
    expect(postMessage.mock.calls.every((c) => c[1] !== '*')).toBe(true);
  });

  it('posts nothing and refuses to render inside a page that is not allowed', async () => {
    const postMessage = embedIn('https://evil.example/');
    renderSite('/embed/assistants/ast_1', { site, fetchImpl: frameFetch({ launcherLabel: null, position: 'BottomRight', allowedOrigins: ['https://partner.example'] }).fetchImpl });
    expect(await screen.findByTestId('embed-not-allowed')).toHaveTextContent(strings.ai.embedNotAllowed);
    expect(postMessage).not.toHaveBeenCalled();
  });
});
