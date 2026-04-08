<div align="center">
   <h1>FluxTranslator</h1>
</div>

FluxTranslator is a Windows desktop overlay for speech to text translation. It listens to your microphone, recognises speech, translates it in real time and displays the result in a compact on screen overlay.

<p align="center">
   <img src="assets/demo.gif" alt="FluxTranslator" width="900">
</p>

## Features

- **Speech recognition** using your microphone.
- **Real-time translation** with two translation modes:
   - **LibreTranslate**
   - **CTranslate2**
- **Overlay UI** that displays translated text on top of other applications.
- **Customisable appearance** including font, size, colors, opacity, padding, borders, and screen position.
- **Hotkey support** for starting recognition, copying the last translation, and stopping everything quickly.
- **Model management** directly from the app.

## Installation

### Windows
1. Go to the [Releases](https://github.com/PawelKawka/FluxTranslator/releases) page.
2. Download `FluxTranslator_Setup.exe`.
3. Run the installer and follow the instructions.
4. Launch the app via the Desktop shortcut or Start Menu.

> [!WARNING]
> #### Windows SmartScreen
> Because this project is free and open source the installer does not come with a digital certificate. Windows may display a SmartScreen message when you first run it.

## Translation Engines

### CTranslate2 (Recommended)
- Uses Helsinki-NLP/opus-mt models converted for CTranslate2
- Best choice if you want local translation without depending on an external service.

> [!NOTE]
> Even though CTranslate2 is recommended not every language pair is available. In some cases a specific pair may be missing and the only working option will be LibreTranslate.

### LibreTranslate

- Default endpoint: `http://localhost:5000/translate`

#### LibreTranslate setup with Docker

- If LibreTranslate is not installed yet, the easiest way to run it is with Docker.

1. Download and install Docker Desktop from [docker.com](https://www.docker.com/).
2. Start Docker Desktop and wait until Docker is running.
3. Run the command below in terminal. This command will install a LibreTranslate instance with the selected languages in Docker:

```bash
docker run -d --name libretranslate -p 5000:5000 -e LT_LOAD_ONLY=LANG,LANG libretranslate/libretranslate
```

> [!NOTE]
> Replace `LANG,LANG` with the languages of your choice. 
> If you want more languages for example English, Polish and German, use: `LT_LOAD_ONLY=en,pl,de`.

## Supported Languages

FluxTranslator includes support for common source and target languages (ISO 639-1 codes):

| Language | Code |
|---|---:|
| English | `en` |
| Polish | `pl` |
| German | `de` |
| Russian | `ru` |
| French | `fr` |
| Italian | `it` |
| Spanish | `es` |
| Czech | `cs` |
| Ukrainian | `uk` |
| Chinese | `zh` |
| Japanese | `ja` |
| Korean | `ko` |
| Portuguese | `pt` |
| Dutch | `nl` |
| Swedish | `sv` |
| Finnish | `fi` |
| Danish | `da` |
| Norwegian | `no` |
| Turkish | `tr` |
| Arabic | `ar` |

> [!WARNING]
> The program was developed and tested on Windows 11. The program may not work properly and may display graphical glitches on Windows 10.

## About

- Developed by Pawel Kawka.
- Open source and free to use.
