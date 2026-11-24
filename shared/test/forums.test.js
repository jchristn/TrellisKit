import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  ApiClient, cardPath, commentTargetOf, conversationBasePath, conversationPath, emptyPollDraft, feedbackTransitions, forumMethods, groupForumsBySection,
  isKnownThread, NotificationCategoryOrder, pollDisplay, pollPayload, pollSelectionProblem, snippetSegments, splitPinned, threadActions, threadCanComment,
  threadCanReact, threadCreateBody, threadFilters, threadPath, togglePollOption, unreadBadge, validatePollDraft, validateThreadDraft
} from '../src/index.js';
import { createForumsFake, makeForum, makePoll, makeThread, makeThreadComment, moderatorViewer } from '../src/testing/index.js';

function recorder() {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ method: init.method, url: url.replace('/api/v1', ''), body: init.body ? JSON.parse(init.body) : undefined });
    return new Response(JSON.stringify({ ok: true }), { status: 200, headers: { 'content-type': 'application/json' } });
  };
  return { calls, client: new ApiClient({ fetchImpl }) };
}

test('every section 19 ApiClient method exists and maps to its route', async () => {
  const names = ['enumerateForums', 'getForum', 'followForum', 'unfollowForum', 'markForumRead', 'enumerateForumThreads', 'enumerateThreads', 'getThread', 'createThread', 'updateThread', 'deleteThread',
    'enumerateThreadRevisions', 'pinThread', 'unpinThread', 'lockThread', 'unlockThread', 'moveThread', 'acceptAnswer', 'clearAcceptedAnswer', 'putThreadReaction', 'deleteThreadReaction',
    'enumerateThreadReactions', 'enumerateThreadComments', 'createThreadComment', 'votePoll', 'removePollVote', 'closePoll', 'setFeedbackStatus', 'promoteThread', 'unpromoteThread',
    'discussFeedPost', 'undiscussFeedPost', 'followThread', 'unfollowThread', 'enumerateMyFollows', 'enumeratePublicThreads', 'getPublicThread', 'adminEnumerateForums', 'adminCreateForum',
    'adminUpdateForum', 'adminOrderForums', 'adminArchiveForum', 'adminUnarchiveForum', 'adminDeleteForum', 'adminEnumerateForumModerators', 'adminEnumerateThreads', 'adminEnumerateTrending',
    'adminForumMetrics', 'createForumMute', 'enumerateForumMutes', 'endForumMute', 'searchContent'];
  for (const name of names) assert.equal(typeof ApiClient.prototype[name], 'function', name);
  assert.equal(Object.keys(forumMethods).length >= names.length, true);

  const { calls, client } = recorder();
  await client.enumerateForums({ maxResults: 500, filters: { followedOnly: true } });
  await client.getForum('general');
  await client.followForum('frm_1');
  await client.enumerateForumThreads('frm_1', { ordering: 'Hot', labels: ['x'], filters: threadFilters({ types: ['Question'], answered: false }) });
  await client.lockThread('thr_1', 'Done');
  await client.moveThread('thr_1', { forumId: 'frm_2' });
  await client.votePoll('pol_1', ['pop_a']);
  await client.setFeedbackStatus('thr_1', 'Reviewed', 'cmt_1');
  await client.adminEnumerateForums({ maxResults: 900 });
  await client.adminOrderForums(['frm_b', 'frm_a']);
  await client.revokeForumModerator('usr_1', 'frm_1');
  await client.adminForumMetrics({ filters: { days: 1000 } });
  await client.endForumMute('fmu_1', 'Lifted');
  await client.enumeratePublicThreads({ maxResults: 50 });
  await client.searchContent({ search: 'reset', filters: { types: ['Thread', 'FeedPost'] } });
  assert.deepEqual(calls.map((c) => c.method + ' ' + c.url), [
    'POST /forums/enumerate', 'GET /forums/general', 'PUT /forums/frm_1/follow', 'POST /forums/frm_1/threads/enumerate', 'POST /threads/thr_1/lock', 'POST /threads/thr_1/move',
    'PUT /polls/pol_1/votes', 'PUT /threads/thr_1/feedback', 'POST /admin/forums/enumerate', 'PUT /admin/forums/order', 'DELETE /admin/moderators/usr_1?resourceType=Forum&resourceId=frm_1',
    'POST /admin/metrics/forums/enumerate', 'DELETE /admin/moderation/forum-mutes/fmu_1', 'POST /public/threads/enumerate', 'POST /search/enumerate'
  ]);
  assert.deepEqual(calls[0].body, { maxResults: 100, filters: { followedOnly: true } });
  assert.deepEqual(calls[2].body, { level: 'NewThreads' });
  assert.deepEqual(calls[3].body, { maxResults: 25, ordering: 'Hot', labels: ['x'], labelMatch: 'All', filters: { type: ['Question'], answered: false } });
  assert.deepEqual(calls[4].body, { reason: 'Done' });
  assert.deepEqual(calls[5].body, { forumId: 'frm_2', notifyAuthor: true });
  assert.deepEqual(calls[7].body, { status: 'Reviewed', reviewCommentId: 'cmt_1' });
  assert.equal(calls[8].body.maxResults, 500);
  assert.equal(calls[13].body.maxResults, 20);
  assert.deepEqual(calls[14].body.filters, { types: ['Thread', 'FeedPost'] });
});

