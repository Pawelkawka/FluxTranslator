<div align="center">
   <h1>FluxTranslator</h1>
</div>

FluxTranslator is a Windows desktop overlay for speech to text translation. It listens to your microphone, recognises speech, translates it in real time and displays the result in a compact on screen overlay. Supports Voice TTS, translated audio can be routed to microphone via VBCable so other apps can receive the translated speech as mic input.

<p align="center">
   <img src="assets/demo.gif" alt="FluxTranslator" width="900">
</p>

<table align="center">
   <tr>
      <td><img src="assets/GeneralTab.png" alt="General tab" width="320"></td>
      <td><img src="assets/AppearanceTab.png" alt="Appearance tab" width="320"></td>
   </tr>
   <tr>
      <td><img src="assets/Behavior.png" alt="Behavior tab" width="320"></td>
      <td><img src="assets/HotkeysTab.png" alt="Hotkeys tab" width="320"></td>
   </tr>
</table>

## Features

- **Speech recognition** using your microphone.
- **Real-time translation** with two translation modes:
   - **LibreTranslate**
   - **CTranslate2**
- **Overlay UI** that displays translated text on top of other applications.
- **Customisable appearance** including font, size, colors, opacity, padding, borders, and screen position.
- **Hotkey support** for starting recognition, copying the last translation, and stopping everything quickly.
- **Model management** directly from the app.
- **Voice TTS** When using VBCable to route audio to the microphone. This is useful when you want to speak in your own language and pass the translated voice output into another app or call.

## Installation

### Requirements
- .NET 8 SDK or later
- Windows 11 / 10
- Python 3.14
- FFmpeg
- VBCable if you want to feed Voice TTS into a microphone input

### Build from source

Building locally ensures the exe has a unique binary signature.

1. Go to the [Releases](https://github.com/PawelKawka/FluxTranslator/releases) or clone this repo.
2. Download the latest source code.
3. Open a PowerShell in the repository.
4. Run script:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\build.ps1
   ```
5. The compiled output lands in the `build\` folder. Run `FluxTranslator.exe` from there.

You can customize the output directory:
```powershell
.\build.ps1 -OutputDir "set path" 
```

> [!IMPORTANT]
> For FluxTranslator to work correctly first run `fluxhelper.py` then run `FluxHelper.exe`

## Voice TTS

- To route Voice TTS into a microphone input you need VBCable. See [How to configure Voice TTS with VBCable](ttsGuide.md).

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
> Example: `LT_LOAD_ONLY=en,pl,de`

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

## About

- Developed by Pawel Kawka.
- Open source and free to use.
- Voice TTS is implemented using [edge-tts](https://github.com/rany2/edge-tts)
