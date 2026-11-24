namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Static rules of the notification types and categories (Phase 2 contract 8.2 and 8.5): the category of each type,
    /// the dedupe priority, and the per-category channel defaults and editability.
    /// Thread safety: stateless.
    /// </summary>
    public static class NotificationCatalog
    {
        /// <summary>
        /// Categories in display order.
        /// </summary>
        public static readonly IReadOnlyList<NotificationCategoryEnum> Categories = new[]
        {
            NotificationCategoryEnum.Replies,
            NotificationCategoryEnum.Mentions,
            NotificationCategoryEnum.Reactions,
            NotificationCategoryEnum.FeedPosts,
            NotificationCategoryEnum.Moderation,
            NotificationCategoryEnum.Account,
            NotificationCategoryEnum.Following,
            NotificationCategoryEnum.Forums,
            NotificationCategoryEnum.Events,
            NotificationCategoryEnum.Library,
            NotificationCategoryEnum.Ideas,
            NotificationCategoryEnum.Support,
            NotificationCategoryEnum.Announcements,
            NotificationCategoryEnum.Messages
        };

        /// <summary>
        /// The category of a type.
        /// </summary>
        /// <param name="type">Type.</param>
        /// <returns>Category.</returns>
        public static NotificationCategoryEnum CategoryOf(NotificationTypeEnum type)
        {
            switch (type)
            {
                case NotificationTypeEnum.PostComment:
                case NotificationTypeEnum.CommentReply:
                case NotificationTypeEnum.VenueReview:
                case NotificationTypeEnum.VenueReviewReply:
                    return NotificationCategoryEnum.Replies;
                case NotificationTypeEnum.Mention:
                    return NotificationCategoryEnum.Mentions;
                case NotificationTypeEnum.Reaction:
                    return NotificationCategoryEnum.Reactions;
                case NotificationTypeEnum.NewFeedPost:
                    return NotificationCategoryEnum.FeedPosts;
                case NotificationTypeEnum.ModerationAction:
                case NotificationTypeEnum.ModerationOverdue:
                case NotificationTypeEnum.ThreadMoved:
                case NotificationTypeEnum.ContentFlagged:
                    return NotificationCategoryEnum.Moderation;
                case NotificationTypeEnum.ThreadReply:
                    return NotificationCategoryEnum.Following;
                case NotificationTypeEnum.DirectMessage:
                case NotificationTypeEnum.MessageRequest:
                    return NotificationCategoryEnum.Messages;
                case NotificationTypeEnum.NewThreadInForum:
                case NotificationTypeEnum.AnswerAccepted:
                case NotificationTypeEnum.FeedbackReviewed:
                case NotificationTypeEnum.PollClosed:
                case NotificationTypeEnum.ThreadPromoted:
                    return NotificationCategoryEnum.Forums;
                case NotificationTypeEnum.EventReminder:
                case NotificationTypeEnum.EventUpdated:
                case NotificationTypeEnum.EventCanceled:
                case NotificationTypeEnum.EventWaitlistPromoted:
                case NotificationTypeEnum.NewEvent:
                    return NotificationCategoryEnum.Events;
                case NotificationTypeEnum.NewLessonInSeries:
                    return NotificationCategoryEnum.Library;
                case NotificationTypeEnum.SubmissionOverdue:
                    return NotificationCategoryEnum.Moderation;
                case NotificationTypeEnum.IdeaStatusChanged:
                    return NotificationCategoryEnum.Ideas;
                case NotificationTypeEnum.TicketOpened:
                case NotificationTypeEnum.TicketMemberReplied:
                case NotificationTypeEnum.TicketOverdue:
                    return NotificationCategoryEnum.Support;
                case NotificationTypeEnum.MetricAlert:
                case NotificationTypeEnum.Broadcast:
                    return NotificationCategoryEnum.Announcements;
                default:
                    return NotificationCategoryEnum.Account;
            }
        }

        /// <summary>
        /// Dedupe priority for "one notification per recipient per source item": CommentReply 3, PostComment 2, Mention 1,
        /// every other type 0. Higher wins.
        /// </summary>
        /// <param name="type">Type.</param>
        /// <returns>Priority.</returns>
        public static int Priority(NotificationTypeEnum type)
        {
            switch (type)
            {
                case NotificationTypeEnum.CommentReply:
                    return 3;
                case NotificationTypeEnum.PostComment:
                    return 2;
                case NotificationTypeEnum.Mention:
                    return 1;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Default in-app setting (on for every category).
        /// </summary>
        /// <param name="category">Category.</param>
        /// <returns>Default.</returns>
        public static bool DefaultInApp(NotificationCategoryEnum category)
        {
            return true;
        }

        /// <summary>
        /// Default push setting (on for every category).
        /// </summary>
        /// <param name="category">Category.</param>
        /// <returns>Default.</returns>
        public static bool DefaultPush(NotificationCategoryEnum category)
        {
            return true;
        }

        /// <summary>
        /// Default email setting: on for FeedPosts, Account (the membership emails), and Announcements (Phase 5 section 9.2),
        /// off otherwise.
        /// </summary>
        /// <param name="category">Category.</param>
        /// <returns>Default.</returns>
        public static bool DefaultEmail(NotificationCategoryEnum category)
        {
            return category == NotificationCategoryEnum.FeedPosts || category == NotificationCategoryEnum.Account || category == NotificationCategoryEnum.Events || category == NotificationCategoryEnum.Support || category == NotificationCategoryEnum.Announcements;
        }

        /// <summary>
        /// Whether members may change the in-app cell (not for Moderation, Account, and Announcements).
        /// </summary>
        /// <param name="category">Category.</param>
        /// <returns>True when editable.</returns>
        public static bool InAppEditable(NotificationCategoryEnum category)
        {
            return category != NotificationCategoryEnum.Moderation && category != NotificationCategoryEnum.Account && category != NotificationCategoryEnum.Announcements && category != NotificationCategoryEnum.Messages;
        }

        /// <summary>
        /// Whether members may change the push cell (every category).
        /// </summary>
        /// <param name="category">Category.</param>
        /// <returns>True when editable.</returns>
        public static bool PushEditable(NotificationCategoryEnum category)
        {
            return true;
        }

        /// <summary>
        /// Whether members may change the email cell (FeedPosts in Phase 2, Following and Forums in Phase 3, Announcements in
        /// Phase 5).
        /// </summary>
        /// <param name="category">Category.</param>
        /// <returns>True when editable.</returns>
        public static bool EmailEditable(NotificationCategoryEnum category)
        {
            return category == NotificationCategoryEnum.FeedPosts || category == NotificationCategoryEnum.Following || category == NotificationCategoryEnum.Forums ||
                category == NotificationCategoryEnum.Events || category == NotificationCategoryEnum.Library || category == NotificationCategoryEnum.Ideas
                || category == NotificationCategoryEnum.Support || category == NotificationCategoryEnum.Announcements || category == NotificationCategoryEnum.Messages;
        }

        /// <summary>
        /// Whether a type groups per target while unread by replacing the unread row with the latest event (the Phase 5
        /// ticket types, group key Ticket:&lt;id&gt;), rather than aggregating actors.
        /// </summary>
        /// <param name="type">Type.</param>
        /// <returns>True for TicketUpdate, TicketOpened, TicketMemberReplied, and TicketOverdue.</returns>
        public static bool IsReplacingGroup(NotificationTypeEnum type)
        {
            return type == NotificationTypeEnum.TicketUpdate || type == NotificationTypeEnum.TicketOpened || type == NotificationTypeEnum.TicketMemberReplied || type == NotificationTypeEnum.TicketOverdue;
        }

        /// <summary>
        /// Whether a category is shown and editable only for support staff (SupportAgent and SystemAdmin).
        /// </summary>
        /// <param name="category">Category.</param>
        /// <returns>True for Support.</returns>
        public static bool IsStaffOnly(NotificationCategoryEnum category)
        {
            return category == NotificationCategoryEnum.Support;
        }

        /// <summary>
        /// Parse a category name (case-insensitive).
        /// </summary>
        /// <param name="value">Text.</param>
        /// <returns>Category, or null when unknown.</returns>
        public static NotificationCategoryEnum? ParseCategory(string? value)
        {
            if (String.IsNullOrWhiteSpace(value) || Int32.TryParse(value, out int _)) return null;
            if (Enum.TryParse(value.Trim(), true, out NotificationCategoryEnum category) && Enum.IsDefined(category)) return category;
            return null;
        }
    }
}
