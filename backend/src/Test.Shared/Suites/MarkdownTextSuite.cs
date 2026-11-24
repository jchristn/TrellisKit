namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Server;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Server plain text of the Markdown subset (excerpts, search text, notifications): headings, inline and fenced
    /// code, horizontal rules, and pipe tables lose their syntax and keep their text, and mentions inside code are not
    /// mentions.
    /// </summary>
    public static class MarkdownTextSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "MarkdownText";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Markdown plain text", new[]
            {
                SuiteCases.Unit(Id, "PlainText.Syntax", "Headings, code, rules, and tables become plain text; code content stays literal", ct =>
                {
                    Check.Equal("Title Some bold and code with *stars* here.", MarkdownNormalizer.ToPlainText("# Title\n\nSome **bold** and `code with *stars*` here."), "Heading and inline code");
                    Check.Equal("Before const a = 1; After", MarkdownNormalizer.ToPlainText("Before\n\n```js\nconst a = 1;\n```\n\nAfter"), "Fenced code");
                    Check.Equal("a b", MarkdownNormalizer.ToPlainText("a\n\n---\n\nb"), "Rule");
                    Check.Equal("Belt Years Blue 2", MarkdownNormalizer.ToPlainText("| Belt | Years |\n|:---|---:|\n| Blue | 2 |"), "Table");
                    Check.Equal("a | b and `tick`", MarkdownNormalizer.ToPlainText("a \\| b and \\`tick\\`"), "Escaped pipe and backticks");
                    Check.Equal("Unclosed fence code", MarkdownNormalizer.ToPlainText("Unclosed fence\n\n~~~\ncode"), "Unclosed fence runs to the end");
                    Check.True(MarkdownNormalizer.Excerpt("```\n" + new string('x', 400) + "\n```").EndsWith("...", StringComparison.Ordinal), "Excerpt of a code block");
                    return Task.CompletedTask;
                }),
                SuiteCases.Http(Id, "Excerpts.Refresh", "Stored excerpts made before the plain-text rules are recomputed from the body; clean ones are left alone", async (f, ct) =>
                {
                    TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
                    TestMember reader = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
                    JsonElement post = await ContentTestKit.PostAsync(f, poster.AccessToken, new { title = "Table post", body = "Hi\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n`code`" }, ct).ConfigureAwait(false);
                    string id = Api.Str(post, "id");
                    Check.Equal("Hi a b 1 2 code", Api.Str(post, "excerpt"), "New posts get clean excerpts");
                    TrellisKit.Core.Database.DatabaseDriverBase db = f.Server.Services.Database;
                    await db.FeedPosts.UpdateExcerptAsync(id, "Hi | a | b | |---|---| `code`", ct).ConfigureAwait(false);
                    TrellisKit.Server.Workers.ExcerptRefreshWorker worker = new TrellisKit.Server.Workers.ExcerptRefreshWorker(db, new AppLogger(TestLogging.CreateModule()));
                    Check.Equal(1, await worker.RefreshAsync(ct).ConfigureAwait(false), "One stale excerpt rewritten");
                    JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + id, reader.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Hi a b 1 2 code", Api.Str(read, "excerpt"), "Excerpt recomputed");
                    Check.Equal(0, await worker.RefreshAsync(ct).ConfigureAwait(false), "Second pass changes nothing");
                }),
                SuiteCases.Unit(Id, "Mentions.NotInCode", "@handles inside inline or fenced code are not mentions", ct =>
                {
                    List<string> handles = MarkdownNormalizer.MentionHandles("Hi @alice, run `@bob --help` and\n\n```\n@carol\n```\n\nthanks @dave");
                    Check.Equal("alice,dave", String.Join(",", handles), "Only mentions outside code");
                    return Task.CompletedTask;
                })
            });
        }

        #endregion
    }
}
