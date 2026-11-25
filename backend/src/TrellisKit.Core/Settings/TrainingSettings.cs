namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Training hub overview settings (docs/TRELLISKIT.md section 15): the fixed maximum of every overview card and the
    /// "New for you" window. Every value is clamped to its range.
    /// </summary>
    public class TrainingSettings
    {
        #region Public-Members

        /// <summary>
        /// Smallest value of every card limit.
        /// </summary>
        public const int MinLimit = 1;

        /// <summary>
        /// Largest value of <see cref="UpNextLimit"/>.
        /// </summary>
        public const int MaxUpNextLimit = 10;

        /// <summary>
        /// Largest value of <see cref="ContinueLimit"/>.
        /// </summary>
        public const int MaxContinueLimit = 25;

        /// <summary>
        /// Largest value of <see cref="RecentSavesLimit"/>.
        /// </summary>
        public const int MaxRecentSavesLimit = 20;

        /// <summary>
        /// Largest value of <see cref="NewForYouLimit"/>.
        /// </summary>
        public const int MaxNewForYouLimit = 20;

        /// <summary>
        /// Smallest value of <see cref="NewForYouDays"/>.
        /// </summary>
        public const int MinNewForYouDays = 1;

        /// <summary>
        /// Largest value of <see cref="NewForYouDays"/>.
        /// </summary>
        public const int MaxNewForYouDays = 90;

        /// <summary>
        /// Most Up next items. Default 3. Minimum 1, maximum 10.
        /// </summary>
        public int UpNextLimit
        {
            get { return _UpNextLimit; }
            set { _UpNextLimit = Math.Clamp(value, MinLimit, MaxUpNextLimit); }
        }

        /// <summary>
        /// Most Continue training items. Default 10. Minimum 1, maximum 25.
        /// </summary>
        public int ContinueLimit
        {
            get { return _ContinueLimit; }
            set { _ContinueLimit = Math.Clamp(value, MinLimit, MaxContinueLimit); }
        }

        /// <summary>
        /// Most Recently saved items. Default 6. Minimum 1, maximum 20.
        /// </summary>
        public int RecentSavesLimit
        {
            get { return _RecentSavesLimit; }
            set { _RecentSavesLimit = Math.Clamp(value, MinLimit, MaxRecentSavesLimit); }
        }

        /// <summary>
        /// Most New for you lessons. Default 6. Minimum 1, maximum 20.
        /// </summary>
        public int NewForYouLimit
        {
            get { return _NewForYouLimit; }
            set { _NewForYouLimit = Math.Clamp(value, MinLimit, MaxNewForYouLimit); }
        }

        /// <summary>
        /// How far back New for you looks, in days. Default 14. Minimum 1, maximum 90.
        /// </summary>
        public int NewForYouDays
        {
            get { return _NewForYouDays; }
            set { _NewForYouDays = Math.Clamp(value, MinNewForYouDays, MaxNewForYouDays); }
        }

        #endregion

        #region Private-Members

        private int _UpNextLimit = 3;
        private int _ContinueLimit = 10;
        private int _RecentSavesLimit = 6;
        private int _NewForYouLimit = 6;
        private int _NewForYouDays = 14;

        #endregion
    }
}
