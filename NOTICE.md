# References and acknowledgements

SyncPlayer is a C# implementation developed with OpenAI Codex assistance. Its own source code is distributed under the MIT License in LICENSE. The following references informed development; their authors do not endorse this project.

## PotSync — Byaidu

Source: https://github.com/Byaidu/PotSync

The PotPlayer control approach in `potsync.py` was used as a reference when implementing `src/Playback/PotPlayer.cs`: enumerate player windows, query position/duration/state through Windows messages, and send playback/seek commands. The message numbers 1024, 273, 20482, 20484, 20485, 20486 and 20002 are reused as interface identifiers. These are not newly invented SyncPlayer commands.

`src/Network/WireEvent.cs` retains PotSync's `progress`/`event` terminology and `type`, `cur`, `total`, `state` fields, with additional fields. SyncPlayer's discovery, approval and multi-device packet envelope is a separate implementation; protocol similarities do not guarantee interoperability with PotSync.

This is reference-based reimplementation of interface calls and an event model, not inclusion of the Python program or a line-for-line translation of its functions. No PotSync source file is included in the build or release. The reference repository and downloaded copy contained no license file when reviewed on 2026-10-05; this project does not claim to relicense that repository under MIT.

## BiuBiuClick — huhai463127310 / 栩风

Source: https://github.com/huhai463127310/BiuBiuClick

Its multi-window control workflow and Windows window-enumeration/input-control approach informed the design. The local source files `WindowHelper.cs`, `KeyController.cs` and `MouseHook.cs` were inspected. SyncPlayer uses its own window listing and navigation observer; those classes, its image-matching implementation, UI assets and assemblies are not included in SyncPlayer. No substantial verbatim implementation from these files was identified in the current C# source review.

BiuBiuClick's reference copy is licensed under MIT, copyright (c) 2021 栩风. The original license text is available at https://github.com/huhai463127310/BiuBiuClick/blob/main/LICENSE.

## PotPlayerControl — ld3l

Source: https://github.com/ld3l/PotPlayerControl

PotSync credits this project for the PotPlayer control interface. SyncPlayer acknowledges this upstream source of interface information; it does not include the Java library or its source files.

## Runtime and player

The self-contained Windows executable includes Microsoft .NET and Windows Forms runtime components. These retain their upstream terms; SyncPlayer's MIT license does not replace third-party license notices. Runtime source and license information: https://github.com/dotnet/runtime and https://github.com/dotnet/winforms.

PotPlayer is installed separately and is not included in the release.

## Optional reference videos

The with-videos package contains two user-provided reference MP4 files for trying synchronization. These media files are not part of the SyncPlayer source or covered by its MIT license. The standard package contains no reference media.
