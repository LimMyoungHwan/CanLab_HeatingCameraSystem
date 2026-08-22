using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class HistoryViewResourceIntegrityTests
    {
        // A {StaticResource} whose key is not defined throws ResourceReferenceKeyNotFoundException when
        // WPF realizes the element. In HistoryView the offending key sat inside the LogItems DataTemplate,
        // so the History screen crashed the whole app the moment it rendered a row. This test fails if any
        // StaticResource key used in HistoryView.xaml is not defined in that view or the app resources.
        [Fact]
        public void HistoryView_has_no_undefined_StaticResource_keys()
        {
            string repoRoot = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
            string viewPath = Path.Combine(repoRoot, "HeatingCameraSystem.Master", "Views", "HistoryView.xaml");
            string appPath = Path.Combine(repoRoot, "HeatingCameraSystem.Master", "App.xaml");

            Assert.True(File.Exists(viewPath), $"Missing {viewPath}");

            string viewXaml = File.ReadAllText(viewPath);
            string appXaml = File.Exists(appPath) ? File.ReadAllText(appPath) : string.Empty;

            var defined = Keys(viewXaml, "x:Key=\"([^\"]+)\"")
                .Concat(Keys(appXaml, "x:Key=\"([^\"]+)\""))
                .ToHashSet(StringComparer.Ordinal);

            var undefined = Keys(viewXaml, @"\{StaticResource\s+([^}]+)\}")
                .Select(k => k.Trim())
                .Distinct(StringComparer.Ordinal)
                .Where(k => !defined.Contains(k))
                .ToList();

            Assert.True(undefined.Count == 0,
                "HistoryView.xaml references undefined StaticResource key(s) (WPF crashes on render): "
                + string.Join(", ", undefined));
        }

        private static IEnumerable<string> Keys(string xaml, string pattern)
        {
            foreach (Match m in Regex.Matches(xaml, pattern))
            {
                yield return m.Groups[1].Value;
            }
        }
    }
}
