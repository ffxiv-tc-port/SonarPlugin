using Sonar;
using Sonar.Enums;
using SonarPlugin.Utility;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace SonarPlugin.Config
{
    public sealed partial class LocalizationConfig
    {
        public record PresetConfig(SonarLanguage Db, string? Plugin, string? Dll);

        public static readonly FrozenDictionary<LocalizationPreset, PresetConfig> s_presets = new Dictionary<LocalizationPreset, PresetConfig>()
        {
            { LocalizationPreset.Default, new(SonarLanguage.Default, null, null) },
            { LocalizationPreset.Japanese, new(SonarLanguage.Japanese, null, null) },
            { LocalizationPreset.English, new(SonarLanguage.English, null, null) },
            { LocalizationPreset.German, new(SonarLanguage.German, null, null) },
            { LocalizationPreset.French, new(SonarLanguage.French, null, null) },
            { LocalizationPreset.Chinese, new(SonarLanguage.ChineseSimplified, null, null) },
            // TC fork: the Traditional Chinese preset also selects the embedded zh-TW AG.EnumLocalization
            // language files. Upstream leaves these null (= English fallbacks baked into the enum attributes).
            { LocalizationPreset.ChineseTraditional, new(SonarLanguage.ChineseTraditional, EnumLocUtils.TcPluginLanguage, EnumLocUtils.TcSonarLanguage) },
            { LocalizationPreset.Korean, new(SonarLanguage.Korean, null, null) },

        }.ToFrozenDictionary();
    }
}
