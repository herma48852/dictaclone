# DictaClone third-party notices

DictaClone 0.1.0 includes the following third-party components. The project
does not modify or replace the terms of their respective licenses.

## Runtime components

| Component | Version | License | Project |
| --- | --- | --- | --- |
| Microsoft .NET and Windows Desktop Runtime | 10.0 | MIT and component-specific notices | https://github.com/dotnet/runtime |
| Microsoft.Extensions.AI.Abstractions | 10.2.0 | MIT | https://dot.net/ |
| NAudio, including Core, Asio, Midi, Wasapi, WinForms, and WinMM packages | 2.3.0 | MIT | https://github.com/naudio/NAudio |
| Whisper.net and Whisper.net runtime packages | 1.9.1 | MIT | https://github.com/sandrohanea/whisper.net |
| whisper.cpp / ggml native runtime, distributed through Whisper.net | Whisper.net 1.9.1 dependency | MIT | https://github.com/ggml-org/whisper.cpp |

The self-contained .NET publish also carries the notices supplied by the .NET
runtime in `ThirdPartyNotices.txt` when that file is present in the Microsoft
runtime distribution.

## Build and installer tooling

The Windows installer is built with Inno Setup 6.7.3. Inno Setup is build
tooling and is not redistributed as a standalone product, although its
generated setup and uninstaller code form part of the installer. Its license
and source are available from https://jrsoftware.org/isinfo.php.

## MIT License text

Copyright notices for the MIT-licensed components include:

- Copyright © .NET Foundation and Contributors.
- Copyright © Mark Heath and NAudio contributors.
- Copyright © Sandro Hanea and Whisper.net contributors.
- Copyright © Georgi Gerganov and whisper.cpp contributors.
- Copyright © 2022 OpenAI for Whisper code and model weights.

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

Package metadata and source repositories are the authority if this summary and
an upstream notice differ.
