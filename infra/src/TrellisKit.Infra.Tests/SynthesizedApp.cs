namespace TrellisKit.Infra.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Amazon.CDK;
    using Amazon.CDK.Assertions;
    using TrellisKit.Infra.Nag;

    /// <summary>
    /// A synthesized CDK app with cached templates. Synthesis uses only placeholder account ids, so no AWS
    /// credentials are needed.
    /// Thread safety: not thread safe; tests run serially.
    /// </summary>
    public abstract class SynthesizedApp
    {
        private readonly Dictionary<string, Template> _Templates = new Dictionary<string, Template>();
        private readonly Dictionary<string, JsonDocument> _Json = new Dictionary<string, JsonDocument>();
        private List<string>? _NagFindings = null;

        /// <summary>
        /// The app.
        /// </summary>
        public App App { get; }

        /// <summary>
        /// Create the app with context values.
        /// </summary>
        /// <param name="context">Context values.</param>
        /// <exception cref="ArgumentNullException">The context is null.</exception>
        protected SynthesizedApp(Dictionary<string, object> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            App = new App(new AppProps { Context = context });
        }

        /// <summary>
        /// cdk-nag AwsSolutions findings (errors and warnings) left after suppressions, across every stack (cached).
        /// </summary>
        /// <returns>"RuleName at construct path" lines; empty when clean.</returns>
        public List<string> NagFindings()
        {
            if (_NagFindings == null) _NagFindings = new AwsSolutionsNagPlugin(App).FindUnsuppressed(App);
            return _NagFindings;
        }

        /// <summary>
        /// Assertion template of a stack (cached).
        /// </summary>
        /// <param name="stack">Stack.</param>
        /// <returns>Template.</returns>
        public Template Template(Stack? stack)
        {
            ArgumentNullException.ThrowIfNull(stack);
            if (!_Templates.TryGetValue(stack.StackName, out Template? template))
            {
                template = Amazon.CDK.Assertions.Template.FromStack(stack);
                _Templates[stack.StackName] = template;
            }

            return template;
        }

        /// <summary>
        /// The synthesized template of a stack as JSON (cached).
        /// </summary>
        /// <param name="stack">Stack.</param>
        /// <returns>Root element.</returns>
        public JsonElement Json(Stack? stack)
        {
            ArgumentNullException.ThrowIfNull(stack);
            if (!_Json.TryGetValue(stack.StackName, out JsonDocument? document))
            {
                document = JsonDocument.Parse(JsonSerializer.Serialize(Template(stack).ToJSON()));
                _Json[stack.StackName] = document;
            }

            return document.RootElement;
        }

        /// <summary>
        /// Every resource of a type in a stack's template.
        /// </summary>
        /// <param name="stack">Stack.</param>
        /// <param name="type">CloudFormation type, for example AWS::S3::Bucket.</param>
        /// <returns>Resource elements (each with Type and Properties).</returns>
        public List<JsonElement> Resources(Stack? stack, string type)
        {
            List<JsonElement> resources = new List<JsonElement>();
            foreach (JsonProperty resource in Json(stack).GetProperty("Resources").EnumerateObject())
            {
                if (resource.Value.GetProperty("Type").GetString() == type) resources.Add(resource.Value);
            }

            return resources;
        }

        /// <summary>
        /// A resource by logical id.
        /// </summary>
        /// <param name="stack">Stack.</param>
        /// <param name="logicalId">Logical id.</param>
        /// <returns>Resource element.</returns>
        public JsonElement Resource(Stack? stack, string logicalId)
        {
            return Json(stack).GetProperty("Resources").GetProperty(logicalId);
        }
    }
}