test('the commentTarget helper routes comments and reactions to the conversation owner', async () => {
  const { calls, client } = recorder();
  const original = { id: 'thr_1', kind: 'Original', commentTarget: { type: 'Thread', id: 'thr_1' } };
  const linked = { id: 'thr_2', kind: 'LinkedFeedPost', commentTarget: { type: 'FeedPost', id: 'fpo_9' } };
  await client.enumerateThreadComments(original, { ordering: 'Top', filters: { depth: 3 } });
  await client.createThreadComment(linked, { body: 'hi' });
  await client.putThreadReaction(linked, 'like');
  await client.deleteThreadReaction('thr_3', 'like');
  await client.enumerateThreadReactions(original);
  assert.deepEqual(calls.map((c) => c.method + ' ' + c.url), ['POST /threads/thr_1/comments/enumerate', 'POST /feed/fpo_9/comments', 'PUT /feed/fpo_9/reactions/like', 'DELETE /threads/thr_3/reactions/like', 'POST /threads/thr_1/reactions/enumerate']);
  assert.equal(conversationBasePath(linked), '/feed/fpo_9');
  assert.throws(() => conversationBasePath(null));
  assert.deepEqual(commentTargetOf({ id: 'fpo_1' }), { type: 'FeedPost', id: 'fpo_1' });
  assert.deepEqual(commentTargetOf({ id: 'fpo_2', kind: 'PromotedThread', commentTarget: { type: 'Thread', id: 'thr_8' } }), { type: 'Thread', id: 'thr_8' });
  assert.deepEqual(commentTargetOf('thr_5'), { type: 'Thread', id: 'thr_5' });
  assert.equal(commentTargetOf('x_1'), null);
  assert.equal(conversationPath({ type: 'Thread', id: 'thr_1' }, 'cmt_2'), '/threads/thr_1?comment=cmt_2');
  assert.equal(conversationPath({ type: 'FeedPost', id: 'fpo_1' }), '/feed/fpo_1');
  assert.equal(conversationPath({ type: 'Idea', id: 'ida_1' }, 'cmt_3'), '/ideas/ida_1?comment=cmt_3');
  assert.equal(conversationPath({ type: 'Lesson', id: 'les_1' }), null);
  assert.equal(cardPath({ id: 'fpo_2', kind: 'PromotedThread', source: { type: 'Thread', id: 'thr_8' } }), '/threads/thr_8');
  assert.equal(cardPath({ id: 'fpo_3', kind: 'Original' }), '/feed/fpo_3');
  assert.equal(cardPath({ id: 'thr_4' }), '/threads/thr_4');
  assert.equal(threadPath('thr_1'), '/threads/thr_1');
});

