# SyncPlayer

English · [简体中文](README.zh-Hans.md) · [繁體中文](README.zh-Hant.md)

Synchronize PotPlayer windows on one Windows computer or across your local network. Follow absolute seeks, playback state and speed; set an offset and mute for each window. Device discovery, connection approval and latency are built in.

**[Download for Windows x64](https://github.com/LuckytoeUSTC/SyncPlayer/releases/latest)** — extract the ZIP and run `SyncPlayer.exe`. No separate .NET installation is needed. Install PotPlayer separately and prepare your videos on each computer.

## 0. Quick start

English is the default on first launch. Click the top-right gear icon, then select **English / 简体中文 / 繁體中文** to change it immediately. Selection applies immediately and is saved automatically; there are no confirmation buttons. The question-mark icon opens the usage guide in the selected language.

### 0.0 On one computer

1. Open your videos in separate PotPlayer windows.
2. Double-click `SyncPlayer.exe`.
3. Select the **Main window** at the top.
4. **Local sync** below the main-window selector is on by default. Check **Follow** for the other local windows you want to synchronize.
5. Play, pause, seek or change speed in the main window. Use **Sync** to recalibrate when needed.

Check **Mute** in a window's row to silence only that window. Use **Offset (s)** if the videos have different starting points.

### 0.1 On two computers

1. Connect both computers to the same local network and open their videos in PotPlayer.
2. Double-click `SyncPlayer.exe` on each computer.
3. Select each computer's **Main window** at the top.
4. On both computers, turn on the **Remote connections** switch below the main-window selector. This automatically turns **Local sync** off. If you also want local followers, turn **Local sync** back on manually.
5. On the controlling computer, select the other computer, choose **Control peer**, then **Connect**.
6. On the other computer, approve the request with **Accept**.
7. On the controlling computer, check **Follow** for the desired windows under the other device's name, then operate the controlling computer's main window.

**If discovery finds nothing:** on the other computer, choose **Copy address**. Paste that address into **Peer address** on the controlling computer, then use the same **Connect** button. If several local addresses appear, they are not necessarily all reachable from the other computer. Try the first one initially; if it fails, choose another address on the shared network and copy it again. You do not need to look up an IP address or configure a port.

## 1. The window table

Local windows appear first, followed by remote windows grouped by device. The main window has no Follow checkbox: it is always the source. Window names show the video filename without the player suffix. A number is added only when filenames on the same device are identical.

| Item | How to use it |
| --- | --- |
| Main window | Select the local source at the top. Selecting local followers is optional. |
| Follow | Check the windows that should follow the source. Uncheck to stop following. With Local sync off, other local windows are hidden and receive no commands. Selections return when switched on again; remote selections remain available. Remote windows require an accepted connection that permits control. |
| Offset (s) | Enter a positive or negative number of seconds, including decimals such as `0.25` or `-1.5`. The main window also supports offsets. Press Enter or click elsewhere to apply; the left and right arrows adjust by 0.1 seconds. A blank value becomes zero. |
| Mute | Check to silence that window; uncheck to restore its sound. This works independently of Follow. Muting the main window does not mute the others. |
| Status | Shows the window role or connection state. Remote device group rows also show network round-trip time. |

Long filenames wrap; hover to read the full name. The list refreshes automatically after opening, closing or changing videos. The square circular-arrow icon also refreshes it manually. Newly discovered local windows start unchecked.

Offset means **this window's position = main window's position + this window's offset − main window's offset**. If the same scene appears at 10 seconds in the main video and 12 seconds in another, enter `2` for the other window. An offset of `-2` places it two seconds before the main window. Changing an enabled follower's offset synchronizes that window. For unchecked windows, the offset is retained for the current session. A follower before its start waits on the first frame; beyond its end it pauses near the last frame rather than replaying or moving to the next video. It resumes when its offset enters the valid range again, including after a backward seek or offset edit. The main window can keep playing while a follower waits at the start or end. Leaving an offset blank commits zero.

Changing the main window's offset moves that window by the change in offset; other windows retain their positions when the target is within the video's range. Positions are limited to the video boundaries. The left and right arrows in the seek input also adjust by 0.1 seconds.
Remote offsets and mute can be changed by the side authorized to control the window. Mute changes made in a remote player are reflected after a network update. Follow selections and offsets are not saved across restarts.

## 2. Playback and seeking

| Control | Purpose |
| --- | --- |
| Triangle / two vertical bars | One square button changes with the main window: a triangle starts playback; two bars pause it. Controls the main window, selected local followers and authorized remote followers. Repeated Pause does not toggle playback. |
| Sync | Synchronize with the main window's current absolute position, speed and playback state, preserving each follower's offset. |
| Reset offsets | To the right of Sync. Sets every main, local and remote window offset to zero, then synchronizes selected followers. Unchecked windows have their stored offsets cleared without moving their playback positions. |
| Local sync | The switch below the main window enables local followers. Turning it off hides other local windows and stops all commands to them, preserving selections for next time. Remote synchronization is independent. |
| Seek | Enter seconds measured from the start of the video in the separate seek area, such as `90.5`, then click Seek. |

Use Left/Right, Ctrl+Left/Right, the timeline, or X/C in the main PotPlayer window as usual; selected followers follow the resulting position and speed. Operating a follower does not change the main window.

Local videos may briefly pause while a seek completes, then resume together. Decoding and computer load can affect how closely the pictures match. If you notice a difference, click Sync.

The first synchronization may briefly change the picture while the players are prepared. Exit SyncPlayer normally so it can restore player options it temporarily changed.

## 3. Connecting devices

**Remote connections** is the switch below the main-window selector, beside Local sync. Turning it on opens the connection area and automatically turns Local sync off. Turning it off disconnects peers, stops remote control, hides remote rows and turns Local sync on. These automatic changes happen only when the Remote connections switch changes; you can turn Local sync on manually while Remote connections remains on. Both computers must enable Remote connections before connecting. By default only Local sync is on.

- **Device name:** edit it and press Enter or leave the field to save. The default looks like `PC-7K3M2Q`, derived from the computer's identity and normally different on each computer. Custom names are saved for the current user. Duplicate names receive a four-character suffix. Use that suffix to distinguish devices with the same name.
- **Local address:** SyncPlayer obtains IP addresses and assigns its port automatically. Copy address copies the full selected address. Multiple network adapters can produce multiple addresses; they are not necessarily all reachable from the other computer. Try the first initially, then another address on the shared network if needed. Addresses with a gateway are listed first. The list updates automatically when the network changes and is checked every two seconds. The IP can change after switching networks or router reassignment; the port can change after restarting SyncPlayer. Copy the currently displayed full address.
- **Devices:** automatically finds SyncPlayer on the local network. Each list entry shows its own round-trip latency once connected. Select a device, then Connect. Multiple devices can stay connected simultaneously; selecting another does not disconnect previous peers. Disconnect affects only the selected peer.
- **Peer address:** paste the other program's full address here if discovery fails.
- **Connect, Disconnect:** request a connection or end the selected connection. Discovery does not automatically grant control.

| Direction | After approval |
| --- | --- |
| Control peer | Select the peer's windows; operate your main window to control them. |
| Peer controls me | The peer selects your windows; its main window controls them. |
| Both ways | Either side can select and control the other's windows. Prefer operating one side at a time to avoid conflicting actions. |

The receiving side sees the device name and requested direction and chooses Accept or Reject. Acceptance authorizes control in that direction. Commands are ignored before approval, after rejection and after disconnection. Changing direction requires a new connection request and approval.

For remote-only synchronization, turn Local sync off. You can select multiple remote windows and multiple computers.

Network changes automatically refresh addresses and discovery. Restarting the program assigns a new port and does not restore control authorization automatically. Copy the current address and reconnect when needed. Interrupted connections show their status. Previously selected remote windows may remain selected after reconnecting; review your selection.

The device's **ms** value shows how quickly the network responds; it is not the difference between the video pictures. Remote playback may differ by more than 0.1 seconds. Network conditions, video decoding and computer load affect the result, so SyncPlayer cannot always keep remote pictures within 0.1 seconds. If they drift apart, click Sync; a wired connection and lower computer load may help.

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

## 5. Installing and updating

Download the latest Windows x64 ZIP, extract it, and keep the EXE, the three usage guides, LICENSE and NOTICE.md together. PotPlayer is installed separately; place the required videos on each computer. SyncPlayer does not send videos to other computers.

To update, close SyncPlayer, extract the new ZIP into a new folder, and launch its `SyncPlayer.exe`. Your language and device name are retained. Review the main window, followers and offsets after reopening; follower selections, offsets and remote connections are not retained across restarts.

## 6. Development and acknowledgements

OpenAI Codex participated in implementation, refactoring, interface changes, debugging, test and packaging scripts, and writing these usage guides. The human developer (LuckytoeUSTC) supplied requirements, made product and interaction decisions, reviewed visible behavior, and provided feedback and release authorization.

[PotSync](https://github.com/Byaidu/PotSync) informed the player-control interface and event format; its message identifiers and event field names are reused in a C# reimplementation. [BiuBiuClick](https://github.com/huhai463127310/BiuBiuClick) informed multi-window interaction and Windows control techniques; its classes and image-matching code are not included. PotSync also credits [PotPlayerControl](https://github.com/ld3l/PotPlayerControl) for interface information. See [source acknowledgements](NOTICE.md) for the specific files and reuse scope.

## 7. License

SyncPlayer source code and usage guides are available under the [MIT License](LICENSE). PotPlayer is a separate product and is not included in the package.
