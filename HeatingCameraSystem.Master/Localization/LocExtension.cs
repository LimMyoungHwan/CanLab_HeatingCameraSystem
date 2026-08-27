using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace HeatingCameraSystem.Master.Localization
{
    /// <summary>
    /// XAML 마크업 확장: <c>{loc:Loc Some_Key}</c>가 번역 문자열을 바인딩하며 언어가 바뀌면
    /// 실시간으로 갱신된다. <see cref="LocalizationManager"/>의 문자열 인덱서를 사용한다.
    /// </summary>
    public sealed class LocExtension : MarkupExtension
    {
        public string Key { get; set; } = string.Empty;

        public LocExtension() { }

        public LocExtension(string key) => Key = key;

        /// <summary>LocalizationManager 인덱서 경로 <c>[Key]</c>에 대한 OneWay 바인딩을 만들어 돌려준다.</summary>
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