test('poll display uses only server fields, hides counts when results are hidden, and computes missing percentages', () => {
  const hidden = pollDisplay({ id: 'pol_1', resultsVisible: false, totalVoters: 38, myVote: [], options: [{ id: 'a', label: 'A', position: 1, voteCount: null, percent: null }] });
  assert.equal(hidden.resultsVisible, false);
  assert.equal(hidden.totalVoters, 38);
  assert.equal(hidden.options[0].percent, null);
  assert.equal(hidden.options[0].barPercent, 0);
  assert.equal(hidden.canChangeVote, true);

  const shown = pollDisplay({ id: 'pol_1', resultsVisible: true, multipleChoice: true, maxChoices: 2, totalVoters: 38, myVote: ['b'], allowVoteChange: false, options: [
    { id: 'b', label: 'Thursday', position: 2, voteCount: 20, percent: 52.6 },
    { id: 'a', label: 'Tuesday', position: 1, voteCount: 12 },
    { id: 'c', label: 'Saturday', position: 3, voteCount: 6, percent: 15.8 }
  ] });
  assert.deepEqual(shown.options.map((o) => [o.id, o.percent, o.isMine, o.isLeading]), [['a', 31.6, false, false], ['b', 52.6, true, true], ['c', 15.8, false, false]]);
  assert.equal(shown.canChangeVote, false);
  assert.equal(shown.maxChoices, 2);
  assert.equal(pollDisplay({ resultsVisible: true, totalVoters: 0, options: [{ id: 'a', voteCount: 0 }] }).options[0].percent, 0);
  assert.equal(pollDisplay(null), null);
  assert.equal(pollDisplay({ isClosed: true, options: [] }).canChangeVote, false);

  const single = { multipleChoice: false, options: [{ id: 'a' }, { id: 'b' }] };
  assert.equal(pollSelectionProblem(single, []), 'selectOne');
  assert.equal(pollSelectionProblem(single, ['a', 'b']), 'tooMany');
  assert.equal(pollSelectionProblem(single, ['z']), 'unknown');
  assert.equal(pollSelectionProblem({ ...single, multipleChoice: true, maxChoices: 1 }, ['a', 'b']), 'tooMany');
  assert.equal(pollSelectionProblem(single, ['a']), null);
  assert.deepEqual(togglePollOption(single, ['a'], 'b'), ['b']);
  assert.deepEqual(togglePollOption(single, ['a'], 'a'), []);
  const multi = { multipleChoice: true, maxChoices: 2 };
  assert.deepEqual(togglePollOption(multi, ['a', 'b'], 'c'), ['a', 'b']);
  assert.deepEqual(togglePollOption(multi, ['a'], 'c'), ['a', 'c']);
});

test('poll drafts validate options, max choices, and the close window; the payload sends voter visibility only when visible', () => {
  const now = new Date('2026-11-11T00:00:00Z');
  const draft = emptyPollDraft();
  assert.equal(draft.resultsVisibility, 'AfterVote');
  assert.deepEqual(validatePollDraft(draft, { now }), { options: 'optionRequired' });
  assert.deepEqual(validatePollDraft({ options: [{ label: 'A' }] }, { now }), { options: 'tooFewOptions' });
  assert.deepEqual(validatePollDraft({ options: [{ label: 'Yes' }, { label: ' yes ' }] }, { now }), { options: 'duplicateOption' });
  assert.deepEqual(validatePollDraft({ options: [{ label: 'A' }, { label: 'B' }], multipleChoice: true, maxChoices: 3 }, { now }), { maxChoices: 'maxChoicesInvalid' });
  assert.deepEqual(validatePollDraft({ options: [{ label: 'A' }, { label: 'B' }], closesUtc: '2026-11-11T00:01:00Z' }, { now }), { closesUtc: 'closeTooSoon' });
  assert.deepEqual(validatePollDraft({ options: [{ label: 'A' }, { label: 'B' }], closesUtc: '2028-11-11T00:00:00Z' }, { now }), { closesUtc: 'closeTooLate' });
  assert.deepEqual(validatePollDraft({ options: Array.from({ length: 21 }, (_, i) => ({ label: 'O' + i })) }, { now }), { options: 'tooManyOptions' });
  assert.deepEqual(pollPayload({ ...draft, options: [{ label: ' A ' }, { label: 'B' }] }), { options: [{ label: 'A' }, { label: 'B' }], multipleChoice: false, maxChoices: null, closesUtc: null, resultsVisibility: 'AfterVote', allowVoteChange: true });
  assert.equal(pollPayload({ ...draft, voterVisibility: 'Visible' }).voterVisibility, 'Visible');
});

