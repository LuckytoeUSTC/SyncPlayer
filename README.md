# SyncPlayer — Usage guide

Control PotPlayer playback on one other Windows computer. Only one computer controls the other at a time; you can request to swap control. Each computer opens its own video. Videos, screens, keyboard and mouse input are not transferred.

## Local sync

Open multiple PotPlayer video windows on one computer, choose a **Main window**, and check **Follow** for the others. **Local sync** is on by default and needs no network. Playback, pause and seeking follow the main window; offsets and mute can be set separately. Choose Local sync or Remote sync in the mode selector; exactly one mode is selected, with Local sync as the default.

## Remote connection

1. Run `SyncPlayer.exe` on both computers, open a video in each PotPlayer, and choose the video window to use as the synchronization reference in **Main window**.
2. Select **Remote sync**, below Main window. Local and remote synchronization are mutually exclusive. Turning on Local sync disconnects the remote connection. Before connecting, fill in server in relay.json beside the program. See Relay configuration below.
3. The controller clicks **Host**, then **Copy code**. The code is valid for an initial join for 10 minutes; a connection lasts up to 8 hours.
4. The receiving computer pastes the code into **Host code** and clicks **Join**. The controller selects one person in **Requests**, then clicks **Accept**. The list grows to 3 rows and scrolls beyond that. Once connected, a single line shows the peer, both roles and RTT; other pending requests end.
5. Remote sync controls only the selected main window on each computer. Synchronization starts automatically once both videos are ready, including videos opened after connecting. Missing videos show a prompt. Playback controls are disabled on the receiving computer. The controller uses play/pause, **Sync**, **Seek**, offsets and mute. Brief network interruptions show **Reconnecting**, preserve the controller and restart RTT measurement on recovery.
6. Click **Disconnect**, or select **Local sync**, to end this computer's connection.

## Swap control

The receiving computer clicks **Takeover**. The current controller clicks **Accept** or **Reject**. Requests expire after 30 seconds. Accepting swaps the roles and sends the new controller's current playback state. You can request to swap back the same way.

## Latency

Local sync uses no network, but player response, seek decoding and audio buffering can still cause brief differences between windows.

Remote sync also depends on the network and relay service. You may notice differences in audio or video playback between computers. Offsets can adjust relative playback positions, but cannot remove changing network delays.

Connection info shows round-trip time (RTT) from this computer through the relay to the other computer and back, sampled every 2 seconds as the median of the last 5 successful measurements. A dash means no measurement or no reply within 6 seconds; disconnecting clears it. Both computers need this version. RTT includes transmission and reply processing, excludes video decoding, audio buffering and display time, and does not directly measure actual audiovisual differences or one-way command latency.

## Relay configuration

Before the first remote connection, open `relay.json` beside `SyncPlayer.exe` in a text editor and replace the `server` value with its HTTPS base URL. Keep the JSON quotes and braces. Both computers must use the same relay. Keep this file alongside the executable; changes apply the next time you create or join a connection.

## References and AI assistance

Developed with OpenAI Codex assistance. The PotPlayer interface and interaction design draw on PotSync, BiuBiuClick and PotPlayerControl; see `NOTICE.md` for the precise use of these references. Project source is licensed under MIT; see `LICENSE`. PotPlayer is required separately.



