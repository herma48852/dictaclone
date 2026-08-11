#!/bin/zsh
set -euo pipefail

script_dir=${0:A:h}
repo_root=${script_dir:h:h}

dotnet restore "$repo_root/tests/DictaClone.Core.Tests/DictaClone.Core.Tests.csproj" --locked-mode --disable-parallel -m:1
dotnet restore "$repo_root/tests/DictaClone.Text.Tests/DictaClone.Text.Tests.csproj" --locked-mode --disable-parallel -m:1
dotnet restore "$repo_root/tests/DictaClone.Infrastructure.Tests/DictaClone.Infrastructure.Tests.csproj" --locked-mode --disable-parallel -m:1
dotnet restore "$repo_root/tests/DictaClone.Speech.Tests/DictaClone.Speech.Tests.csproj" --locked-mode --disable-parallel -m:1
dotnet restore "$repo_root/tests/DictaClone.Mac.Tests/DictaClone.Mac.Tests.csproj" --locked-mode --disable-parallel -m:1

dotnet test "$repo_root/tests/DictaClone.Core.Tests/DictaClone.Core.Tests.csproj" --no-restore -m:1
dotnet test "$repo_root/tests/DictaClone.Text.Tests/DictaClone.Text.Tests.csproj" --no-restore -m:1
dotnet test "$repo_root/tests/DictaClone.Infrastructure.Tests/DictaClone.Infrastructure.Tests.csproj" --no-restore -m:1
dotnet test "$repo_root/tests/DictaClone.Speech.Tests/DictaClone.Speech.Tests.csproj" --no-restore -m:1
dotnet test "$repo_root/tests/DictaClone.Mac.Tests/DictaClone.Mac.Tests.csproj" --no-restore -m:1
