# SyncPlayer

English · [简体中文](README.zh-Hans.md) · [繁體中文](README.zh-Hant.md)

Synchronize PotPlayer windows on one Windows computer or across your local network. Follow absolute seeks, playback state and speed; set an offset and mute for each window. Device discovery, connection approval and latency are built in.

**[Download for Windows x64](https://github.com/LuckytoeUSTC/SyncPlayer/releases/latest)** — extract the ZIP and run `SyncPlayer.exe`. No separate .NET installation is needed. Install PotPlayer separately and prepare your videos on each computer.

## 0. Quick start

English is the default on first launch. Click the top-right gear icon, then select **English / 简体中文 / 繁體中文** to change it immediately. Selection applies immediately and is saved automatically; there are no confirmation buttons. The question-mark icon opens the manual in the selected language.

### 0.0 On one computer

1. Open your videos in separate PotPlayer windows.
2. Run `SyncPlayer.exe` and select the **Main window**.
3. Turn **Local sync** on and check **Follow** for the other local windows you want to synchronize.
4. Play, pause, seek or change speed in the main window. The square playback button switches between a triangle (play) and two bars (pause). Use **Align** to recalibrate when needed.

Check **Mute** in a window's row to silence only that window. Use **Offset (s)** if the videos have different starting points.

### 0.1 On two computers

1. Open videos and SyncPlayer on both computers, connected to the same local network.
2. On both computers, turn **Remote connections** on. On the controlling computer, select the other computer, choose **Control peer**, then **Connect**.
3. On the other computer, approve the request with **Accept**.
4. On the controlling computer, check **Follow** for the desired windows under the other device's name.
5. Operate the controlling computer's main window. For remote synchronization only, turn **Local sync** off.

**If discovery finds nothing:** on the other computer, turn **Remote connections** on and choose **Copy address**. Paste that address into **Peer address** on the controlling computer, then use the same **Connect** button. You do not need to look up an IP address or configure a port.

## 1. The window table

Local windows appear first, followed by remote windows grouped by device. The main window has no Follow checkbox: it is always the source. Window names show the video filename without the player suffix. A number is added only when filenames on the same device are identical.

| Item | How to use it |
| --- | --- |
| Main window | Select the local source at the top. Selecting local followers is optional. |
| Follow | Check the windows that should follow the source. Uncheck to stop following. With Local sync off, other local windows are hidden and receive no commands. Selections return when switched on again; remote selections remain available. Remote windows require an accepted connection that permits control. |
| Offset (s) | Enter a positive or negative number of seconds, including decimals such as `0.25` or `-1.5`. The main window is the reference and has no editable offset. |
| Mute | Check to silence that window; uncheck to restore its sound. This works independently of Follow. Muting the main window does not mute the others. |
| Status | Shows the window role or connection state. Remote device group rows also show network round-trip time. |

Long filenames wrap; hover to read the full name. The list refreshes automatically after opening, closing or changing videos. The square circular-arrow icon also refreshes it manually. Newly discovered local windows start unchecked.

Offset means **this window's position = main window's position + offset**. If the same scene appears at 10 seconds in the main video and 12 seconds in another, enter `2` for the other window. An offset of `-2` places it two seconds before the main window. Changing an enabled follower's offset applies alignment. For unchecked windows, the offset is retained for the current session. A follower before its start waits on the first frame; beyond its end it pauses near the last seekable frame, with a roughly 300 ms decoding guard to prevent automatic replay or playlist advancement. It resumes when its offset enters the valid range again, including after a backward seek or offset edit. A paused follower while the source keeps playing is intentional at these boundaries. Leaving an offset blank commits zero.

Remote offsets and mute can be changed by the side authorized to control the window. Mute changes made in a remote player are reflected after a network update. Follow selections and offsets are not saved across restarts.

## 2. Playback and seeking

| Control | Purpose |
| --- | --- |
| Triangle / two vertical bars | One square button changes with the main window: a triangle starts playback; two bars pause it. Controls the main window, selected local followers and authorized remote followers. Repeated Pause does not toggle playback. |
| Align | Align to the main window's current absolute position, speed and playback state, preserving each follower's offset. |
| Local sync | The switch below the main window enables local followers. Turning it off hides other local windows and stops all commands to them, preserving selections for next time. Remote synchronization is independent. |
| Seek | Enter seconds measured from the start of the video in the separate seek area, such as `90.5`, then click Seek. |

Left/Right, Ctrl+Left/Right, timeline clicks and X/C speed changes are handled using the player's actual resulting position and speed. SyncPlayer does not assume that every seek shortcut moves the same number of seconds. Operations in a follower do not change the main window.

Local seeking waits for positioning before resuming playback. Brief speed adjustments during that operation compensate for decoder startup differences. Normal playback does not use periodic corrective seeks. Speed changes are propagated directly; a brief follower-only rate adjustment can remove the resulting timing difference without seeking. The local target is approximately 100 ms, depending on decoding and computer load.

When first preparing synchronization, SyncPlayer checks accurate seeking and temporarily disables keyframe-only seeking if necessary. It restores options it changed on normal exit. Preparation may briefly change the picture before restoring and aligning it. Forcefully terminating the process may prevent restoration.

## 3. Connecting devices

**Remote connections** is beside Local sync. Turning it on opens the connection area; turning it off disconnects peers, stops remote control and hides remote rows. Both computers must turn it on before connecting. By default only Local sync is on.

- **Device name:** edit it and press Enter or leave the field to save. The default looks like `PC-7K3M2Q`, derived from the computer's identity and normally different on each computer. Custom names are saved for the current user. Duplicate names receive a four-character suffix. Connections use internal identities, so duplicate names do not select the wrong device.
- **Local address:** SyncPlayer obtains IP addresses and assigns its port automatically. Copy address copies the full selected address. Multiple network adapters produce multiple addresses; connections with a gateway are listed first.
- **Devices:** automatically finds SyncPlayer on the local network. Each list entry shows its own round-trip latency once connected. Select a device, then Connect. Multiple devices can stay connected simultaneously; selecting another does not disconnect previous peers. Disconnect affects only the selected peer.
- **Peer address:** paste the other program's full address here if discovery fails.
- **Connect, Disconnect:** request a connection or end the selected connection. Discovery does not automatically grant control.

| Direction | After approval |
| --- | --- |
| Control peer | Select the peer's windows; your main window sends operations. |
| Peer controls me | The peer selects your windows; its main window sends operations. |
| Both ways | Either side can select and control the other's windows. Prefer operating one side at a time to avoid conflicting actions. |

The receiving side sees the device name and requested direction and chooses Accept or Reject. Acceptance authorizes control in that direction. Commands are ignored before approval, after rejection and after disconnection. Changing direction requires a new connection request and approval.

For remote-only synchronization, turn Local sync off. You can select multiple remote windows and multiple computers.

Network changes automatically refresh addresses and discovery. Restarting the program assigns a new port and does not restore control authorization automatically. Copy the current address and reconnect when needed. Interrupted connections show their status. Previously selected remote windows may remain selected after reconnecting; review your selection.

The group row's **ms** value is network round-trip time. It indicates connection responsiveness, not the actual picture difference or an exact one-way delay. Remote synchronization uses operation events and absolute positions. It does not promise the local 100 ms target: network travel, decoding and the two computers' performance affect visual alignment.

## 4. Troubleshooting

| Problem | What to do |
| --- | --- |
| No local windows | Open a video in PotPlayer and click Refresh. Enable multiple player instances for multiple windows. Run the app and player on the same desktop with the same permission level. |
| Cannot check a remote window | Connect and obtain approval. For local followers, turn Local sync on. For remote followers, turn Remote connections on and check that the direction permits your control. Double-click a remote row to open that device's connection area. |
| Computer not found | Use Copy address and manual connection. Guest Wi-Fi or client isolation may block communication. |
| No local address | Check Ethernet or Wi-Fi. The address refreshes when networking returns; no manual port configuration is needed. |
| Approval request fails | The peer must open SyncPlayer and accept. Copy its current address again and check the local network. |
| Firewall prompt | On a trusted local network, allow SyncPlayer on private networks. Do not disable the entire firewall. Manual connections also require messages to pass through the network. |
| Several IP addresses | Choose the address on the same local network as the peer. Try the first address initially, then another if necessary. |
| Pictures still differ | Check the content and timelines. Use an offset for different edit starting points. Different-length videos cannot show matching content at every position. |
| Window closed or not responding | The list refreshes. Reopen the video and review the main window and followers. A failed window does not make the interface wait indefinitely. |
| Offset cannot be submitted | Enter a number between `-86400` and `86400` seconds. Decimals are supported; remove nonnumeric characters. |

SyncPlayer does not transmit video files. Prepare the videos on each computer first. Discovery over the public internet is outside the current feature's scope.

## 5. Building and validation

The release ZIP contains a self-contained Windows x64 EXE and the three manuals. Keep the manuals beside the EXE. On first launch the interface is English, Local sync is on, and Remote connections is off. Existing language and device-name preferences are retained in the current Windows user's settings.

Building from source requires the .NET 10 SDK. Run `powershell -ExecutionPolicy Bypass -File scripts/build.ps1`. `Start.cmd` launches the local build or the packaged EXE. `scripts/package.ps1` produces the self-contained ZIP and its SHA-256 checksum in `artifacts`. Microsoft runtime files are downloaded from the official NuGet feed when needed.

| Folder | Responsibility |
| --- | --- |
| `src/UI` | Window layout, table, settings and synchronization orchestration. |
| `src/Playback` | PotPlayer interop, seek detection, rate adjustment and offset boundaries. |
| `src/Network` | LAN discovery, approval, multiple connections, latency and protocol. |
| `src/Configuration` | Language, device identity and saved preferences. |
| `tests` | Diagnostics and regression checks, excluded from the release EXE. |
| `scripts` | Build, test and package entry points. |

`scripts/test.ps1` runs protocol, discovery and language checks. Add `-Video` to operate two real PotPlayer windows, using local videos in `test-video`; add `-Screenshots` for UI previews. Reports go to `diagnostics`. Tests attempt to restore the players' original position, state, speed and mute afterward. These scripts require the SDK and a diagnostics build; release EXEs do not contain test commands.

Real local videos have tested playback/pause, absolute and backward seeks, repeated alignment, speed changes without periodic seeks, mute, blank offsets, start/end holds and automatic re-entry. Position differences use player readings, not frame-by-frame image comparison. Physical key activation was restricted in the development environment, so those checks used PotPlayer's key interface and absolute seeking.

Three services on one computer tested simultaneous connections, per-device latency, independent control and disconnect, approval/rejection, manual connection and port conflicts. Discovery used a real local network adapter. Adapter priority, no-network data and address changes used simulated adapter data. Two physical computers, real adapter removal, router isolation and firewall prompts remain untested.

## 6. AI contribution

OpenAI Codex participated in implementation, refactoring, interface changes, debugging, test and packaging scripts, and writing these manuals. A human supplied requirements, reviewed visible behavior and provided feedback and release authorization. The validation above distinguishes actual tests from simulated or untested scenarios.
