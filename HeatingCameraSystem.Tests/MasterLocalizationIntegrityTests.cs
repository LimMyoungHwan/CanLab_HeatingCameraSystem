using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    /// <summary>
    /// Master reads external Resources/Lang/{en,ko}.txt at runtime with no compile-time check, so a
    /// duplicate key, a key present in only one language, or a {loc:Loc} key with no entry all fail
    /// silently (raw key or wrong-language text shown to the operator). Mirrors the AgentUI guard.
    /// </summary>
    public class MasterLocalizationIntegrityTests
    {
        private static string RepoRoot => Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));

        private static string MasterDir => Path.Combine(RepoRoot, "HeatingCameraSystem.Master");

        private static string LangPath(string code) =>
            Path.Combine(MasterDir, "Resources", "Lang", $"{code}.txt");

        private static List<KeyValuePair<string, string>> LoadEntries(string code)
        {
            string path = LangPath(code);
            Assert.True(File.Exists(path), $"Missing {path}");

            var entries = new List<KeyValuePair<string, string>>();
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                int separator = line.IndexOf('=');
                if (separator <= 0) continue;

                entries.Add(new KeyValuePair<string, string>(
                    line.Substring(0, separator).Trim(), line.Substring(separator + 1).Trim()));
            }
            return entries;
        }

        private static HashSet<string> Keys(string code) =>
            new(LoadEntries(code).Select(e => e.Key), StringComparer.Ordinal);

        [Fact]
        public void Language_files_define_the_same_key_set()
        {
            var ko = Keys("ko");
            var en = Keys("en");

            var onlyKo = ko.Except(en, StringComparer.Ordinal).ToList();
            var onlyEn = en.Except(ko, StringComparer.Ordinal).ToList();

            Assert.True(onlyKo.Count == 0 && onlyEn.Count == 0,
                $"Language key sets diverged. Only in ko: [{string.Join(", ", onlyKo)}]. Only in en: [{string.Join(", ", onlyEn)}].");
        }

        [Fact]
        public void Language_files_have_no_duplicate_keys()
        {
            foreach (string code in new[] { "ko", "en" })
            {
                var dups = LoadEntries(code)
                    .GroupBy(e => e.Key, StringComparer.Ordinal)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();

                Assert.True(dups.Count == 0, $"{code}.txt defines duplicate key(s): {string.Join(", ", dups)}");
            }
        }

        [Fact]
        public void All_xaml_loc_keys_exist_in_both_languages()
        {
            var ko = Keys("ko");
            var en = Keys("en");

            var xamlFiles = Directory.EnumerateFiles(MasterDir, "*.xaml", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                         && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .ToList();
            Assert.NotEmpty(xamlFiles);

            var used = xamlFiles
                .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"\{loc:Loc\s+([^}\s]+)\s*\}")
                    .Select(m => m.Groups[1].Value))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            Assert.NotEmpty(used);

            var missing = used.Where(key => !ko.Contains(key) || !en.Contains(key)).ToList();

            Assert.True(missing.Count == 0,
                "Master XAML uses loc key(s) missing from ko.txt or en.txt: " + string.Join(", ", missing));
        }
    }
}
