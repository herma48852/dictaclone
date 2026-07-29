# DictaClone

DictaClone is a planned local-first voice-to-text application for Windows 11 x64.
Its core workflow is simple: hold a global shortcut, speak, release it, and insert
the transcription wherever the text cursor is active.

The project will provide local Whisper transcription, a Windows tray interface,
configurable shortcuts, clipboard and character-by-character insertion modes,
custom vocabulary, and optional Smart Edit features. Audio and transcripts will
remain local unless the user explicitly configures a cloud provider.

## Project status

Planning is complete. Application implementation has not started.

See the [implementation plan](docs/IMPLEMENTATION_PLAN.md) for the proposed
architecture, milestones, unit and regression tests, privacy constraints, and
release criteria.

## Initial target

- Windows 11 x64
- C# and .NET 10
- WPF desktop application
- Local Whisper speech recognition
- Self-contained, non-administrator installation

## License

A project license has not yet been selected.
