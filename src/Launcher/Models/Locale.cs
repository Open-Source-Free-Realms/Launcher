using System.Collections.Generic;

namespace Launcher.Models;

/// <summary>
/// Locales supported by the original game.
/// </summary>
public enum LocaleType
{
    None,
    zh_CN,  // Chinese - Simplified
    de_DE,  // German
    fr_FR,  // French
    en_GB,  // English (United Kingdom)
    ja_JP,  // Japanese
    ko_KR,  // Korean
    zh_TW,  // Chinese - Traditional
    en_US,  // English
    es_ES,  // Spanish
    it_IT,  // Italian
    pt_PT,  // Portuguese (Portugal)
    ru_RU,  // Russian
    sv_SE,  // Swedish
    pt_BR,  // Portuguese (Brazil)
    es_MX,  // Spanish (Mexico)
    nl_NL,  // Dutch
    pl_PL,  // Polish
    fi_FL,  // Finnish (Finland)
    da_DK,  // Danish
    nn_NO   // Norwegian Nynorsk (Norway)
}

public class Locale
{
    public string Name { get; set; }
    public LocaleType Type { get; set; }

    public Locale(LocaleType type, string name)
    {
        Type = type;
        Name = name;
    }

    public static readonly List<Locale> Supported = [
        new Locale(LocaleType.en_US, "English"),
        new Locale(LocaleType.fr_FR, "French"),
        new Locale(LocaleType.de_DE, "German"),
        new Locale(LocaleType.es_ES, "Spanish")
        ];
}