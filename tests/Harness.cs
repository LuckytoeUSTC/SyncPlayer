using SyncPlayer;
using System.Collections.Concurrent;
class Harness {
 [STAThread] static void Main(string[] args) {
  if(args.Contains("--language-test")) { LanguageTest(); return; }
  if(args.Length == 2 && args[0] == "--network-only") { Test(args[1]=="configured" ? "" : args[1]).GetAwaiter().GetResult(); Console.WriteLine("PASS network integration including RTT"); return; }
  Test().GetAwaiter().GetResult();
  ApplicationConfiguration.Initialize();
  using var form=new MainForm(runEngine:false);
  form.Show(); Application.DoEvents(); var remote = typeof(MainForm).GetField("remoteConnectionSwitch",System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(form) as RadioButton; remote!.Checked=true; Application.DoEvents();
  using var image=new Bitmap(form.Width,form.Height);form.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save("tests/ui.png");form.Close();
  Console.WriteLine("PASS client integration and UI render");
 }
 static void LanguageTest() {
  ApplicationConfiguration.Initialize();
  Localization.Select("en",false);
  using var form=new MainForm(runEngine:false);
  form.Show();
  var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
  var defaultLocal=(RadioButton)typeof(MainForm).GetField("localSyncSwitch",flags)!.GetValue(form)!;
  var defaultRemote=(RadioButton)typeof(MainForm).GetField("remoteConnectionSwitch",flags)!.GetValue(form)!;
  if(!defaultLocal.Checked || defaultRemote.Checked)throw new Exception("Mode selection must default to local");
  ((RadioButton)typeof(MainForm).GetField("remoteConnectionSwitch",flags)!.GetValue(form)!).Checked=true;
  var localMode=(RadioButton)typeof(MainForm).GetField("localSyncSwitch",flags)!.GetValue(form)!;
  var remoteMode=(RadioButton)typeof(MainForm).GetField("remoteConnectionSwitch",flags)!.GetValue(form)!;
  if(localMode.Checked)throw new Exception("Remote and local synchronization enabled together");
  localMode.Checked=true;if(remoteMode.Checked || !localMode.Checked)throw new Exception("Switch to local did not disable remote");
  remoteMode.Checked=true;if(localMode.Checked || !remoteMode.Checked)throw new Exception("Switch to remote did not disable local");
  if(typeof(MainForm).GetField("relayAddress",flags)!=null || typeof(MainForm).GetField("relaySettingsRow",flags)!=null)throw new Exception("Relay settings still exposed in UI");
  var change=typeof(MainForm).GetMethod("ChangeLanguage",flags)!;
  Control[] Descendants(Control root)=>root.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(Descendants(c))).ToArray();
  var controls=Descendants(form);
  var code=(TextBox)typeof(MainForm).GetField("manualAddress",flags)!.GetValue(form)!;
  code.Text="23456789ABCD";
  foreach(string language in new[]{"en","zh-Hans","zh-Hant","en"}) {
   change.Invoke(form,[language,false]);Application.DoEvents();
   if(localMode.Checked==remoteMode.Checked || localMode.Text!=Localization.T("本地同步") || remoteMode.Text!=Localization.T("远程同步"))throw new Exception("Mode selection or translation");
   var request=(Button)typeof(MainForm).GetField("requestMasterButton",flags)!.GetValue(form)!;
   if(request.Text != Localization.T("接管"))throw new Exception("Request button language");
   var takeoverTips=(ToolTip)typeof(MainForm).GetField("tooltips",flags)!.GetValue(form)!;
   if(takeoverTips.GetToolTip(request)!=Localization.T("申请主控，需对方接受"))throw new Exception("Takeover tooltip");
   string rttCaption=(string)typeof(MainForm).GetMethod("RttCaption",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.Invoke(null,[123.0])!;
   if(rttCaption!=(language=="en" ? "RTT 123 ms" : "往返 123 ms"))throw new Exception("RTT caption language");
   var tips=(ToolTip)typeof(MainForm).GetField("tooltips",flags)!.GetValue(form)!;
   foreach(var icon in controls.OfType<IconButton>().Where(i=>i.Symbol is "refresh" or "help" or "settings")) {
    string caption=Localization.T(icon.Symbol switch{"refresh"=>"刷新","help"=>"说明",_=>"语言"});
    if(tips.GetToolTip(icon)!=caption || icon.AccessibleName!=caption)throw new Exception("Icon tooltip mismatch: "+icon.Symbol);
   }
   var grid=(DataGridView)typeof(MainForm).GetField("grid",flags)!.GetValue(form)!;
   if(grid.Columns["included"]!.HeaderText != (language=="en" ? "Follow" : "同步"))throw new Exception("Window synchronization column language");
   if(code.Text!="23456789ABCD")throw new Exception("User input changed");
   if(language=="en")foreach(var c in controls.Where(c=>c is Label or Button))if(System.Text.RegularExpressions.Regex.IsMatch(c.Text,@"\p{IsCJKUnifiedIdeographs}"))throw new Exception("Mixed language: "+c.Text);
   foreach(var c in controls.OfType<TextBox>())if(language=="en" && System.Text.RegularExpressions.Regex.IsMatch(c.PlaceholderText,@"\p{IsCJKUnifiedIdeographs}"))throw new Exception("Untranslated placeholder");
   using var image=new Bitmap(form.Width,form.Height);form.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save("tests/ui-"+language+".png");
   foreach(var button in controls.OfType<Button>()) if(button is not IconButton && button.Height < button.Font.Height + (int)Math.Ceiling(8*button.DeviceDpi/96.0))throw new Exception("Insufficient text height: "+button.Text);
   if(typeof(MainForm).GetField("localAddress",flags)!.GetValue(form) is not TextBox { ReadOnly:true })throw new Exception("Connection code is not read-only text");
   Console.WriteLine("PASS "+language+": UI, placeholders, status, preserved code");
  }
  typeof(MainForm).GetMethod("ShowSettingsMenu",flags)!.Invoke(form,new object?[]{null});
  var menu=(ToolStripDropDownMenu)typeof(MainForm).GetField("settingsMenu",flags)!.GetValue(form)!;
  if(menu.Items.Count!=3)throw new Exception("Language menu contains non-language settings");menu.Close();
  var relay=(RelayService)typeof(MainForm).GetField("lan",flags)!.GetValue(form)!;
  typeof(RelayService).GetField("owner",flags)!.SetValue(relay,true);
  var applicants=(ListBox)typeof(MainForm).GetField("applicants",flags)!.GetValue(form)!;
  var update=typeof(MainForm).GetMethod("UpdateUi",flags)!;
  for(int count=1;count<=4;count++) {
   typeof(RelayService).GetMethod("Handle",flags)!.Invoke(relay,[new RelayPacket{Type="request",Nonce="request-"+count,Name="Applicant "+count}]);
   update.Invoke(form,null);
   if(applicants.Height!=Math.Min(count,3)*applicants.ItemHeight+(int)Math.Ceiling(4*form.DeviceDpi/96.0))throw new Exception("Request list height");
  }
  using(var pendingImage=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(pendingImage,new Rectangle(Point.Empty,form.Size));pendingImage.Save("tests/ui-applicants.png"); }
  var accept=(Button)typeof(MainForm).GetField("acceptButton",flags)!.GetValue(form)!;
  if(accept.Enabled)throw new Exception("Can accept without selecting applicant");
  applicants.SelectedIndex=1;
  var handle=typeof(RelayService).GetMethod("Handle",flags)!;
  handle.Invoke(relay,[new RelayPacket{Type="request-cancelled",Nonce="request-2"}]);update.Invoke(form,null);
  if(accept.Enabled || applicants.SelectedIndex!=-1)throw new Exception("Selection moved to another applicant after withdrawal");
  applicants.SelectedIndex=1;accept.PerformClick();
  if(relay.Requests.Length!=2 || relay.Requests.Any(r=>r.Nonce=="request-3"))throw new Exception("Accepted the wrong applicant");
  handle.Invoke(relay,[new RelayPacket{Type="paired",Id="test-peer",Name="Test",Session="test-session",CanControl=true,Windows=[]}]);
  var queue=(ConcurrentQueue<Action>)typeof(MainForm).GetField("actions",flags)!.GetValue(form)!;
  while(queue.TryDequeue(out var action))action();
  handle.Invoke(relay,[new RelayPacket{Type="hello",Name="Test",Windows=[new("primary","Primary",Primary:true,Ready:false)]}]);
  while(queue.TryDequeue(out var waitingAction))waitingAction();
  update.Invoke(form,null);
  var windowGrid=(DataGridView)typeof(MainForm).GetField("grid",flags)!.GetValue(form)!;
  if(windowGrid.Columns["included"]!.Visible)throw new Exception("Remote mode still has a window selection column");
  if(windowGrid.Rows.Cast<DataGridViewRow>().Single(r=>r.Cells[1].ToolTipText=="Primary").Cells[2].ReadOnly==false)throw new Exception("Empty remote player is editable");
  handle.Invoke(relay,[new RelayPacket{Type="hello",Name="Test",Windows=[new("primary","Primary",Primary:true)]}]);
  while(queue.TryDequeue(out var readyAction))readyAction();
  typeof(MainForm).GetMethod("UpdateUi",flags)!.Invoke(form,null);
  var connected=(TableLayoutPanel)typeof(MainForm).GetField("connectedDeviceRow",flags)!.GetValue(form)!;
  var pending=(TableLayoutPanel)typeof(MainForm).GetField("incomingBar",flags)!.GetValue(form)!;
  if(!connected.Visible || pending.Visible)throw new Exception("Connected peer line or hidden request list");
  if(windowGrid.Rows.Cast<DataGridViewRow>().Single(r=>r.Cells[1].ToolTipText=="Primary").Cells[2].ReadOnly)throw new Exception("Late-ready primary did not become available");
  handle.Invoke(relay,[new RelayPacket{Type="paired",Id="test-peer",Name="Test",Session="receiver-session",CanControl=false,Windows=[new("primary","Primary",Primary:true)]}]);update.Invoke(form,null);
  foreach(string buttonName in new[]{"playbackButton","syncButton","jumpButton","resetOffsetsButton"})if(((Button)typeof(MainForm).GetField(buttonName,flags)!.GetValue(form)!).Enabled)throw new Exception("Receiver control is enabled: "+buttonName);
  var roles=(Label)typeof(MainForm).GetField("connectedDevice",flags)!.GetValue(form)!;
  if(roles.Text!=Localization.F("本机受控 · 对方主控 · {0}","Test"))throw new Exception("Receiver role label");
  var connectionStatus=(Label)typeof(MainForm).GetField("connectionStatus",flags)!.GetValue(form)!;
  if(!connectionStatus.Text.Contains(Localization.T("请打开视频")))throw new Exception("Missing video preparation hint");
  using(var connectedImage=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(connectedImage,new Rectangle(Point.Empty,form.Size));connectedImage.Save("tests/ui-connected.png"); }
  handle.Invoke(relay,[new RelayPacket{Type="offline"}]);update.Invoke(form,null);
  if(relay.ConnectionStatus!="正在重连" || ((Label)typeof(MainForm).GetField("connectedRtt",flags)!.GetValue(form)!).Text!="" || !relay.IsReceiver)throw new Exception("Reconnect status, stale RTT or receiver permissions");
  Console.WriteLine("PASS automatic primary selection, selected applicant acceptance, three-row cap, connected peer line");
  // Audit every new relay message, including errors and dynamic request templates.
  string source=File.ReadAllText("src/Network/RelayService.cs");
  foreach(System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(source,"\"([^\"\\r\\n]*[\\p{IsCJKUnifiedIdeographs}][^\"\\r\\n]*)\"")) {
   string key=m.Groups[1].Value;
   if(Localization.T(key)==key)throw new Exception("Missing translation: "+key);
  }
  var remoteSwitch=(RadioButton)typeof(MainForm).GetField("remoteConnectionSwitch",flags)!.GetValue(form)!;
  localMode.Checked=true;
  localMode.Checked=true;
  typeof(MainForm).GetField("masterHandle",flags)!.SetValue(form,(nint)123);
  var hintMethod=typeof(MainForm).GetMethod("LocalSyncHint",flags)!;
  if(string.IsNullOrEmpty((string)hintMethod.Invoke(form,null)!))throw new Exception("Missing local empty-selection hint");
  typeof(MainForm).GetField("message",flags)!.SetValue(form,Localization.T("操作未完成，请检查窗口或连接"));update.Invoke(form,null);
  if(((Label)typeof(MainForm).GetField("status",flags)!.GetValue(form)!).Text!=Localization.T("操作未完成，请检查窗口或连接"))throw new Exception("Guidance hides error");
  typeof(MainForm).GetField("localFollowers",flags)!.SetValue(form,new nint[]{456});
  if(!string.IsNullOrEmpty((string)hintMethod.Invoke(form,null)!))throw new Exception("Hint persists after selection");
  typeof(MainForm).GetField("masterHandle",flags)!.SetValue(form,(nint)0);
  Console.WriteLine("PASS local synchronization hint and all icon tooltips");
  form.Close();Console.WriteLine("PASS all relay messages translated"); RttProtocolTest(); RelayConfigurationTest(); ReliableQueueTest();
 }
 static void RttProtocolTest() {
  using var service=new RelayService("rtt-test");
  var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
  var handle=typeof(RelayService).GetMethod("Handle",flags)!;
  void Receive(RelayPacket p)=>handle.Invoke(service,[p]);
  Receive(new(){Type="paired",Id="peer",Session="session",CanControl=true});
  string nonce=new string('a',32);
  void Pending(int milliseconds) {
   typeof(RelayService).GetField("pingNonce",flags)!.SetValue(service,nonce);
   typeof(RelayService).GetField("pingAt",flags)!.SetValue(service,System.Diagnostics.Stopwatch.GetTimestamp()-(long)(milliseconds*System.Diagnostics.Stopwatch.Frequency/1000.0));
  }
  Pending(100);
  Receive(new(){Type="pong",Session="old",Nonce=nonce});
  Receive(new(){Type="pong",Session="session",Nonce=new string('b',32)});
  if(service.Devices[0].Rtt.HasValue)throw new Exception("Accepted stale or unrelated RTT reply");
  foreach(int ms in new[]{100,110,120,130,2000}) { Pending(ms);Receive(new(){Type="pong",Session="session",Nonce=nonce}); }
  if(service.Devices[0].Rtt is <119 or >150)throw new Exception("RTT median did not resist outlier");
  double? measured=service.Devices[0].Rtt;
  Receive(new(){Type="pong",Session="session",Nonce=nonce});
  if(service.Devices[0].Rtt!=measured)throw new Exception("Accepted duplicate RTT reply");
  Pending(7000);Receive(new(){Type="pong",Session="session",Nonce=nonce});
  if(service.Devices[0].Rtt.HasValue)throw new Exception("Timed-out RTT remains visible");
  Receive(new(){Type="paired",Id="peer",Session="new-session",CanControl=false});
  if(service.Devices[0].Rtt.HasValue)throw new Exception("RTT carried across sessions");
  Console.WriteLine("PASS RTT median, stale/duplicate replies, timeout and session reset");
 }
 static void RelayConfigurationTest() {
  string path=Path.Combine("tests","relay-configuration-test.json");
  try {
   foreach(string invalid in new[]{"{", "{}", "{\"server\":\"http://relay.example\"}", "{\"server\":\"https://relay.example/?token=test\"}"}) {
    File.WriteAllText(path,invalid);bool rejected=false;
    try { RelayConfiguration.Load(path); }catch(IOException){rejected=true;}
    if(!rejected)throw new Exception("Invalid relay configuration accepted");
   }
   File.WriteAllText(path,"{\"server\":\"https://relay.example/\"}");
   if(RelayConfiguration.Load(path)!="https://relay.example")throw new Exception("Valid relay configuration rejected");
   bool emptyDefaultRejected=false;try{RelayConfiguration.Load();}catch(IOException){emptyDefaultRejected=true;}
   if(!emptyDefaultRejected)throw new Exception("Release must not contain a default relay address");
   Console.WriteLine("PASS external relay JSON configuration and invalid-input errors");
  } finally { File.Delete(path); }
 }
 static void ReliableQueueTest() {
  using var service=new RelayService("queue-test");
  var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
  var reliable=System.Threading.Channels.Channel.CreateUnbounded<RelayPacket>();
  var controls=System.Threading.Channels.Channel.CreateBounded<RelayPacket>(new System.Threading.Channels.BoundedChannelOptions(32){FullMode=System.Threading.Channels.BoundedChannelFullMode.DropOldest});
  typeof(RelayService).GetField("queue",flags)!.SetValue(service,reliable);
  typeof(RelayService).GetField("controlQueue",flags)!.SetValue(service,controls);
  typeof(RelayService).GetField("owner",flags)!.SetValue(service,true);
  var handle=typeof(RelayService).GetMethod("Handle",flags)!;
  handle.Invoke(service,[new RelayPacket{Type="request",Nonce="join",Name="Guest"}]);service.Respond("join",true);
  handle.Invoke(service,[new RelayPacket{Type="paired",Id="peer",CanControl=true,Session="session",Windows=[new("win","Video",Primary:true)]}]);
  for(int n=1;n<=100;n++)service.SendControl("peer",new(){cur=n},new("win"));
  handle.Invoke(service,[new RelayPacket{Type="master-request",Nonce="transfer",Session="session",Name="Guest"}]);service.Respond("transfer",true);
  var critical=new List<RelayPacket>();while(reliable.Reader.TryRead(out var packet))critical.Add(packet);
  if(!critical.Any(p=>p.Type=="accept" && p.Nonce=="join") || !critical.Any(p=>p.Type=="master-accept" && p.Nonce=="transfer"))throw new Exception("Control burst dropped critical approval");
  var buffered=new List<RelayPacket>();while(controls.Reader.TryRead(out var packet))buffered.Add(packet);
  if(buffered.Count!=32 || buffered[^1].Event?.cur!=100)throw new Exception("Control queue not bounded or lost latest state");
  Console.WriteLine("PASS bounded control burst preserves join and transfer approval messages");
 }
 static async Task Until(Func<bool> fn) { for(int n=0;n<400;n++){if(fn())return;await Task.Delay(40);}throw new Exception("timeout"); }
 static async Task Test(string server="http://127.0.0.1:8787"){
  using var master=new RelayService("master");using var slave=new RelayService("slave");
  master.Failed+=text=>Console.WriteLine("Master: "+text);slave.Failed+=text=>Console.WriteLine("Guest: "+text);
  master.Server=slave.Server=server;
  master.Publish([new("masterwin","master video",Primary:true)]);slave.Publish([new("slavewin","slave video",Primary:true)]);
  var received=new ConcurrentQueue<WireEvent?>();var reverse=new ConcurrentQueue<WireEvent?>();
  slave.Controlled+=(ev,targets,id)=>{if(targets.Offset!=500)throw new Exception("offset");received.Enqueue(ev);};
  master.Controlled+=(ev,targets,id)=>reverse.Enqueue(ev);
  master.Create();await Until(()=>master.Addresses.Length==1);slave.Connect(master.Addresses[0]);
  await Until(()=>master.Requests.Length==1);master.Respond(master.Requests[0].Nonce,true);
  await Until(()=>master.Devices.Length==1 && slave.Devices.Length==1);
  if(!master.Devices[0].CanSend || slave.Devices[0].CanSend || !slave.Devices[0].CanReceive)throw new Exception("direction");
  await Until(()=>master.Devices[0].Rtt.HasValue && slave.Devices[0].Rtt.HasValue);
  if(master.Devices[0].Rtt is <=0 or >=6000 || slave.Devices[0].Rtt is <=0 or >=6000)throw new Exception("Invalid RTT");
  Console.WriteLine($"PASS bidirectional peer RTT: {master.Devices[0].Rtt:0} / {slave.Devices[0].Rtt:0} ms");
  for(int n=1;n<=15;n++)master.SendControl(slave.Id,new(){cur=n*1000,state=2,speed=1500},new("slavewin",500,true));
  await Until(()=>received.Count==15);if(received.Last()?.cur!=15000)throw new Exception("seek");
  slave.SendControl(master.Id,new(){state=1},new("masterwin",0));await Task.Delay(100);if(reverse.Count!=0)throw new Exception("reverse");
  slave.RequestMaster();await Until(()=>master.Requests.Any(r=>r.Transfer));master.Respond(master.Requests[0].Nonce,false);await Task.Delay(100);
  if(!master.HasControl || slave.HasControl)throw new Exception("rejection changed rights");
  slave.RequestMaster();await Until(()=>master.Requests.Any(r=>r.Transfer));master.Respond(master.Requests[0].Nonce,true);
  await Until(()=>slave.HasControl && !master.HasControl);
  await Until(()=>master.Devices[0].Rtt.HasValue && slave.Devices[0].Rtt.HasValue);
  if(master.Devices[0].CanSend || !slave.Devices[0].CanSend)throw new Exception("swap rights");
  slave.SendControl(master.Id,new(){cur=22000,state=1},new("masterwin",0));await Until(()=>reverse.Count==1);
  int before=received.Count;master.SendControl(slave.Id,new(){state=1},new("slavewin",500));await Task.Delay(100);if(received.Count!=before)throw new Exception("old master still controls");
  master.RequestMaster();await Until(()=>slave.Requests.Any(r=>r.Transfer));slave.Respond(slave.Requests[0].Nonce,true);await Until(()=>master.HasControl && !slave.HasControl);
  slave.Disconnect();await Until(()=>master.Devices.Length==0);
 }
}





