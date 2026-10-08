using System.Text.Json;

namespace SyncPlayer;

static class Localization
{
    public static string Language { get; private set; } = DeviceIdentity.LoadSetting("language") is "zh-Hans" or "zh-Hant" ? DeviceIdentity.LoadSetting("language")! : "en";
    public static string WindowSyncCaption => Language == "en" ? "Follow" : "同步";
    public static string Manual => Language switch { "zh-Hans" => "README.zh-Hans.md", "zh-Hant" => "README.zh-Hant.md", _ => "README.md" };
    static readonly Dictionary<string, string[]> Words = new();
    static Localization()
    {
        foreach (var line in Data.Split('\n', StringSplitOptions.RemoveEmptyEntries)) {
            var parts = line.TrimEnd('\r').Split('|');
            Words.Add(parts[0], [parts[1], parts[2]]);
        }
    }
    public static string T(string text)
    {
        if (!Words.TryGetValue(text, out var value)) {
            var original = Words.FirstOrDefault(p => p.Value.Contains(text));
            if (original.Key == null) return text;
            text = original.Key; value = original.Value;
        }
        return Language switch { "zh-Hans" => text, "zh-Hant" => value[1], _ => value[0] };
    }
    public static void Select(string language, bool save = true) { if (language is not ("en" or "zh-Hans" or "zh-Hant")) throw new ArgumentException("Unknown language"); if (save) DeviceIdentity.SaveSetting("language", language); Language = language; }
    public static string F(string key, params object[] args) => string.Format(T(key), args);
    public static void Apply(Control root)
    {
        // Translate only application-owned controls; media and device names are user data.
        if (root is Label or Button or CheckBox or RadioButton or Form) root.Text = T(root.Text);
        if (root is TextBox box) box.PlaceholderText = T(box.PlaceholderText);
        if (root is DataGridView grid) foreach (DataGridViewColumn column in grid.Columns) column.HeaderText = T(column.HeaderText);
        foreach (Control child in root.Controls) Apply(child);
    }
    const string Data = """
发起|Host|發起
加入|Join|加入
接管|Takeover|接管
主控|Controller|主控
受控|Controlled|受控
中转服务|Relay service|中轉服務
往返|RTT|往返
远程同步|Remote sync|遠端同步
申请列表|Requests|申請清單
请打开视频|Please open a video|請開啟影片
请对方打开视频|Ask the other computer to open a video|請對方開啟影片
正在重连|Reconnecting|正在重新連線
连接码无效或连接已过期，请重新发起|Invalid or expired connection; create a new connection|連線碼無效或連線已過期，請重新發起
本机主控 · 对方受控 · {0}|This PC: controller · Peer: controlled · {0}|本機主控 · 對方受控 · {0}
本机受控 · 对方主控 · {0}|This PC: controlled · Peer: controller · {0}|本機受控 · 對方主控 · {0}
申请主控，需对方接受|Request control; the other computer must accept|申請主控，需對方接受
创建连接失败，请检查网络后重试|Could not create a connection; check the network and retry|無法建立連線，請檢查網路後重試
请检查程序旁 relay.json 中的 server，须为有效的 HTTPS 中转网址|Check server in relay.json beside the program; use a valid HTTPS relay URL|請檢查程式旁 relay.json 中的 server，須為有效的 HTTPS 中轉網址
连接码|Connection code|連線碼
复制连接码|Copy code|複製連線碼
主控连接码|Host code|主控連線碼
粘贴主控的12位连接码|Paste the host's 12-character code|貼上主控的12位連線碼
请粘贴主控的连接码|Paste the host's connection code|請貼上主控的連線碼
连接码无效，请复制主控显示的12位连接码|Invalid code. Copy the host's 12-character code.|連線碼無效，請複製主控顯示的12位連線碼
尚未创建连接码|Create a connection to get a code|尚未建立連線碼
创建连接或输入连接码|Create a connection or enter a code|建立連線或輸入連線碼
中转地址须为 https:// 地址|The relay address must start with https://|中轉位址須以 https:// 開頭
请输入完整的12位连接码|Enter the complete 12-character connection code|請輸入完整的12位連線碼
正在创建连接|Creating connection|正在建立連線
正在加入|Joining connection|正在加入
创建连接失败|Could not create a connection|無法建立連線
等待另一台电脑加入（连接码10分钟内有效）|Waiting for the other computer; code valid for 10 minutes|等待另一台電腦加入（連線碼10分鐘內有效）
等待主控接受|Waiting for the host to accept|等待主控接受
连接已关闭|Connection closed|連線已關閉
中转消息无效|Invalid relay message|中轉訊息無效
中转连接失败：{0}|Relay connection failed: {0}|中轉連線失敗：{0}
等待对方批准主控申请|Waiting for control approval|等待對方批准主控申請
已断开，可重新创建或加入|Disconnected. Create or join a connection.|已中斷，可重新建立或加入
等待对方批准主控申请（30秒内有效）|Waiting for control approval; request valid for 30 seconds|等待對方批准主控申請（30秒內有效）
主控申请被拒绝或已超时|Control request declined or expired|主控申請被拒絕或已逾時
主控已拒绝，请重新加入|Host declined. Try joining again.|主控已拒絕，請重新加入
{0} 申请主控（30秒内有效）|{0} requests control (valid for 30 seconds)|{0} 申請主控（30秒內有效）
本地同步|Local sync|本機同步
远程连接|Remote connections|遠端連線
请先开启远程连接|Turn on Remote connections first|請先開啟遠端連線
就绪|Ready|就緒
粘贴对方地址|Paste peer address|貼上對方位址
主窗口|Main window|主視窗
刷新|Refresh|重新整理
设备|Devices|裝置
说明|Help|說明
跳转时间（秒）|Seek time in seconds|跳轉時間（秒）
减少时间|Decrease time|減少時間
增加时间|Increase time|增加時間
请再打开一个视频窗口，并勾选「同步」|Open another video window and select Follow|請再開啟一個影片視窗，並勾選「同步」
请在列表中勾选需要同步的窗口|Select the windows to follow in the list|請在清單中勾選需要同步的視窗
设置|Settings|設定
语言|Language|語言
保存|Save|儲存
取消|Cancel|取消
播放控制|Playback|播放控制
▶ 播放|▶ Play|▶ 播放
播放|Playing|播放
开始播放|Play|開始播放
暂停|Pause|暫停
停止|Stopped|停止
同步|Sync|同步
取消偏移|Reset offsets|取消偏移
已取消偏移|Offsets reset|已取消偏移
跳转|Seek|跳轉
秒|s|秒
本机名称|Device name|本機名稱
断开|Disconnect|中斷連線
接受|Accept|接受
拒绝|Reject|拒絕
跟随|Follow|跟隨
片头等待|Waiting at start|片頭等待
片尾停住|Holding at end|片尾停住
未启用|Disabled|未啟用
{0} 台已连接|{0} connected|{0} 台已連線
窗口|Window|視窗
偏移(秒)|Offset (s)|偏移(秒)
静音|Mute|靜音
状态|Status|狀態
请输入 -86400 到 86400 的秒数|Enter seconds between -86400 and 86400|請輸入 -86400 到 86400 的秒數
请检查输入的秒数|Check the time in seconds|請檢查輸入的秒數
名称已保存|Name saved|名稱已儲存
等待对方确认|Waiting for approval|等待對方確認
本机|This device|本機
已连接|Connected|已連線
可控制|Controllable|可控制
接收控制|Receiving control|接收控制
未连接|Not connected|未連線
未打开视频|No video open|未開啟影片
未找到使用说明|Usage guide not found|找不到使用說明
操作未完成，请检查窗口或连接|Operation failed. Check the window or connection.|操作未完成，請檢查視窗或連線
请选择主窗口|Select a main window|請選擇主視窗
已跳转|Seek complete|已跳轉
已同步|Synced|已同步
已接收 · {0}|Received · {0}|已接收 · {0}
打开视频后选择主窗口|Open a video and select the main window|開啟影片後選擇主視窗
窗口未响应，请刷新或重新选择|Window not responding. Refresh or select it again.|視窗沒有回應，請重新整理或重新選擇
设备名称须为 1–32 个字符|Device name must contain 1–32 characters|裝置名稱須為 1–32 個字元
播放器未能加载视频|Player could not load the video|播放器無法載入影片
测试无法激活播放器|Test could not activate the player|測試無法啟用播放器
播放器未能暂停|Player could not pause|播放器無法暫停
跳转未响应|Seek did not respond|跳轉沒有回應
播放器未能精确定位|Player could not seek accurately|播放器無法精確定位
主窗口尚未加载视频|Main window has no video loaded|主視窗尚未載入影片
播放器未能暂停，同步已停止|Player could not pause. Synchronization stopped.|播放器無法暫停，同步已停止
跳转未稳定，窗口保持暂停，请重试同步|Seek did not settle. Windows remain paused; retry Sync.|跳轉未穩定，視窗保持暫停，請重試同步
需要两个播放器|Two player windows are required|需要兩個播放器
同步后状态不一致|Playback states differ after alignment|同步後狀態不一致
连续同步或重复状态命令改变了播放状态|Repeated alignment or state commands changed playback state|連續同步或重複狀態指令改變了播放狀態
SyncPlayer 错误|SyncPlayer error|SyncPlayer 錯誤
窗口 {0} 未响应或已关闭|Window {0} is closed or not responding|視窗 {0} 沒有回應或已關閉
窗口 {0} 命令未确认|Window {0} did not confirm the command|視窗 {0} 指令未確認
窗口 {0} 跳转未确认|Window {0} did not confirm the seek|視窗 {0} 跳轉未確認
无法控制窗口 {0}，请检查权限或刷新列表|Cannot control window {0}. Check permissions or refresh.|無法控制視窗 {0}，請檢查權限或重新整理
诊断日志：{0}|Diagnostic log: {0}|診斷記錄：{0}
""";
}






