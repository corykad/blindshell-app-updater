using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Runtime.InteropServices;
using System.Windows.Automation.Provider;

namespace BlindShellUpdater {
static class Program {
    public static readonly string Home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlindShellUpdater");
    [STAThread] static void Main(string[] args) {
        Directory.CreateDirectory(Home);
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        try {
            if (args.Contains("--self-test")) { Tests.Run(); return; }
            if (args.Contains("--announcement-test")) {using(var control=new Control()){control.CreateControl();ScreenReader.Announce(control,"Updater test. Download is fifty percent complete.");Thread.Sleep(5000);}return;}
            if (args.Contains("--session-test")) {
                string log=Path.Combine(Home,"session-test.log"),pkg="org.example.blindshellupdater.testonly";File.WriteAllText(log,"");
                var e=new Engine(s=>File.AppendAllText(log,s+Environment.NewLine));e.Init();var p=e.Detect();e.Normal(p);
                if(e.Run(Path.Combine(e.Tools,"adb.exe"),new[]{"-s",p.Serial,"shell","pm path "+pkg},60000,false).Code==0)throw new Exception("Test package already exists; refusing to replace it.");
                string dir=Path.Combine(e.Session,"session-test");Directory.CreateDirectory(dir);string manifest=Path.Combine(dir,"AndroidManifest.xml"),unsigned=Path.Combine(dir,"unsigned.apk"),apk=Path.Combine(dir,"test.apk");
                File.WriteAllText(manifest,"<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" package=\""+pkg+"\" android:versionCode=\"1\" android:versionName=\"1\"><uses-sdk android:minSdkVersion=\"29\"/><application android:hasCode=\"false\" android:label=\"Updater installation test\"/></manifest>");
                e.Run(Path.Combine(e.Tools,"aapt.exe"),new[]{"package","-f","-M",manifest,"-I",Path.Combine(e.Tools,"android.jar"),"-F",unsigned});
                e.Run(Path.Combine(e.Tools,"java","bin","java.exe"),new[]{"-jar",Path.Combine(e.Tools,"apksigner.jar"),"sign","--ks",Path.Combine(e.Tools,"wrapper-menu-key.p12"),"--ks-key-alias","blindshell-menu","--ks-pass","pass:menuentry","--key-pass","pass:menuentry","--out",apk,unsigned});
                string splitDir=Path.Combine(dir,"split");Directory.CreateDirectory(splitDir);string splitManifest=Path.Combine(splitDir,"AndroidManifest.xml"),splitUnsigned=Path.Combine(dir,"split-unsigned.apk"),split=Path.Combine(dir,"split.apk");
                File.WriteAllText(splitManifest,"<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" package=\""+pkg+"\" split=\"feature\" android:versionCode=\"1\" android:versionName=\"1\" android:isFeatureSplit=\"true\"><uses-sdk android:minSdkVersion=\"29\"/><application android:hasCode=\"false\"/></manifest>");
                e.Run(Path.Combine(e.Tools,"aapt.exe"),new[]{"package","-f","-M",splitManifest,"-I",Path.Combine(e.Tools,"android.jar"),"-F",splitUnsigned});
                e.Run(Path.Combine(e.Tools,"java","bin","java.exe"),new[]{"-jar",Path.Combine(e.Tools,"apksigner.jar"),"sign","--ks",Path.Combine(e.Tools,"wrapper-menu-key.p12"),"--ks-key-alias","blindshell-menu","--ks-pass","pass:menuentry","--key-pass","pass:menuentry","--out",split,splitUnsigned});
                try {e.InstallSession(p,new List<string>{apk,split},0,"installation test");string paths=e.Shell(p,"pm path "+pkg);if(!paths.Contains("base.apk")||!paths.Contains("split_feature.apk"))throw new Exception("Test package and split were not installed.");File.AppendAllText(log,"Test package and split installed successfully.\r\n");}
                finally {var removed=e.Run(Path.Combine(e.Tools,"adb.exe"),new[]{"-s",p.Serial,"uninstall",pkg},60000,false);File.AppendAllText(log,"Test cleanup: "+removed.Text.Trim()+"\r\n");}
                return;
            }
            if (args.Contains("--inspect")) {
                var e=new Engine(s=>File.AppendAllText(Path.Combine(Home,"inspect.log"),s+Environment.NewLine));e.Init();var p=e.Detect();var apps=e.Scan(p);
                File.WriteAllText(Path.Combine(Home,"inspection.json"),new JavaScriptSerializer().Serialize(new {phone=p,apps=apps}));return;
            }
            if (args.Contains("--integration-test")) {
                var log=Path.Combine(Home,"integration-test.log");Action<string> report=s=>File.AppendAllText(log,s+Environment.NewLine);File.WriteAllText(log,"");
                var e=new Engine(report);e.Init();var p=e.Detect();var a=e.Scan(p).Single(x=>x.Package=="com.facebook.stella");e.AuroraCheck(p,a);report(a.Status+" "+a.Available);
                if(!a.AutoDownload)throw new Exception("No newer Meta AI update available: "+a.Status);string file=e.AuroraDownload(p,a);report(e.Install(p,a,file));return;
            }
            if (args.Contains("--browse-test")) {
                var e=new Engine(s=>{});e.Init();var p=e.Detect();var categories=e.Browse(p,"categories","");
                var search=e.Browse(p,"search","Be My Eyes");
                if(categories.Length==0||!search.Cast<Dictionary<string,object>>().Any(x=>Convert.ToString(x["package"])=="com.bemyeyes.bemyeyes"))throw new Exception("Browse integration test failed.");
                var category=categories.Cast<Dictionary<string,object>>().First(x=>Convert.ToString(x["name"])=="Productivity");var results=e.Browse(p,"category",Convert.ToString(category["url"]));if(results.Length==0)throw new Exception("Category results were empty.");
                File.WriteAllText(Path.Combine(Home,"browse-test.txt"),categories.Length+" categories, "+search.Length+" search results, "+results.Length+" category results; all passed.");return;
            }
            using (var mutex = new Mutex(false, args.Contains("--preview") ? "Local\\BlindShellUpdater-preview" : "Local\\BlindShellUpdater-v1")) {
                bool owns=false;try{owns=mutex.WaitOne(0);}catch(AbandonedMutexException){owns=true;}
                if (!owns) { MessageBox.Show("Another BlindShell updater window is already open. Close that window, then reopen BlindShell App Updater from the desktop shortcut to use the latest version.","BlindShell App Updater"); return; }
                try {
                    var form = new BrowseForm();
                    if (args.Contains("--preview")) { form.Show(); Application.DoEvents(); using(var b=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(b,new Rectangle(0,0,form.Width,form.Height)); b.Save(Path.Combine(Home,"preview.png")); } form.Dispose(); return; }
                    Application.Run(form);
                } finally { mutex.ReleaseMutex(); }
            }
        } catch(Exception e) { File.WriteAllText(Path.Combine(Home,"startup-error.txt"),e.ToString()); if(!args.Any(x=>x.StartsWith("--")))MessageBox.Show(e.Message,"BlindShell Updater",MessageBoxButtons.OK,MessageBoxIcon.Error); Environment.ExitCode=1; }
    }
}
class Result { public int Code; public string Text; }
class Phone { public string Serial, State; public int Sdk; }
class AppInfo { public string Package, Name, Version, Status="Not checked", Available="", Url="", Developer="", Price=""; public long Code, OnlineCode; public bool AutoDownload, IsNew; }
class ApkInfo { public string Package, Version, Split; public long Code; public int MinSdk; }

static class Data {
    public static string Sha(byte[] b) { using(var s=SHA256.Create()) return BitConverter.ToString(s.ComputeHash(b)).Replace("-","").ToLowerInvariant(); }
    public static bool PackageName(string s) { return Regex.IsMatch(s,@"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z0-9_]+)+$"); }
    public static string SetAdb(string xml, string value) {
        var doc=XDocument.Parse(xml); var hits=doc.Descendants("setting").Where(x=>(string)x.Attribute("name")=="adb_enabled").ToList();
        if(hits.Count!=1) throw new Exception("The phone's settings format is not supported. Nothing was changed.");
        var pattern=@"(<setting\b[^>]*\bname=""adb_enabled""[^>]*\bvalue="")[^""]*("")";
        if(Regex.Matches(xml,pattern).Count!=1) throw new Exception("Cannot safely locate the USB debugging setting.");
        return Regex.Replace(xml,pattern,m=>m.Groups[1].Value+value+m.Groups[2].Value);
    }
    public static string GetAdb(string xml) { return (string)XDocument.Parse(xml).Descendants("setting").Single(x=>(string)x.Attribute("name")=="adb_enabled").Attribute("value"); }
    public static ulong Var(byte[] b,ref int i) { ulong n=0; for(int s=0;s<64;s+=7) { if(i>=b.Length) throw new Exception("Truncated phone properties."); byte v=b[i++]; n|=(ulong)(v&127)<<s; if(v<128)return n; } throw new Exception("Invalid phone properties."); }
    public static List<KeyValuePair<int,byte[]>> Fields(byte[] b) {
        var r=new List<KeyValuePair<int,byte[]>>(); int i=0;
        while(i<b.Length) { ulong t=Var(b,ref i); if((t&7)!=2 || (t>>3)==0)throw new Exception("Unsupported phone properties format."); ulong len=Var(b,ref i); if(len>(ulong)(b.Length-i))throw new Exception("Truncated phone properties."); var v=new byte[(int)len]; Buffer.BlockCopy(b,i,v,0,v.Length);i+=v.Length;r.Add(new KeyValuePair<int,byte[]>((int)(t>>3),v)); } return r;
    }
    static byte[] V(ulong n) { var b=new List<byte>();while(n>127){b.Add((byte)((n&127)|128));n>>=7;}b.Add((byte)n);return b.ToArray(); }
    public static byte[] Field(int n,byte[] b) { return V((ulong)(n*8+2)).Concat(V((ulong)b.Length)).Concat(b).ToArray(); }
    public static string GetUsb(byte[] b) { var rows=Fields(b).Where(x=>x.Key==1).Select(x=>Fields(x.Value)).Where(x=>x.Any(y=>y.Key==1 && Encoding.UTF8.GetString(y.Value)=="persist.sys.usb.config")).ToList(); if(rows.Count>1)throw new Exception("Duplicate USB properties."); return rows.Count==0?null:Encoding.UTF8.GetString(rows[0].Single(x=>x.Key==2).Value); }
    public static byte[] SetUsb(byte[] b,string value) {
        GetUsb(b);var result=new List<byte>();bool found=false;
        foreach(var f in Fields(b)) { bool match=f.Key==1 && Fields(f.Value).Any(x=>x.Key==1 && Encoding.UTF8.GetString(x.Value)=="persist.sys.usb.config");
            if(match) { found=true; if(value!=null) { var inner=Fields(f.Value).SelectMany(x=>Field(x.Key,x.Key==2?Encoding.UTF8.GetBytes(value):x.Value)).ToArray();result.AddRange(Field(1,inner)); } }
            else result.AddRange(Field(f.Key,f.Value));
        }
        if(!found && value!=null)result.AddRange(Field(1,Field(1,Encoding.UTF8.GetBytes("persist.sys.usb.config")).Concat(Field(2,Encoding.UTF8.GetBytes(value))).ToArray()));
        return result.ToArray();
    }
    public static ApkInfo Badging(string s) {
        var m=Regex.Match(s,@"^package: name='([^']+)' versionCode='(\d+)' versionName='([^']*)'",RegexOptions.Multiline);
        var sdk=Regex.Match(s,@"^sdkVersion:'(\d+)'",RegexOptions.Multiline);
        if(!m.Success||!PackageName(m.Groups[1].Value))throw new Exception("The selected file is not a readable Android app.");
        var split=Regex.Match(s.Split('\n')[0],@"\bsplit='([^']+)'");
        return new ApkInfo{Package=m.Groups[1].Value,Code=long.Parse(m.Groups[2].Value),Version=m.Groups[3].Value,MinSdk=sdk.Success?int.Parse(sdk.Groups[1].Value):1,Split=split.Success?split.Groups[1].Value:""};
    }
    public static List<string> Unpack(string file,string dest) {
        Directory.CreateDirectory(dest);
        if(Path.GetExtension(file).Equals(".apk",StringComparison.OrdinalIgnoreCase)) { string p=Path.Combine(dest,"base.apk");File.Copy(file,p);return new List<string>{p}; }
        var files=new List<string>();long total=0;
        using(var z=ZipFile.OpenRead(file)) {
            foreach(var e in z.Entries.Where(x=>x.FullName.EndsWith(".apk",StringComparison.OrdinalIgnoreCase))) {
                if(e.FullName.Contains("..") || e.FullName.Contains("/") || e.FullName.Contains("\\"))throw new Exception("This bundle uses an unsupported folder layout. Choose a flat APKM bundle or a single APK.");
                total+=e.Length;if(total>1500000000 || files.Count>=100)throw new Exception("This bundle is too large.");
                string p=Path.Combine(dest,e.FullName); if(File.Exists(p))throw new Exception("Duplicate APK in the bundle.");e.ExtractToFile(p);files.Add(p);
            }
        }
        if(files.Count==0)throw new Exception("No APK files were found in the bundle.");return files;
    }
}

class Engine {
    public string Tools; public Action<string> Log; public string Session=Path.Combine(Program.Home,"sessions",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));
    const string Usb="/data/property/persistent_properties", Settings="/data/system/users/0/settings_global.xml";
    public Engine(Action<string> log) {Log=log;Directory.CreateDirectory(Session);}
    public void Init() {
        Log("Preparing the included Android tools. No separate Java or Android installation is needed.");
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("tools.zip")) {
            if(stream==null)throw new Exception("The embedded tools are missing. Rebuild the application.");
            byte[] bytes;using(var ms=new MemoryStream()){stream.CopyTo(ms);bytes=ms.ToArray();}
            Tools=Path.Combine(Program.Home,"tools",Data.Sha(bytes).Substring(0,16));
            if(!File.Exists(Path.Combine(Tools,"ready"))) {
                Directory.CreateDirectory(Tools);using(var ms=new MemoryStream(bytes))using(var zip=new ZipArchive(ms))foreach(var e in zip.Entries) {
                    string p=Path.GetFullPath(Path.Combine(Tools,e.FullName));if(!p.StartsWith(Tools+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Invalid embedded tools path.");
                    if(e.Name==""){Directory.CreateDirectory(p);continue;}Directory.CreateDirectory(Path.GetDirectoryName(p));e.ExtractToFile(p,true);
                }File.WriteAllText(Path.Combine(Tools,"ready"),"1");
            }
        }
    }
    static string Quote(string s) { return "\""+Regex.Replace(s,@"(\\*)""",m=>m.Groups[1].Value+m.Groups[1].Value+"\\\"").TrimEnd('\\')+new string('\\',s.Reverse().TakeWhile(c=>c=='\\').Count()*2)+"\""; }
    public Result Run(string exe,IEnumerable<string> args,int timeout=60000,bool require=true) {
        var si=new ProcessStartInfo(exe,string.Join(" ",args.Select(Quote))){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        using(var p=Process.Start(si)) {
            var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();
            if(!p.WaitForExit(timeout)){try{p.Kill();}catch{}throw new Exception("The operation timed out. Reconnect the phone and check its state before trying again.");}
            Task.WaitAll(stdout,stderr);var r=new Result{Code=p.ExitCode,Text=stdout.Result+stderr.Result};
            if(require&&r.Code!=0)throw new Exception(r.Text.Trim());return r;
        }
    }
    public Result Adb(Phone p,params string[] args) { return Run(Path.Combine(Tools,"adb.exe"),(p==null?new string[0]:new[]{"-s",p.Serial}).Concat(args),60000); }
    public string Shell(Phone p,string cmd) { return Adb(p,"shell",cmd).Text.Trim(); }
    public static double? SessionProgress(string dump,int id) {
        var active=dump.Split(new[]{"Historical install sessions:"},StringSplitOptions.None)[0];
        var match=Regex.Match(active,@"(?ms)^\s*Session\s+"+id+@":(.*?)(?=^\s*Session\s+\d+:|\z)");
        if(!match.Success)return null;var value=Regex.Match(match.Groups[1].Value,@"\bmProgress=([0-9]+(?:\.[0-9]+)?)");double parsed;
        return value.Success&&double.TryParse(value.Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out parsed)&&parsed>=0&&parsed<=1?(double?)parsed:null;
    }
    void WriteSession(Phone phone,int session,string apk,string name) {
        long size=new FileInfo(apk).Length;
        var args=new[]{"-s",phone.Serial,"exec-in","cmd","package","install-write","-S",size.ToString(CultureInfo.InvariantCulture),session.ToString(CultureInfo.InvariantCulture),name,"-"};
        var info=new ProcessStartInfo(Path.Combine(Tools,"adb.exe"),string.Join(" ",args.Select(Quote))){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        using(var proc=Process.Start(info)) {
            var stdout=proc.StandardOutput.ReadToEndAsync();var stderr=proc.StandardError.ReadToEndAsync();
            var transfer=Task.Run(()=>{using(var source=File.OpenRead(apk))using(var input=proc.StandardInput.BaseStream)source.CopyTo(input);});
            if(!proc.WaitForExit(600000)){try{proc.Kill();}catch{}throw new Exception("Sending app files to Android timed out.");}
            try{transfer.GetAwaiter().GetResult();}catch(Exception ex){throw new Exception("Could not send an app file to Android: "+ex.Message);}
            Task.WaitAll(stdout,stderr);string output=stdout.Result+stderr.Result;
            // adb exec-in transports binary input without returning remote stdout. Android
            // validates the complete staged session when install-commit runs.
            if(proc.ExitCode!=0)throw new Exception("Could not send an app file to Android: "+output.Trim());
        }
    }
    void WaitForStaging(Phone p,int id,long written,long total) {
        var deadline=DateTime.UtcNow.AddMinutes(2);double expected=(double)written/total;
        while(DateTime.UtcNow<deadline){
            string active=Shell(p,"dumpsys package installs").Split(new[]{"Historical install sessions:"},StringSplitOptions.None)[0];
            var match=Regex.Match(active,@"(?ms)^\s*Session\s+"+id+@":(.*?)(?=^\s*Session\s+\d+:|\z)");
            if(!match.Success)throw new Exception("Android's installation session disappeared while staging files.");
            string section=match.Groups[1].Value;var value=Regex.Match(section,@"\bmClientProgress=([0-9]+(?:\.[0-9]+)?)");var bridges=Regex.Match(section,@"\bmBridges=(\d+)");var fds=Regex.Match(section,@"\bmFds=(\d+)");double progress;
            if(value.Success&&bridges.Success&&fds.Success&&double.TryParse(value.Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out progress)&&progress+0.00001>=expected&&bridges.Groups[1].Value=="0"&&fds.Groups[1].Value=="0")return;
            Thread.Sleep(250);
        }
        throw new Exception("Android did not finish staging an installation file in time.");
    }
    public void InstallSession(Phone p,IList<string> apks,int baseIndex,string appName) {
        long bytes=apks.Sum(x=>new FileInfo(x).Length);string create=Shell(p,"cmd package install-create -r -S "+bytes.ToString(CultureInfo.InvariantCulture));
        var match=Regex.Match(create,@"Success: created install session \[(\d+)\]");if(!match.Success)throw new Exception("Android did not create an install session: "+create);
        int id=int.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture);bool commitStarted=false;var cancellation=new CancellationTokenSource();int lastReported=-1;object progressLock=new object();
        Action<double> report=value=>{int percent=(int)Math.Round(value*100);lock(progressLock){if(percent>lastReported){lastReported=percent;Log("Android reports installation progress: "+percent+" percent.");}}};
        var monitor=Task.Run(async()=>{while(!cancellation.IsCancellationRequested){try{var progress=SessionProgress(Shell(p,"dumpsys package installs"),id);if(progress.HasValue)report(progress.Value);}catch{}try{await Task.Delay(2000,cancellation.Token);}catch(TaskCanceledException){break;}}});
        try {
            var order=Enumerable.Range(0,apks.Count).OrderBy(i=>i==baseIndex?0:1).ToList();
            long written=0;for(int i=0;i<order.Count;i++){Log("Sending installation file "+(i+1)+" of "+order.Count+" to Android.");string file=apks[order[i]];WriteSession(p,id,file,i==0?"base.apk":"split"+i+".apk");written+=new FileInfo(file).Length;WaitForStaging(p,id,written,bytes);try{var progress=SessionProgress(Shell(p,"dumpsys package installs"),id);if(progress.HasValue)report(progress.Value);}catch{}}
            Log("Android is installing "+appName+". Its reported progress may pause during optimization.");commitStarted=true;
            var commit=Run(Path.Combine(Tools,"adb.exe"),new[]{"-s",p.Serial,"shell","cmd package install-commit "+id},600000,false);
            if(commit.Code!=0||!Regex.IsMatch(commit.Text,@"(?m)^Success\r?$")){try{Shell(p,"cmd package install-abandon "+id);}catch{}throw new Exception("Android did not confirm installation: "+commit.Text);}
        } catch {
            if(!commitStarted){try{Shell(p,"cmd package install-abandon "+id);}catch{}}
            throw;
        } finally {cancellation.Cancel();try{monitor.Wait(3000);}catch{}cancellation.Dispose();}
    }
    public Phone Detect() {
        var rows=Regex.Matches(Adb(null,"devices").Text,@"(?m)^([^\s]+)\s+(device|recovery|unauthorized|offline|sideload)\s*$").Cast<Match>().ToList();
        if(rows.Count==0)throw new Exception("No phone detected. First-time setup: connect a USB data cable. On the phone, open Settings, About phone, then Service menu. Wait for the Service menu to start. Then, in this Windows app, choose Prepare USB. The app will restart the phone. Do not select Factory reset.");
        if(rows.Count!=1)throw new Exception("More than one Android device is connected. Disconnect the others first.");
        var p=new Phone{Serial=rows[0].Groups[1].Value,State=rows[0].Groups[2].Value};
        if(!Regex.IsMatch(p.Serial,@"^[A-Za-z0-9_.:-]+$"))throw new Exception("Unsupported device identifier.");
        if(p.State=="unauthorized")throw new Exception("Unlock the phone and accept its USB debugging authorization prompt, then check again.");
        if(p.State!="device"&&p.State!="recovery")throw new Exception("The phone is "+p.State+". Reconnect it in normal mode or Service menu.");
        return p;
    }
    public void Normal(Phone p) {
        if(p.State!="device")throw new Exception("The phone is in Service menu. Choose Prepare USB connection first, or restart normally if already prepared.");
        if(Shell(p,"getprop sys.boot_completed")!="1")throw new Exception("The phone is still starting. Wait a moment, then check again.");
        if(!int.TryParse(Shell(p,"getprop ro.build.version.sdk"),out p.Sdk))throw new Exception("Cannot read Android version.");
        if(!Shell(p,"pm path com.matapo.blindshell").Contains("package:"))throw new Exception("BlindShell software was not detected. This tool is intended for BlindShell phones.");
    }
    public List<AppInfo> Scan(Phone p) {
        Normal(p);Log("Reading installed user apps and their version numbers.");
        var names=Shell(p,"pm list packages -3").Split('\n').Where(x=>x.Trim().StartsWith("package:")).Select(x=>x.Trim().Substring(8)).Where(Data.PackageName).Distinct().ToList();
        var all=new List<AppInfo>();
        foreach(var name in names) {if(name=="org.example.bshelper")continue;var dump=Shell(p,"dumpsys package "+name);var v=Regex.Match(dump,@"versionCode=(\d+)");var n=Regex.Match(dump,@"versionName=([^\r\n]+)");if(!v.Success)continue;
            all.Add(new AppInfo{Package=name,Name=Friendly(name),Version=n.Success?n.Groups[1].Value:"Unknown",Code=long.Parse(v.Groups[1].Value),Status=name.StartsWith("com.matapo.")||name.StartsWith("meta.")?"Use BlindShell catalog":"Not checked"});
        }return all.OrderBy(x=>x.Package=="com.facebook.stella"?0:1).ThenBy(x=>x.Name).ToList();
    }
    public static string Friendly(string p) { var d=new Dictionary<string,string>{{"com.facebook.stella","Meta AI"},{"com.facebook.katana","Facebook"},{"com.whatsapp","WhatsApp"},{"com.facebook.orca","Messenger"},{"com.instagram.android","Instagram"},{"org.fdroid.fdroid","F-Droid"},{"org.mozilla.firefox","Firefox"},{"gov.loc.nls.dtb","BARD Mobile"}};return d.ContainsKey(p)?d[p]:p; }
    public void Helper(Phone p) {
        string apk=Path.Combine(Tools,"aurora-helper.apk");
        if(!File.Exists(apk))throw new Exception("The Aurora helper is missing from this build.");
        var result=Run(Path.Combine(Tools,"adb.exe"),new[]{"-s",p.Serial,"install","-r",apk},120000);
        if(!result.Text.Contains("Success"))throw new Exception("Could not install the update helper: "+result.Text);
    }
    public void AuroraCheck(Phone p,AppInfo a) {
        Normal(p);if(a.Package.StartsWith("com.matapo.")||a.Package.StartsWith("meta.")){a.Status="Use BlindShell catalog";return;}
        try {
            Helper(p);string result=Shell(p,"am broadcast -a org.example.bshelper.CHECK -n org.example.bshelper/.Receiver --es package "+a.Package);
            var match=Regex.Match(result,@"data=""(\{.*\})""",RegexOptions.Singleline);if(!match.Success)throw new Exception("Aurora did not return a version.");
            var obj=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(match.Groups[1].Value);
            if(Convert.ToString(obj["package"])!=a.Package)throw new Exception("Unexpected package returned.");
            a.OnlineCode=Convert.ToInt64(obj["code"]);a.Available=Convert.ToString(obj["version"]);a.AutoDownload=a.OnlineCode>a.Code;
            a.Url="aurora";a.Status=a.AutoDownload?"Update available from Google Play":"No newer version from Google Play";
        } catch(Exception ex) {a.AutoDownload=false;a.Url="";a.Status="Could not check: "+ex.Message;}
    }
    public object[] Browse(Phone p,string operation,string query) {
        Normal(p);Helper(p);if(query.Length>120)throw new Exception("Search text is too long.");
        string safe="'"+query.Replace("'","'\\''")+"'";
        Shell(p,"am broadcast -a org.example.bshelper.CHECK -n org.example.bshelper/.Receiver --es operation "+operation+" --es query "+safe);
        string json=Shell(p,"cat /sdcard/Android/data/org.example.bshelper/files/status.json");
        var result=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(json);
        if(Convert.ToString(result["state"])!="done")throw new Exception("Browse failed: "+(result.ContainsKey("error")?result["error"]:"No response"));
        return (object[])result["items"];
    }
    public string AuroraDownload(Phone p,AppInfo app) {
        Normal(p);Helper(p);Log("Downloading "+app.Name+" on the phone. Keep it connected and online.");
        Shell(p,"am broadcast -a org.example.bshelper.CHECK -n org.example.bshelper/.Receiver --es package "+app.Package+" --es operation download");
        string statusPath="/sdcard/Android/data/org.example.bshelper/files/status.json";Dictionary<string,object> data=null;
        int lastPercent=-1;for(int i=0;i<180;i++) {Thread.Sleep(2000);string s=Shell(p,"cat "+statusPath);data=new JavaScriptSerializer().DeserializeObject(s) as Dictionary<string,object>;
            if(data!=null && Convert.ToString(data["state"])=="error")throw new Exception("Download failed: "+Convert.ToString(data["error"]));
            if(data!=null && Convert.ToString(data["state"])=="working" && data.ContainsKey("completed") && data.ContainsKey("total")){long completed=Convert.ToInt64(data["completed"]),total=Convert.ToInt64(data["total"]);if(total>0){int percent=(int)Math.Min(99,completed*100/total);if(percent!=lastPercent){lastPercent=percent;Log("Downloading "+app.Name+": "+percent+" percent.");}}else Log("Downloading "+app.Name+": "+(completed/1048576)+" megabytes received.");}
            if(data!=null && Convert.ToString(data["state"])=="done")break; data=null;
        }
        if(data==null)throw new Exception("Download timed out. Check the phone's internet connection.");
        if(Convert.ToString(data["package"])!=app.Package || Convert.ToInt64(data["code"])<=app.Code)throw new Exception("Unexpected download version.");
        var files=(object[])data["files"];if(files.Length==0||files.Length>50)throw new Exception("Unexpected APK count.");
        string dir=Path.Combine(Session,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        for(int i=0;i<files.Length;i++){string path=Convert.ToString(files[i]);if(path!="/storage/emulated/0/Android/data/org.example.bshelper/files/download/part"+i+".apk")throw new Exception("Unexpected download path.");Log("Copying downloaded file "+(i+1)+" of "+files.Length+" to this computer. Press Alt+A to read status.");Pull(p,path,Path.Combine(dir,"part"+i+".apk"));}
        Log("Download complete. Preparing and verifying the app files; keep the phone connected.");string bundle=Path.Combine(Session,Guid.NewGuid().ToString("N")+".apkm");ZipFile.CreateFromDirectory(dir,bundle);return bundle;
    }
    void Pull(Phone p,string remote,string local) { Adb(p,"pull",remote,local); }
    string Mount(Phone p,string name) {
        string mounts=Shell(p,"cat /proc/mounts");
        if(mounts.Split('\n').Any(x=>x.Split(' ').Length>1 && x.Split(' ')[1]=="/"+name))throw new Exception("The "+name+" partition is already mounted. Restart Service menu before preparing USB.");
        Shell(p,"mount -t ext4 -o ro,noload /dev/block/bootdevice/by-name/"+(name=="data"?"userdata":name)+" /"+name);return name;
    }
    public void Prepare(bool restore) {
        Phone p=Detect();if(p.State!="recovery")throw new Exception("Open Settings > About phone > Service menu on the phone first. This operation only runs in recovery mode.");
        if(!Shell(p,"id").Contains("uid=0"))throw new Exception("This recovery does not permit USB preparation.");
        bool system=false,data=false;string dir=Path.Combine(Program.Home,"backups",p.Serial,DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,4));Directory.CreateDirectory(dir);
        try {
            Mount(p,"system");system=true;
            string props=Shell(p,"cat /system/system/build.prop");
            if(!Regex.IsMatch(props,@"(?m)^ro.product.system.model=BS-(03|2|02)\r?$") || !Regex.IsMatch(props,@"(?m)^ro.build.version.sdk=29\r?$"))throw new Exception("This firmware is outside the tested recovery configuration (BlindShell Android 10). No settings were changed.");
            Shell(p,"test -f /system/system/priv-app/BlindShell/BlindShell.apk");
            Mount(p,"data");data=true;Pull(p,Settings,Path.Combine(dir,"settings_global.xml"));Pull(p,Usb,Path.Combine(dir,"persistent_properties"));
            var oldSettings=File.ReadAllBytes(Path.Combine(dir,"settings_global.xml"));var oldProps=File.ReadAllBytes(Path.Combine(dir,"persistent_properties"));
            string value="1",usb="mtp,adb";
            if(restore) {
                string original=Directory.GetDirectories(Path.GetDirectoryName(dir)).Where(x=>File.Exists(Path.Combine(x,"enabled.marker"))).OrderBy(x=>x).FirstOrDefault();
                if(original==null)throw new Exception("No USB-preparation backup for this phone was created by this app. No settings were changed.");
                value=Data.GetAdb(File.ReadAllText(Path.Combine(original,"settings_global.xml")));usb=Data.GetUsb(File.ReadAllBytes(Path.Combine(original,"persistent_properties")));
            }
            byte[] settings=Encoding.UTF8.GetBytes(Data.SetAdb(Encoding.UTF8.GetString(oldSettings),value));byte[] properties=Data.SetUsb(oldProps,usb);
            File.WriteAllBytes(Path.Combine(dir,"settings.new"),settings);File.WriteAllBytes(Path.Combine(dir,"properties.new"),properties);
            File.WriteAllText(Path.Combine(dir,"manifest.txt"),"Serial: "+p.Serial+"\r\nOriginal settings SHA256: "+Data.Sha(oldSettings)+"\r\nOriginal properties SHA256: "+Data.Sha(oldProps));
            Adb(p,"push",Path.Combine(dir,"settings.new"),"/tmp/bsup-settings");Adb(p,"push",Path.Combine(dir,"properties.new"),"/tmp/bsup-properties");
            // Stage a rollback copy in recovery RAM before either write. Keep existing owners and SELinux labels.
            Shell(p,"cp "+Settings+" /tmp/bsup-settings-before && cp "+Usb+" /tmp/bsup-properties-before");
            Log("Backups saved. Applying two USB settings; firmware and app data are unchanged.");
            bool wrote=false;
            try {
                Shell(p,"mount -o remount,rw /data");wrote=true;
                Shell(p,"cat /tmp/bsup-properties > "+Usb+" && cat /tmp/bsup-settings > "+Settings+" && sync");
                Pull(p,Settings,Path.Combine(dir,"settings.verified"));Pull(p,Usb,Path.Combine(dir,"properties.verified"));
                if(!settings.SequenceEqual(File.ReadAllBytes(Path.Combine(dir,"settings.verified"))) || !properties.SequenceEqual(File.ReadAllBytes(Path.Combine(dir,"properties.verified"))))throw new Exception("Settings verification failed.");
                File.WriteAllText(Path.Combine(dir,restore?"restored.marker":"enabled.marker"),"Verified");
            } catch(Exception failure) {
                if(wrote) {try{Shell(p,"cat /tmp/bsup-properties-before > "+Usb+" && cat /tmp/bsup-settings-before > "+Settings+" && sync");}catch{throw new Exception("Connection lost during preparation. Re-enter Service menu and use the backups in "+dir+". Original error: "+failure.Message);}}
                throw;
            } finally { if(wrote)Shell(p,"mount -o remount,ro /data"); }
        } finally {
            if(data) {try{Shell(p,"umount /data");}catch(Exception e){Log("Could not unmount data: "+e.Message);}}
            if(system) {try{Shell(p,"umount /system");}catch(Exception e){Log("Could not unmount system: "+e.Message);}}
        }
        Adb(p,"reboot");Log(restore?"Original USB settings restored. Phone restarting.":"USB preparation complete. Phone restarting. Wait for it to finish, then choose Read phone.");
    }
    public List<string> Certificates(string apk,int sdk) {
        string text=Run(Path.Combine(Tools,"java","bin","java.exe"),new[]{"-jar",Path.Combine(Tools,"apksigner.jar"),"verify","--min-sdk-version",sdk.ToString(),"--max-sdk-version",sdk.ToString(),"--print-certs",apk}).Text;
        var certs=Regex.Matches(text,@"(?m)^Signer[^\r\n]*certificate SHA-256 digest: ([a-fA-F0-9]{64})\r?$").Cast<Match>().Select(x=>x.Groups[1].Value.ToLowerInvariant()).Distinct().OrderBy(x=>x).ToList();
        if(certs.Count==0)throw new Exception("No verified APK signing certificate was found.");return certs;
    }
    ApkInfo Info(string file) { return Data.Badging(Run(Path.Combine(Tools,"aapt.exe"),new[]{"dump","badging",file}).Text); }
    public string Install(Phone p,AppInfo app,string file) {
        Normal(p);if(!Data.PackageName(app.Package))throw new Exception("Invalid application identifier.");
        if(app.Package.StartsWith("com.matapo.") || app.Package.StartsWith("meta."))throw new Exception("Update BlindShell's own apps and wrappers through its catalog.");
        string work=Path.Combine(Session,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        var apks=Data.Unpack(file,Path.Combine(work,"candidate"));var infos=apks.Select(Info).ToList();
        if(infos.Count(x=>x.Split=="")!=1)throw new Exception("Choose a bundle containing exactly one base APK.");
        var main=infos.Single(x=>x.Split=="");if(main.Package!=app.Package)throw new Exception("This file is for "+main.Package+", not the selected app.");
        if(infos.Any(x=>x.Package!=main.Package || x.Code!=main.Code) || infos.Select(x=>x.Split).Distinct().Count()!=infos.Count)throw new Exception("The bundle contains mismatched APKs.");
        string dump=Run(Path.Combine(Tools,"adb.exe"),new[]{"-s",p.Serial,"shell","dumpsys package "+app.Package},60000,false).Text;var vm=Regex.Match(dump,@"versionCode=(\d+)");if(!vm.Success&&!app.IsNew)throw new Exception("The selected app is no longer installed.");
        if(vm.Success&&main.Code<=long.Parse(vm.Groups[1].Value))throw new Exception("This package is not newer than the installed app. Nothing was changed.");
        if(main.MinSdk>p.Sdk)throw new Exception("This update requires a newer Android version than the phone has.");
        var paths=Run(Path.Combine(Tools,"adb.exe"),new[]{"-s",p.Serial,"shell","pm path "+app.Package},60000,false).Text.Split('\n').Where(x=>x.StartsWith("package:")).Select(x=>x.Substring(8).Trim()).ToList();
        string installed=paths.FirstOrDefault(x=>x.EndsWith("/base.apk"));if(installed==null&&!app.IsNew)throw new Exception("Cannot locate the installed app for signature verification.");
        List<string> expected;
        if(installed!=null){Log("Verifying the update against the app already on this phone.");string current=Path.Combine(work,"installed-base.apk");Pull(p,installed,current);expected=Certificates(current,p.Sdk);}
        else{Log("Verifying signatures across the new app's downloaded files.");expected=Certificates(apks[infos.FindIndex(x=>x.Split=="")],p.Sdk);}
        foreach(var a in apks)if(!expected.SequenceEqual(Certificates(a,p.Sdk)))throw new Exception("The update's signing certificate does not match the installed app. Installation was blocked.");
        Log("Signatures verified. Preparing Android installation of "+app.Name+" "+main.Version+".");
        InstallSession(p,apks,infos.FindIndex(x=>x.Split==""),app.Name);
        string after=Shell(p,"dumpsys package "+app.Package);if(!Regex.IsMatch(after,@"versionCode="+main.Code+@"\b"))throw new Exception("Installation finished but version verification failed. Read the phone again.");
        string outcome=app.Name+(installed==null?" installed, version ":" updated to ")+main.Version+(installed==null?".":". Existing app data was kept.");
        if(installed==null){try{EnsureMenuEntry(p,app.Package,app.Name);outcome+=" A BlindShell third-party menu entry was added; reopen the phone's app menu.";}catch(Exception ex){outcome+=" The app installed, but its BlindShell menu entry could not be created: "+ex.Message;}}
        return outcome;
    }
    public void EnsureMenuEntry(Phone p,string package,string label) {
        Normal(p);if(!Data.PackageName(package)||package.StartsWith("meta."))throw new Exception("Invalid app package.");
        var existing=Run(Path.Combine(Tools,"adb.exe"),new[]{"-s",p.Serial,"shell","pm path meta."+package},60000,false);
        if(existing.Code==0&&existing.Text.Contains("package:")){Log("A BlindShell menu entry already exists.");return;}
        var target=Run(Path.Combine(Tools,"adb.exe"),new[]{"-s",p.Serial,"shell","pm path "+package},60000,false);
        if(target.Code!=0||!target.Text.Contains("package:"))throw new Exception("The app is not installed on this phone.");
        if(string.IsNullOrWhiteSpace(label))label=package; if(label.Length>120)label=label.Substring(0,120);
        string escaped=System.Security.SecurityElement.Escape(label),dir=Path.Combine(Session,"menu-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(dir,"res","values"));
        File.WriteAllText(Path.Combine(dir,"AndroidManifest.xml"),"<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" package=\"meta."+package+"\" android:versionCode=\"1\" android:versionName=\"1.0\"><uses-sdk android:minSdkVersion=\"29\" android:targetSdkVersion=\"29\"/><application android:label=\"@string/app_name\" android:hasCode=\"false\"><meta-data android:name=\"real-package\" android:value=\""+package+"\"/><meta-data android:name=\"label\" android:resource=\"@string/app_name\"/><meta-data android:name=\"synonyms\" android:resource=\"@array/synonyms\"/><meta-data android:name=\"group\" android:value=\"THIRDPARTY\"/><meta-data android:name=\"from-aurora\" android:value=\"true\"/></application></manifest>",Encoding.UTF8);
        File.WriteAllText(Path.Combine(dir,"res","values","strings.xml"),"<resources><string name=\"app_name\">"+escaped+"</string><string-array name=\"synonyms\"><item>"+escaped+"</item></string-array></resources>",Encoding.UTF8);
        string unsigned=Path.Combine(dir,"unsigned.apk"),signed=Path.Combine(dir,"menu.apk");Log("Creating the BlindShell menu entry for "+label+".");
        Run(Path.Combine(Tools,"aapt.exe"),new[]{"package","-f","-M",Path.Combine(dir,"AndroidManifest.xml"),"-S",Path.Combine(dir,"res"),"-I",Path.Combine(Tools,"android.jar"),"-F",unsigned});
        Run(Path.Combine(Tools,"java","bin","java.exe"),new[]{"-jar",Path.Combine(Tools,"apksigner.jar"),"sign","--ks",Path.Combine(Tools,"wrapper-menu-key.p12"),"--ks-key-alias","blindshell-menu","--ks-pass","pass:menuentry","--key-pass","pass:menuentry","--out",signed,unsigned});
        var result=Run(Path.Combine(Tools,"adb.exe"),new[]{"-s",p.Serial,"install",signed},120000);if(!result.Text.Contains("Success"))throw new Exception("Android did not confirm menu entry installation.");
    }
}

class StatusBox:TextBox { public void Announce() { AccessibilityNotifyClients(AccessibleEvents.ValueChange,-1); } }
static class ScreenReader {
    [DllImport("UIAutomationCore.dll",CharSet=CharSet.Unicode,PreserveSig=true)] static extern int UiaRaiseNotificationEvent(IRawElementProviderSimple provider,int kind,int processing,string message,string activityId);
    static object jaws;
    public static void Announce(Control control,string message) {
        try {
            if(Process.GetProcessesByName("jfw").Length>0){
                if(jaws==null){var type=Type.GetTypeFromProgID("FreedomSci.JAWSAPI",false);if(type!=null)jaws=Activator.CreateInstance(type);}
                if(jaws!=null){var result=jaws.GetType().InvokeMember("SayString",BindingFlags.InvokeMethod,null,jaws,new object[]{message,true});if(result is bool && (bool)result)return;}
            }
        }catch{jaws=null;}
        try {var provider=AutomationInteropProvider.HostProviderFromHandle(control.Handle);if(provider!=null)UiaRaiseNotificationEvent(provider,2,3,message,"BlindShellUpdater.Progress");}catch{}
    }
}
class BrowseForm:Form {
    Engine engine;Phone phone;TextBox search=new TextBox();StatusBox status=new StatusBox();ComboBox categories=new ComboBox();ListView results=new ListView();List<Button> buttons=new List<Button>();FlowLayoutPanel categoryPanel=new FlowLayoutPanel();Button categoryToggle;bool busy,announcingProgress;int lastSpokenBucket=-1;string lastSpokenPhase="",lastProgressMessage="",lastProgressKind="";DateTime progressStarted;System.Windows.Forms.Timer progressTimer=new System.Windows.Forms.Timer();
    public BrowseForm(){engine=new Engine(Say);Text="BlindShell App Updater - install progress enabled";ClientSize=new Size(1000,650);MinimumSize=new Size(800,550);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Segoe UI",11);AutoScaleMode=AutoScaleMode.Dpi;
        var grid=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(16)};Controls.Add(grid);grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));grid.RowStyles.Add(new RowStyle(SizeType.Percent,100));grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));grid.RowStyles.Add(new RowStyle(SizeType.Absolute,70));grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(new Label{Text="BlindShell App Updater\r\nConnect your phone. For a first connection, choose First-time setup. Press F1 for the readme.",AutoSize=true},0,0);
        var row=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,WrapContents=true};grid.Controls.Add(row,0,1);
        row.Controls.Add(new Label{Text="Search apps:",AutoSize=true,Margin=new Padding(3,11,3,3)});search.Width=260;search.AccessibleName="Search apps";row.Controls.Add(search);Button(row,"&Search",async()=>await LoadResults("search",search.Text.Trim()));Button(row,"&Installed apps",async()=>await Installed(false));Button(row,"Chec&k updates",async()=>await Installed(true));
        categoryToggle=new Button{Text="Show or hide categories",AccessibleName="Show or hide categories",AutoSize=true,MinimumSize=new Size(90,38)};row.Controls.Add(categoryToggle);buttons.Add(categoryToggle);categoryToggle.Click+=(sender,eventArgs)=>{categoryPanel.Visible=!categoryPanel.Visible;if(categoryPanel.Visible){Say("Categories shown. Use Refresh categories to load them, then choose a category and press Enter.");categories.Focus();}else Say("Categories hidden.");};
        categoryPanel.AutoSize=true;categoryPanel.WrapContents=false;row.Controls.Add(categoryPanel);categoryPanel.Controls.Add(new Label{Text="Category:",AutoSize=true,Margin=new Padding(3,11,3,3)});categories.Width=210;categories.DropDownStyle=ComboBoxStyle.DropDownList;categories.AccessibleName="App category";categories.AccessibleDescription="Choose a category with the arrow keys and press Enter to load its apps.";categoryPanel.Controls.Add(categories);Button(categoryPanel,"Refresh &categories",async()=>await Categories(true));Button(categoryPanel,"Show ca&tegory",async()=>await ShowCategory());
        results.View=View.Details;results.FullRowSelect=true;results.MultiSelect=false;results.HideSelection=false;results.Dock=DockStyle.Fill;results.AccessibleName="Apps and search results";results.AccessibleDescription="Use arrow keys to select an app, then press Alt D for details or Alt I to install.";results.Columns.Add("App",280);results.Columns.Add("Installed version or developer",250);results.Columns.Add("Available version or price",180);results.Columns.Add("Status",250);grid.Controls.Add(results,0,2);
        var actions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};grid.Controls.Add(actions,0,3);Button(actions,"&BlindShell-related apps",async()=>await Related());Button(actions,"App &details",async()=>{var a=Selected();if(a!=null)MessageBox.Show(this,"App: "+a.Name+"\r\nDeveloper: "+a.Developer+"\r\nPrice: "+a.Price+"\r\nPackage: "+a.Package+"\r\n"+a.Status+"\r\n\r\nAccessibility and compatibility with BlindShell have not been verified.","App details");await Task.CompletedTask;});Button(actions,"&Install selected app",async()=>await Install());Button(actions,"Add selected app to phone menu",async()=>await AddMenuEntry());
        status.Multiline=true;status.ReadOnly=true;status.Dock=DockStyle.Fill;status.AccessibleName="Browse status";status.Text="Search for an app, load categories, or choose BlindShell-related apps.";grid.Controls.Add(status,0,4);
        var footer=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,WrapContents=true};grid.Controls.Add(footer,0,5);Button(footer,"Read st&atus",async()=>{status.Focus();status.SelectAll();await Task.CompletedTask;});Button(footer,"&First-time setup",async()=>{ShowSetup();await Task.CompletedTask;});Button(footer,"Prepare &USB",async()=>await PrepareUsb(false));Button(footer,"Install &helper",async()=>await Work(async()=>{await Connect();Say("Android helper is installed.");}));Button(footer,"Restore USB settings",async()=>await PrepareUsb(true));Button(footer,"Install downloaded file",async()=>await InstallFile());Button(footer,"&Readme (F1)",async()=>{ShowReadme();await Task.CompletedTask;});Button(footer,"C&lose",async()=>{Close();await Task.CompletedTask;});
        progressTimer.Interval=20000;progressTimer.Tick+=(sender,args)=>{if(announcingProgress&&!string.IsNullOrEmpty(lastProgressMessage)){int seconds=(int)(DateTime.UtcNow-progressStarted).TotalSeconds;ScreenReader.Announce(status,"Still working. Elapsed time "+(seconds/60)+" minutes "+(seconds%60)+" seconds. "+lastProgressMessage);}};
        Shown+=async(s,a)=>{bool ready=false;await Work(async()=>{await Task.Run(()=>engine.Init());Say("Ready. Connect the phone, then search, choose a category, or select Installed apps.");ready=true;});if(ready)ScreenReader.Announce(status,"Latest BlindShell updater opened. Installation progress announcements are enabled.");try{phone=await Task.Run(()=>engine.Detect());if(phone.State=="device")await Categories(false);}catch{}};
        FormClosing+=(s,a)=>{if(busy){a.Cancel=true;MessageBox.Show(this,"Please wait for the current operation to finish.");}};
    }
    class CategoryItem {public string Name,Url;public override string ToString(){return Name;}}
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData){
        if(keyData==Keys.F1){ShowReadme();return true;}
        if(keyData==Keys.Enter && categories.Focused && !busy){
            if(categories.DroppedDown)categories.DroppedDown=false;
            BeginInvoke(new Action(async()=>await ShowCategory()));return true;
        }
        if(keyData==Keys.Enter && search.Focused && !busy){BeginInvoke(new Action(async()=>await LoadResults("search",search.Text.Trim())));return true;}
        return base.ProcessCmdKey(ref msg,keyData);
    }
    void ShowReadme(){using(var f=new Form{Text="BlindShell App Updater readme",StartPosition=FormStartPosition.CenterParent,Size=new Size(820,650),MinimumSize=new Size(500,400),Font=Font,KeyPreview=true}){var box=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=true,Dock=DockStyle.Fill,AccessibleName="Readme and instructions"};using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("readme.txt"))using(var reader=new StreamReader(stream,Encoding.UTF8))box.Text=reader.ReadToEnd();f.Controls.Add(box);f.Shown+=(sender,args)=>{box.Focus();box.SelectionStart=0;};f.KeyDown+=(sender,args)=>{if(args.KeyCode==Keys.Escape)f.Close();};f.ShowDialog(this);}}
    void ShowSetup(){MessageBox.Show(this,"First-time setup\r\n\r\n1. Connect the phone to this computer with a USB data cable.\r\n\r\n2. On the phone, open Settings, then About phone, then Service menu. Wait for the Service menu to finish starting.\r\n\r\n3. Return to this Windows app and select Prepare USB. The app backs up the USB settings, enables USB debugging, and restarts the phone. Keep the cable connected.\r\n\r\n4. Wait for the phone's normal menu to return. Then use Search, Installed apps, or Check updates in this Windows app. The helper installs automatically.\r\n\r\nLeave USB debugging enabled for future use. Do not choose Factory reset on the phone.","First-time setup",MessageBoxButtons.OK,MessageBoxIcon.Information);}
    async Task ShowCategory(){var c=categories.SelectedItem as CategoryItem;if(c==null){Say("Categories are not loaded. Choose Refresh categories first.");return;}await LoadResults("category",c.Url);}
    void Button(Control c,string name,Func<Task> f){var b=new Button{Text=name,AccessibleName=name.Replace("&",""),AutoSize=true,MinimumSize=new Size(90,38)};c.Controls.Add(b);if(name!="Read st&atus")buttons.Add(b);b.Click+=async(s,e)=>{try{await f();}catch(Exception x){Say(x.Message);MessageBox.Show(this,x.Message,"Action needed");}};}
    void Say(string s){if(IsDisposed)return;if(InvokeRequired){BeginInvoke(new Action<string>(Say),s);return;}status.Text=s;status.Announce();if(announcingProgress){lastProgressMessage=s;var m=Regex.Match(s,@"(\d+) percent");var mb=Regex.Match(s,@"(\d+) megabytes received");if(m.Success||mb.Success){string kind=s.StartsWith("Android reports")?"install":"download";if(kind!=lastProgressKind){lastProgressKind=kind;lastSpokenBucket=-1;}int bucket=m.Success?int.Parse(m.Groups[1].Value)/10:int.Parse(mb.Groups[1].Value)/10;if(bucket!=lastSpokenBucket){lastSpokenBucket=bucket;ScreenReader.Announce(status,s);}}else if(s!=lastSpokenPhase){lastSpokenPhase=s;ScreenReader.Announce(status,s);}}}
    void StartProgress(){announcingProgress=true;lastSpokenBucket=-1;lastSpokenPhase="";lastProgressKind="";lastProgressMessage="";progressStarted=DateTime.UtcNow;progressTimer.Start();}
    void EndProgress(){progressTimer.Stop();announcingProgress=false;lastProgressMessage="";}
    async Task Work(Func<Task> f){if(busy)return;busy=true;foreach(var b in buttons)b.Enabled=false;try{await f();}catch(Exception e){Say(e.Message);MessageBox.Show(this,e.Message,"Action needed");}finally{busy=false;foreach(var b in buttons)b.Enabled=true;}}
    async Task Connect(){phone=await Task.Run(()=>engine.Detect());await Task.Run(()=>engine.Normal(phone));await Task.Run(()=>engine.Helper(phone));}
    async Task AddMenuEntry(){var a=Selected();if(a==null)return;await Work(async()=>{await Connect();await Task.Run(()=>engine.EnsureMenuEntry(phone,a.Package,a.Name));Say("Menu entry ready for "+a.Name+". Reopen the third-party apps menu on the phone.");MessageBox.Show(this,"Menu entry ready. Reopen the third-party apps menu on the phone.","Phone menu");});}
    async Task PrepareUsb(bool restore){if(MessageBox.Show(this,restore?"On the phone, open Settings, About phone, then Service menu. Restore the original USB settings and restart?":"Connect the phone with a USB data cable. On the phone, open Settings, About phone, then Service menu. Wait for Service menu to start.\r\n\r\nThen select Yes here to back up USB settings, enable USB debugging, and restart the phone. Wait for its normal menu before using this app. Do not choose Factory reset.",restore?"Restore USB settings":"Prepare USB",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;await Work(async()=>{await Task.Run(()=>engine.Prepare(restore));phone=null;Say("Phone restarting. Wait until its normal menu returns, then use Search, Installed apps, or Check updates.");});}
    async Task InstallFile(){var a=Selected();if(a==null)return;using(var dialog=new OpenFileDialog{Title="Choose update for "+a.Name,Filter="Android app files (*.apk;*.apkm)|*.apk;*.apkm"}){if(dialog.ShowDialog(this)!=DialogResult.OK)return;string file=dialog.FileName;StartProgress();try{await Work(async()=>{Say("Preparing to install "+a.Name+".");await Connect();var outcome=await Task.Run(()=>engine.Install(phone,a,file));Say(outcome);MessageBox.Show(this,outcome,"Installation complete");});}finally{EndProgress();}}}
    async Task Installed(bool check){await Work(async()=>{await Connect();Say("Reading apps installed on the phone…");var apps=await Task.Run(()=>engine.Scan(phone));if(check){foreach(var a in apps){Say("Checking "+a.Name+"…");await Task.Run(()=>engine.AuroraCheck(phone,a));}}results.Items.Clear();foreach(var a in apps){var item=new ListViewItem(new[]{a.Name,"Installed: "+a.Version,a.Available,a.Status}){Tag=a};results.Items.Add(item);}Say(results.Items.Count+" installed apps. Use arrow keys to select an app, then choose App details or Install selected app.");if(results.Items.Count>0){results.Items[0].Selected=true;results.Items[0].Focused=true;}results.Focus();});}
    async Task Categories(bool focus){await Work(async()=>{await Connect();Say("Loading categories from Google Play...");var data=await Task.Run(()=>engine.Browse(phone,"categories",""));var current=categories.SelectedItem as CategoryItem;string previous=current==null?null:current.Url;var items=new List<CategoryItem>();foreach(Dictionary<string,object> d in data)items.Add(new CategoryItem{Name=Convert.ToString(d["name"]),Url=Convert.ToString(d["url"])});if(items.Count==0)throw new Exception("No categories were returned. Check the phone's internet connection and try Refresh categories.");categories.BeginUpdate();categories.Items.Clear();categories.Items.AddRange(items.ToArray());int selected=items.FindIndex(x=>x.Url==previous);categories.SelectedIndex=selected>=0?selected:0;categories.EndUpdate();Say(categories.Items.Count+" categories loaded. Tab to App category, choose one with the arrow keys, and press Enter to load its apps.");if(focus)categories.Focus();});}
    async Task LoadResults(string op,string q){if(string.IsNullOrWhiteSpace(q)){Say("Enter a search term first.");search.Focus();return;}await Work(async()=>{await Connect();Say("Finding apps. Please wait...");var data=await Task.Run(()=>engine.Browse(phone,op,q));if(op=="search")categoryPanel.Visible=false;ShowResults(data,op=="category"?"Category results":"Search results");});}
    void ShowResults(object[] data,string source){results.Items.Clear();foreach(Dictionary<string,object> d in data){string pkg=Convert.ToString(d["package"]);if(!Data.PackageName(pkg))continue;var a=new AppInfo{Package=pkg,Name=Convert.ToString(d["name"]),Developer=Convert.ToString(d["developer"]),Price=Convert.ToString(d["price"]),IsNew=true,Code=0,Status="Compatibility not verified"};if(string.IsNullOrEmpty(a.Name))a.Name=pkg;if(string.IsNullOrEmpty(a.Price))a.Price="Unknown; free apps only";var item=new ListViewItem(new[]{a.Name,a.Developer,a.Price,a.Status}){Tag=a};results.Items.Add(item);}Say(source+": "+results.Items.Count+" apps. Use the arrow keys to select one; app details and install buttons follow the list.");if(results.Items.Count>0){results.Items[0].Selected=true;results.Items[0].Focused=true;}results.Focus();}
    async Task Related(){await Work(async()=>{await Connect();Say("Finding apps mentioned in BlindShell materials...");var combined=new List<object>();foreach(string term in new[]{"Meta AI","Be My Eyes","Aira","ScripTalk"}){try{var data=await Task.Run(()=>engine.Browse(phone,"search",term));if(data.Length>0)combined.Add(data[0]);}catch{}}ShowResults(combined.ToArray(),"Apps mentioned by BlindShell users and materials; not a popularity ranking");});}
    AppInfo Selected(){if(results.SelectedItems.Count==0){Say("Select an app from the results first.");return null;}return (AppInfo)results.SelectedItems[0].Tag;}
    async Task Install(){var a=Selected();if(a==null)return;if(MessageBox.Show(this,"Install "+a.Name+" by "+a.Developer+"?\r\n\r\nThis is a third-party app. Its accessibility and BlindShell compatibility have not been verified. Google Play may not offer this app for your device or region.","Install app",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;StartProgress();try{await Work(async()=>{Say("Preparing to install "+a.Name+".");await Connect();Say("Checking and downloading "+a.Name+". This can take several minutes.");if(!a.IsNew && (!a.AutoDownload || a.Url!="aurora"))throw new Exception("Choose Check updates first; only apps with an available update can be updated.");var file=await Task.Run(()=>engine.AuroraDownload(phone,a));var outcome=await Task.Run(()=>engine.Install(phone,a,file));Say(outcome);MessageBox.Show(this,outcome,"Installation complete");});}finally{EndProgress();}}
}

static class Tests {
    static int n;static void Check(bool ok,string msg){if(!ok)throw new Exception("Test failed: "+msg);n++;}
    public static void Run() {
        string xml="<settings><setting id=\"67\" name=\"adb_enabled\" value=\"0\" defaultValue=\"0\" /><setting name=\"other\" value=\"keep\" /></settings>";
        var enabled=Data.SetAdb(xml,"1");Check(Data.GetAdb(enabled)=="1","enable");Check(Data.SetAdb(enabled,"0")==xml,"restore without other changes");
        var original=Data.Field(1,Data.Field(1,Encoding.UTF8.GetBytes("persist.other")).Concat(Data.Field(2,Encoding.UTF8.GetBytes("keep"))).ToArray());
        var props=Data.SetUsb(original,"mtp,adb");Check(Data.GetUsb(props)=="mtp,adb","new USB property");Check(Data.SetUsb(props,null).SequenceEqual(original),"remove added USB property");Check(Data.GetUsb(Data.SetUsb(props,"mtp"))=="mtp","replace property");Check(Data.SetUsb(props,"mtp,adb").SequenceEqual(props),"idempotence");
        bool bad=false;try{Data.Fields(new byte[]{10,100,1});}catch{bad=true;}Check(bad,"truncated protobuf rejected");bad=false;try{Data.SetAdb("<settings/>","1");}catch{bad=true;}Check(bad,"missing setting rejected");
        Check(Data.PackageName("com.facebook.stella")&&!Data.PackageName("foo;reboot"),"shell-safe package names");
        string dump="Active install sessions:\n  Session 23:\n mProgress=0.42 mCommitted=true\n  Session 24:\n mProgress=0.91\nHistorical install sessions:\n  Session 25:\n mProgress=0.9\n";
        Check(Engine.SessionProgress(dump,23)==0.42 && Engine.SessionProgress(dump,24)==0.91 && !Engine.SessionProgress(dump,25).HasValue,"match only the selected active install session");
        var apk=Data.Badging("package: name='com.facebook.stella' versionCode='969802293' versionName='298.0.0.28.160'\nsdkVersion:'29'\n");Check(apk.Code==969802293&&apk.MinSdk==29,"APK metadata");
        string dir=Path.Combine(Program.Home,"test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        using(var zip=ZipFile.Open(Path.Combine(dir,"bad.apkm"),ZipArchiveMode.Create)){using(var w=new StreamWriter(zip.CreateEntry("../escape.apk").Open()))w.Write("bad");}
        bad=false;try{Data.Unpack(Path.Combine(dir,"bad.apkm"),Path.Combine(dir,"out"));}catch{bad=true;}Check(bad,"ZIP traversal rejected");
        File.WriteAllText(Path.Combine(Program.Home,"self-test.txt"),n+" checks passed.");
    }
}
}