test('thread drafts, create bodies, filters, grouping, and menus', () => {
  assert.deepEqual(validateThreadDraft({ type: 'Discussion', title: 'Hi', body: '' }), { title: 'titleTooShort', body: 'bodyRequired' });
  assert.deepEqual(validateThreadDraft({ type: 'Feedback', title: 'Review my video', body: '', videoCount: 0 }), { media: 'videoRequired' });
  assert.deepEqual(validateThreadDraft({ type: 'Poll', title: 'Which night?', pollProblems: { options: 'optionRequired' } }), { poll: 'optionRequired' });
  assert.deepEqual(validateThreadDraft({ type: 'Poll', title: 'Which night?', allowedTypes: ['Discussion'] }), { type: 'typeNotAllowed' });
  assert.deepEqual(validateThreadDraft({ type: 'Question', title: 'How do I reset?', body: 'Help' }), {});
  const body = threadCreateBody({ type: 'Feedback', title: ' T ', body: '', mediaIds: ['med_1'], focus: ' my topics ', poll: { x: 1 } });
  assert.equal(body.poll, null);
  assert.deepEqual(body.feedback, { focus: 'my topics' });
  assert.equal(body.title, 'T');
  assert.deepEqual(threadFilters({ pollOpen: false, followedOnly: true, forumIds: Array.from({ length: 60 }, (_, i) => 'frm_' + i) }).forumIds.length, 50);
  assert.deepEqual(threadFilters({}), {});

  const groups = groupForumsBySection([{ id: 'b', name: 'B', section: 'Help', sortOrder: 30 }, { id: 'a', name: 'A', section: 'Community', sortOrder: 10 }, { id: 'c', name: 'C', sortOrder: 20 }, { id: 'd', name: 'D', section: 'Community', sortOrder: 40 }]);
  assert.deepEqual(groups.map((g) => [g.section, g.forums.map((f) => f.id)]), [['Community', ['a', 'd']], ['', ['c']], ['Help', ['b']]]);
  assert.deepEqual(splitPinned([{ id: 1, isPinned: true }, { id: 2 }]).pinned.map((t) => t.id), [1]);
  assert.equal(unreadBadge(0), '');
  assert.equal(unreadBadge(99), '99+');
  assert.equal(unreadBadge(4), '4');
  assert.ok(isKnownThread({ id: 'thr_1', kind: 'LinkedFeedPost' }));
  assert.ok(!isKnownThread({ id: 'thr_1', kind: 'Future' }));

  const mod = { id: 'thr_1', type: 'Feedback', state: 'Published', isPinned: true, isLocked: false, feedback: { status: 'Open' }, poll: null, viewer: { ...moderatorViewer, canPromote: true, canReport: true, canEdit: false } };
  assert.deepEqual(threadActions(mod), ['unpin', 'lock', 'move', 'editMetadata', 'hide', 'remove', 'promote', 'feedbackStatus', 'report']);
  assert.deepEqual(threadActions({ ...mod, state: 'Hidden', promotion: { feedPostId: 'fpo_1' } }).filter((a) => ['unhide', 'remove', 'unpromote', 'unpin'].includes(a)), ['unhide', 'remove', 'unpromote']);
  assert.deepEqual(feedbackTransitions(mod), ['InReview', 'Closed']);
  assert.deepEqual(feedbackTransitions({ ...mod, viewer: {} }, { isAuthor: true }), ['Closed']);
  assert.deepEqual(feedbackTransitions({ ...mod, viewer: {}, feedback: { status: 'Reviewed' } }, { isAuthor: true }), []);
  assert.equal(threadCanReact({ isLocked: true, viewer: { canReact: true } }), false);
  assert.equal(threadCanReact({ isLocked: false, viewer: { canReact: true } }), true);
  assert.equal(threadCanComment({ isLocked: false, state: 'Published', viewer: { canComment: true } }), true);
  assert.equal(threadCanComment({ isLocked: true, state: 'Published', viewer: { canComment: true } }), false);
  assert.ok(NotificationCategoryOrder.includes('Following') && NotificationCategoryOrder.includes('Forums'));
});

