namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Knowledge grounding settings.
    /// </summary>
    public class AssistantKnowledgeView
    {
        #region Public-Members

        /// <summary>
        /// Enabled.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Mode.
        /// </summary>
        public AssistantKnowledgeModeEnum Mode { get; set; } = AssistantKnowledgeModeEnum.Keyword;

        /// <summary>
        /// Most items.
        /// </summary>
        public int MaxContextItems { get; set; } = 6;

        /// <summary>
        /// Most characters.
        /// </summary>
        public int MaxContextCharacters { get; set; } = 12000;

        /// <summary>
        /// Citations.
        /// </summary>
        public bool Citations { get; set; } = true;

        /// <summary>
        /// Sources.
        /// </summary>
        public List<KnowledgeSourceView> Sources { get; set; } = new List<KnowledgeSourceView>();

        #endregion
    }
}
