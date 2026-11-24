namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Deterministic checks that keep AI features from becoming coding or general-purpose bots (docs/INTEGRATION.md
    /// owner requirement R7): requests to produce or fix code are refused before any model call, and replies that
    /// contain code are withheld. Measured and tuned with the scope benchmark (docs/BENCHMARKS.md).
    /// <para>
    /// A request is code when a request frame (write, fix, how do I, can you, and a few non-English verbs) is followed
    /// in the same sentence by an unambiguous technical term (SQL, JavaScript, regex, Dockerfile, stack trace), or by an
    /// ambiguous word only in a technical construction (a Python script, written in Swift, a function that returns, an
    /// Excel macro, build me an app), or when the message carries an error message, markup, or code itself. Ambiguous
    /// words on their own (training program, class schedule, macros, python pose, Java coffee, dress code, bicep curl)
    /// never count, and a member describing their own job ("a Python developer", "writing SQL all day") is not a request.
    /// </para>
    /// Thread safety: stateless.
    /// </summary>
    public static class OffScopeDetector
    {
        #region Private-Members

        private const RegexOptions _Ci = RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant;
        private const RegexOptions _Lines = RegexOptions.Multiline | RegexOptions.Compiled | RegexOptions.CultureInvariant;
        private static readonly TimeSpan _Timeout = TimeSpan.FromMilliseconds(200);

        // Words that ask for something, in the same sentence before the technical term.
        private const string _Frame =
            @"\b(?:write|writing|rewrite|generate|create|code|build|fix|debug|refactor|implement|convert|translate|port|explain|optimi[sz]e|complete|review|make|show|give|help|need|want|teach|draft|produce|correct|edit|modify|update|add|develop|program|automate|deploy|test|" +
            @"how\s+(?:do|can|would|should|could|to)|what(?:'s|’s|\s+is|\s+are|\s+does)|why\s+(?:does|is|do|am)|can\s+you|could\s+you|would\s+you|please|example|" +
            @"escrib\w*|cre[aá]\w*|gener\w*|g[eé]n[eè]r\w*|haz(?:me)?|[eé]cri\w*|schreib\w*|erstell\w*|scriv\w*|programm\w*|codific\w*|desarroll\w*|d[eé]velopp\w*)\b[^.?!\n]{0,80}?";

        // Unambiguous technical terms: no training, nutrition, booking, or community message needs them.
        private const string _Strong =
            @"(?:c\+\+|c#|asp\.net|(?<![\w.])\.net\b|\b(?:html5?|css3?|scss|sql|mysql|postgres(?:ql)?|sqlite|mongodb|nosql|javascript|typescript|jsx|tsx|json|yaml|toml|xml|xpath|regex(?:es)?|regexp|regular\s+expressions?|" +
            @"php|kotlin|golang|powershell|dockerfile|docker[- ]compose|kubernetes|k8s|helm\s+charts?|terraform|ansible|jquery|node\.?js|npm|django|flask\s+(?:app|route|api)|fastapi|spring\s+boot|laravel|graphql|" +
            @"rest(?:ful)?\s+api|api\s+(?:endpoints?|calls?|requests?|clients?|keys?|wrappers?|routes?)|webhooks?|nginx|webpack|github|gitlab|git\s+(?:repo|repository|commit|branch|merge|rebase)|pull\s+requests?|stack\s?traces?|traceback|compilers?|" +
            @"unit\s+tests?|integration\s+tests?|source\s+code|codebase|boilerplate\s+code|code\s+snippets?|pseudo-?code|leetcode|hackerrank|list\s+comprehensions?|for[- ]loops?|while[- ]loops?|linked\s+lists?|binary\s+(?:search\s+)?trees?|" +
            @"hash\s?maps?|null\s+pointer|segfault|segmentation\s+fault|syntax\s+errors?|runtime\s+errors?|recursion|recursive\s+functions?|big[- ]o\s+notation|lambda\s+functions?|async\/await|css\s+grid|flexbox|tailwind\s+css|" +
            @"react(?:\.?js|\s+native|\s+components?|\s+hooks?)|vue\.?js|angularjs|next\.js|wordpress\s+(?:plugins?|themes?)|chrome\s+extensions?|browser\s+extensions?|arduino|raspberry\s+pi\b|excel\s+formulas?|vba|apps\s+script|power\s+query|" +
            @"matlab|pandas\s+dataframe|numpy|jupyter|tensorflow|pytorch|selenium\s+(?:webdriver|scripts?|tests?)|web\s?scrapers?|scrape\s+(?:a\s+|the\s+|this\s+|my\s+|their\s+)?(?:website|site|web\s?page|page|data|emails?|reviews?|listings?)|" +
            @"curl\s+(?:commands?|requests?)|(?:with|using|via)\s+curl|" +
            @"bash\s+(?:scripts?|commands?|one-liners?|functions?|aliases?|loops?)|bash[- ]s[ck]ripts?|in\s+bash|shell\s+(?:scripts?|commands?|one-liners?)|cron\s?jobs?|crontab|database\s+schemas?|" +
            @"discord\s+bots?|telegram\s+bots?|slack\s+bots?|smart\s+contracts?|center\s+a\s+div|salesforce\s+(?:apex|triggers?|flows?\s+code)|apex\s+(?:triggers?|classes?|code)|node\s+(?:apps?|servers?|scripts?|modules?|packages?|backends?|apis?)|" +
            @"time\s+complexity|space\s+complexity|quick\s?sort|merge\s?sort|bubble\s+sort|heap\s?sort|binary\s+search|dynamic\s+programming|data\s+structures?)\b)";

        // Language names that are also everyday words (python pose, Java coffee, Swift, Ruby) count only in a
        // technical construction.
        private const string _LanguageNames = @"python|java|ruby|rust|swift|kotlin|golang|dart|perl|lua|scala|elixir|haskell";
        private const string _Languages = @"(?:" + _LanguageNames + @")";
        private const string _Frameworks = @"(?:react|vue|angular|flutter|svelte)";
        private const string _CodeNouns = @"(?:code|codes|script|scripts|program|programs|function|functions|method|methods|library|libraries|package|packages|module|modules|file|files|syntax|app|apps|bot|bots|tutorial|project|projects|api|sdk|servlet|backend|server|component|components|homework|assignments?|interviews?|bootcamp)";
        private const string _ComputeVerbs = @"(?:tracks?|calculates?|computes?|counts?|logs?|parses?|sorts?|converts?|sends?|emails?|scrapes?|downloads?|generates?|automates?|reads?|writes?)";
        private const string _TechContext = @"(?:syntax|compiler|interpreter|import|install|pip|variables?|arrays?|dictionar(?:y|ies)|strings?|integers?|exceptions?|runtime|ide|boolean|null|undefined|recursion|lambda|decorators?|regex)";

        private static readonly Regex[] _Requests = new[]
        {
            new Regex(_Frame + _Strong, _Ci, _Timeout),
            // a Python script, Java method, Rust function, Go program.
            new Regex(@"\b(?:" + _LanguageNames + @"|react|vue|angular|flutter|svelte)\s+" + _CodeNouns + @"\b", _Ci, _Timeout),
            new Regex(@"\bgo\s+(?:program|programs|code|function|module|package|server|struct)\b", _Ci, _Timeout),
            // a Python thing that tracks my reps.
            new Regex(@"\b" + _Languages + @"\s+[\w-]+\s+(?:that|which|to)\s+" + _ComputeVerbs + @"\b", _Ci, _Timeout),
            // written in Swift, a chatbot in Python using, convert this to Python.
            new Regex(@"\b(?:write|writing|code|program|implement|build|make|create|develop|rewrite|redo|script)\b[^.?!\n]{0,60}?\b(?:in|using)\s+(?:" + _LanguageNames + @"|react|vue|angular|flutter|svelte|go|c|r)\b(?=\s*(?:[.,;:!?)]|$)|\s+(?:and|for|that|with|so|to|using|code|please)\b)", _Ci, _Timeout),
            new Regex(@"\b(?:convert|translate|port|rewrite|migrate)\b[^.?!\n]{0,60}?\b(?:to|into|in)\s+(?:" + _LanguageNames + @"|c|r)\b", _Ci, _Timeout),
            new Regex(@"\b(?:s[ck]ripts?|functions?|funci[oó]n|fonction|funktion|code|c[oó]digo|codice)\s+(?:in|en|em|auf|with|using)\s+(?:" + _LanguageNames + @"|go|" + _Frameworks + @")\b", _Ci, _Timeout),
            new Regex(@"\b" + _Languages + @"\b[^.?!\n]{0,40}?\b" + _TechContext + @"\b", _Ci, _Timeout),
            new Regex(@"\b" + _TechContext + @"\b[^.?!\n]{0,40}?\b" + _Languages + @"\b", _Ci, _Timeout),
            // write a function, a function that returns.
            new Regex(@"\b(?:write|create|implement|code|define|make|build|give\s+me|show\s+me)\s+(?:me\s+)?(?:a|an|the|this|my|some)\s+(?:[\w-]+\s+){0,2}functions?\b", _Ci, _Timeout),
            new Regex(@"\bfunctions?\s+(?:that|which)\s+(?:returns?|calculates?|computes?|converts?|parses?|sorts?|checks?|validates?|takes?|counts?|finds?|reverses?|prints?|generates?)\b", _Ci, _Timeout),
            // a bash script, a script that scrapes, a script that automatically books.
            new Regex(@"\b(?:bash|shell|python|powershell|batch|automation|cron|node|javascript|js|ruby|perl|lua|apps|autohotkey|ahk|tampermonkey|greasemonkey|user|vbs|vbscript|applescript|sheets|spreadsheet|excel|office|outlook|gmail|zapier)[- ]?s[ck]ripts?\b", _Ci, _Timeout),
            new Regex(@"\bs[ck]ripts?\s+(?:that|to|which)\s+(?:automatically\b|(?:automates?|scrapes?|downloads?|renames?|parses?|installs?|deploys?|backs?\s+up|fetch(?:es)?|monitors?|deletes?|copies|converts?|crawls?|uploads?|syncs?)\b)", _Ci, _Timeout),
            new Regex(@"\bs[ck]ripts?\s+(?:that|which)\s+(?:emails?|texts?|notifies|alerts?|posts?|checks?|pings?)\b", _Ci, _Timeout),
            // an Excel macro (macros alone are nutrition).
            new Regex(@"\b(?:write|create|record|build|code|make|need|fix|debug|give\s+me)\s+(?:me\s+)?(?:an?\s+|the\s+|this\s+|my\s+|some\s+)?(?:excel|vba|word|outlook|spreadsheet|google\s+sheets|sheets|keyboard)\s+macros?\b", _Ci, _Timeout),
            // database queries.
            new Regex(@"\b(?:sql|database|db|mongo(?:db)?|graphql|elasticsearch)\s+quer(?:y|ies)\b", _Ci, _Timeout),
            new Regex(@"\bquer(?:y|ies)\s+(?:that|to|which)\s+(?:selects?|joins?|returns?|counts?|groups?|updates?|deletes?|inserts?)\b", _Ci, _Timeout),
            // build me a website, a landing page, an app, a plugin (not a website bio or listing).
            new Regex(@"\b(?:build|code|develop|program|make|create)\s+(?:me\s+)?(?:an?|the|my|our)\s+(?:[\w'-]+\s+){0,2}(?:website|web\s?site|web\s?page|landing\s+page|web\s?app|mobile\s+app|ios\s+app|android\s+app|iphone\s+app|app|plugin|chatbot|bot)\b(?!\s+(?:bio|copy|text|blurb|description|headline|tagline|content|post|caption|announcement|listing|profile|account))", _Ci, _Timeout),
            // write code, explain this code (not a dress, promo, or door code).
            new Regex(@"\b(?:write|writing|generate|fix|debug|review|explain|refactor|optimi[sz]e|complete|finish|comment|document|convert|translate|port|test)\s+(?:me\s+)?(?:some\s+|the\s+|this\s+|that\s+|my\s+|a\s+|an\s+|your\s+)?(?:piece\s+of\s+|bit\s+of\s+|sample\s+|example\s+|working\s+|simple\s+|basic\s+|quick\s+|python\s+|java\s+|front-?end\s+|back-?end\s+|source\s+)?code\b(?!\s+(?:of|words?|names?|phrases?|numbers?|systems?|colou?rs?)\b)", _Ci, _Timeout),
            new Regex(@"\bcode\s+(?:that|which)\s+(?:[\w-]+\s+){0,2}(?:calculates?|generates?|tracks?|counts?|prints?|returns?|sorts?|parses?)\b", _Ci, _Timeout),
            new Regex(@"\b(?:my|this|the|our)\s+code\s+(?:keeps|won't|wont|doesn't|doesnt|isn't|isnt|is\s+not|does\s+not|fails|crashes|throws|returns|compiles|runs)\b", _Ci, _Timeout),
            new Regex(@"\bcoding\s+(?:homework|assignments?|projects?|problems?|questions?|challenges?|interviews?|exercises?|tasks?|bootcamps?|courses?|languages?|tutorials?|help)\b|\bprogramming\s+(?:languages?|homework|assignments?|interviews?|projects?|problems?|tutorials?)\b", _Ci, _Timeout),
            // call the Strava API.
            new Regex(@"\b(?:call|query|hit|consume)\s+(?:the|an?|my|their)\s+(?:[\w.-]+\s+){0,2}api\b", _Ci, _Timeout)
        };

        // Messages that carry an error message or markup need no request frame.
        private static readonly Regex[] _Evidence = new[]
        {
            new Regex(@"traceback\s+\(most\s+recent\s+call\s+last\)|segmentation\s+fault|null(?:pointer|reference)exception|\b(?:type|syntax|value|key|index|attribute|reference|name|import|modulenotfound)error\b|undefined\s+is\s+not\s+a\s+function|cannot\s+read\s+propert(?:y|ies)\s+of\s+(?:undefined|null)|unexpected\s+token|unhandled\s+(?:promise\s+)?rejection|npm\s+err!|command\s+not\s+found|cannot\s+find\s+symbol", _Ci, _Timeout),
            new Regex(@"<\/?(?:div|span|script|style|html|body|head|ul|ol|li|table|tr|td|th|form|input|button|img|section|nav|header|footer|iframe|svg)\b[^>]*>", _Ci, _Timeout)
        };

        // A member describing their own work is not asking for code.
        private static readonly Regex[] _SelfDescription = new[]
        {
            new Regex(@"\b(?:[\w#+.]+[\s-]){0,2}(?:developers?|engineers?|programmers?|coders?|devs?|analysts?|administrators?|admins?)\b", _Ci, _Timeout),
            new Regex(@"\b(?:writing|coding|programming|working\s+(?:with|in|on)|work\s+(?:with|in|on)|i\s+write|i\s+code)\s+(?:[\w#+.]+\s+){0,2}(?:all\s+day|every\s+day|for\s+a\s+living|at\s+work|for\s+work|full[- ]time|daily)\b", _Ci, _Timeout)
        };

        // Code evidence that is decisive on its own (weight 2).
        private static readonly Regex[] _Decisive = new[]
        {
            new Regex(@"^\s*def\s+\w+\s*\([^)\n]*\)\s*(?:->\s*[\w\[\], .]+)?:\s*$", _Lines, _Timeout),
            new Regex(@"^\s*(?:async\s+)?function\s+\w+\s*\(", _Lines, _Timeout),
            new Regex(@"<\?php|<script\b|#include\s*[<""]|public\s+static\s+void\s+main|System\.out\.print|console\.log\(", RegexOptions.Compiled | RegexOptions.CultureInvariant, _Timeout),
            new Regex(@"\bSELECT\s+[\w.*,() ]+?\s+FROM\s+\w+|\b(?:INSERT\s+INTO|CREATE\s+TABLE|DROP\s+TABLE|ALTER\s+TABLE|DELETE\s+FROM)\b|\bUPDATE\s+\w+\s+SET\b", RegexOptions.Compiled | RegexOptions.CultureInvariant, _Timeout),
            new Regex(@"^\s*apiVersion:\s*\S+|^\s*import\s+[\w.]+(?:\s+as\s+\w+)?\s*$|^\s*from\s+[\w.]+\s+import\s+\w+|^#!\/", _Lines, _Timeout)
        };

        // Code evidence counted per occurrence; two occurrences make code.
        private static readonly Regex[] _Signals = new[]
        {
            new Regex(@"^\s*(?:class|public|private|protected|static|func|fn|package|using|let|const|var|return|if|elif|else|for|while|try|catch|except|switch|case)\b[^\n]*[(){};=]", _Lines, _Timeout),
            new Regex(@"=>\s*\{|\)\s*\{\s*$|<\/[a-zA-Z][\w-]*>", _Lines, _Timeout),
            new Regex(@"\b(?:print|printf|len|range|input)\(", RegexOptions.Compiled | RegexOptions.CultureInvariant, _Timeout),
            new Regex(@"^\s*\$\s+\S+|^\s*(?:npm|npx|pip3?|git|sudo|apt-get|apt|brew|docker|kubectl|curl|wget|cd|mkdir|chmod|python3?|node|dotnet|cargo|yarn|pnpm)\s+\S+", _Lines, _Timeout),
            new Regex(@"^\s*(?:FROM|RUN|CMD|COPY|WORKDIR|ENTRYPOINT|EXPOSE|ENV|ARG)\s+\S", _Lines, _Timeout),
            new Regex(@"^\s*(?:kind|metadata|spec|services|image|ports|volumes|replicas|containers|steps|jobs|runs-on|uses):", _Lines, _Timeout),
            new Regex(@"^\s*(?:Sub|Function)\s+\w+\(|^\s*End\s+(?:Sub|Function|If)\s*$|^\s*Dim\s+\w+\s+As\s+\w+|^\s*Next\s+\w+\s*$|^\s*(?:done|fi|esac|then|do)\s*$", _Lines, _Timeout),
            new Regex(@"\$\{?[A-Za-z_]\w*\}?|\b[a-zA-Z_]\w*\.[a-zA-Z_]\w*\(|\b[A-Za-z_]\w*\(\)", RegexOptions.Compiled | RegexOptions.CultureInvariant, _Timeout),
            new Regex(@"^\s*[a-z_]\w*\s*=\s*[\[{(]|\b[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+\s*=[^=]", _Lines, _Timeout),
            new Regex(@"`[^`\n]*(?:[(){}\[\];=<>$\\]|\b(?:import|install|def|return|const|let|var|function|sudo|npm|pip|git|cd)\b|\w\.\w+\()[^`\n]*`", RegexOptions.Compiled | RegexOptions.CultureInvariant, _Timeout),
            new Regex(@"\[[^\]\s]{1,30}\][+*?]|\^\[|\\[dwsb][+*?]?", RegexOptions.Compiled | RegexOptions.CultureInvariant, _Timeout),
            new Regex(@"^[ \t]*[.#]?[A-Za-z][\w-]*(?:(?:[ \t]*[,>+~][ \t]*|[ \t]+)[.#]?[A-Za-z][\w-]*)*[ \t]*\{[ \t]*$|^[ \t]*[a-z-]+[ \t]*:[ \t]*[^;\n]+;[ \t]*$", _Lines, _Timeout)
        };

        private static readonly Regex _ClosingBrace = new Regex(@"^\s*\}\s*[;,)]*\s*$", _Lines, _Timeout);
        private static readonly Regex _JsonKeys = new Regex(@"""[\w -]{1,40}""\s*:\s*[""\[{\d]", RegexOptions.Compiled | RegexOptions.CultureInvariant, _Timeout);
        private static readonly Regex _SemicolonLines = new Regex(@";\s*$", _Lines, _Timeout);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a member message asks for code or contains code to work on.
        /// </summary>
        /// <param name="text">Message text.</param>
        /// <returns>True when the message is out of scope.</returns>
        public static bool IsCodeRequest(string? text)
        {
            if (String.IsNullOrWhiteSpace(text)) return false;
            if (text.Contains("```", StringComparison.Ordinal)) return true;
            string request = text;
            foreach (Regex self in _SelfDescription) request = Replace(self, request, " someone ");
            foreach (Regex pattern in _Requests) if (Matches(pattern, request)) return true;
            foreach (Regex evidence in _Evidence) if (Matches(evidence, text)) return true;
            return CodeSignalCount(text) >= 2;
        }

        /// <summary>
        /// Whether a reply contains code.
        /// </summary>
        /// <param name="text">Reply text.</param>
        /// <returns>True when the reply must be withheld.</returns>
        public static bool ContainsCode(string? text)
        {
            if (String.IsNullOrWhiteSpace(text)) return false;
            if (text.Contains("```", StringComparison.Ordinal) || text.Contains("~~~", StringComparison.Ordinal)) return true;
            return CodeSignalCount(text) >= 2;
        }

        #endregion

        #region Private-Methods

        private static int CodeSignalCount(string text)
        {
            foreach (Regex decisive in _Decisive) if (Matches(decisive, text)) return 2;
            int count = 0;
            foreach (Regex signal in _Signals)
            {
                count += Count(signal, text);
                if (count >= 2) return count;
            }
            count += Math.Min(1, Count(_ClosingBrace, text));
            int keys = Count(_JsonKeys, text);
            count += keys >= 3 ? 2 : keys == 2 ? 1 : 0;
            count += Count(_SemicolonLines, text) >= 3 ? 1 : 0;
            return count;
        }

        private static bool Matches(Regex regex, string text)
        {
            try
            {
                return regex.IsMatch(text);
            }
            catch (RegexMatchTimeoutException)
            {
                return true;
            }
        }

        private static int Count(Regex regex, string text)
        {
            try
            {
                return regex.Matches(text).Count;
            }
            catch (RegexMatchTimeoutException)
            {
                return 2;
            }
        }

        private static string Replace(Regex regex, string text, string replacement)
        {
            try
            {
                return regex.Replace(text, replacement);
            }
            catch (RegexMatchTimeoutException)
            {
                return text;
            }
        }

        #endregion
    }
}
