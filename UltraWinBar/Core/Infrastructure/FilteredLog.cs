using ManagedShell.Common.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace UltraWinBar.Utilities
{
    internal sealed class FilteredLog : ILog
    {
        private readonly ILog _inner;
        private volatile MaskSet _maskSet = new MaskSet([], false);

        public FilteredLog(ILog inner, IEnumerable<string> masks)
        {
            _inner = inner;
            UpdateMasks(masks);
        }

        public void UpdateMasks(IEnumerable<string> masks)
        {
            List<MaskRule> rules = [];

            foreach (string rawMask in masks ?? [])
            {
                string mask = rawMask?.Trim();
                if (string.IsNullOrEmpty(mask))
                {
                    continue;
                }

                bool exclude = mask[0] == '!';
                if (exclude)
                {
                    mask = mask.Substring(1);
                }

                if (mask.Length == 0)
                {
                    continue;
                }

                string pattern = "^" + Regex.Escape(mask)
                    .Replace("\\*", ".*")
                    .Replace("\\?", ".") + "$";
                rules.Add(new MaskRule(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), exclude));
            }

            _maskSet = new MaskSet(rules, rules.Any(rule => !rule.Exclude));
        }

        public void Log(object sender, LogEventArgs e)
        {
            if (e.Severity == LogSeverity.Error || e.Severity == LogSeverity.Fatal || IsAllowed(e.Message ?? string.Empty))
            {
                _inner.Log(sender, e);
            }
        }

        private bool IsAllowed(string message)
        {
            MaskSet maskSet = _maskSet;
            if (maskSet.Rules.Count == 0)
            {
                return true;
            }

            bool allowed = !maskSet.HasIncludeRules;
            foreach (MaskRule rule in maskSet.Rules)
            {
                if (rule.Pattern.IsMatch(message))
                {
                    allowed = !rule.Exclude;
                }
            }

            return allowed;
        }

        private sealed class MaskSet
        {
            public List<MaskRule> Rules { get; }
            public bool HasIncludeRules { get; }

            public MaskSet(List<MaskRule> rules, bool hasIncludeRules)
            {
                Rules = rules;
                HasIncludeRules = hasIncludeRules;
            }
        }

        private sealed class MaskRule
        {
            public Regex Pattern { get; }
            public bool Exclude { get; }

            public MaskRule(Regex pattern, bool exclude)
            {
                Pattern = pattern;
                Exclude = exclude;
            }
        }
    }
}
