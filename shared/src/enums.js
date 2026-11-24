/**
 * Enumerations shared with the backend. Values match the backend's enum names exactly.
 */

/** Forum thread types. */
export const ThreadType = Object.freeze({
  Discussion: 'Discussion',
  Question: 'Question',
  Poll: 'Poll',
  Feedback: 'Feedback'
});

/** Built-in roles. */
export const Role = Object.freeze({
  User: 'User',
  SystemAdmin: 'SystemAdmin',
  Moderator: 'Moderator',
  FeedModerator: 'FeedModerator',
  DirectoryModerator: 'DirectoryModerator',
  EventManager: 'EventManager',
  LibraryEditor: 'LibraryEditor',
  SupportAgent: 'SupportAgent',
  Communicator: 'Communicator',
  ReleaseManager: 'ReleaseManager'
});
