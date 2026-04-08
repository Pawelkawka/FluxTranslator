namespace FluxTranslator.Core;

public enum TranslationEngine
{
    LibreTranslate,
    CTranslate2,
}

public static class AppSettings
{
    public const string AppName         = "FluxTranslator";
    public const string AppPublisher    = "PawelKawka";
    public const string AppBaseVersion  = "1.1.1";
    public const string AppBuild        = "0804202601";
    public const string AppVersion      = AppBaseVersion + "." + AppBuild;
    public const string ReleaseApiUrl   = "https://api.github.com/repos/PawelKawka/FluxTranslator/releases/latest";
    public const string ReleasesPageUrl = "https://github.com/PawelKawka/FluxTranslator/releases";
    public const int    SttPort         = 5001;

    public const string DefaultHotkeyTranslate = "Ctrl+M";
    public const string DefaultHotkeyCopy      = "Ctrl+Shift+C";
    public const string DefaultHotkeyKillAll   = "Ctrl+Q";

    public const string           DefaultLibreTranslateUrl  = "http://localhost:5000/translate";
    public const TranslationEngine DefaultTranslationEngine = TranslationEngine.LibreTranslate;
    public const string           DefaultCTranslate2ModelsDir = "models";

    public const int    DefaultFontSize          = 18;
    public const string DefaultTextColor         = "#FFFFFF";
    public const string DefaultBackgroundColor   = "#000000";
    public const int    DefaultBackgroundOpacity = 80;
    public const int    DefaultPadding           = 10;
    public const int    DefaultBorderWidth       = 0;
    public const string DefaultBorderColor       = "#4c4c4c";
    public const int    DefaultCornerRadius      = 10;
    public const string DefaultFontFamily        = "Segoe UI";
    public const bool   DefaultFontBold          = false;

    public const int    DefaultOverlayDisplayTime    = 15;
    public const double DefaultInitialSilenceTimeout  = 4.0;
    public const double DefaultSilenceTimeout         = 0.2;

    public const string DefaultSourceLanguage  = "pl-PL";
    public const string DefaultTargetLanguage  = "en";
    public const string DefaultOverlayPosition = "top_center";

    public static readonly IReadOnlyDictionary<string, string> SourceLanguages =
        new Dictionary<string, string>
        {
            { "en-US", "English (US)" },
            { "pl-PL", "Polish"       },
            { "de-DE", "German"       },
            { "ru-RU", "Russian"      },
            { "fr-FR", "French"       },
            { "it-IT", "Italian"      },
            { "es-ES", "Spanish"      },
            { "cs-CZ", "Czech"        },
            { "uk-UA", "Ukrainian"    },
            { "zh-CN", "Chinese"      },
            { "ja-JP", "Japanese"     },
            { "ko-KR", "Korean"       },
            { "pt-PT", "Portuguese"   },
            { "nl-NL", "Dutch"        },
            { "sv-SE", "Swedish"      },
            { "fi-FI", "Finnish"      },
            { "da-DK", "Danish"       },
            { "no-NO", "Norwegian"    },
            { "tr-TR", "Turkish"      },
            { "ar-SA", "Arabic"       },
        };

    public static readonly IReadOnlyDictionary<string, string> TargetLanguages =
        new Dictionary<string, string>
        {
            { "en", "English" },
            { "pl", "Polish"  },
            { "de", "German"  },
            { "ru", "Russian" },
            { "fr", "French"  },
            { "it", "Italian" },
            { "es", "Spanish" },
            { "cs", "Czech"   },
            { "uk", "Ukrainian" },
            { "zh", "Chinese" },
            { "ja", "Japanese" },
            { "ko", "Korean" },
            { "pt", "Portuguese" },
            { "nl", "Dutch" },
            { "sv", "Swedish" },
            { "fi", "Finnish" },
            { "da", "Danish" },
            { "no", "Norwegian" },
            { "tr", "Turkish" },
            { "ar", "Arabic" },
        };

    public static readonly IReadOnlyDictionary<string, string> CTranslate2Languages =
        new Dictionary<string, string>
        {
            { "en", "English"    },
            { "pl", "Polish"     },
            { "de", "German"     },
            { "ru", "Russian"    },
            { "fr", "French"     },
            { "it", "Italian"    },
            { "es", "Spanish"    },
            { "cs", "Czech"      },
            { "uk", "Ukrainian"  },
            { "zh", "Chinese"    },
            { "ja", "Japanese"   },
            { "ko", "Korean"     },
            { "pt", "Portuguese" },
            { "nl", "Dutch"      },
            { "sv", "Swedish"    },
            { "fi", "Finnish"    },
            { "da", "Danish"     },
            { "no", "Norwegian"  },
            { "tr", "Turkish"    },
            { "ar", "Arabic"     },
        };

    public static readonly IReadOnlyDictionary<string, string> OverlayPositions =
        new Dictionary<string, string>
        {
            { "top_left",      "Top Left"      },
            { "top_center",    "Top Center"    },
            { "top_right",     "Top Right"     },
            { "bottom_left",   "Bottom Left"   },
            { "bottom_center", "Bottom Center" },
            { "bottom_right",  "Bottom Right"  },
        };
}
