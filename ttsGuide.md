# Guide for configure Voice TTS with VBCable

## What you need

- VB‑Cable (VB‑Audio Virtual Cable)
- Voicemeeter (the standard version is sufficient; Banana/Potato are optional)
- FluxTranslator with Voice TTS enabled

## Step-by-Step Guide

# 1. Install
- Install VB‑Cable: https://vb-audio.com/Cable/
- Install Voicemeeter: https://vb-audio.com/Voicemeeter/
- Restart your computer after installation.

# 2. Configure FluxTranslator
- Open the `Voice TTS` tab in FluxTranslator.
- In the `Output Device` field select: `CABLE Input (VB‑Audio Virtual Cable)`.

# 3. Configure Voicemeeter
- `Hardware Input 1`: select your physical microphone.
- `Hardware Input 2`: select `CABLE Output (VB‑Audio Virtual Cable)` to receive audio from FluxTranslator

# 4. Change the audio input so that Voice TTS is heard in games and applications as a microphone.
- In the applications microphone settings select: `Voicemeeter Out B1 (VB‑Audio Voicemeeter VAIO)`
- If you want Voicemeeter to be used by default in every application go to the sound settings and set `CABLE Output (VB‑Audio Virtual Cable)` as the default device and the default communication device. 

> [!IMPORTANT]
> When using Voicemeeter as the default device it is recommended to enable “Run on Windows Startup” 
