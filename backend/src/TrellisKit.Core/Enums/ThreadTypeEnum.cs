namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Forum thread type (docs/PHASE3_CONTRACT.md section 2.2).
    /// </summary>
    public enum ThreadTypeEnum
    {
        /// <summary>
        /// A conversation.
        /// </summary>
        Discussion,

        /// <summary>
        /// A question that can have an accepted answer.
        /// </summary>
        Question,

        /// <summary>
        /// A poll; the thread title is the question.
        /// </summary>
        Poll,

        /// <summary>
        /// A request for review of a video.
        /// </summary>
        Feedback
    }
}
