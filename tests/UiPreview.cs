namespace SyncPlayer;

static class UiPreview
{
    public static void Attach(MainForm form, string[] args)
    {
            if (args.Contains("--ui-check")) {
                var peers = new List<LanService>();
                if (args.Contains("--ui-peers")) foreach (string name in new[] { "PC-Living", "PC-Study" }) {
                    var peer = new LanService(name, false); peer.Publish([new("preview", "Remote video.mp4")]); peer.Start(); peers.Add(peer);
                }
                form.FormClosed += (_, _) => { foreach (var peer in peers) peer.Dispose(); };
                var timer = new System.Windows.Forms.Timer { Interval = 1200 }; int step = 0, connectionWait = 0;
                timer.Tick += (_, _) => {
                    foreach (var peer in peers) foreach (var request in peer.Requests) peer.Respond(request.Nonce, true);
                    if (step == 1 && peers.Count > 0 && form.TestNetwork.Devices.Count(p => p.Connected && p.Rtt.HasValue) < peers.Count && connectionWait++ < 6) return;
                    if (step < 2) {
                        form.SaveUi(Path.Combine(Environment.CurrentDirectory, $"ui-{step}.png"), step);
                        if (step == 0 && peers.Count > 0) {
                            form.TestRemoteConnection(true);
                            foreach (var peer in peers) form.TestNetwork.Connect($"127.0.0.1:{peer.Port}", ControlDirection.Send);
                        }
                        step++;
                    }
                    else if (step == 2) { step++; form.SaveSettingsUi(Path.Combine(Environment.CurrentDirectory, "ui-settings.png")); }
                    else { timer.Stop(); timer.Dispose(); form.Close(); }
                };
                form.Shown += (_, _) => timer.Start();
            }
    }
}
