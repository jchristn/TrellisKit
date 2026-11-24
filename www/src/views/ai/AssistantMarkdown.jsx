import MarkdownBody from '../../components/feed/MarkdownBody.jsx';

/**
 * An assistant reply rendered with the shared Markdown subset (AI contract section 9.2): the same
 * renderer as posts, so fenced and inline code, tables, and rules show the same way. Never HTML.
 * Mentions are not resolved in replies.
 * @param {{ text: string }} props
 */
function AssistantMarkdown({ text }) {
  return <MarkdownBody body={text || ''} linkMentions={false} className="ai-md" />;
}

export default AssistantMarkdown;
