# DictaClone speech-model licensing

DictaClone does not bundle a speech model in either the portable archive or the
installer. On first use, the user may choose to download one of the supported
models into `%LocalAppData%\DictaClone\Models`.

## Supported downloads

| DictaClone name | File | Source | SHA-256 |
| --- | --- | --- | --- |
| `base.en` | `ggml-base.en.bin` | `ggerganov/whisper.cpp` on Hugging Face | `A03779C86DF3323075F5E796CB2CE5029F00EC8869EEE3FDFB897AFE36C6D002` |
| `small.en` | `ggml-small.en.bin` | `ggerganov/whisper.cpp` on Hugging Face | `C6138D6D58ECC8322097E0F987C32F1BE8BB0A18532A3F88F734D1BBF9C41E5D` |

These are converted OpenAI Whisper model weights published for whisper.cpp.
OpenAI states that the Whisper code and model weights are released under the
MIT License. The source model repository also identifies its license as MIT:

- https://github.com/openai/whisper
- https://github.com/openai/whisper/blob/main/LICENSE
- https://huggingface.co/ggerganov/whisper.cpp

The MIT license text appears in `THIRD-PARTY-NOTICES.md` beside this file.
Users remain responsible for deciding whether a model and its outputs are
appropriate for their use and jurisdiction.
