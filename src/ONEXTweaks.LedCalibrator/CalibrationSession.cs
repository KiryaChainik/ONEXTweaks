using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ONEXTweaks.Calibration
{
    internal sealed class CalibrationSession
    {
        private readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=1048576,RecursionLimit=40};
        private readonly string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ONEXTweaks","dev");
        private BridgeClient bridge;private Dictionary<string,object> original,lastApplied,pendingApplied;
        private readonly string id=Guid.NewGuid().ToString("N");private bool completed,ownerChanged;
        private DateTime deadline,lastArm;private bool arming;
        private readonly bool verification;private string resumedFrom;
        public CalibrationSession(bool isVerification=false){verification=isVerification;}
        public bool Started {get;private set;} public DateTime Deadline {get{return deadline;}}
        public string ResultPath {get;private set;} public bool OriginalRestored {get;private set;}
        public string RestoreMessage {get;private set;}
        public string ResumeMessage {get;private set;}
        public Dictionary<int,int[]> Acknowledged {get;private set;}
        public int ApplyCount {get;private set;} public double LastApplyMilliseconds {get;private set;}
        public readonly List<CaseResult> Results=new List<CaseResult>();
        public int TryResume(CalibrationPlan plan)
        {
            if(verification)return 0;
            try{
                string pointer=Path.Combine(root,"Reports","led-calibration-latest.json");
                if(!File.Exists(pointer))return 0;
                var latest=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(pointer));
                string filename=Convert.ToString(latest["resultFile"]);
                if(Path.GetFileName(filename)!=filename||!filename.StartsWith("visual-",StringComparison.Ordinal)||!filename.EndsWith(".json",StringComparison.Ordinal))return 0;
                var archive=json.Deserialize<CalibrationArchive>(File.ReadAllText(Path.Combine(root,"Reports","LedCalibration",filename)));
                if(archive.schema!=1||archive.kind!="owner_visual_calibration"||(archive.completed&&archive.protocolVersion>=2)||archive.results==null)return 0;
                var restored=new List<CaseResult>();
                foreach(var result in archive.results){
                    var sample=plan.cases.FirstOrDefault(c=>c.id==result.caseId);
                    if(sample==null||!string.Equals(sample.hex,result.referenceSrgb,StringComparison.OrdinalIgnoreCase)||result.hardwareBrightness<1||result.hardwareBrightness>4||result.zones==null||result.zones.Length!=4||result.zones.Select(z=>z.zoneId).Distinct().Count()!=4)throw new InvalidDataException("Calibration cases differ");
                    foreach(var group in CalibrationLayout.Zones)foreach(int zoneId in group.hardwareZoneIds){var zone=result.zones.FirstOrDefault(z=>z.zoneId==zoneId);if(zone==null)throw new InvalidDataException("Calibration zones differ");CalibrationMath.Validate(zone.desiredWire);if(!new[]{"unrated","accepted","provisional","needs_work","skipped"}.Contains(zone.rating))throw new InvalidDataException("Invalid rating");}
                    if(!result.zones.First(z=>z.zoneId==1).desiredWire.SequenceEqual(result.zones.First(z=>z.zoneId==2).desiredWire))throw new InvalidDataException("Stick group differs");
                    restored.RemoveAll(r=>r.caseId==result.caseId&&r.hardwareBrightness==result.hardwareBrightness);restored.Add(result);
                }
                Results.AddRange(restored);resumedFrom=archive.sessionId;return Results.Count;
            }catch(Exception){ResumeMessage="Прошлый файл сохранён, но продолжить его автоматически не удалось.";return 0;}
        }
        private string SnapshotPath {get{return Path.Combine(root,"privateReports","led-primary-session.json");}}
        internal static void Atomic(string path,string contents)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+".tmp";
            File.WriteAllText(temp,contents,new UTF8Encoding(false));
            for(int attempt=0;;attempt++)try{if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);break;}catch(IOException){if(attempt>=5)throw;System.Threading.Thread.Sleep(15);}
        }
        private Dictionary<string,object> Clone(Dictionary<string,object> value){return json.Deserialize<Dictionary<string,object>>(json.Serialize(value));}
        private static IEnumerable<Dictionary<string,object>> Zones(Dictionary<string,object> value){return ((IEnumerable)value["zones"]).Cast<object>().Select(z=>(Dictionary<string,object>)z);}
        private string Semantic(Dictionary<string,object> value)
        {
            var parts=new List<string>{Convert.ToBoolean(value["lightingEnabled"])?"on":"off"};
            foreach(var zone in Zones(value).OrderBy(z=>Convert.ToInt32(z["id"])))foreach(string field in new[]{"id","enabled","brightness","mode","preset","r","g","b","breathing"})parts.Add(field+":"+Convert.ToString(zone[field],System.Globalization.CultureInfo.InvariantCulture));
            return string.Join("|",parts);
        }
        private async Task<Dictionary<string,object>> LightingAsync()
        {
            var reply=await bridge.SendAsync(new Dictionary<string,object>{{"OnexHardwareHomeGet",true}});
            return LightingReply(reply);
        }
        private Dictionary<string,object> LightingReply(Dictionary<string,object> reply)
        {
            var hardware=json.Deserialize<Dictionary<string,object>>((string)reply["OnexHardwareHomeState"]);
            var lighting=(Dictionary<string,object>)hardware["lighting"];
            if(!Convert.ToBoolean(lighting["available"]))throw new IOException("Контроллер подсветки недоступен");
            object transform;if(!lighting.TryGetValue("colorTransform",out transform)||!new[]{"srgb-to-linear","owner-calibrated-v1"}.Contains(Convert.ToString(transform)))throw new IOException("Версия цветового преобразования помощника изменилась. Проверка остановлена.");
            object wire;
            if(lighting.TryGetValue("acknowledgedWireColors",out wire)&&wire!=null){
                Acknowledged=new Dictionary<int,int[]>();
                foreach(Dictionary<string,object> z in (IEnumerable)wire)Acknowledged[Convert.ToInt32(z["id"])]=new[]{Convert.ToInt32(z["r"]),Convert.ToInt32(z["g"]),Convert.ToInt32(z["b"])};
            }
            return new Dictionary<string,object>{{"lightingEnabled",Convert.ToBoolean(lighting["enabled"])},{"zones",lighting["zones"]}};
        }
        private void SaveSnapshot()
        {
            if(original==null)return;
            Atomic(SnapshotPath,json.Serialize(new Dictionary<string,object>{{"schema",1},{"id",id},{"writer","LedCalibrator"},{"completed",completed},{"original",original},{"lastApplied",lastApplied},{"pendingApplied",pendingApplied},{"recoveryDeadlineUtc",deadline.ToString("o")}}));
        }
        private async Task ArmGuardAsync()
        {
            string code=@"$ErrorActionPreference='Stop'
$d=Join-Path $env:LOCALAPPDATA 'ONEXTweaks\dev'
$snapshot=[IO.File]::ReadAllText((Join-Path $d 'privateReports\led-primary-session.json'))|ConvertFrom-Json
$trigger=New-ScheduledTaskTrigger -Once -At ([DateTime]::Parse($snapshot.recoveryDeadlineUtc).ToLocalTime())
$path=Join-Path $d 'tools\compare-led-primary.ps1'
$a=New-ScheduledTaskAction -Execute powershell.exe -Argument ('-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File '+[char]34+$path+[char]34+' -Color Restore -Recovery')
$p=New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
$s=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Minutes 2)
Register-ScheduledTask ONEXTweaks-LedPrimaryRecovery -Action $a -Principal $p -Trigger $trigger -Settings $s -Force|Out-Null";
            await ShellAsync(code);lastArm=DateTime.UtcNow;
        }
        public async Task StartAsync()
        {
            if(Started)throw new InvalidOperationException("Калибровка уже запущена");
            completed=false;ownerChanged=false;
            await ShellAsync(@"$ErrorActionPreference='Stop'
$d=Join-Path $env:LOCALAPPDATA 'ONEXTweaks\dev'
$path=Join-Path $d 'privateReports\led-primary-session.json'
if((Test-Path $path) -and !([IO.File]::ReadAllText($path)|ConvertFrom-Json).completed){
 $p=Start-Process powershell.exe -WindowStyle Hidden -Wait -PassThru -ArgumentList ('-NoProfile -NonInteractive -ExecutionPolicy Bypass -File '+[char]34+(Join-Path $d 'tools\compare-led-primary.ps1')+[char]34+' -Color Restore -KeepPaletteWindow')
 if($p.ExitCode){throw 'Previous lighting session needs attention'}
}
if([Windows.Gaming.UI.GameBar,Windows.Gaming.UI,ContentType=WindowsRuntime]::IsInputRedirected){$signal=[Threading.EventWaitHandle]::OpenExisting('Local\ONEXTweaks.ToggleOverlay.v1');try{$null=$signal.Set();Start-Sleep -Milliseconds 300}finally{$signal.Dispose()}}
Get-Process XboxGamingBar -ErrorAction SilentlyContinue|Where-Object Path -like '*ONEXTweaks.Widget*'|ForEach-Object{Stop-Process -Id $_.Id;$null=$_.WaitForExit(3000)}");
            bridge=new BridgeClient();
            Exception failure=null;
            try{
                await bridge.ConnectAsync();original=await LightingAsync();lastApplied=Clone(original);deadline=DateTime.UtcNow.AddMinutes(15);
                SaveSnapshot();await ArmGuardAsync();Started=true;
                ResultPath=Path.Combine(root,"Reports","LedCalibration",(verification?"qa-":"visual-")+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+id.Substring(0,8)+".json");
                SaveResults(false);
            }catch(Exception error){failure=error;}
            if(failure!=null){bridge.Dispose();Started=false;completed=true;SaveSnapshot();await ShellAsync("Unregister-ScheduledTask ONEXTweaks-LedPrimaryRecovery -Confirm:$false -ErrorAction SilentlyContinue;Start-ScheduledTask ONEXTweaks-Startup");throw failure;}
        }
        public void Touch()
        {if(!Started||completed)return;deadline=DateTime.UtcNow.AddMinutes(15);SaveSnapshot();}
        public async Task ApplyAsync(Dictionary<int,int[]> desired,int brightness)
        {
            var latency=Stopwatch.StartNew();
            if(!Started||completed)throw new IOException("Сессия проверки не активна");
            if(brightness<1||brightness>4)throw new ArgumentOutOfRangeException("brightness");
            var current=await LightingAsync();
            if(Semantic(current)!=Semantic(lastApplied)){
                ownerChanged=true;completed=true;SaveSnapshot();throw new IOException("Подсветка изменена вне калибратора. Новые настройки сохранены, проверка остановлена.");
            }
            Touch();
            if(DateTime.UtcNow-lastArm>TimeSpan.FromSeconds(60)&&!arming){arming=true;try{await ArmGuardAsync();}finally{arming=false;}}
            var next=Clone(current);next["lightingEnabled"]=true;
            foreach(var zone in Zones(next)){
                int zoneId=Convert.ToInt32(zone["id"]);int[] rgb;
                if(!desired.TryGetValue(zoneId,out rgb)){
                    // The hidden connector has not been visually assessed. Preserve its
                    // effective enabled state while temporarily enabling the tested zones.
                    if(!Convert.ToBoolean(current["lightingEnabled"]))zone["enabled"]=false;
                    continue;
                }
                var encoded=CalibrationMath.Encoded(rgb);
                zone["enabled"]=true;zone["mode"]=2;zone["breathing"]=false;zone["brightness"]=brightness;
                zone["r"]=encoded[0];zone["g"]=encoded[1];zone["b"]=encoded[2];
            }
            // Explicit diagnostic bypass prevents calibrating an already-corrected
            // color. This request-only flag is ignored by persisted HomeSettings.
            next["calibrationRaw"]=true;
            pendingApplied=next;SaveSnapshot();
            var reply=await bridge.SendAsync(new Dictionary<string,object>{{"OnexLightingSet",json.Serialize(next)},{"OnexUserInput",true}});
            // Set already returns the authoritative Home result; a second Get would
            // only repeat that reply and delay every drag position.
            var acknowledged=LightingReply(reply);
            if(Semantic(acknowledged)!=Semantic(next))throw new IOException("Помощник подтвердил другое состояние подсветки");
            lastApplied=Clone(next);pendingApplied=null;SaveSnapshot();
            ApplyCount++;LastApplyMilliseconds=latency.Elapsed.TotalMilliseconds;
        }
        public void SaveResult(CaseResult result)
        {Results.RemoveAll(r=>r.caseId==result.caseId&&r.hardwareBrightness==result.hardwareBrightness);Results.Add(result);SaveResults(false);Touch();}
        public void SaveResults(bool finished)
        {
            if(ResultPath==null)return;
            Atomic(ResultPath,json.Serialize(new{schema=1,protocolVersion=2,appVersion="0.3.0.0",sessionId=id,resumedFromSession=resumedFrom,kind=verification?"lifecycle_verification_no_owner_ratings":"owner_visual_calibration",completed=finished,brightnessStages=CalibrationSchedule.Levels,plannedSamples=96,reviewedSamples=Results.Count(CalibrationSchedule.Reviewed),referenceEncoding="sRGB",desiredWireEncoding="OEM RGB bytes before agent transport encoding",adjustmentBlueBasis=CalibrationMath.BlueBalance,logicalZones=CalibrationLayout.Zones,results=Results.ToArray(),originalLightingRestored=OriginalRestored,restoreMessage=RestoreMessage,physicalColorReadback=false,colorimetryMeasured=false,productionProfileInstalled=false,unassessedZones=new[]{7}}));
            if(!verification)Atomic(Path.Combine(root,"Reports","led-calibration-latest.json"),json.Serialize(new{schema=1,protocolVersion=2,sessionId=id,completed=finished,resultFile=Path.GetFileName(ResultPath),savedSamples=Results.Count,ratedSamples=Results.Count(CalibrationSchedule.Reviewed),plannedSamples=96,originalLightingRestored=OriginalRestored,productionProfileInstalled=false}));
        }
        public async Task EndAsync(bool finished)
        {
            if(!Started){RestoreMessage="Подсветка не менялась";return;}
            Exception failure=null;
            try{
                if(!ownerChanged){
                    var current=await LightingAsync();
                    bool known=Semantic(current)==Semantic(lastApplied)||(pendingApplied!=null&&Semantic(current)==Semantic(pendingApplied));
                    if(known){
                        await bridge.SendAsync(new Dictionary<string,object>{{"OnexLightingSet",json.Serialize(original)},{"OnexUserInput",true}});
                        var restored=await LightingAsync();OriginalRestored=Semantic(restored)==Semantic(original);
                        if(!OriginalRestored)throw new IOException("Восстановление подсветки не подтверждено");RestoreMessage="Исходная подсветка восстановлена";
                    }else{ownerChanged=true;RestoreMessage="Сохранены более новые настройки подсветки";}
                }else RestoreMessage="Сохранены более новые настройки подсветки";
                completed=true;SaveSnapshot();bridge.Dispose();
                await ShellAsync("Unregister-ScheduledTask ONEXTweaks-LedPrimaryRecovery -Confirm:$false -ErrorAction SilentlyContinue;Start-ScheduledTask ONEXTweaks-Startup");
            }catch(Exception error){failure=error;}
            try{if(failure!=null){
                bridge.Dispose();
                // A fresh client performs restoration after this client's pipe is closed.
                await ShellAsync(@"$d=Join-Path $env:LOCALAPPDATA 'ONEXTweaks\dev'
$p=Start-Process powershell.exe -WindowStyle Hidden -Wait -PassThru -ArgumentList ('-NoProfile -NonInteractive -ExecutionPolicy Bypass -File '+[char]34+(Join-Path $d 'tools\compare-led-primary.ps1')+[char]34+' -Color Restore -KeepPaletteWindow')
if($p.ExitCode){throw 'Fallback lighting restoration failed'}");
                var report=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(root,"Reports","led-primary-phase.json")));
                object restored;OriginalRestored=report.TryGetValue("restored",out restored)&&Convert.ToBoolean(restored);
                RestoreMessage=OriginalRestored?"Исходная подсветка восстановлена":"Проверь состояние подсветки; страховка остаётся активной";
            }}finally{Started=false;SaveResults(finished);}
        }
        private static async Task ShellAsync(string code)
        {
            string encoded=Convert.ToBase64String(Encoding.Unicode.GetBytes(code));
            var info=new ProcessStartInfo("powershell.exe","-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand "+encoded){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            using(var process=Process.Start(info)){
                Task<string> output=process.StandardOutput.ReadToEndAsync(),error=process.StandardError.ReadToEndAsync();
                await Task.Run(()=>process.WaitForExit());await output;string diagnostic=await error;
                if(process.ExitCode!=0){string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ONEXTweaks","dev","privateReports","led-calibrator-error.log");Atomic(path,diagnostic);throw new IOException("Не удалось выполнить подготовку или восстановление. Подробности сохранены локально.");}
            }
        }
    }
}
