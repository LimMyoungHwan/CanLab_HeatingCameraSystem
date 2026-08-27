using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    /// <summary>
    /// A {loc:Loc} key with no entry in the language files renders as the raw key in the operator's
    /// face instead of a label, and a key present in only one language silently falls back the moment
    /// the language is switched. Both fail silently at runtime, so they are caught here.
    /// </summary>
    public class AgentUiLocalizationIntegrityTests
    {
        private static string RepoRoot => Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));

        private static string LangPath(string code) =>
            Path.Combine(RepoRoot, "HeatingCameraSystem.AgentUI", "Resources", "Lang", $"{code}.txt");

        private static Dictionary<string, string> LoadLang(string code)
        {
            string path = LangPath(code);
            Assert.True(File.Exists(path), $"Missing {path}");

            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                int separator = line.IndexOf('=');
                if (separator <= 0) continue;

                entries[line.Substring(0, separator).Trim()] = line.Substring(separator + 1);
            }
            return entries;
        }

        [Fact]
        public void MainWindow_loc_keys_all_exist_in_both_languages()
        {
            string xamlPath = Path.Combine(RepoRoot, "HeatingCameraSystem.AgentUI", "MainWindow.xaml");
            Assert.True(File.Exists(xamlPath), $"Missing {xamlPath}");

            var ko = LoadLang("ko");
            var en = LoadLang("en");

            var used = Regex.Matches(File.ReadAllText(xamlPath), @"\{loc:Loc\s+([^}\s]+)\s*\}")
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            Assert.NotEmpty(used);

            var missing = used
                .Where(key => !ko.ContainsKey(key) || !en.ContainsKey(key))
                .ToList();

            Assert.True(missing.Count == 0,
                "MainWindow.xaml uses loc key(s) missing from ko.txt or en.txt: " + string.Join(", ", missing));
        }

        [Fact]
        public void Language_files_define_the_same_key_set()
        {
            var ko = LoadLang("ko");
            var en = LoadLang("en");

            var onlyKo = ko.Keys.Except(en.Keys, StringComparer.Ordinal).ToList();
            var onlyEn = en.Keys.Except(ko.Keys, StringComparer.Ordinal).ToList();

            Assert.True(onlyKo.Count == 0 && onlyEn.Count == 0,
                $"Language key sets diverged. Only in ko: [{string.Join(", ", onlyKo)}]. Only in en: [{string.Join(", ", onlyEn)}].");
        }

        [Fact]
        public void Fault_badge_labels_are_translated_not_left_as_the_key()
        {
            var ko = LoadLang("ko");
            var en = LoadLang("en");

            foreach (string key in new[] { "Cam_NoSerial", "Cam_NoSignal" })
            {
                Assert.True(ko.TryGetValue(key, out string? koText) && !string.IsNullOrWhiteSpace(koText), $"ko.txt missing {key}");
                Assert.True(en.TryGetValue(key, out string? enText) && !string.IsNullOrWhiteSpace(enText), $"en.txt missing {key}");
                Assert.NotEqual(key, koText);
                Assert.NotEqual(key, enText);
            }
        }
    }
}
