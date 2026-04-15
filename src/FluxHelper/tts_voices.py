"""Voice and language configuration for the TTS engine."""

VOICE_FALLBACKS = {
    "no-NO-FinnNeural": "no-NO-PernilleNeural",
    "no-NO-PernilleNeural": "no-NO-FinnNeural",
    "sv-SE-SofieNeural": "sv-SE-MattiasNeural",
    "sv-SE-MattiasNeural": "sv-SE-SofieNeural",
    "da-DK-ChristelNeural": "da-DK-JeppeNeural",
    "da-DK-JeppeNeural": "da-DK-ChristelNeural",
    "fi-FI-NooraNeural": "fi-FI-HarriNeural",
    "fi-FI-HarriNeural": "fi-FI-NooraNeural",
}

TTS_LANGUAGES = {
    "en": {
        "name": "English",
        "voices": [
            "en-US-EmmaMultilingualNeural",
            "en-US-AvaMultilingualNeural",
            "en-US-AndrewMultilingualNeural",
            "en-US-BrianMultilingualNeural",
            "en-US-JennyNeural",
            "en-US-GuyNeural",
            "en-GB-SoniaNeural",
            "en-GB-RyanNeural",
        ],
    },
    "pl": {
        "name": "Polish",
        "voices": [
            "pl-PL-ZofiaNeural",
            "pl-PL-MarekNeural",
        ],
    },
    "de": {
        "name": "German",
        "voices": [
            "de-DE-KatjaNeural",
            "de-DE-ConradNeural",
            "de-DE-AmalaNeural",
            "de-DE-BerndNeural",
        ],
    },
    "ru": {
        "name": "Russian",
        "voices": [
            "ru-RU-SvetlanaNeural",
            "ru-RU-DmitryNeural",
        ],
    },
    "fr": {
        "name": "French",
        "voices": [
            "fr-FR-DeniseNeural",
            "fr-FR-HenriNeural",
            "fr-FR-EloiseNeural",
        ],
    },
    "it": {
        "name": "Italian",
        "voices": [
            "it-IT-ElsaNeural",
            "it-IT-DiegoNeural",
            "it-IT-IsabellaNeural",
        ],
    },
    "es": {
        "name": "Spanish",
        "voices": [
            "es-ES-ElviraNeural",
            "es-ES-AlvaroNeural",
            "es-ES-AbrilNeural",
        ],
    },
    "cs": {
        "name": "Czech",
        "voices": [
            "cs-CZ-VlastaNeural",
            "cs-CZ-AntoninNeural",
        ],
    },
    "uk": {
        "name": "Ukrainian",
        "voices": [
            "uk-UA-PolinaNeural",
            "uk-UA-OstapNeural",
        ],
    },
    "zh": {
        "name": "Chinese",
        "voices": [
            "zh-CN-XiaoxiaoNeural",
            "zh-CN-YunxiNeural",
            "zh-CN-YunjianNeural",
            "zh-CN-XiaoyiNeural",
        ],
    },
    "ja": {
        "name": "Japanese",
        "voices": [
            "ja-JP-NanamiNeural",
            "ja-JP-KeitaNeural",
        ],
    },
    "ko": {
        "name": "Korean",
        "voices": [
            "ko-KR-SunHiNeural",
            "ko-KR-InJoonNeural",
        ],
    },
    "pt": {
        "name": "Portuguese",
        "voices": [
            "pt-PT-RaquelNeural",
            "pt-PT-DuarteNeural",
            "pt-BR-FranciscaNeural",
            "pt-BR-AntonioNeural",
        ],
    },
    "nl": {
        "name": "Dutch",
        "voices": [
            "nl-NL-ColetteNeural",
            "nl-NL-FennaNeural",
            "nl-NL-MaartenNeural",
        ],
    },
    "sv": {
        "name": "Swedish",
        "voices": [
            "sv-SE-SofieNeural",
            "sv-SE-MattiasNeural",
        ],
    },
    "fi": {
        "name": "Finnish",
        "voices": [
            "fi-FI-NooraNeural",
            "fi-FI-HarriNeural",
        ],
    },
    "da": {
        "name": "Danish",
        "voices": [
            "da-DK-ChristelNeural",
            "da-DK-JeppeNeural",
        ],
    },
    "no": {
        "name": "Norwegian",
        "voices": [
            "no-NO-PernilleNeural",
            "no-NO-FinnNeural",
        ],
    },
    "tr": {
        "name": "Turkish",
        "voices": [
            "tr-TR-EmelNeural",
            "tr-TR-AhmetNeural",
        ],
    },
    "ar": {
        "name": "Arabic",
        "voices": [
            "ar-SA-ZariyahNeural",
            "ar-SA-HamedNeural",
        ],
    },
}

DEFAULT_VOICE = "en-US-EmmaMultilingualNeural"
