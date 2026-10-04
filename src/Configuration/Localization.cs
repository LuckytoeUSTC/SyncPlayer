using System.Text.Json;

namespace SyncPlayer;

static class Localization
{
    public static string Language { get; private set; } = DeviceIdentity.LoadSetting("language") is "zh-Hans" or "zh-Hant" ? DeviceIdentity.LoadSetting("language")! : "en";
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
        if (root is Label or Button or CheckBox or Form) root.Text = T(root.Text);
        if (root is TextBox box) box.PlaceholderText = T(box.PlaceholderText);
        if (root is DataGridView grid) foreach (DataGridViewColumn column in grid.Columns) column.HeaderText = T(column.HeaderText);
        foreach (Control child in root.Controls) Apply(child);
    }
    const string Data = """
本地同步|Local sync|本機同步
远程连接|Remote connections|遠端連線
请先开启远程连接|Turn on Remote connections first|請先開啟遠端連線
就绪|Ready|就緒
粘贴对方地址|Paste peer address|貼上對方位址
主窗口|Main window|主視窗
刷新|Refresh|重新整理
设备|Devices|裝置
? 说明|? Help|? 說明
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
对齐|Align|對齊
跳转|Seek|跳轉
秒|s|秒
本机名称|Device name|本機名稱
本机地址|Local address|本機位址
复制地址|Copy address|複製位址
当前没有局域网地址|No LAN address available|目前沒有區域網路位址
对方地址|Peer address|對方位址
我控制对方|Control peer|我控制對方
对方控制我|Peer controls me|對方控制我
双向|Both ways|雙向
连接|Connect|連線
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
请选择设备或粘贴对方地址|Select a device or paste its address|請選擇裝置或貼上對方位址
等待对方确认|Waiting for approval|等待對方確認
地址无法识别，请复制对方显示的地址|Invalid address. Copy the address shown on the peer.|無法辨識位址，請複製對方顯示的位址
本机|This device|本機
已连接|Connected|已連線
可控制|Controllable|可控制
接收控制|Receiving control|接收控制
未连接|Not connected|未連線
未打开视频|No video open|未開啟影片
 请求：| requests: | 請求：
控制本机|control this device|控制本機
由本机控制|be controlled by this device|由本機控制
双向控制|control both ways|雙向控制
未找到说明书|Manual not found|找不到說明書
操作未完成，请检查窗口或连接|Operation failed. Check the window or connection.|操作未完成，請檢查視窗或連線
请选择主窗口|Select a main window|請選擇主視窗
已跳转|Seek complete|已跳轉
已对齐|Aligned|已對齊
已接收 · {0}|Received · {0}|已接收 · {0}
打开视频后选择主窗口|Open a video and select the main window|開啟影片後選擇主視窗
窗口未响应，请刷新或重新选择|Window not responding. Refresh or select it again.|視窗沒有回應，請重新整理或重新選擇
设备名称须为 1–32 个字符|Device name must contain 1–32 characters|裝置名稱須為 1–32 個字元
搜索中|Searching|搜尋中
搜索不可用|Discovery unavailable|搜尋無法使用
搜索已关闭|Discovery disabled|搜尋已關閉
无局域网地址|No LAN address|無區域網路位址
这是本机地址，请选择另一台设备|This is your address. Select another device.|這是本機位址，請選擇另一台裝置
设备数量过多|Too many devices|裝置數量過多
已断开|Disconnected|已中斷
对方已拒绝|Peer declined|對方已拒絕
对方未接受连接|Peer did not accept the connection|對方未接受連線
连接未确认，请检查对方是否已打开 SyncPlayer|Connection not approved. Check that the peer has opened SyncPlayer.|連線未確認，請檢查對方是否已開啟 SyncPlayer
连接中断|Connection lost|連線中斷
网络发送失败：|Network send failed: |網路傳送失敗：
播放器未能加载视频|PlayerWindow could not load the video|播放器無法載入影片
测试无法激活播放器|Test could not activate the player|測試無法啟用播放器
播放器未能暂停|PlayerWindow could not pause|播放器無法暫停
跳转未响应|Seek did not respond|跳轉沒有回應
播放器未能精确定位|PlayerWindow could not seek accurately|播放器無法精確定位
主窗口尚未加载视频|Main window has no video loaded|主視窗尚未載入影片
播放器未能暂停，对齐已停止|PlayerWindow could not pause. Alignment stopped.|播放器無法暫停，對齊已停止
跳转未稳定，窗口保持暂停，请重试对齐|Seek did not settle. Windows remain paused; retry Align.|跳轉未穩定，視窗保持暫停，請重試對齊
需要两个播放器|Two player windows are required|需要兩個播放器
对齐后状态不一致|Playback states differ after alignment|對齊後狀態不一致
连续对齐或重复状态命令改变了播放状态|Repeated alignment or state commands changed playback state|連續對齊或重複狀態指令改變了播放狀態
SyncPlayer 错误|SyncPlayer error|SyncPlayer 錯誤
窗口 {0} 未响应或已关闭|Window {0} is closed or not responding|視窗 {0} 沒有回應或已關閉
窗口 {0} 命令未确认|Window {0} did not confirm the command|視窗 {0} 指令未確認
窗口 {0} 跳转未确认|Window {0} did not confirm the seek|視窗 {0} 跳轉未確認
无法控制窗口 {0}，请检查权限或刷新列表|Cannot control window {0}. Check permissions or refresh.|無法控制視窗 {0}，請檢查權限或重新整理
诊断日志：{0}|Diagnostic log: {0}|診斷記錄：{0}
""";
}
