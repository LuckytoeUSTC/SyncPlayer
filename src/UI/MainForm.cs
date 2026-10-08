using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace SyncPlayer;

sealed partial class MainForm : Form
{
    readonly ComboBox master = new AlignedComboBox() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly DataGridView grid = new() {
        Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
        RowHeadersVisible = false, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
        SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = false,
        EnableHeadersVisualStyles = false, EditMode = DataGridViewEditMode.EditOnEnter
    };
    readonly RadioButton localSyncSwitch = ModeChoice("本地同步", true);
    readonly RadioButton remoteConnectionSwitch = ModeChoice("远程同步", false);
    readonly IconButton playbackButton = new();
    readonly ToolTip tooltips = new();
    volatile int playbackState, requestedPlaybackState = -1;
    volatile int masterDuration;
    long playbackVersion;
    readonly TimeInput seek = new() { Maximum = 864000, Width = 180 };
    readonly Label status = Label(Localization.T("就绪")), details = Label("");
    readonly TextBox localAddress = new() { ReadOnly = true, Width = 320, BackColor = Color.White };
    readonly TextBox deviceName = new() { Width = 220 };
    readonly ListBox applicants = new() { Dock = DockStyle.Top, Height = 32, IntegralHeight = false };
    readonly Label connectedDevice = Label(""), connectedRtt = Label("");
    TableLayoutPanel? connectedDeviceRow;
    Button? acceptButton, rejectButton;
    readonly TextBox manualAddress = new() { Dock = DockStyle.Fill, PlaceholderText = Localization.T("粘贴主控的12位连接码") };
    Button? requestMasterButton;
    Button? syncButton, resetOffsetsButton, jumpButton;
    readonly Label connectionStatus = Label("");
    readonly TableLayoutPanel incomingBar = new() { AutoSize = true, Dock = DockStyle.Top, Visible = false, ColumnCount = 1 };
    readonly Panel connectionPanel = new() { Dock = DockStyle.Top, AutoSize = true, Visible = false };
    readonly System.Windows.Forms.Timer ui = new() { Interval = 200 };
    readonly object gate = new();
    readonly ConcurrentQueue<Action> actions = new();
    readonly CancellationTokenSource cancel = new();
    readonly PlaybackChangeDetector detector = new();
    readonly NavigationMonitor input;
    readonly RelayService lan;
    readonly ConcurrentDictionary<nint, WindowOptions> localOptions = new();
    readonly ConcurrentDictionary<string, WindowOptions> remoteOptions = new();
    readonly ConcurrentDictionary<nint, bool> actualMute = new();
    readonly Queue<string> trace = new();
    List<PlayerWindow> localPlayers = [];
    nint masterHandle;
    nint[] localFollowers = [];
    Dictionary<nint, int> localOffsets = new();
    int mainOffset, appliedMainOffset;
    readonly ConcurrentDictionary<nint, int> localBoundary = new();
    RemoteClock? remoteClock;
    string remotePrimarySignature = "";
    sealed class RemoteClock
    {
        public double Position, At; public int Offset, Duration, Speed = 1000, State = 1, Boundary; public string Peer = ""; public nint Handle;
        public double Now(double at) => Position + (State == 2 ? (at - At) * Speed : 0);
    }
    bool needsPrepare = true, refreshing, stopped;
    bool syncing => syncMode == SyncMode.Local;
    enum SyncMode { Local, Remote }
    SyncMode syncMode = SyncMode.Local;
    bool changingMode;
    string message = Localization.T("就绪"), readings = "", deviceFingerprint = "", windowFingerprint = "", requestFingerprint = "";
    DateTime lastWindowRefresh = DateTime.MinValue;
    double lastMutePoll;
    long jumpVersion;
    int connectionExtraHeight;
    Font? groupFont;
    public int AutomaticSeeks { get; private set; }
    sealed class WindowOptions { public bool Included; public int Offset; public bool? Muted; }
    sealed record WindowRow(nint Handle, string Peer, string Window, bool Primary = false);
    sealed record GroupRow(string Peer);
    sealed record RequestChoice(ConnectionRequest Request) { public override string ToString() => Request.Transfer ? Localization.F("{0} 申请主控（30秒内有效）", Request.Name) : Request.Name; }

    public MainForm(bool runEngine = true)
    {
        Text = "SyncPlayer"; ClientSize = new Size(900, 720); MinimumSize = new Size(780, 570);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoValidate = AutoValidate.EnableAllowFocusChange;
        Font = new Font("Microsoft YaHei UI", 9);
        BackColor = Color.FromArgb(246, 247, 249);
        playbackButton.Emphasized = true; playbackButton.BackColor = Color.FromArgb(70, 78, 90);
        playbackButton.FlatAppearance.BorderColor = playbackButton.BackColor;
        input = new NavigationMonitor(() => masterHandle);
        lan = new RelayService(DeviceIdentity.LoadName());
        lan.SetEnabled(false);
        lan.Controlled += ReceiveRemote;
        lan.Paired += transferred => actions.Enqueue(() => {
            remoteClock = null; remotePrimarySignature = ""; SyncRemotePrimary();
        });
        lan.WindowsChanged += () => actions.Enqueue(() => {
            SyncRemotePrimary();
        });
        lan.Failed += text => message = text;
        BuildInterface();
        RefreshPlayers();
        ui.Tick += (_, _) => UpdateUi(); ui.Start();
        FormClosed += (_, _) => { ui.Stop(); settingsMenu?.Dispose(); StopEngine(); };
        FormClosing += (_, e) => { grid.CancelEdit(); e.Cancel = false; cancel.Cancel(); };
        Shown += (_, _) => {
            var area = Screen.FromControl(this).WorkingArea;
            MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width - 30), Math.Min(MinimumSize.Height, area.Height - 40));
            Size = new Size(Math.Min(Width, area.Width - 24), Math.Min(Height, area.Height - 40));
            FitHeaders();
            AlignControlRows();
        };
        DpiChanged += (_, _) => BeginInvoke((Action)(() => { AlignControlRows(); FitHeaders(); WrapTitles(); }));
        if (runEngine) _ = Task.Run(Loop);
    }
}
