using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace HeatingCameraSystem.AgentUI.Localization
{
    /// <summary>언어 선택 UI에 표시되는 항목(언어 코드 + 표시명).</summary>
    public sealed class LanguageOption
    {
        public string Code { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// 외부 <c>Resources/Lang/&lt;code&gt;.txt</c> 파일(key=value, # 주석) 기반 런타임 i18n.
    /// exe 옆에 새 txt 파일을 놓기만 하면 언어가 추가된다 — 재빌드 불필요. 활성 언어에 키가 없으면
    /// 영어가 fallback이다. XAML은 <c>{loc:Loc Key}</c> 확장으로 바인딩한다.
    /// </summary>
    public sealed class LocalizationManager : INotifyPropertyChanged
    {
        private const string FallbackCode = "en";
        private const string DefaultCode = "ko";

        private static readonly string LangDir =
            Path.Combine(AppContext.BaseDirectory, "Resources", "Lang");
        private static readonly string PrefFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeatingCameraSystem", "language.txt");

        public static LocalizationManager Instance { get; } = new();

        private Dictionary<string, string> _current = new(StringComparer.Ordinal);
        private Dictionary<string, string> _fallback = new(StringComparer.Ordinal);
        private string _currentCode = DefaultCode;

        public event PropertyChangedEventHandler? PropertyChanged;

        public IReadOnlyList<LanguageOption> AvailableLanguages { get; private set; } =
            Array.Empty<LanguageOption>();

        private LocalizationManager()
        {
            Discover();
            _fallback = Load(FallbackCode);
            SetLanguage(LoadPreferredCode(), persist: false);
        }

        /// <summary>키 조회: 활성 언어 → 영어 fallback → 키 문자열 자체 순으로 반환한다.</summary>
        public string this[string key]
        {
            get
            {
                if (_current.TryGetValue(key, out string? v)) return v;
                if (_fallback.TryGetValue(key, out string? f)) return f;
                return key;
            }
        }

        public string CurrentLanguage
        {
            get => _currentCode;
            set => SetLanguage(value, persist: true);
        }

        public void SetLanguage(string code, bool persist = true)
        {
            if (string.IsNullOrWhiteSpace(code)) return;
            _current = Load(code);
            _currentCode = code;
            if (persist) SavePreferredCode(code);
            // "Item[]"은 WPF의 인덱서 변경 토큰: 모든 {loc:Loc} 바인딩을 한 번에 갱신한다.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        }

        /// <summary>LangDir의 txt 파일을 훑어 언어 목록을 만든다. 표시명은 각 파일의 Lang_Name 키에서 읽는다.</summary>
        private void Discover()
        {
            var list = new List<LanguageOption>();
            if (Directory.Exists(LangDir))
            {
                foreach (string file in Directory.EnumerateFiles(LangDir, "*.txt").OrderBy(f => f))
                {
                    string code = Path.GetFileNameWithoutExtension(file);
                    Dictionary<string, string> dict = Parse(file);
                    string name = dict.TryGetValue("Lang_Name", out string? n) && !string.IsNullOrWhiteSpace(n)
                        ? n
                        : code;
                    list.Add(new LanguageOption { Code = code, DisplayName = name });
                }
            }
            AvailableLanguages = list;
        }

        private static Dictionary<string, string> Load(string code)
        {
            string path = Path.Combine(LangDir, code + ".txt");
            return File.Exists(path) ? Parse(path) : new Dictionary<string, string>(StringComparer.Ordinal);
        }

        private static Dictionary<string, string> Parse(string path)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    dict[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Loc] parse failed for {path}: {ex.Message}");
            }
            return dict;
        }

        private static string LoadPreferredCode()
        {
            try
            {
                if (File.Exists(PrefFile))
                {
                    string code = File.ReadAllText(PrefFile).Trim();
                    if (!string.IsNullOrWhiteSpace(code)) return code;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Loc] preference load failed: {ex.Message}");
            }
            return DefaultCode;
        }

        private static void SavePreferredCode(string code)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PrefFile)!);
                File.WriteAllText(PrefFile, code);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Loc] preference save failed: {ex.Message}");
            }
        }
    }
}