test('search snippets split into highlighted runs without HTML', () => {
  assert.deepEqual(snippetSegments('try the reset option', [{ start: 8, length: 5 }]), [{ text: 'try the ', highlight: false }, { text: 'reset', highlight: true }, { text: ' option', highlight: false }]);
  assert.deepEqual(snippetSegments('abc', [{ start: 0, length: 10 }, { start: 1, length: 1 }]), [{ text: 'abc', highlight: true }]);
  assert.deepEqual(snippetSegments('abc', []), [{ text: 'abc', highlight: false }]);
  assert.deepEqual(snippetSegments('', null), [{ text: '', highlight: false }]);
});

test('the forums fake serves the contract routes through ApiClient', async () => {
  const general = makeForum('frm_g', { name: 'General', section: 'Community', sortOrder: 10, allowedThreadTypes: ['Discussion', 'Question', 'Poll'] });
  const help = makeForum('frm_h', { name: 'Help', section: 'Community', sortOrder: 20 });
  const pinned = makeThread('thr_p', general, { isPinned: true, pinnedUtc: '2026-11-10T00:00:00Z', title: 'Welcome' });
  const question = makeThread('thr_q', general, { type: 'Question', title: 'How do I reset my photo?', lastActivityUtc: '2026-11-10T19:00:00Z', viewer: { ...moderatorViewer, canReact: true, canComment: true } });
  const poll = makeThread('thr_v', general, { type: 'Poll', title: 'Which night?', poll: makePoll('pol_1', 'thr_v', ['Tue', 'Thu']) });
  const fake = createForumsFake({ forums: [help, general], threads: [pinned, question, poll], comments: [makeThreadComment('cmt_1', 'thr_q', { body: 'Try the reset option under Account' })], feedPosts: [{ id: 'fpo_1', title: 'News', body: 'Hello', author: null, commentCount: 0, reactionCounts: {}, reactionTotal: 0, myReactions: [] }] });
  const client = new ApiClient({ fetchImpl: fake.fetchImpl });

  const forums = await client.enumerateForums();
  assert.deepEqual(forums.objects.map((f) => f.id), ['frm_g', 'frm_h']);
  const first = await client.enumerateForumThreads('frm_g', { maxResults: 1 });
  assert.deepEqual(first.objects.map((t) => t.id), ['thr_p']);
  const second = await client.enumerateForumThreads('frm_g', { maxResults: 1, continuationToken: first.continuationToken });
  assert.deepEqual(second.objects.map((t) => t.id), ['thr_q']);
  assert.equal(second.objects[0].body, undefined);
  assert.equal(second.objects[0].hasAcceptedAnswer, false);
  const third = await client.enumerateForumThreads('frm_g', { maxResults: 1, continuationToken: second.continuationToken });
  assert.deepEqual(third.objects.map((t) => t.id), ['thr_v']);
  assert.equal(third.endOfResults, true);

  const search = await client.enumerateThreads({ search: 'reset' });
  assert.equal(search.objects[0].searchSnippet.field, 'Title');
  const viaComment = await client.enumerateThreads({ search: 'Account' });
  assert.equal(viaComment.objects[0].searchSnippet.commentId, 'cmt_1');

  const accepted = await client.acceptAnswer('thr_q', 'cmt_1');
  assert.equal(accepted.acceptedAnswer.id, 'cmt_1');
  await assert.rejects(client.acceptAnswer('thr_q', 'cmt_x'), (err) => err.code === 'InvalidAcceptedAnswer');

  const voted = await client.votePoll('pol_1', ['pop_1_2']);
  assert.equal(voted.resultsVisible, true);
  assert.equal(voted.totalVoters, 1);
  assert.equal(pollDisplay(voted).options[1].percent, 100);
  await client.closePoll('pol_1');
  await assert.rejects(client.votePoll('pol_1', ['pop_1_1']), (err) => err.code === 'PollClosed');

  const locked = await client.lockThread('thr_q', 'Answered');
  assert.equal(locked.isLocked, true);
  await assert.rejects(client.createThreadComment(locked, { body: 'late' }), (err) => err.code === 'ThreadLocked');
  await assert.rejects(client.putThreadReaction(locked, 'like'), (err) => err.code === 'ThreadLocked');

  await assert.rejects(client.createThread('frm_g', { type: 'Feedback', title: 'Video please' }), (err) => err.code === 'ThreadTypeNotAllowed');
  await assert.rejects(client.createThread('frm_h', { type: 'Feedback', title: 'Video please', mediaIds: ['med_img'] }), (err) => err.code === 'FeedbackVideoRequired');
  const created = await client.createThread('frm_h', { type: 'Discussion', title: 'New one', body: 'Text' });
  assert.equal(created.isFollowed, true);

  const card = await client.promoteThread('thr_p', { notifyMembers: false });
  assert.equal(card.kind, 'PromotedThread');
  assert.deepEqual(card.commentTarget, { type: 'Thread', id: 'thr_p' });
  await assert.rejects(client.promoteThread('thr_p'), (err) => err.code === 'AlreadyPromoted');

  const linked = await client.discussFeedPost('fpo_1', { forumId: 'frm_h' });
  assert.equal(linked.kind, 'LinkedFeedPost');
  await client.createThreadComment(linked, { body: 'One conversation' });
  const direct = await fake.handle({ method: 'POST', path: '/threads/' + linked.id + '/comments', body: { body: 'fork' } });
  assert.equal(direct.body.error, 'UseCommentTarget');
  assert.equal((await client.getThread(linked.id)).commentCount, 1);

  await client.followForum('frm_h');
  const follows = await client.enumerateMyFollows({ filters: { targetType: 'Forum' } });
  assert.deepEqual(follows.objects.map((f) => f.target.id), ['frm_h']);

  const admin = await client.adminCreateForum({ name: 'Show and tell' });
  assert.equal(admin.slug, 'show-and-tell');
  await assert.rejects(client.adminCreateForum({ name: 'Show and tell' }), (err) => err.code === 'SlugInUse');
  await assert.rejects(client.adminDeleteForum('frm_g'), (err) => err.code === 'ForumNotEmpty');
  assert.deepEqual(await client.adminOrderForums([admin.id, 'frm_h', 'frm_g']), { updated: 3 });
  await assert.rejects(client.adminOrderForums(['frm_h']), (err) => err.code === 'ValidationFailed');
  const mute = await client.createForumMute({ userId: 'usr_5', forumId: 'frm_g', untilUtc: null, reason: 'Spam', notifyMember: true });
  assert.equal((await client.enumerateForumMutes({ filters: { forumId: 'frm_g' } })).objects.length, 1);
  await client.endForumMute(mute.id);
  assert.equal((await client.enumerateForumMutes()).objects.length, 0);
  const results = await client.searchContent({ search: 'reset', filters: { types: ['Thread', 'Comment'] } });
  assert.deepEqual(results.objects.map((r) => r.type), ['Thread', 'Comment']);
  assert.equal((await client.adminEnumerateTrending()).objects[0].rank, 1);
  assert.equal((await client.adminForumMetrics({ filters: { days: 30 } })).byForum.objects.length, 3);
});

