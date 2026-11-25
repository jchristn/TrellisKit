namespace TrellisKit.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// Migration 126, "Saves and collections": a member's saved targets, named private collections, and the
    /// collection memberships. Labels and tags live in the shared labels and tags tables (target types Favorite and
    /// FavoriteCollection). Notes are reviewed through the content changelog (<see cref="NoteSource"/>).
    /// </summary>
    internal static class FavoriteSchema
    {
        #region Internal-Members

        internal const int Version = 126;

        internal const string Description = "Saves and collections";

        /// <summary>
        /// The content changelog source of save notes (a note set to null is a deletion).
        /// </summary>
        internal static readonly ContentChangeSource NoteSource = new ContentChangeSource("favorites", "favorites", "Favorite", "id", "userid", "note", "", "~note", "@targettype", "targetid", requireText: true);

        #endregion

        #region Internal-Methods

        internal static List<string> Statements()
        {
            List<string> statements = new List<string>
            {
                "CREATE TABLE IF NOT EXISTS favorites (id TEXT PRIMARY KEY, propertyid TEXT NOT NULL, userid TEXT NOT NULL, targettype TEXT NOT NULL, targetid TEXT NOT NULL, note TEXT NULL, " +
                    "createdutc TIMESTAMPTZ NOT NULL, lastupdateutc TIMESTAMPTZ NOT NULL, UNIQUE (propertyid, userid, targettype, targetid));",
                "CREATE INDEX IF NOT EXISTS favorites_user_created ON favorites (propertyid, userid, createdutc DESC, id DESC);",
                "CREATE INDEX IF NOT EXISTS favorites_target ON favorites (propertyid, targettype, targetid);",
                "CREATE TABLE IF NOT EXISTS favoritecollections (id TEXT PRIMARY KEY, propertyid TEXT NOT NULL, userid TEXT NOT NULL, name TEXT NOT NULL, position INTEGER NOT NULL, " +
                    "createdutc TIMESTAMPTZ NOT NULL, lastupdateutc TIMESTAMPTZ NOT NULL);",
                "CREATE UNIQUE INDEX IF NOT EXISTS favoritecollections_name ON favoritecollections (propertyid, userid, lower(name));",
                "CREATE INDEX IF NOT EXISTS favoritecollections_user ON favoritecollections (propertyid, userid, position, id);",
                "CREATE TABLE IF NOT EXISTS favoritecollectionitems (collectionid TEXT NOT NULL REFERENCES favoritecollections(id) ON DELETE CASCADE, " +
                    "favoriteid TEXT NOT NULL REFERENCES favorites(id) ON DELETE CASCADE, propertyid TEXT NOT NULL, position INTEGER NOT NULL, PRIMARY KEY (collectionid, favoriteid));",
                "CREATE INDEX IF NOT EXISTS favoritecollectionitems_favorite ON favoritecollectionitems (propertyid, favoriteid);"
            };
            statements.AddRange(NoteSource.Statements());
            return statements;
        }

        #endregion
    }
}
