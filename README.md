# NeuroSync for Unity: Nova Voice Avatar

[![Unity Version](https://img.shields.io/badge/Unity-6000.6.5f1-black.svg?style=for-the-badge&logo=unity)](https://unity.com/)
[![Inference Engine](https://img.shields.io/badge/Inference-2.6.1-blue.svg?style=for-the-badge)](https://docs.unity3d.com/Packages/com.unity.ai.inference@2.6/manual/index.html)
[![Hugging Face](https://img.shields.io/badge/%F0%9F%A4%97%20Hugging%20Face-Models-yellow.svg?style=for-the-badge)](https://huggingface.co/AnimaVR/NEUROSYNC)

This project runs the NeuroSync lip-sync model directly in Unity with the **Unity AI Inference Engine** (formerly Sentis).

> **About this fork:** This repository is a fork of [skykim/NeuroSync_Unity](https://github.com/skykim/NeuroSync_Unity). It adds **Nova**, a voice assistant that runs entirely on your device. You can talk to the NeuroSync avatar in English, Korean or Arabic, and it answers with synthesized speech and live lip-sync. It needs no API keys or cloud services. The original `LipSyncScene` demo still works as before.

## ✨ Key Features

### From the original project
- **On-device lip-sync:** runs the NeuroSync model on the user's device with the Inference Engine.
- **Local API server:** includes an example Flask-based API server for model inference as an alternative.

### Added in this fork: "Ask Nova anything"
- **Fully on-device voice pipeline:** **Mic → Silero VAD → speech-to-text → Gemma3-270M → Supertonic 3 TTS → NeuroSync lip-sync**.
- **Three languages:**
  - Speech-to-text uses SenseVoice for English and Korean, and Whisper base for Arabic.
  - The EN/KR/AR toggle selects the speech-to-text language. You can also type your input.
- **Replies in the user's language:**
  - Gemma3-270M replies in the same language (script) as your input.
  - An empty reply, or one in the wrong language, is retried once and then replaced with a "not sure" line.
- **Live lip-sync:**
  - `AvatarLipSync` runs NeuroSync on each TTS clip as it starts and drives the ARKit blendshapes on the head. The teeth follow the jaw.
  - If NeuroSync fails, the jaw moves with the audio loudness instead.
- **Voice picker:** choose from the Supertonic voices F1–F5 / M1–M5. Each choice plays a short sample.
- **Right-to-left text:** NotoSansKR and NotoSansArabic font fallbacks. Arabic is shaped and laid out right-to-left with [RTLTMPro](https://github.com/hk1ll3r/RTLTMPro).
- **Editable persona:** Nova's system prompt, voices and greetings live in `Assets/VoiceAgent/Resources/Persona.json`.

## ⚙️ Requirements

- **Unity:** `6000.6.5f1`
- **Inference Engine:** `2.6.1`
- **A microphone** for voice input (typed input also works)

The voice pipeline models install automatically as UPM packages from Hugging Face:

| Package | Role |
| --- | --- |
| `com.sky.sentis.silero-vad` | Voice activity detection |
| `com.sky.sentis.sensevoice` | Speech-to-text (English, Korean) |
| `com.sky.sentis.gemma3-270m-it` | Reply generation |
| `com.sky.sentis.supertonic` | Text-to-speech |
| `com.nosuchstudio.rtltmpro` | Arabic / RTL text rendering |

## 🧠 NeuroSync Model

This project needs the NeuroSync model converted for the Inference Engine (`.sentis`, converted from ONNX).

1. The original model is available from the [AnimaVR Hugging Face repository](https://huggingface.co/AnimaVR/NEUROSYNC).
2. To convert it to ONNX yourself, follow the instructions in [skykim/NeuroSync_Local_API_ONNX](https://github.com/skykim/NeuroSync_Local_API_ONNX).

Ready-to-use `.sentis` files are included in `StreamingAssets.zip` (see below).

## 🚀 Getting Started

### 1. Project setup

1. Clone this repository.
2. Download and unzip **[StreamingAssets.zip](https://drive.google.com/file/d/1ruLW-G1EcVpA80muLV_7OLDIaTXzemBB/view?usp=sharing)**.
3. Put its contents (`neurosync_fp16.sentis` and `audio_feature_extractor_fp16.sentis`) in `Assets/StreamingAssets/`.
4. Open the project in Unity `6000.6.5f1` and wait for the packages to resolve. The first import downloads the voice models from Hugging Face.
5. *(Needed for Arabic speech input only)* Run **VoiceAgent > Speech > Download Whisper Base**. This installs Whisper (about 194 MB) into `Assets/StreamingAssets/Whisper/`.

### 2. Run the Nova voice demo

1. Open `Assets/Scenes/NovaAvatarScene.unity`.
2. Enter Play Mode.
3. Choose a language with the **EN / KR / AR** toggle (top left). You can also pick a voice there.
4. Tap the orb and speak, or type a message in the chat panel on the right. Nova answers out loud and the avatar lip-syncs to the reply.

### 3. Run the original lip-sync demo

1. Open `Assets/Scenes/LipSyncScene.unity`.
2. Enter Play Mode.
3. Press `Space` to play the demo audio and see the lip-sync.

The demo cycles through these pre-recorded prompts:

> 1.  (English) As a good friend once said, manners maketh man.
> 2.  (German) Wie mein guter Freund immer sagt: Gute Manieren machen erst den Menschen aus.
> 3.  (Japanese) いい友達が言ってたんだけど、礼儀正しさが人を作るんだって。
> 4.  (Korean) 좋은 친구가 그러더라고, 예의가 사람을 만든다고.

## 📦 Building a Player

`ModelBuildStaging` copies the voice models from the package cache into `StreamingAssets/Models/` before a build and removes the copy afterwards. On Android, the models are copied out of the APK the first time the app launches.

## 🗂️ Repository Notes

- **No Git LFS.** GitHub rejects LFS uploads to public forks, so binary assets are committed as regular git files.
- **Not tracked in git (too large):**
  - `Assets/StreamingAssets/*.sentis`: the NeuroSync models (about 485 MB). Get them from `StreamingAssets.zip`.
  - `Assets/StreamingAssets/Whisper/`: installed from the VoiceAgent menu.
  - `Assets/StreamingAssets/Models/`: staged automatically during builds.
- **Code layout:**
  - `Assets/VoiceAgent/`: the voice dialog (audio, speech-to-text, LLM, TTS, UI, language and RTL handling).
  - `Assets/Models/AvatarLipSync.cs`: connects TTS output to NeuroSync.

## 🙏 Credits

- [skykim/NeuroSync_Unity](https://github.com/skykim/NeuroSync_Unity): the original Unity integration of NeuroSync.
- [AnimaVR/NEUROSYNC](https://huggingface.co/AnimaVR/NEUROSYNC): the NeuroSync model.
- The Sky-Kim `com.sky.sentis.*` packages: Silero VAD, SenseVoice, Gemma3-270M and Supertonic for the Inference Engine.

[![NeuroSync in Unity](https://img.youtube.com/vi/w8UOyJVR37A/0.jpg)](https://www.youtube.com/watch?v=w8UOyJVR37A)
