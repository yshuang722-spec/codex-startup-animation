using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;

class Boot : Window {
 const double CardDelaySeconds=.50, ProgressCompleteAt=6.30+CardDelaySeconds, ReadyHoldSeconds=2.50, SceneSeconds=6.95+ReadyHoldSeconds+CardDelaySeconds;
 [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);
 [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 delegate bool EnumWindowsProc(IntPtr h,IntPtr l);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb,IntPtr l);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint processId);
 [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h,uint flags);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr h,ref WINDOWPLACEMENT p);
 [DllImport("user32.dll")] static extern bool SetWindowPlacement(IntPtr h,ref WINDOWPLACEMENT p);
 [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h,uint flags);
 [DllImport("user32.dll",CharSet=CharSet.Auto)] static extern bool GetMonitorInfo(IntPtr h,ref MONITORINFO i);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
 [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
 [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h,int n);
 [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h,int a,out RECT r,int s);
 struct RECT { public int L,T,R,B; }
 struct POINT { public int X,Y; }
 struct WINDOWPLACEMENT { public int Length,Flags,ShowCmd; public POINT MinPosition,MaxPosition; public RECT NormalPosition; }
 struct MONITORINFO { public int Size; public RECT Monitor,Work; public uint Flags; }
 double transitionStart=-1,nextReadyProbe=0; bool readinessPending=false,handoffReady=false;
 string appId; bool appDiscoveryComplete=false,appFailureShown=false; RECT savedWindowBounds; bool hasSavedWindowBounds=false; WINDOWPLACEMENT savedPlacement; bool hasSavedPlacement=false; bool preparing=false,launchQueued=false,clientHidden=false,clientShown=false,savedMax=false; int restoreCmd=4; Stopwatch clock=new Stopwatch(); IntPtr client; string root,userData; double phase=-1,nextClientProbe=0; bool sized=false; bool arranging=false; double dpi=1; RECT lastOverlayBounds; uint lastClientDpi; string matchSummary="";
  Geometry logo; Typeface mono=new Typeface("Consolas"), sans=new Typeface("Segoe UI");
 static string log; static void Log(string s){File.AppendAllText(log,DateTime.Now.ToString("O")+" "+s+Environment.NewLine);}
 static string ReadConfiguredAppId(string dataDir){try{string path=Path.Combine(dataDir,"client-app.txt");if(File.Exists(path)){string value=File.ReadAllText(path).Trim();if(value.Contains("!"))return value;}}catch{}return null;}
 static void SaveConfiguredAppId(string dataDir,string value){try{if(!String.IsNullOrWhiteSpace(value))File.WriteAllText(Path.Combine(dataDir,"client-app.txt"),value,Encoding.ASCII);}catch(Exception e){Log("APP cache write error="+e.GetType().Name);}}
 static string GetUserDataDirectory(string fallback){try{string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);if(!String.IsNullOrWhiteSpace(local)){string path=Path.Combine(local,"Codex System Boot");Directory.CreateDirectory(path);return path;}}catch{}return fallback;}
 static Geometry LoadLogo(string root){string external=Path.Combine(root,"logo-mark.path");if(File.Exists(external))return Geometry.Parse(File.ReadAllText(external));using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("logo-mark.path")){if(stream==null)throw new FileNotFoundException("Embedded logo geometry was not found.");using(StreamReader reader=new StreamReader(stream))return Geometry.Parse(reader.ReadToEnd());}}
 static void SetComProperty(object target,string name,object value){target.GetType().InvokeMember(name,BindingFlags.SetProperty,null,target,new object[]{value});}
 static void CreateOrUpdateDesktopShortcut(string root){
  object shell=null,shortcut=null;
  try{
   string executable=Path.Combine(root,"ChatGPT-Boot.exe"),icon=Path.Combine(root,"ChatGPT-Green-GPT.ico");
   string desktop=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
   if(!File.Exists(executable)||String.IsNullOrWhiteSpace(desktop))return;if(!File.Exists(icon))icon=executable;
   Type shellType=Type.GetTypeFromProgID("WScript.Shell");if(shellType==null)return;
   shell=Activator.CreateInstance(shellType);
   string shortcutPath=Path.Combine(desktop,"Codex 启动.lnk");
   shortcut=shellType.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{shortcutPath});
   SetComProperty(shortcut,"TargetPath",executable);SetComProperty(shortcut,"WorkingDirectory",root);SetComProperty(shortcut,"IconLocation",icon+",0");SetComProperty(shortcut,"Description","Codex Engine Boot");
   shortcut.GetType().InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);
   foreach(string oldName in new string[]{"ChatGPT System Boot.lnk","Codex System Boot.lnk"}){
    string oldPath=Path.Combine(desktop,oldName);object oldLink=null;
    try{
     if(!File.Exists(oldPath))continue;
     oldLink=shellType.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{oldPath});
     string oldTarget=Convert.ToString(oldLink.GetType().InvokeMember("TargetPath",BindingFlags.GetProperty,null,oldLink,null));
     if(String.Equals(Path.GetFullPath(oldTarget),Path.GetFullPath(executable),StringComparison.OrdinalIgnoreCase))File.Delete(oldPath);
    }catch(Exception e){Log("SHORTCUT migration error="+e.GetType().Name);}
    finally{if(oldLink!=null&&Marshal.IsComObject(oldLink))Marshal.ReleaseComObject(oldLink);}
   }
   Log("SHORTCUT ready path="+shortcutPath);
  }catch(Exception e){Log("SHORTCUT create error="+e.GetType().Name);}
  finally{try{if(shortcut!=null&&Marshal.IsComObject(shortcut))Marshal.ReleaseComObject(shortcut);}catch{}try{if(shell!=null&&Marshal.IsComObject(shell))Marshal.ReleaseComObject(shell);}catch{}}
 }
 bool ProbeHandoffReady(){
  try{
   string themeRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Documents\\Codex\\2026-10-12\\windows-codex-github-codex-css-css\\outputs\\GreenRoom");
   if(!File.Exists(Path.Combine(themeRoot,"state","theme-enabled")))return client!=IntPtr.Zero&&clock.Elapsed.TotalSeconds-phase>1;
   string node=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".cache\\codex-runtimes\\codex-primary-runtime\\dependencies\\node\\bin\\node.exe");
   ProcessStartInfo info=new ProcessStartInfo(node,"\""+Path.Combine(themeRoot,"handoff-ready.mjs")+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
   using(Process process=Process.Start(info)){if(process==null)return false;if(!process.WaitForExit(4000))return false;return process.ExitCode==0;}
  }catch{return false;}
 }
 static bool TryLaunchThemeClient(){
  try{
   string themeRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Documents\\Codex\\2026-10-12\\windows-codex-github-codex-css-css\\outputs\\GreenRoom");
   string enabled=Path.Combine(themeRoot,"state","theme-enabled"),launcher=Path.Combine(themeRoot,"GreenRoomLaunch.exe");
   if(!File.Exists(enabled)||!File.Exists(launcher))return false;
   Process.Start(new ProcessStartInfo(launcher){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=themeRoot});
   Log("APP activation through enabled theme launcher");return true;
  }catch(Exception e){Log("THEME launch fallback="+e.GetType().Name);return false;}
 }
 static string DiscoverInstalledAppId(){
  const string script="$apps=@(Get-StartApps | Where-Object { $_.Name -match '(?i)Codex|ChatGPT' }); $item=$apps | Where-Object { $_.Name -match '(?i)Codex' } | Select-Object -First 1; if($null -eq $item){$item=$apps | Select-Object -First 1}; if($item){$item.AppID}";
  try{
   string encoded=Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
   ProcessStartInfo info=new ProcessStartInfo("powershell.exe","-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand "+encoded);
   info.UseShellExecute=false;info.CreateNoWindow=true;info.WindowStyle=ProcessWindowStyle.Hidden;info.RedirectStandardOutput=true;
   using(Process process=Process.Start(info)){
    if(process==null||!process.WaitForExit(4500)){try{if(process!=null)process.Kill();}catch{}return null;}
    string output=process.StandardOutput.ReadToEnd().Trim();
    foreach(string line in output.Split(new char[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)){string value=line.Trim();if(value.Contains("!"))return value;}
   }
  }catch(Exception e){Log("APP discovery error="+e.GetType().Name);}
  return null;
 }
 [STAThread] static void Main(string[] args){
  bool created; using(var mutex=new System.Threading.Mutex(true,"Local\\ChatGPTArtBootV3",out created)){
   if(!created)return;
   string root=AppDomain.CurrentDomain.BaseDirectory; string userData=GetUserDataDirectory(root); log=Path.Combine(userData,"startup.log");
   try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch{}
   string configured=args.Length>0?args[0]:ReadConfiguredAppId(userData);
   try { new Application().Run(new Boot(root,userData,configured)); }
   catch(Exception e){Log("ERROR "+e);}
  }
 }
 Boot(string dir,string dataDir,string aumid){
  root=dir;userData=dataDir;appId=aumid;Title="GPT Engine Boot";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
  AllowsTransparency=true; Background=Brushes.Transparent; Topmost=true; ShowInTaskbar=false; ShowActivated=false;
  Left=SystemParameters.PrimaryScreenWidth/2-SystemParameters.PrimaryScreenWidth/2;
  Top=0; Width=SystemParameters.PrimaryScreenWidth; Height=SystemParameters.PrimaryScreenHeight;
   logo=LoadLogo(root);
  Loaded+=(s,e)=>{dpi=PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice.M11;
   Log("START screen="+Width+"x"+Height+" dpi="+dpi);
   CreateOrUpdateDesktopShortcut(root);
   if(!String.IsNullOrWhiteSpace(appId)){appDiscoveryComplete=true;Log("APP configured id="+appId);}
   Task.Factory.StartNew(()=>DiscoverInstalledAppId()).ContinueWith(task=>Dispatcher.BeginInvoke(new Action(()=>{string found=task.Status==TaskStatus.RanToCompletion?task.Result:null;if(!String.IsNullOrWhiteSpace(found)){appId=found;SaveConfiguredAppId(userData,found);Log("APP discovered id="+appId);}else if(String.IsNullOrWhiteSpace(appId)){appId=null;Log("APP discovery complete none");}appDiscoveryComplete=true;})),TaskScheduler.Default);
   client=FindClient(); if(client!=IntPtr.Zero){ CaptureClientPlacement(); Log("CLIENT retained for background rendering="+client+" maximized="+savedMax+" showCmd="+(hasSavedPlacement?savedPlacement.ShowCmd:-1)+" "+matchSummary); }
   RECT initialBounds,initialClient;uint initialDpi;bool known,maximized,touches;string mode;
   if(GetOverlayBounds(out initialBounds,out initialClient,out initialDpi,out known,out maximized,out touches,out mode))PlaceOverlay(initialBounds,initialDpi);
   clock.Start();
   CompositionTarget.Rendering+=Frame;
  };
  KeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.Escape)Close();};
  Closed+=(s,e)=>{CompositionTarget.Rendering-=Frame;RevealClient(true);Log("CLOSED");};
 }
 void CaptureClientPlacement(){
  WINDOWPLACEMENT placement=new WINDOWPLACEMENT();placement.Length=Marshal.SizeOf(typeof(WINDOWPLACEMENT));
  hasSavedPlacement=GetWindowPlacement(client,ref placement);
  savedPlacement=placement;
  hasSavedWindowBounds=!IsIconic(client)&&GetWindowRect(client,out savedWindowBounds);
  Log("CLIENT capture screen="+savedWindowBounds.L+","+savedWindowBounds.T+","+savedWindowBounds.R+","+savedWindowBounds.B+" normal="+placement.NormalPosition.L+","+placement.NormalPosition.T+","+placement.NormalPosition.R+","+placement.NormalPosition.B);
  savedMax=IsZoomed(client)||(hasSavedPlacement&&placement.ShowCmd==3);
  restoreCmd=savedMax?3:4;
  if(hasSavedPlacement&&placement.ShowCmd==2)restoreCmd=9;
 }
 IntPtr FindClient(){
  IntPtr foreground=GetForegroundWindow(),foregroundRoot=GetAncestor(foreground,2),best=IntPtr.Zero,bestMax=IntPtr.Zero,bestForeground=IntPtr.Zero;
  uint foregroundPid=0;GetWindowThreadProcessId(foreground,out foregroundPid);long bestStart=Int64.MinValue,bestMaxStart=Int64.MinValue;int matches=0,maximizedMatches=0;
  EnumWindowsProc callback=delegate(IntPtr hwnd,IntPtr lparam){
   if(!IsWindowVisible(hwnd)||GetAncestor(hwnd,2)!=hwnd)return true;
   uint pid=0;GetWindowThreadProcessId(hwnd,out pid);if(pid==0)return true;
   try{using(Process process=Process.GetProcessById((int)pid)){
    string processName=process.ProcessName;
    if(!processName.Equals("ChatGPT",StringComparison.OrdinalIgnoreCase)&&!processName.Equals("Codex",StringComparison.OrdinalIgnoreCase))return true;
    matches++;long started=0;try{started=process.StartTime.Ticks;}catch{}
    bool maximized=IsZoomed(hwnd);WINDOWPLACEMENT placement=new WINDOWPLACEMENT();placement.Length=Marshal.SizeOf(typeof(WINDOWPLACEMENT));if(GetWindowPlacement(hwnd,ref placement)&&placement.ShowCmd==3)maximized=true;
    if(maximized){maximizedMatches++;if(bestMax==IntPtr.Zero||started>bestMaxStart){bestMax=hwnd;bestMaxStart=started;}}
    if((hwnd==foregroundRoot||pid==foregroundPid)&&bestForeground==IntPtr.Zero)bestForeground=hwnd;
    if(best==IntPtr.Zero||started>bestStart){best=hwnd;bestStart=started;}
   }}catch{}return true;
  };
  EnumWindows(callback,IntPtr.Zero);
  IntPtr chosen=bestForeground!=IntPtr.Zero?bestForeground:(bestMax!=IntPtr.Zero?bestMax:best);
  matchSummary="windows="+matches+" maximized="+maximizedMatches+" foreground="+foreground+" selected="+chosen;
  return chosen;
 } bool Near(RECT a,RECT b,int tolerance){return Math.Abs(a.L-b.L)<=tolerance&&Math.Abs(a.T-b.T)<=tolerance&&Math.Abs(a.R-b.R)<=tolerance&&Math.Abs(a.B-b.B)<=tolerance;}
 bool GetOverlayBounds(out RECT r,out RECT windowBounds,out uint clientDpi,out bool monitorKnown,out bool maximized,out bool touchesMonitor,out string mode){
  r=new RECT();windowBounds=new RECT();clientDpi=0;monitorKnown=false;maximized=false;touchesMonitor=false;mode="windowed";
  IntPtr overlay=new WindowInteropHelper(this).Handle;
  IntPtr target=client!=IntPtr.Zero?client:overlay;
  if(client!=IntPtr.Zero){GetWindowRect(client,out windowBounds);RECT visible;if(DwmGetWindowAttribute(client,9,out visible,16)==0)windowBounds=visible;}
  clientDpi=GetDpiForWindow(target);
  MONITORINFO monitor=new MONITORINFO();monitor.Size=Marshal.SizeOf(typeof(MONITORINFO));
  IntPtr monitorHandle=MonitorFromWindow(target,2);monitorKnown=monitorHandle!=IntPtr.Zero&&GetMonitorInfo(monitorHandle,ref monitor);
  if(!monitorKnown)return false;
  WINDOWPLACEMENT placement=new WINDOWPLACEMENT();placement.Length=Marshal.SizeOf(typeof(WINDOWPLACEMENT));
  maximized=(clientHidden&&savedMax)||IsZoomed(client)||(GetWindowPlacement(client,ref placement)&&placement.ShowCmd==3);
  // Always use the physical monitor rectangle, including its taskbar area.
  // Exact fullscreen bounds also let the shell recognize the active fullscreen window.
  r=monitor.Monitor;mode="fullscreen";touchesMonitor=true;
  return true;
 }
 void PlaceOverlay(RECT r,uint clientDpi){
  if(clientDpi>0)dpi=clientDpi/96.0;
  arranging=true;try{SetWindowPos(new WindowInteropHelper(this).Handle,new IntPtr(-1),r.L,r.T,r.R-r.L,r.B-r.T,0x10);UpdateLayout();}finally{arranging=false;}
  lastOverlayBounds=r;lastClientDpi=clientDpi;
 }
 void KeepOverlayAboveTaskbar(bool activate){
  IntPtr overlay=new WindowInteropHelper(this).Handle;
  SetWindowPos(overlay,new IntPtr(-1),0,0,0,0,0x13);
  if(activate)SetForegroundWindow(overlay);
 }
 void Frame(object s,EventArgs e){
  if(arranging)return;
  double t=clock.Elapsed.TotalSeconds;
  if(client!=IntPtr.Zero&&!IsWindow(client)){
   Log("CLIENT previous window exited; waiting for themed replacement");
   client=IntPtr.Zero;sized=false;clientShown=false;clientHidden=false;hasSavedPlacement=false;hasSavedWindowBounds=false;
  }
  if(client==IntPtr.Zero&&preparing&&t>=nextClientProbe){nextClientProbe=t+.15;client=FindClient();}
  if(preparing)KeepOverlayAboveTaskbar(false);
  if(t>=5 && !sized){ if(!preparing){preparing=true;phase=t;KeepOverlayAboveTaskbar(true);Log("SCENE animation started");}
   if(!launchQueued&&(client!=IntPtr.Zero||appDiscoveryComplete)){
    launchQueued=true;
    Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>{try{
     Log("OPAQUE ready before activation; existingClient="+(client!=IntPtr.Zero));
     if(!TryLaunchThemeClient()&&client==IntPtr.Zero&&!String.IsNullOrWhiteSpace(appId))
      Process.Start(new ProcessStartInfo("explorer.exe","shell:AppsFolder\\"+appId){UseShellExecute=true});
    }catch(Exception ex){Log("APP launch error="+ex.GetType().Name);}}));
   }
   if(client==IntPtr.Zero){InvalidateVisual();if(t>20){Log("ERROR client window unavailable appId="+(appId??"none"));Opacity=0;Hide();Close();if(!appFailureShown){appFailureShown=true;Dispatcher.BeginInvoke(new Action(()=>MessageBox.Show(String.IsNullOrWhiteSpace(appId)?"没有找到已安装的 Codex 或 ChatGPT 桌面客户端。请先安装客户端，然后重新运行桌面快捷方式。":"已启动 Codex / ChatGPT，但没有找到它的主窗口。请确认客户端可以正常打开后再试。","Codex 启动器",MessageBoxButton.OK,MessageBoxImage.Information)));}} return;}
   if(!clientHidden&&!clientShown)CaptureClientPlacement();
   if(IsIconic(client))ShowWindow(client,9);
   RECT r,windowBounds;uint clientDpi;bool monitorKnown,maximized,touchesMonitor;string mode;
   if(!GetOverlayBounds(out r,out windowBounds,out clientDpi,out monitorKnown,out maximized,out touchesMonitor,out mode))return;
   Log("CLIENT visible behind opaque animation="+client);
   sized=true;if(!Near(r,lastOverlayBounds,0)||clientDpi!=lastClientDpi)PlaceOverlay(r,clientDpi);KeepOverlayAboveTaskbar(true);
   MONITORINFO monitor=new MONITORINFO();monitor.Size=Marshal.SizeOf(typeof(MONITORINFO));bool hasMonitor=GetMonitorInfo(MonitorFromWindow(client,2),ref monitor);
   Log("SCENE mode="+mode+" maximized="+maximized+" touchesMonitor="+touchesMonitor+" window="+(windowBounds.R-windowBounds.L)+"x"+(windowBounds.B-windowBounds.T)+" at="+windowBounds.L+","+windowBounds.T+" monitor="+(hasMonitor?(monitor.Monitor.R-monitor.Monitor.L)+"x"+(monitor.Monitor.B-monitor.Monitor.T):"unknown")+" overlay="+(r.R-r.L)+"x"+(r.B-r.T)+" dpi="+clientDpi+" client="+client);
  }
  if(sized&&t-phase<SceneSeconds+.33&&IsWindowVisible(client)&&!IsIconic(client)){
   RECT r,windowBounds;uint clientDpi;bool monitorKnown,maximized,touchesMonitor;string mode;
   if(GetOverlayBounds(out r,out windowBounds,out clientDpi,out monitorKnown,out maximized,out touchesMonitor,out mode)&&(!Near(r,lastOverlayBounds,2)||clientDpi!=lastClientDpi))PlaceOverlay(r,clientDpi);
  }
   if(sized&&!handoffReady&&!readinessPending&&t>=nextReadyProbe){
    nextReadyProbe=t+.5;readinessPending=true;
    Task.Factory.StartNew(()=>ProbeHandoffReady()).ContinueWith(task=>Dispatcher.BeginInvoke(new Action(()=>{readinessPending=false;if(task.Status==TaskStatus.RanToCompletion&&task.Result){handoffReady=true;Log("HANDOFF renderer and theme ready");}})),TaskScheduler.Default);
   }
   if(sized&&transitionStart<0&&t-phase>=SceneSeconds&&handoffReady){transitionStart=t;RevealClient();Log("HANDOFF aperture starting after ready");}
   if(transitionStart>=0&&t-transitionStart>=.33){SetForegroundWindow(client);Log("RETURN real-client="+client);Close();return;}
   if(preparing&&transitionStart<0&&t-phase>20){Log("HANDOFF readiness fallback; revealing client instead of holding fullscreen");Opacity=0;Hide();RevealClient(true);Close();return;}
  InvalidateVisual();
 }
 void RevealClient(bool activate=false){
  if(client==IntPtr.Zero)return;
  if(clientShown){if(activate)SetForegroundWindow(client);return;}
  clientShown=true;
  if(clientHidden){
   // Ordinary windows already retain their rectangle while hidden. Reapplying
   // WINDOWPLACEMENT can let Windows clamp an oversized restore rectangle.
   if(savedMax&&hasSavedPlacement&&!IsZoomed(client)){WINDOWPLACEMENT placement=savedPlacement;placement.ShowCmd=3;SetWindowPlacement(client,ref placement);}
   ShowWindow(client,savedMax?8:restoreCmd);
   if(!savedMax&&hasSavedWindowBounds)SetWindowPos(client,IntPtr.Zero,savedWindowBounds.L,savedWindowBounds.T,savedWindowBounds.R-savedWindowBounds.L,savedWindowBounds.B-savedWindowBounds.T,0x14);
   clientHidden=false;
  }
  // Keep the fullscreen animation foreground until its aperture has opened.
  if(activate)SetForegroundWindow(client);else KeepOverlayAboveTaskbar(true);
  RECT actual;GetWindowRect(client,out actual);
  Log("CLIENT reveal="+client+" maximized="+IsZoomed(client)+" expectedMax="+savedMax+" screen="+actual.L+","+actual.T+","+actual.R+","+actual.B);
 }
 Brush ColorB(byte r,byte g,byte b,double alpha){return new SolidColorBrush(Color.FromArgb((byte)(Math.Max(0,Math.Min(1,alpha))*255),r,g,b));}
 Brush green= new SolidColorBrush(Color.FromRgb(87,255,169));
 void Text(DrawingContext d,string s,double x,double y,double size,Brush b,bool code=false){
  var f=new FormattedText(s,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,code?mono:sans,size,b,dpi); d.DrawText(f,new Point(x,y));
 }
  void CenterText(DrawingContext d,string s,double cx,double y,double size,Brush b){
   var f=new FormattedText(s,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,sans,size,b,dpi); d.DrawText(f,new Point(cx-f.Width/2,y));
  }
  void NeonText(DrawingContext d,string s,double cx,double y,double size){
   var f=new FormattedText(s,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,sans,size,ColorB(101,255,151,1),dpi);
   Geometry glyph=f.BuildGeometry(new Point(cx-f.Width/2,y));
   for(int k=5;k>=1;k--)d.DrawGeometry(null,new Pen(ColorB(34,255,104,.045),k*1.15),glyph);
   d.DrawGeometry(ColorB(130,255,170,1),null,glyph);
  }
  void CodexGlowText(DrawingContext d,string s,double x,double y,double size){
   var f=new FormattedText(s,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,sans,size,ColorB(104,255,154,1),dpi);
   Geometry glyph=f.BuildGeometry(new Point(x,y));
   for(int k=5;k>=1;k--)d.DrawGeometry(null,new Pen(ColorB(45,255,115,.052),k*1.35),glyph);
   d.DrawGeometry(ColorB(108,255,157,1),null,glyph);
  }
  void CodexGlowTextCentered(DrawingContext d,string s,double cx,double y,double size){
   var f=new FormattedText(s,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),size,ColorB(108,255,157,1),dpi);
   Geometry glyph=f.BuildGeometry(new Point(cx-f.Width/2,y));
   for(int k=5;k>=1;k--)d.DrawGeometry(null,new Pen(ColorB(45,255,115,.052),k*1.35),glyph);
   d.DrawGeometry(ColorB(108,255,157,1),null,glyph);
  }
  void RefinedModelText(DrawingContext d,string s,double cx,double y,double size,Brush color,bool charged){
   var f=new FormattedText(s,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,sans,size,color,dpi);
   f.SetFontWeight(FontWeights.Medium,0,s.Length);Point origin=new Point(cx-f.Width/2,y);
   if(charged){Geometry glyph=f.BuildGeometry(origin);for(int k=4;k>=1;k--)d.DrawGeometry(null,new Pen(ColorB(40,255,114,.045),k*.85),glyph);d.DrawGeometry(color,null,glyph);}
   else d.DrawText(f,origin);
  }
  void CodexGlowMark(DrawingContext d,double cx,double cy,double size){
   var mark=new StreamGeometry();
   using(var path=mark.Open()){
    path.BeginFigure(new Point(32,5),false,true);
    path.BezierTo(new Point(39,5),new Point(44,8),new Point(47,13),true,false);
    path.BezierTo(new Point(56,13),new Point(61,20),new Point(59,29),true,false);
    path.BezierTo(new Point(66,36),new Point(62,47),new Point(53,50),true,false);
    path.BezierTo(new Point(51,59),new Point(42,64),new Point(34,60),true,false);
    path.BezierTo(new Point(25,65),new Point(15,61),new Point(13,52),true,false);
    path.BezierTo(new Point(4,49),new Point(1,39),new Point(6,32),true,false);
    path.BezierTo(new Point(2,23),new Point(7,14),new Point(16,13),true,false);
    path.BezierTo(new Point(18,7),new Point(25,3),new Point(32,5),true,false);
    path.BeginFigure(new Point(22,25),false,false);path.LineTo(new Point(27,33),true,false);path.LineTo(new Point(22,41),true,false);
    path.BeginFigure(new Point(36,42),false,false);path.LineTo(new Point(47,42),true,false);
   }
   mark.Freeze();d.PushTransform(new TranslateTransform(cx-size/2,cy-size/2));d.PushTransform(new ScaleTransform(size/68,size/68));
   for(int k=5;k>=1;k--){Pen glow=new Pen(ColorB(42,255,105,.052),k*1.45);glow.StartLineCap=PenLineCap.Round;glow.EndLineCap=PenLineCap.Round;glow.LineJoin=PenLineJoin.Round;d.DrawGeometry(null,glow,mark);}
   Pen stroke=new Pen(ColorB(94,255,143,1),2.25);stroke.StartLineCap=PenLineCap.Round;stroke.EndLineCap=PenLineCap.Round;stroke.LineJoin=PenLineJoin.Round;d.DrawGeometry(null,stroke,mark);d.Pop();d.Pop();
  }
  void SoftCard(DrawingContext d,double x,double y,double w,double h,Brush fill,double radius=12){
   d.DrawRoundedRectangle(fill,new Pen(ColorB(90,231,175,.19),.7),new Rect(x,y,w,h),radius,radius);
  }
  static readonly string[] CodeNouns={"horizon","workspace","afterglow","quietField","softSignal","starlight","canvas","daybreak","contour","echo","northWindow","luminousField","openCircuit","moonGarden","clearCurrent","velvetSky","signalBloom"};
  static readonly string[] CodeActions={"shape","weave","render","trace","compose","settle","align","gather","tune","bloom","thread","unfold","illuminate","reconcile","synchronize","discover","carry"};
  static readonly string[] CodeMoods={"softly","in the distance","under glass","slowly","between quiet layers","without a map","in green light","after midnight","like distant starlight","almost silently","with patient hands","beneath a pale sky","where edges soften","in a room of dawn","along the horizon","as noise falls away","under a calm current"};
  static readonly string[] CodeColors={"#42F5B3","#6CF5C8","#9BE8D0","#1B6D55","#D2FFF1","#85DDBC","#8DEFC0","#36C995","#B8F7DC"};
  static readonly string[] CodeCurves={"ease-out","soft spring","long arc","quiet drift","slow bloom","gentle return","patient orbit","unhurried wave","softly accelerating","a wide spiral","a breath between beats"};
  static readonly string[] CodeWords={"paper moon","open air","small orbit","green room","distant shore","soft focus","clear water","silver thread","night garden","warm static","quiet constellation","window after rain","a map of light","the shape of silence","a garden in winter","slow-moving weather","a signal from home","the first line of morning","where the river turns","the color of remembering","a lantern in the fog"};
  static readonly string[] CodePlaces={"north window","violet horizon","open water","upper gallery","quiet station","glass garden","far shoreline","winter orchard","small observatory","inner courtyard","blue hour","unfinished room","edge of morning","silver valley","soft-lit archive","western sky","hidden atrium"};
  static readonly string[] CodeTextures={"fine grain beneath the glass","room around every thought","thin light on the surface","an echo beside an open door","a pattern under the noise","details left unfinished","warmth from a rainlit window","one clear note in silence","a slower rhythm for the next idea","the outline of an unnamed thing","a long breath between signals","room for the unexpected"};
  string TerminalLine(int n){
   int slot=n%60,block=n/60;
   if(slot==0||slot==15||slot==16||slot==29||slot==30||slot==40||slot==41||slot==52||slot==59)return "";
   int a=(n*17+n/7*13+block*5+3)%CodeNouns.Length,b=(n*11+n/9*7+block*7+5)%CodeActions.Length,c=(n*19+n/5*3+block*11+1)%CodeMoods.Length;
   int color=(n*7+n/11+block*3)%CodeColors.Length,curve=(n*5+n/13*3+block*5)%CodeCurves.Length,word=(n*13+n/6*5+block*17)%CodeWords.Length;
   int place=(n*7+n/4*3+block*13+2)%CodePlaces.Length,texture=(n*11+n/8*5+block*7+4)%CodeTextures.Length;
   string noun=CodeNouns[a],action=CodeActions[b],phrase=CodeWords[word],mood=CodeMoods[c],location=CodePlaces[place],surface=CodeTextures[texture];
   switch(slot){
    case 1:return "// \""+phrase+"\" settles at the "+location+" as the noise falls away";
    case 2:return "const "+noun+" = await interface."+action+"(\""+phrase+"\", {";
    case 3:return "  title: \""+phrase+" / "+location+"\", // the first thought arrives slowly";
    case 4:return "  palette: \""+CodeColors[color]+"\", // " +CodeWords[(word+5)%CodeWords.Length]+" held in green light";
    case 5:return "  intention: \""+surface+"\", // " +CodeMoods[(c+4)%CodeMoods.Length];
    case 6:return "  atmosphere: \""+mood+" over the "+location+"\", // signal settles";
    case 7:return "  motion: \""+CodeCurves[curve]+"\", // " +CodeTextures[(texture+3)%CodeTextures.Length];
    case 8:return "  anchor: \""+CodePlaces[(place+6)%CodePlaces.Length]+" near \""+phrase+"\",";
    case 9:return "  spacing: "+(18+(n*7)%47)+"px, // keep the quiet parts open and unhurried";
    case 10:return "  signal: await channel."+CodeActions[(b+5)%CodeActions.Length]+"(\""+noun+" / "+location+"\"),";
    case 11:return "  layers: [\""+CodeWords[(word+2)%CodeWords.Length]+"\", \""+CodeWords[(word+9)%CodeWords.Length]+"\"],";
    case 12:return "  texture: \""+CodeTextures[(texture+7)%CodeTextures.Length]+"\", // nothing needs to hurry";
    case 13:return "  onReady: scene => scene."+CodeActions[(b+9)%CodeActions.Length]+"(\""+CodeMoods[(c+8)%CodeMoods.Length]+"\")";
    case 14:return "});  // leave a little room for whatever comes next";
    case 17:return "// a second pass follows the signal toward the "+location+", listening for detail";
    case 18:return "type "+Char.ToUpperInvariant(noun[0])+noun.Substring(1)+"Sequence = { // fragments gathered along the way";
    case 19:return "  opening: \""+phrase+"\"; // " +CodeMoods[(c+7)%CodeMoods.Length]+" beyond the visible edge";
    case 20:return "  route: \""+location+" -> "+CodePlaces[(place+8)%CodePlaces.Length]+"\"; // a luminous crossing";
    case 21:return "  interval: \""+CodeCurves[(curve+4)%CodeCurves.Length]+" / " +CodeWords[(word+11)%CodeWords.Length]+"\";";
    case 22:return "  surface: \""+surface+"\"; // " +CodeWords[(word+7)%CodeWords.Length];
    case 23:return "  response: await memory."+CodeActions[(b+11)%CodeActions.Length]+"(\""+CodeNouns[(a+8)%CodeNouns.Length]+"\");";
    case 24:return "  color: \""+CodeColors[(color+5)%CodeColors.Length]+" / "+CodeColors[(color+2)%CodeColors.Length]+"\";";
    case 25:return "  cadence: \""+CodeMoods[(c+10)%CodeMoods.Length]+"\"; // let each small connection settle";
    case 26:return "  caption: \""+CodeWords[(word+13)%CodeWords.Length]+" at the "+CodePlaces[(place+3)%CodePlaces.Length]+"\";";
    case 27:return "  trace: [\""+CodeNouns[(a+3)%CodeNouns.Length]+"\", \""+noun+"\", \""+CodeNouns[(a+12)%CodeNouns.Length]+"\"],";
    case 28:return "  return { focus: \""+phrase+"\", drift: \""+CodeCurves[(curve+7)%CodeCurves.Length]+"\" };";
    case 31:return "// "+CodeMoods[(c+6)%CodeMoods.Length]+" / "+CodeTextures[(texture+2)%CodeTextures.Length];
    case 32:return "const fragments = [ // pieces of a thought, arranged without a fixed grid";
    case 33:return "  \""+phrase+" / "+location+"; the background grows quiet\",";
    case 34:return "  \""+CodeWords[(word+6)%CodeWords.Length]+" carried " +CodeMoods[(c+2)%CodeMoods.Length]+" through the channel\",";
    case 35:return "  \""+CodeTextures[(texture+5)%CodeTextures.Length]+" / " +CodeWords[(word+15)%CodeWords.Length]+"\",";
    case 36:return "  \""+CodePlaces[(place+10)%CodePlaces.Length]+" becoming a place for the next idea\"";
    case 37:return "];  // the unfinished lines are part of the composition";
    case 38:return "const afterimage = await canvas."+CodeActions[(b+13)%CodeActions.Length]+"(fragments);";
    case 39:return "return { afterimage, focus: \""+CodeWords[(word+12)%CodeWords.Length]+"\" };";
    case 42:return "// the channel turns toward the "+location+"; a new layer gathers in the quiet";
    case 43:return "const "+CodeNouns[(a+7)%CodeNouns.Length]+" = await renderer."+CodeActions[(b+3)%CodeActions.Length]+"({";
    case 44:return "  source: \""+CodePlaces[(place+5)%CodePlaces.Length]+" / "+CodeWords[(word+8)%CodeWords.Length]+"\",";
    case 45:return "  prose: \""+CodeTextures[(texture+9)%CodeTextures.Length]+"\", // a note left beside the open window";
    case 46:return "  motif: \""+CodeWords[(word+3)%CodeWords.Length]+" in " +CodeMoods[(c+9)%CodeMoods.Length]+"\",";
    case 47:return "  orbit: \""+CodeCurves[(curve+2)%CodeCurves.Length]+" / "+CodePlaces[(place+11)%CodePlaces.Length]+"\",";
    case 48:return "  warmth: \""+CodeColors[(color+4)%CodeColors.Length]+"\", // let the bright parts breathe";
    case 49:return "  details: await archive."+CodeActions[(b+14)%CodeActions.Length]+"(\""+CodeWords[(word+17)%CodeWords.Length]+"\"),";
    case 50:return "  preserve: \""+CodeMoods[(c+12)%CodeMoods.Length]+"\" // nothing here needs to hurry";
    case 51:return "});  // the next passage can begin wherever it wants";
    case 53:return "// a few lines remain open for the next idea to find its own shape";
    case 54:return "const narrative = [ // gathered fragments, held loosely together";
    case 55:return "  \""+CodeWords[(word+4)%CodeWords.Length]+"; " +CodeTextures[(texture+4)%CodeTextures.Length]+"\",";
    case 56:return "  \""+CodePlaces[(place+14)%CodePlaces.Length]+" turning slowly toward " +CodeWords[(word+10)%CodeWords.Length]+"\",";
    case 57:return "  \""+CodeMoods[(c+5)%CodeMoods.Length]+"; a small light remains along the edge\"";
    case 58:return "];  // continue when the whole scene is ready to listen";
    default:return "};  // retain the pause between one thought and the next";
   }
  }
 void Rect(DrawingContext d,double x,double y,double w,double h,Brush b){if(w>0&&h>0)d.DrawRectangle(b,null,new Rect(x,y,w,h));}
 protected override void OnRender(DrawingContext d){
  base.OnRender(d); double t=clock.Elapsed.TotalSeconds,w=ActualWidth,h=ActualHeight;
  if(!sized){if(preparing)Scene(d,Math.Max(0,t-phase),w,h);else Engine(d,Math.Min(t,5),w,h);return;}
  double p=transitionStart<0?Math.Min(t-phase,SceneSeconds-.001):SceneSeconds+t-transitionStart;
   if(p<SceneSeconds){Scene(d,p,w,h);return;}
   // Snap shut, hold a bright filament briefly, then snap the aperture open to the live app.
  double c=p-SceneSeconds; double half=h/2;
   if(c<.055){double scale=Math.Max(.001,1-c/.055);d.PushTransform(new ScaleTransform(1,scale,w/2,h/2));Scene(d,SceneSeconds,w,h);d.Pop();}
   else if(c<.20){Rect(d,-36,-36,w+72,h+72,ColorB(0,0,0,1));}
   else {double opening=Math.Min(1,(c-.20)/.09); double band=half*opening; Rect(d,-36,-36,w+72,half-band+36,ColorB(0,0,0,1));Rect(d,-36,half+band,w+72,half-band+72,ColorB(0,0,0,1));}
   if(c<.29){double a=c<.20?1:Math.Max(0,1-(c-.20)/.09);Rect(d,0,half-8,w,16,ColorB(30,255,226,a*.26));Rect(d,0,half-4,w,8,ColorB(150,255,250,a*.62));Rect(d,0,half-1,w,2,ColorB(255,255,255,a));}
 }
 void Engine(DrawingContext d,double t,double w,double h){
   double size=Math.Min(w*.352,h*.688),x=w/2,y=h/2; double a=Math.Min(.84,t/.55); double angle=-31*t*t*t;
  // The desktop remains visible: no backdrop is drawn in this phase.
   d.PushOpacity(a);d.PushTransform(new RotateTransform(angle,x,y));
   double tint=Math.Min(1,Math.Max(0,(t-.35)/1.25));
  var gradient=new LinearGradientBrush(Color.FromRgb(0,(byte)(211*tint),(byte)(238*tint)),Color.FromRgb((byte)(25*tint),(byte)(255*tint),(byte)(158*tint)),45);
   d.PushTransform(new TranslateTransform(x-size/2,y-size/2));d.PushTransform(new ScaleTransform(size/721,size/721));
   d.DrawGeometry(gradient,null,logo); d.Pop();d.Pop();d.Pop();d.Pop();
  double radius=size*.56;
  for(int i=0;i<36;i++) {double lit=Math.Min(1,Math.Max(0,(t*9-i)));double ang=(i*10-90)*Math.PI/180;
   var pt=new Point(x+radius*Math.Cos(ang),y+radius*Math.Sin(ang)); var pt2=new Point(x+radius*Math.Cos(ang+.10),y+radius*Math.Sin(ang+.10));
   d.DrawLine(new Pen(ColorB(36,255,201,.08+.75*lit),Math.Max(2,size*.008)),pt,pt2);
  }
  double flash=Math.Max(0,1-Math.Abs(t-4.65)/.18);
  if(flash>0){for(int k=8;k>=1;k--)d.DrawEllipse(ColorB(170,255,244,flash*.07),null,new Point(x,y),size*k*.025,size*k*.025);d.DrawLine(new Pen(ColorB(220,255,255,flash),2),new Point(x-size*.65,y),new Point(x+size*.65,y));}
 }
 void FloatingCard(DrawingContext d,double p,double start,double closeAt,double x,double y,double width,double height,string category,string title,string detail,string extra,string status,Brush accent){
  const double closeDuration=.22;
  double age=p-start;
  if(age<0||p>closeAt+closeDuration)return;
  double enterT=Math.Max(0,Math.Min(1,age/.20)),closeT=Math.Max(0,Math.Min(1,(p-closeAt)/closeDuration));
  double enter=enterT*enterT*(3-2*enterT),closing=closeT*closeT*(3-2*closeT);
  double alpha=enter*(1-closing),scale=.95+.05*enter-.04*closing;
  double dy=(1-enter)*13-7*closing,cx=x+width/2,cy=y+height/2;
  d.PushOpacity(alpha);d.PushTransform(new TranslateTransform(0,dy));d.PushTransform(new ScaleTransform(scale,scale,cx,cy));
  d.DrawRoundedRectangle(ColorB(4,14,18,.96),new Pen(ColorB(88,203,190,.64),1.1),new Rect(x,y,width,height),9,9);
  Rect(d,x+1,y+1,width-2,2,accent);
  d.DrawEllipse(accent,null,new Point(x+15,y+17),3.2,3.2);
  Text(d,category,x+25,y+10,Math.Min(10,width/30),ColorB(158,220,211,.9),true);
  Rect(d,x+13,y+31,width-26,1,ColorB(105,158,152,.25));
  double titleY=y+Math.Max(38,height*.29),detailY=y+Math.Max(59,height*.47);
  Text(d,title,x+14,titleY,Math.Min(17,width/22),ColorB(233,250,246,.98));
  Text(d,detail,x+14,detailY,Math.Min(11,width/31),ColorB(148,196,188,.87),true);
  if(!String.IsNullOrEmpty(extra))Text(d,extra,x+14,y+height*.67,Math.Min(10,width/34),ColorB(126,224,204,.88),true);
  double badgeY=y+height-32,badgeW=Math.Min(width-28,Math.Max(112,status.Length*7.2+24));
  d.DrawRoundedRectangle(ColorB(7,40,35,.93),null,new Rect(x+13,badgeY,badgeW,20),5,5);
  Rect(d,x+20,badgeY+8,5,5,accent);
  Text(d,status,x+32,badgeY+3,10,ColorB(174,255,221,.96),true);
  Text(d,"x",x+width-24,y+8,14,ColorB(171,211,203,.7),true);
  d.Pop();d.Pop();d.Pop();
 }
 void Telemetry(DrawingContext d,double p,double sidebar,double h){
  double top=427,available=h-top-24;if(available<125)return;
  double width=sidebar-36,height=available,x=18,fs=Math.Min(10,width/26);
  d.PushOpacity(Math.Max(0,Math.Min(.9,(p-1.4)*.8)));
  Text(d,"NEURAL CONSTELLATION",x+8,top+8,Math.Min(11,width/18),ColorB(139,241,204,.9),true);
  Text(d,"VERTICAL SIGNAL MAP",x+8,top+24,fs,ColorB(99,174,154,.75),true);
  Rect(d,x+8,top+42,width-16,1,ColorB(89,205,170,.25));
  double gy=top+57,gh=Math.Max(150,height-244),cx=x+width/2;
  d.PushClip(new RectangleGeometry(new Rect(x,gy-5,width,gh+10)));
  // A quiet star field gives the neural layers a deep, vertical sense of space.
  for(int i=0;i<52;i++){
   double sx=x+12+(width-24)*((i*47%97)/97.0),sy=gy+gh*((i*67%89)/89.0);
   double twinkle=.35+.35*(1+Math.Sin(p*1.7+i*2.13));double radius=(i%9==0)?1.35:.65;
   d.DrawEllipse(ColorB(127,255,215,.16+twinkle*.28),null,new Point(sx,sy),radius,radius);
   if(i%13==0)d.DrawLine(new Pen(ColorB(113,255,213,.12),.55),new Point(sx-3,sy),new Point(sx+3,sy));
  }
  int[] counts={3,5,7,8,7,4};Point[][] layers=new Point[counts.Length][];
  for(int l=0;l<counts.Length;l++){
   layers[l]=new Point[counts[l]];double rowY=gy+gh*(l+.5)/counts.Length;
   double drift=Math.Sin(p*.33+l*.9)*width*.035;
   for(int n=0;n<counts[l];n++){
    double spread=(width-42)*(n+.5)/counts[l];double organic=Math.Sin(n*2.1+l*1.7)*width*.035;
    layers[l][n]=new Point(x+21+spread+organic+drift,rowY+Math.Sin(n*2.4+l)*5);
   }
  }
  for(int l=0;l<layers.Length-1;l++)for(int a=0;a<layers[l].Length;a++){
   int mapped=(int)Math.Round((a+.5)*layers[l+1].Length/layers[l].Length-.5);
   for(int offset=-1;offset<=1;offset++){
    int b=mapped+offset;if(b<0||b>=layers[l+1].Length)continue;
    Point from=layers[l][a],to=layers[l+1][b];
    d.DrawLine(new Pen(ColorB(56,185,145,offset==0?.2:.105),offset==0?.85:.6),from,to);
    if((a*3+b+l)%3==0){double flow=(p*.25+l*.21+a*.17+b*.13)%1;Point signal=new Point(from.X+(to.X-from.X)*flow,from.Y+(to.Y-from.Y)*flow);d.DrawEllipse(ColorB(77,255,181,.16),null,signal,3.8,3.8);d.DrawEllipse(ColorB(166,255,221,.9),null,signal,1.25,1.25);}
   }
  }
  for(int l=0;l<layers.Length;l++)for(int n=0;n<layers[l].Length;n++){
   Point pt=layers[l][n];double pulse=.4+.3*(1+Math.Sin(p*2.4-l*.8-n*.7));
   d.DrawEllipse(ColorB(48,255,166,.06+pulse*.065),null,pt,9,9);
   d.DrawEllipse(ColorB(3,25,21,.98),new Pen(ColorB(105,255,194,pulse*.82),.9),pt,3.5,3.5);
   d.DrawEllipse(ColorB(170,255,225,pulse),null,pt,1.35,1.35);
  }
  d.Pop();
  double chartY=top+height-156,chartX=x+10,chartW=width-20,chartH=53;
  Text(d,"RUNTIME SIGNAL",chartX,chartY-18,fs,ColorB(126,211,179,.82),true);
  Text(d,"NODES 34   LINKS 68",chartX+chartW*.48,chartY-18,fs,ColorB(126,211,179,.82),true);
  Rect(d,chartX,chartY+chartH,chartW,1,ColorB(92,194,159,.32));
  int bars=28;double gap=2,bw=(chartW-gap*(bars-1))/bars;
  for(int i=0;i<bars;i++){
   double seed=(i*73+19)%101/101.0;
   double wave=.48+.24*Math.Sin(p*(.65+seed*.8)+i*2.39)
    +.17*Math.Sin(p*1.31+i*4.71)+.11*Math.Sin(p*.43+i*.83);
   wave=Math.Max(.06,Math.Min(1,wave));
   double heightBar=5+wave*(chartH-6);
   double strength=Math.Min(1,Math.Max(.15,p/6.8));
   Rect(d,chartX+i*(bw+gap),chartY+chartH-heightBar,bw,heightBar,ColorB(57,230,157,.19+strength*(.16+.19*wave)));
  }
  double creditY=chartY+chartH+28,creditSize=Math.Min(11,width/25);
  Text(d,"Artemis 制作",chartX-1,creditY,creditSize,ColorB(66,255,160,.10));
  Text(d,"Artemis 制作",chartX+1,creditY,creditSize,ColorB(66,255,160,.10));
  Text(d,"Artemis 制作",chartX,creditY,creditSize,ColorB(104,235,169,.72));
  Text(d,p>4?"SIGNAL FLOW // ONLINE":"SIGNAL FLOW // SYNCHRONIZING",x+8,top+height-17,fs,ColorB(130,223,191,.78),true);
  d.Pop();
 }
 void Scene(DrawingContext d,double p,double w,double h){
   double progress=Math.Min(1,Math.Max(0,p/ProgressCompleteAt));
   Rect(d,-36,-36,w+72,h+72,ColorB(2,10,12,1)); double fs=Math.Max(11,Math.Min(15,w/100)),line=fs*1.48;
  for(int i=0;i<w/24;i++){double y=(p*(55+i%7*13)+i*89)% (h+180)-180;for(int j=0;j<12;j++)Text(d,((i*17+j*31)%16).ToString("X"),i*24,y+j*18,12,ColorB(15,210,126,.035+j*.006),true);}
  double sidebar=w*.21,terminal=w*.58;
  Rect(d,terminal,0,w-terminal,h,ColorB(0,12,10,.94));
  Text(d,"GPT ENGINE / INTERFACE COMPILER",terminal+20,18,fs,green,true);
  d.PushClip(new RectangleGeometry(new Rect(terminal+16,48,w-terminal-24,h-110)));
  int rows=(int)Math.Ceiling((h-95)/line),generated=(int)(p*32);
  FormattedText monoSample=new FormattedText("MMMMMMMMMMMMMMMM",System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,mono,fs,green,dpi);
  int maxCodeChars=Math.Max(32,(int)((w-terminal-52)/(monoSample.WidthIncludingTrailingWhitespace/16.0)));
  for(int i=0;i<Math.Min(rows,generated);i++){
   int n=Math.Max(0,generated-rows)+i;string sourceLine=TerminalLine(n);
   if(sourceLine.Length>maxCodeChars)sourceLine=sourceLine.Substring(0,maxCodeChars-2)+"..";
   if(sourceLine.Length>0)Text(d,sourceLine,terminal+20,50+i*line,fs,ColorB(78,255,157,.65+(i%4)*.1),true);
  }d.Pop();
  d.PushOpacity(Math.Min(1,p*2));Rect(d,0,0,sidebar,h,ColorB(13,26,27,.95));CodexGlowText(d,"Codex",24,25,22);d.Pop();
  string[] nav={"+  New chat","Search conversations","Library","Explore GPTs","Recent conversations","Project workspace","Settings"};
  for(int i=0;i<nav.Length;i++)if(p>.16+i*.19){SoftCard(d,14,85+i*47,sidebar-28,36,ColorB(28,65,53,.65),10);Text(d,nav[i],24,93+i*47,Math.Min(14,fs),green);}
  Telemetry(d,p,sidebar,h);
  if(p>.75)CodexGlowText(d,"Codex",sidebar+24,24,18);
  if(p>1.3){
   double centerX=(sidebar+terminal)/2,markY=h*.36-64,questionY=h*.36+35;
   CodexGlowMark(d,centerX,markY,88.8);
   CodexGlowTextCentered(d,"我们要构建什么？",centerX,questionY,Math.Min(34,w/34));
  }
  double inputH=Math.Min(190,h*.28),inputX=sidebar+12,inputY=h-inputH-23,inputW=terminal-sidebar-24;
  for(int i=0;i<3;i++)if(p>1.7+i*.24){double cw=(terminal-sidebar-64)/3,x=sidebar+20+i*(cw+12),y=inputY-60-20;SoftCard(d,x,y,cw,60,ColorB(19,49,41,.9),13);Text(d,new[]{"Create","Explore","Think"}[i],x+14,y+20,fs,green);}
  if(p>2.5){
   SoftCard(d,inputX,inputY,inputW,inputH,ColorB(34,63,56,1),22);
    Text(d,"Message Codex  _",inputX+15,inputY+11,fs,ColorB(195,242,216,1));
    string[] modelNames={"Codex-3","GPT-5.5","GPT-5.6 Luna","GPT-5.6 Terra","GPT-5.6 Sol","GPT-5.6 Turbo","GPT-6 Luna","GPT-6 Sol","GPT-6 Astra"};
    string[] modelLevels={"轻度","轻度","中高","中高","极高","极高","极高","极高","Ultra"};
   double selectorStart=2.5/ProgressCompleteAt;
   double selectorProgress=Math.Max(0,Math.Min(1,(progress-selectorStart)/(1-selectorStart)));
   bool charged=progress>=1;
   int modelIndex=Math.Min(modelNames.Length-1,(int)(selectorProgress*modelNames.Length));
   string modelName=modelNames[modelIndex],modelLevel=modelLevels[modelIndex];
    double chipW=Math.Min(274,inputW-16),chipH=88;
    if(chipW>=140){
     double chipX=inputX+inputW-chipW-8,chipY=inputY+inputH-chipH-8;
     double glow=selectorProgress,centerX=chipX+chipW/2;
     d.DrawRoundedRectangle(ColorB(48,255,173,.035+.12*glow),null,new Rect(chipX-3,chipY-3,chipW+6,chipH+6),19,19);
     d.DrawRoundedRectangle(ColorB(5,20,18,.98),new Pen(ColorB(70,(byte)(156+99*glow),(byte)(117+75*glow),.55+.4*glow),1.15),new Rect(chipX,chipY,chipW,chipH),17,17);
     // Model control: power icon, centered tier/model, and progress-strength slider.
     double iconY=chipY+19;Pen iconPen=new Pen(ColorB(160,(byte)(215+40*glow),(byte)(184+70*glow),.72+.28*glow),1.8);iconPen.StartLineCap=PenLineCap.Round;iconPen.EndLineCap=PenLineCap.Round;
     Point[] bolt={new Point(chipX+20,iconY-8),new Point(chipX+14,iconY+1),new Point(chipX+19,iconY+1),new Point(chipX+16,iconY+9),new Point(chipX+26,iconY-3),new Point(chipX+21,iconY-3)};
     if(charged){
      var boltGeometry=new StreamGeometry();using(var ctx=boltGeometry.Open()){ctx.BeginFigure(bolt[0],true,true);for(int i=1;i<bolt.Length;i++)ctx.LineTo(bolt[i],true,false);}
      for(int k=5;k>=1;k--)d.DrawGeometry(null,new Pen(ColorB(40,255,102,.07),k*1.3),boltGeometry);
      d.DrawGeometry(ColorB(87,255,136,1),new Pen(ColorB(160,255,189,1),.7),boltGeometry);
     }else for(int i=0;i<bolt.Length;i++)d.DrawLine(iconPen,bolt[i],bolt[(i+1)%bolt.Length]);
     double selectorW=Math.Min(chipW*.58,174),selectorH=43,selectorX=centerX-selectorW/2,selectorY=chipY+3;
     d.DrawRoundedRectangle(ColorB(43,255,172,.035+.12*glow),null,new Rect(selectorX,selectorY,selectorW,selectorH),10,10);
     byte textG=(byte)(188+67*glow),textB=(byte)(128+65*glow);
     RefinedModelText(d,modelLevel,centerX,selectorY+2,14,ColorB(111,textG,textB,.7+.3*glow),charged);
     double modelSize=Math.Max(12,Math.Min(15,(selectorW-27)/(modelName.Length*.55)));
     RefinedModelText(d,modelName,centerX-3,selectorY+20,modelSize,ColorB(171,(byte)(220+35*glow),(byte)(194+61*glow),.78+.22*glow),charged);
     Text(d,"›",selectorX+selectorW-14,selectorY+19,13,ColorB(148,textG,textB,.8+.2*glow));
     double trackX=chipX+13,trackY=chipY+63,trackW=chipW-26,trackH=14;
     if(charged)for(int k=6;k>=1;k--)d.DrawRoundedRectangle(ColorB(36,255,104,.035),null,new Rect(trackX-k*1.2,trackY-k*1.2,trackW+k*2.4,trackH+k*2.4),trackH/2+k*1.2,trackH/2+k*1.2);
     d.DrawRoundedRectangle(ColorB(21,72,54,.95),null,new Rect(trackX,trackY,trackW,trackH),trackH/2,trackH/2);
     double fillW=trackW*selectorProgress;
     if(fillW>0)d.DrawRoundedRectangle(new LinearGradientBrush(Color.FromRgb(38,183,116),Color.FromRgb((byte)(74+30*glow),255,(byte)(150+90*glow)),0),null,new Rect(trackX,trackY,fillW,trackH),trackH/2,trackH/2);
     if(charged)d.DrawRoundedRectangle(new LinearGradientBrush(Color.FromRgb(36,239,91),Color.FromRgb(142,255,175),0),null,new Rect(trackX,trackY,trackW,trackH),trackH/2,trackH/2);
     foreach(double dotAt in new double[]{.08,.5,.91})if(dotAt*trackW<fillW-8)d.DrawEllipse(ColorB(193,255,220,.52),null,new Point(trackX+trackW*dotAt,trackY+trackH/2),2.3,2.3);
     double thumbX=trackX+7.5+(trackW-15)*selectorProgress,thumbY=trackY+trackH/2;
     d.DrawEllipse(ColorB(105,255,189,.20+.16*glow),null,new Point(thumbX,thumbY),12,12);
     d.DrawEllipse(ColorB(228,255,240,1),new Pen(ColorB(80,224,160,.95),.8),new Point(thumbX,thumbY),9,9);
   }
  }
  // Two oversized cards anchor the composition; smaller cards overlap them in deliberate layers.
  FloatingCard(d,p,.48+CardDelaySeconds,4.65+CardDelaySeconds,w*.03,h*.20,w*.23,h*.13,"ACCESS / GATEWAY","Conversation channel","Encrypted stream established","","ACCESS GRANTED",ColorB(59,239,190,.94));
  FloatingCard(d,p,1.03+CardDelaySeconds,4.83+CardDelaySeconds,w*.38,h*.13,w*.42,h*.23,"MEMORY / INDEX","Context archive linked","Recent history mapped to core","CONTEXT WINDOW  //  ALIGNED","SYNC COMPLETE",ColorB(104,190,255,.94));
  FloatingCard(d,p,1.58+CardDelaySeconds,5.01+CardDelaySeconds,w*.34,h*.40,w*.19,h*.12,"TOOLS / RUNTIME","Sandbox bridge","Toolchain attached","","READY",ColorB(179,143,255,.94));
  FloatingCard(d,p,2.13+CardDelaySeconds,5.19+CardDelaySeconds,w*.46,h*.43,w*.27,h*.15,"ENGINE / TELEMETRY","Neural bus stable","Signal integrity nominal","","CORE ONLINE",ColorB(255,205,117,.94));
  FloatingCard(d,p,2.68+CardDelaySeconds,5.37+CardDelaySeconds,w*.045,h*.52,w*.41,h*.23,"MODEL / ROUTING","Inference path selected","Primary route confirmed","MODEL ROUTE  //  COMMITTED","ROUTE LOCKED",ColorB(255,145,125,.94));
  FloatingCard(d,p,3.23+CardDelaySeconds,5.55+CardDelaySeconds,w*.75,h*.62,w*.22,h*.13,"NETWORK / RELAY","Secure relay active","Latency corridor clear","","LINK STABLE",ColorB(102,223,255,.94));
  FloatingCard(d,p,3.78+CardDelaySeconds,5.73+CardDelaySeconds,w*.33,h*.69,w*.25,h*.12,"UI / RENDERER","Interface layers built","Navigation and canvas linked","","RENDER READY",ColorB(155,255,154,.94));
  FloatingCard(d,p,4.33+CardDelaySeconds,5.91+CardDelaySeconds,w*.68,h*.20,w*.20,h*.15,"SAFETY / GUARDRAIL","Policy layer mounted","Response boundary loaded","","PROTECTION ON",ColorB(238,151,213,.94));
  for(double y=0;y<h;y+=4)Rect(d,0,y,w,1,ColorB(0,0,0,.055));
  int progressPercent=(int)(progress*100);
  Text(d,p<ProgressCompleteAt?"COMPILING  "+Math.Min(99,progressPercent)+"%":"CORE ONLINE / INTERFACE READY",terminal+20,h-51,fs,green,true);
  double barHeight=Math.Max(3,h*.004),barY=h-barHeight-2,fillWidth=w*progress;
  Rect(d,0,barY,w,barHeight,ColorB(24,61,53,.88));
  if(fillWidth>0){var barBrush=new LinearGradientBrush(Color.FromRgb(43,239,176),Color.FromRgb(113,255,238),0);Rect(d,0,barY,fillWidth,barHeight,barBrush);Rect(d,Math.Max(0,fillWidth-24),barY-2,Math.Min(28,fillWidth),barHeight+4,ColorB(115,255,231,.30));}
  if(p>=5.75+CardDelaySeconds&&p<6.10+CardDelaySeconds)Text(d,"> Launch GPT Interface _",terminal+20,h-29,fs,Brushes.White,true);
  double readyAt=ProgressCompleteAt+.02;
  if(p>=readyAt){
   double titleAlpha=Math.Min(1,(p-readyAt)/.18),titleSize=Math.Min(46,Math.Min(w/34,h/25))*1.30;
   double titleTop=28,subTop=titleTop+titleSize*1.24+8,enterTop=subTop+29;
   double titleWidth=22*titleSize*.60,boxW=titleWidth+72,boxH=Math.Max(154,enterTop+36),boxX=(w-boxW)/2,boxY=(h-boxH)/2;
   d.PushOpacity(titleAlpha);
   d.DrawRoundedRectangle(ColorB(1,13,15,.97),new Pen(ColorB(104,241,212,.92),1.5),new Rect(boxX,boxY,boxW,boxH),12,12);
   Rect(d,boxX+22,boxY+12,boxW-44,2,ColorB(93,255,210,.86));
   Text(d,"SYSTEM READY TO LAUNCH",(w-titleWidth)/2,boxY+titleTop,titleSize,Brushes.White,true);
   double subWidth=24*12*.60;
   Text(d,"GPT INTERFACE  //  ONLINE",(w-subWidth)/2,boxY+subTop,12,ColorB(143,255,217,.96),true);
   if(p>=6.56+CardDelaySeconds)Text(d,"> ENTER  ↵",(w-10*16*.60)/2,boxY+enterTop,16,ColorB(220,255,242,1),true);
   d.Pop();
  }
 }
}
