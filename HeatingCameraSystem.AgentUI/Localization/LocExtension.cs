using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace HeatingCameraSystem.AgentUI.Localization
{
    /// <summary>
    /// XAML 마크업 확장: <c>{loc:Loc Some_Key}</c>가 언어 변경 시 라이브로 갱신되는 지역화
    /// 문자열을 바인딩한다. <see cref="LocalizationManager"/>의 문자열 인덱서를 기반으로 한다.
    /// </summary>
    public sealed class LocExtension : MarkupExtension
    {
        public string Key { get; set; } = string.Empty;

        public LocExtension() { }

        public LocExtension(string key) => Key = key;

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var binding = new Binding($"[{Key}]")
            {
                Source = LocalizationManager.Instance,
                Mode = BindingMode.OneWay
            };
            return binding.ProvideValue(serviceProvider);
        }
    }
}
