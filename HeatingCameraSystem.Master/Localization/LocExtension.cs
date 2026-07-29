using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace HeatingCameraSystem.Master.Localization
{
    /// <summary>
    /// XAML markup extension: <c>{loc:Loc Some_Key}</c> binds a localized string that live-updates
    /// when the language changes. Backed by <see cref="LocalizationManager"/>'s string indexer.
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
