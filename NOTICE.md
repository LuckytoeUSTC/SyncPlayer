# SyncPlayer references and acknowledgements

SyncPlayer source code is distributed under the MIT License; see `LICENSE` for the copyright notice and terms. Development was assisted by OpenAI Codex. The projects below informed the interface implementation or interaction design. Their inclusion here does not imply endorsement or participation by their authors.

## PotSync — Byaidu

Source: https://github.com/Byaidu/PotSync

The approach in `potsync.py` informed the C# implementation of PotPlayer window enumeration, status queries, playback, pause and seeking through Windows messages. Message numbers are used as player interface identifiers. Synchronization events retain the `progress` and `event` terminology and the `type`, `cur`, `total` and `state` field names.

SyncPlayer implements its own relay connections, join approval, control transfer and round-trip measurements; interoperability with PotSync is not guaranteed. No PotSync Python source files are included in the release. The local reference copy previously reviewed contained no license file. SyncPlayer's MIT License does not apply to that reference project.

## BiuBiuClick — huhai463127310 / 栩风

Source: https://github.com/huhai463127310/BiuBiuClick

Its multi-window workflow and the window enumeration and input observation approaches in `WindowHelper.cs`, `KeyController.cs` and `MouseHook.cs` informed the design. SyncPlayer uses its own implementation. Those classes, image-matching code, interface assets and assemblies are not included in the release.

The local reference copy is licensed under MIT, Copyright (c) 2021 栩风. Original license: https://github.com/huhai463127310/BiuBiuClick/blob/main/LICENSE

## PotPlayerControl — ld3l

Source: https://github.com/ld3l/PotPlayerControl

PotSync credits this project as a source of PotPlayer control interface information. SyncPlayer retains this acknowledgement of the upstream interface reference. Its Java library and source files are not included in the release.

## Runtime and interface assets

The self-contained Windows executable includes Microsoft .NET and Windows Forms runtime components. Their licenses and third-party terms are preserved in the release's `licenses` folder. SyncPlayer's MIT License does not replace those terms.

Runtime sources: https://github.com/dotnet/runtime and https://github.com/dotnet/winforms

The language button icon was derived from a user-supplied reference image using an image generation tool. The application retains the original three-square Windows Forms icon.

PotPlayer must be installed separately. The release contains no player, test videos or other reference media.