test('admin forum helpers: audiences per access model, form validation, and reordering', async () => {
  const { audienceOptions, validateForumForm, moveItem, applyPreferenceChange } = await import('../src/index.js');
  assert.deepEqual(audienceOptions({}), ['Members', 'Public']);
  assert.deepEqual(audienceOptions({ access: { model: 'Open' } }), ['SignedIn', 'Public']);
  assert.deepEqual(audienceOptions({ access: { model: 'PartiallyGated' } }), ['Members', 'SignedIn', 'Public']);
  assert.deepEqual(validateForumForm({ name: 'G', slug: 'Bad Slug', allowedThreadTypes: [], color: 'red' }), { name: 'nameLength', slug: 'slugInvalid', allowedThreadTypes: 'typesRequired', color: 'colorInvalid' });
  assert.deepEqual(validateForumForm({ name: 'General', slug: 'general', allowedThreadTypes: ['Discussion'], color: '#1F5FBF' }), {});
  assert.deepEqual(moveItem(['a', 'b', 'c'], 2, 0), ['c', 'a', 'b']);
  assert.deepEqual(moveItem(['a', 'b'], 0, 5), ['a', 'b']);
  assert.equal(applyPreferenceChange({ categories: [] }, { autoFollowOnComment: false }).autoFollowOnComment, false);
});
