namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Data;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Row mappers of the direct messaging tables (shared by the conversation and message method sets).
    /// </summary>
    internal static class MessagingRows
    {
        internal static Conversation Conversation(DataRow row)
        {
            return new Conversation
            {
                Id = RowReader.GetString(row, "id"),
                Kind = RowReader.GetEnum(row, "kind", ConversationKindEnum.Direct),
                DirectKey = RowReader.GetNullableString(row, "directkey"),
                LastSeq = RowReader.GetLong(row, "lastseq"),
                LastMessageId = RowReader.GetNullableString(row, "lastmessageid"),
                LastMessageUtc = RowReader.GetNullableDateTime(row, "lastmessageutc"),
                CreatedByUserId = RowReader.GetNullableString(row, "createdbyuserid"),
                PurgeAfterUtc = RowReader.GetNullableDateTime(row, "purgeafterutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        internal static ConversationMember Member(DataRow row)
        {
            return new ConversationMember
            {
                Id = RowReader.GetString(row, "id"),
                ConversationId = RowReader.GetString(row, "conversationid"),
                UserId = RowReader.GetString(row, "userid"),
                Role = RowReader.GetEnum(row, "role", ConversationMemberRoleEnum.Member),
                Folder = RowReader.GetEnum(row, "folder", ConversationFolderEnum.Inbox),
                RequestState = RowReader.GetNullableEnum<ConversationRequestStateEnum>(row, "requeststate"),
                RequestMessageCount = RowReader.GetInt(row, "requestmessagecount"),
                AcceptedUtc = RowReader.GetNullableDateTime(row, "acceptedutc"),
                DeclinedUtc = RowReader.GetNullableDateTime(row, "declinedutc"),
                LastReadSeq = RowReader.GetLong(row, "lastreadseq"),
                LastDeliveredSeq = RowReader.GetLong(row, "lastdeliveredseq"),
                HiddenBeforeSeq = RowReader.GetLong(row, "hiddenbeforeseq"),
                IsMuted = RowReader.GetBool(row, "ismuted"),
                MutedUntilUtc = RowReader.GetNullableDateTime(row, "muteduntilutc"),
                LastActivityUtc = RowReader.GetDateTime(row, "lastactivityutc"),
                JoinedUtc = RowReader.GetDateTime(row, "joinedutc"),
                LeftUtc = RowReader.GetNullableDateTime(row, "leftutc"),
                DeletedUtc = RowReader.GetNullableDateTime(row, "deletedutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        internal static Message Message(DataRow row)
        {
            return new Message
            {
                Id = RowReader.GetString(row, "id"),
                ConversationId = RowReader.GetString(row, "conversationid"),
                Seq = RowReader.GetLong(row, "seq"),
                ClientMessageId = RowReader.GetString(row, "clientmessageid"),
                AuthorUserId = RowReader.GetNullableString(row, "authoruserid"),
                Body = RowReader.GetNullableString(row, "body") ?? String.Empty,
                OriginalBody = RowReader.GetNullableString(row, "originalbody"),
                Excerpt = RowReader.GetNullableString(row, "excerpt") ?? String.Empty,
                ReplyToMessageId = RowReader.GetNullableString(row, "replytomessageid"),
                LinkPreviewId = RowReader.GetNullableString(row, "linkpreviewid"),
                ShowLinkPreview = RowReader.GetBool(row, "showlinkpreview"),
                AttachmentCount = RowReader.GetInt(row, "attachmentcount"),
                HasVideo = RowReader.GetBool(row, "hasvideo"),
                State = RowReader.GetEnum(row, "state", MessageStateEnum.Visible),
                EditedUtc = RowReader.GetNullableDateTime(row, "editedutc"),
                DeletedUtc = RowReader.GetNullableDateTime(row, "deletedutc"),
                ModeratedUtc = RowReader.GetNullableDateTime(row, "moderatedutc"),
                ModeratedByUserId = RowReader.GetNullableString(row, "moderatedbyuserid"),
                ReactionTotal = RowReader.GetInt(row, "reactiontotal"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        internal static MemberMessagingSettings Settings(DataRow row)
        {
            return new MemberMessagingSettings
            {
                UserId = RowReader.GetString(row, "userid"),
                DmPolicy = RowReader.GetEnum(row, "dmpolicy", DmPolicyEnum.Everyone),
                ReadReceipts = RowReader.GetBool(row, "readreceipts"),
                TypingIndicators = RowReader.GetBool(row, "typingindicators"),
                PushPreview = RowReader.GetEnum(row, "pushpreview", PushPreviewEnum.SenderOnly),
                IgnoreNotificationHours = RowReader.GetBool(row, "ignorenotificationhours"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        internal static MessageReportSnapshot Snapshot(DataRow row)
        {
            return new MessageReportSnapshot
            {
                Id = RowReader.GetString(row, "id"),
                ReportId = RowReader.GetString(row, "reportid"),
                ConversationId = RowReader.GetString(row, "conversationid"),
                ReporterUserId = RowReader.GetNullableString(row, "reporteruserid"),
                CapturedUtc = RowReader.GetDateTime(row, "capturedutc"),
                PurgeAfterUtc = RowReader.GetNullableDateTime(row, "purgeafterutc")
            };
        }

        internal static MessageReportSnapshotItem SnapshotItem(DataRow row)
        {
            return new MessageReportSnapshotItem
            {
                Id = RowReader.GetString(row, "id"),
                SnapshotId = RowReader.GetString(row, "snapshotid"),
                MessageId = RowReader.GetString(row, "messageid"),
                Seq = RowReader.GetLong(row, "seq"),
                AuthorUserId = RowReader.GetNullableString(row, "authoruserid"),
                Body = RowReader.GetNullableString(row, "body") ?? String.Empty,
                OriginalBody = RowReader.GetNullableString(row, "originalbody"),
                State = RowReader.GetEnum(row, "state", MessageStateEnum.Visible),
                IsReported = RowReader.GetBool(row, "isreported"),
                AttachmentSummary = RowReader.GetNullableString(row, "attachmentsummary"),
                EditedUtc = RowReader.GetNullableDateTime(row, "editedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
