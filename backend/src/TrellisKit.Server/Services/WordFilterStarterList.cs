namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The engine's starter word filter list: common English profanity, sexual vulgarity, and slurs. Administrators
    /// load it from the word filter page (POST /api/v1/admin/moderation/word-filter/starter) with action Flag and then
    /// edit, block, or delete terms. Mild words (damn, crap) and words with common innocent meanings are left out to
    /// keep the moderation queue useful. Substring terms are roots that do not occur inside ordinary English words; every
    /// other term matches whole words only, with its common inflections listed. Thread safety: immutable.
    /// </summary>
    public static class WordFilterStarterList
    {
        #region Private-Members

        private static readonly string[] _Substring = new[]
        {
            "fuck", "shit", "cocksuck", "dickhead", "jackass", "dumbass", "asshat", "douchebag"
        };

        private static readonly string[] _Word = new[]
        {
            "ass", "asses", "asshole", "assholes", "arse", "arsehole",
            "bastard", "bastards", "bitch", "bitches", "bitchy", "bitching",
            "bollocks", "cock", "cocks", "cunt", "cunts",
            "dick", "dicks", "dildo", "douche", "fag", "fags", "faggot", "faggots",
            "jizz", "kike", "kikes", "nigga", "niggas", "nigger", "niggers", "piss", "pissing",
            "porn", "prick", "pricks", "pussy", "pussies", "retard", "retarded", "retards", "slut", "sluts", "slutty",
            "spic", "spics", "chink", "chinks", "tits", "titties", "tranny", "trannies", "twat", "twats",
            "wank", "wanker", "wankers", "whore", "whores", "skank", "skanks"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// The starter terms of one match type, in a stable order.
        /// </summary>
        /// <param name="matchType">Word (whole words) or Substring.</param>
        /// <returns>A new list (never null; empty for an unknown match type).</returns>
        public static List<string> Terms(WordFilterMatchTypeEnum matchType)
        {
            switch (matchType)
            {
                case WordFilterMatchTypeEnum.Substring:
                    return _Substring.ToList();
                case WordFilterMatchTypeEnum.Word:
                    return _Word.ToList();
                default:
                    return new List<string>();
            }
        }

        /// <summary>
        /// Every starter term, Word terms first.
        /// </summary>
        /// <returns>A new list (never null).</returns>
        public static List<string> All()
        {
            return _Word.Concat(_Substring).ToList();
        }

        #endregion
    }
}
