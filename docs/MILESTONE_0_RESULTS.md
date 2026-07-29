# Milestone 0 results

Status: complete and awaiting review

Completed: 2026-07-29

## Outcome

Milestone 0 established a reproducible .NET 10 Windows solution and proved that
local CPU transcription is viable on the target laptop. `base.en` is the selected
default model because it was approximately three times faster than `small.en`,
used less than half as much peak memory, and produced the same zero word-error
score on the fixed benchmark.

## Target environment

- Windows 11 x64, version 25H2, build 26200.8875
- Intel Core i7-1370P, 20 logical processors
- 31.66 GiB physical RAM
- Intel Iris Xe graphics
- .NET SDK 10.0.302 and runtime 10.0.10
- Whisper.net 1.9.1 CPU runtime
- NAudio 2.3.0

The SDK is pinned in `global.json`. Development scripts prefer an ignored,
repository-local `.dotnet` installation and otherwise use an installed `dotnet`.

## Delivered

- Solution and project boundaries for application, core, audio, speech, text,
  Windows integration, infrastructure, developer tools, and tests.
- Central package versions, package lock files, nullable analysis, deterministic
  builds, warnings-as-errors, and recommended .NET analyzers.
- One-command clean restore/build/test flow.
- Checksum-verified model download script.
- Fixed 11-second JFK WAV fixture and reviewed golden transcript.
- WASAPI microphone enumeration and live capture probe.
- Whisper benchmark runner with transcript scoring.
- Initial unit and architecture tests.
- Twelve passing unit, architecture, fixture-integration, and composition tests.

## Verification commands

```powershell
.\scripts\Install-DotNet.ps1
.\scripts\Test.ps1 -Clean
.\scripts\Invoke-AudioProbe.ps1
.\scripts\Download-Models.ps1
.\scripts\Invoke-Milestone0Benchmark.ps1
```

Downloaded models and generated benchmark JSON are ignored by Git.

## WASAPI proof

The probe found two active capture devices:

- Default: `Microphone (NexiGo N930AF FHD webcam Audio)`
- `Microphone Array (2- Realtek(R) Audio)`

A one-second capture from the default device received 360,960 bytes in 15
buffers using its native 48 kHz, stereo, 32-bit floating-point format. This
proves device discovery and microphone capture; resampling to Whisper's input
format belongs to Milestone 3.

## Whisper benchmark

Fixture:

- File: `tests/Fixtures/audio/jfk.wav`
- Duration: 11.000 seconds
- Size: 352,078 bytes
- SHA-256: `59DFB9A4ACB36FE2A2AFFC14BACBEE2920FF435CB13CC314A08C13F66BA7860E`
- Reference transcript: 22 normalized words
- Threads: 10

| Model | Model size | Load | Inference | Real-time factor | Peak working set | Word error rate |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `base.en` | 141.1 MiB | 410 ms | 954 ms | 0.087 | 375.4 MiB | 0.0% |
| `small.en` | 465.0 MiB | 379 ms | 2,938 ms | 0.267 | 819.6 MiB | 0.0% |

Both models were substantially faster than real time. `base.en` transcribed the
11-second fixture in under one second and is the selected V1 default.
`small.en` remains an optional accuracy model for a broader corpus where it may
outperform `base.en`.

## Model integrity

| Model | Bytes | SHA-256 |
| --- | ---: | --- |
| `ggml-base.en.bin` | 147,964,211 | `A03779C86DF3323075F5E796CB2CE5029F00EC8869EEE3FDFB897AFE36C6D002` |
| `ggml-small.en.bin` | 487,614,201 | `C6138D6D58ECC8322097E0F987C32F1BE8BB0A18532A3F88F734D1BBF9C41E5D` |

## Exit-gate assessment

- Clean restore/build/test from one command: passed.
- At least one model faster than real time: passed; both models passed.
- Model/runtime decision recorded with evidence: passed; use the
  Whisper.net CPU runtime and `base.en` as the default.

## Limits of this spike

This benchmark uses one short, clean English recording. It proves feasibility,
not production accuracy. Milestone 3 will add the larger regression corpus,
noise/silence cases, resampling, device removal handling, and model-quality
thresholds described in the implementation plan.
