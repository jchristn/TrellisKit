namespace TrellisKit.Core.Helpers
{
    using System;
    using System.Linq;
    using System.Text.RegularExpressions;
    /// <summary>Stable bounded BCP47 subset for configured catalog names, without relying on installed locale data.</summary>
    public static class CatalogLanguageTags
    {
        private static readonly Regex _Pattern=new Regex("^[A-Za-z]{2,8}(?:-[A-Za-z]{4})?(?:-(?:[A-Za-z]{2}|[0-9]{3}))?$",RegexOptions.CultureInvariant);
        /// <summary>Canonical language[-Script][-REGION], or null for unsupported syntax, null, or more than35 characters.</summary>
        public static string? Normalize(string? value)
        {
            string input=(value??String.Empty).Trim();if(input.Length>35||!_Pattern.IsMatch(input))return null;
            string[] parts=input.Split('-');parts[0]=parts[0].ToLowerInvariant();
            for(int i=1;i<parts.Length;i++)parts[i]=parts[i].Length==4?Char.ToUpperInvariant(parts[i][0])+parts[i].Substring(1).ToLowerInvariant():parts[i].ToUpperInvariant();
            return String.Join("-",parts);
        }
    }
}
