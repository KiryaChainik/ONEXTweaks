using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
[assembly: AssemblyVersion("0.4.1.0")]
[assembly: AssemblyFileVersion("0.4.1.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8",FrameworkDisplayName=".NET Framework 4.8")]
namespace ONEXTweaks.Calibration
{
 internal sealed class MainForm:Form
 {
  private readonly CalibrationPlan plan;private readonly CalibrationSession session;
  private readonly int[] ids={1,2,5,6};private readonly CalibrationZoneGroup[] groups=CalibrationLayout.Zones;
  private readonly Dictionary<int,ComboBox> ratings=new Dictionary<int,ComboBox>();
  private readonly Dictionary<int,int[]> current=new Dictionary<int,int[]>();private readonly Dictionary<int,double[]> axes=new Dictionary<int,double[]>();
  private readonly Label title=new Label(),status=new Label(),clock=new Label();private readonly Panel swatch=new Panel();
  private readonly ComboBox zone=new ComboBox(),step=new ComboBox(),brightness=new ComboBox();private readonly TextBox comment=new TextBox();
  private readonly TrackBar[] axisSliders={new TrackBar(),new TrackBar(),new TrackBar()},wireSliders={new TrackBar(),new TrackBar(),new TrackBar()};
  private readonly Label[] axisValues={new Label(),new Label(),new Label()};private readonly NumericUpDown[] rgb={new NumericUpDown(),new NumericUpDown(),new NumericUpDown()};
  private readonly Button start=new Button(),previous=new Button(),next=new Button(),reset=new Button(),finish=new Button();private readonly List<Control> editControls=new List<Control>();
  private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=500},applyTimer=new System.Windows.Forms.Timer{Interval=40};
  private readonly bool previewOnly;private int index;private bool loading,busy,starting,applyRunning,closing,allowClose;private long generation,appliedGeneration;
  private readonly string[] ratingText={CalibratorText.Get("Не оценено"),CalibratorText.Get("Подходит"),CalibratorText.Get("Приемлемо, не идеально"),CalibratorText.Get("Нужна доработка"),CalibratorText.Get("Пропустить")},ratingValues={"unrated","accepted","provisional","needs_work","skipped"};
  private int Level{get{return CalibrationSchedule.Levels[Math.Max(0,brightness.SelectedIndex)];}}private int Position{get{return brightness.SelectedIndex*plan.cases.Length+index;}}
  private int Step{get{return CalibrationSchedule.Steps[Math.Max(0,step.SelectedIndex)];}}private bool Settled{get{return !applyRunning&&generation==appliedGeneration;}}
  private static Dictionary<int,int[]> Copy(Dictionary<int,int[]> value){return value.ToDictionary(p=>p.Key,p=>(int[])p.Value.Clone());}
  internal MainForm(CalibrationPlan value,bool preview=false,bool uiQa=false,bool verify=false,bool verifyLocale=false)
  {
   previewOnly=preview;session=new CalibrationSession(uiQa||verify||verifyLocale);plan=value;if(plan==null||plan.cases==null||plan.cases.Length!=24)throw new InvalidDataException(CalibratorText.Get("Нужен набор из 24 образцов"));
   foreach(var sample in plan.cases){if(!System.Text.RegularExpressions.Regex.IsMatch(sample.hex??"","^#[0-9A-Fa-f]{6}$"))throw new InvalidDataException(CalibratorText.Get("Неверный цвет образца"));foreach(int id in ids)CalibrationMath.Validate(sample.initialWireByZone[id.ToString()]);}
   int restored=preview||uiQa||verify||verifyLocale?0:session.TryResume(plan);
   Text=CalibratorText.Get("ONEXTweaks · Калибровка LED 0.3");Name="LedCalibrator";AutoScaleMode=AutoScaleMode.None;Font=new Font("Segoe UI",10);BackColor=Color.FromArgb(23,26,32);ForeColor=Color.White;StartPosition=FormStartPosition.CenterScreen;
   var area=Screen.PrimaryScreen.WorkingArea;ClientSize=new Size(Math.Min(1050,area.Width-30),Math.Min(680,area.Height-40));MinimumSize=new Size(Math.Min(780,area.Width-10),Math.Min(610,area.Height-20));AutoScroll=true;
   var root=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Padding=new Padding(12)};root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));Controls.Add(root);
   title.Name="CurrentReference";title.Dock=DockStyle.Fill;title.Font=new Font("Segoe UI",13,FontStyle.Bold);Add(root,title,52);
   status.Name="CalibrationStatus";status.Dock=DockStyle.Fill;status.Text=preview?CalibratorText.Get("Проверка интерфейса: подсветка не меняется."):restored>0?CalibratorText.Get("Прошлые результаты сохранены. Нажми «Продолжить»."):CalibratorText.Get(session.ResumeMessage)??CalibratorText.Get("Нажми «Начать». До этого подсветка не меняется.");Add(root,status,34);
   var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,64));body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,36));
   var left=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,Padding=new Padding(0,0,12,0)};left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
   var selectors=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,55));selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,45));
   zone.Name="ZoneChoice";zone.AccessibleName=CalibratorText.Get("Выбор зоны подсветки");zone.DropDownStyle=ComboBoxStyle.DropDownList;zone.Dock=DockStyle.Fill;zone.Items.Add(CalibratorText.Get("Все 3 зоны"));foreach(var group in groups)zone.Items.Add(group.key=="lower"?CalibratorText.Get("Нижняя · кнопка меню"):CalibratorText.Get(group.name));zone.SelectedIndex=2;selectors.Controls.Add(zone,0,0);
   step.Name="AdjustmentStep";step.AccessibleName=CalibratorText.Get("Шаг кнопок и стрелок");step.DropDownStyle=ComboBoxStyle.DropDownList;step.Dock=DockStyle.Fill;step.Items.AddRange(new object[]{CalibratorText.Get("Шаг 1 · точный"),CalibratorText.Get("Шаг 2 · небольшой"),CalibratorText.Get("Шаг 5 · средний"),CalibratorText.Get("Шаг 10 · крупный"),CalibratorText.Get("Шаг 20 · очень крупный")});step.SelectedIndex=2;selectors.Controls.Add(step,1,0);Add(left,selectors,38);
   string[] captions={CalibratorText.Get("Тон"),CalibratorText.Get("Насыщенность"),CalibratorText.Get("Яркость цвета RGB")};
   for(int a=0;a<3;a++){
    int axis=a;var row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=5,RowCount=1};row.RowStyles.Add(new RowStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,164));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,48));row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,48));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,72));
    row.Controls.Add(new Label{Text=CalibratorText.Get(captions[a]),Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,0);
    var minus=MakeButton("−","Adjust_"+a);var plus=MakeButton("+","Adjust_"+(a+3));minus.AccessibleName=CalibratorText.Get("Уменьшить: ")+captions[a];plus.AccessibleName=CalibratorText.Get("Увеличить: ")+captions[a];minus.Click+=(s,e)=>AdjustAxis(axis,-Step);plus.Click+=(s,e)=>AdjustAxis(axis,Step);row.Controls.Add(minus,1,0);row.Controls.Add(plus,3,0);editControls.Add(minus);editControls.Add(plus);
    SetupSlider(axisSliders[a],"Axis_"+a,captions[a],a==0?359:100);axisSliders[a].ValueChanged+=(s,e)=>{if(!loading)SetAxis(axis,axisSliders[axis].Value);};row.Controls.Add(axisSliders[a],2,0);editControls.Add(axisSliders[a]);axisValues[a].Dock=DockStyle.Fill;axisValues[a].TextAlign=ContentAlignment.MiddleRight;row.Controls.Add(axisValues[a],4,0);Add(left,row,46);
   }
   Add(left,new Label{Text=CalibratorText.Get("Тон — сам оттенок. Насыщенность: 0% — без оттенка, 100% — насыщенный цвет. RGB: ниже — темнее, выше — светлее; яркость LED выбирается справа."),Dock=DockStyle.Fill,ForeColor=Color.Silver},66);
   for(int c=0;c<3;c++){
    int channel=c;var row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1};row.RowStyles.Add(new RowStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,28));row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,70));string label=new[]{"R","G","B"}[c];row.Controls.Add(new Label{Text=label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,0);
    SetupSlider(wireSliders[c],"WireSlider_"+c,CalibratorText.Get("Канал ")+label,255);wireSliders[c].ValueChanged+=(s,e)=>{if(!loading)SetChannel(channel,wireSliders[channel].Value);};row.Controls.Add(wireSliders[c],1,0);editControls.Add(wireSliders[c]);rgb[c].Name="Wire_"+c;rgb[c].AccessibleName=CalibratorText.Get("Точное значение ")+label;rgb[c].Maximum=255;rgb[c].Dock=DockStyle.Fill;rgb[c].ValueChanged+=(s,e)=>{if(!loading)SetChannel(channel,(int)rgb[channel].Value);};row.Controls.Add(rgb[c],2,0);editControls.Add(rgb[c]);Add(left,row,38);
   }
   var right=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1};right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));swatch.Name="ReferenceSwatch";swatch.AccessibleName=CalibratorText.Get("Экранный цвет для сравнения");swatch.Dock=DockStyle.Fill;swatch.Margin=new Padding(3,0,3,10);Add(right,swatch,120);
   brightness.Name="HardwareBrightness";brightness.AccessibleName=CalibratorText.Get("Этап яркости LED");brightness.DropDownStyle=ComboBoxStyle.DropDownList;brightness.Dock=DockStyle.Fill;for(int i=0;i<4;i++)brightness.Items.Add(CalibratorText.Get("Этап ")+(i+1)+CalibratorText.Get("/4 · LED яркость ")+CalibrationSchedule.Levels[i]+"/4");brightness.SelectedIndex=0;Add(right,brightness,36);
   foreach(var group in groups){
    var row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1};row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.RowStyles.Add(new RowStyle(SizeType.Absolute,28));row.RowStyles.Add(new RowStyle(SizeType.Percent,100));row.Controls.Add(new Label{Name="ZoneRatingHeading_"+group.key,TextAlign=ContentAlignment.MiddleLeft,Text=group.key=="lower"?CalibratorText.Get("Нижняя · кнопка меню"):CalibratorText.Get(group.name),Dock=DockStyle.Fill},0,0);
    var rating=new ComboBox{Name="Rating_"+group.key,AccessibleName=CalibratorText.Get("Оценка зоны: ")+CalibratorText.Get(group.name),DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};rating.Items.AddRange(ratingText);rating.SelectedIndex=0;foreach(int id in group.hardwareZoneIds)ratings[id]=rating;rating.SelectedIndexChanged+=(s,e)=>{if(!loading&&session.Started){SaveCurrent();UpdateEnabled();}};row.Controls.Add(rating,0,1);Add(right,row,58);
   }
   Add(right,new Label{Name="CommentHeading",Text=CalibratorText.Get("Комментарий к этому цвету и уровню яркости:"),Dock=DockStyle.Fill},28);comment.Name="CaseComment";comment.AccessibleName=CalibratorText.Get("Комментарий к образцу");comment.MaxLength=1000;comment.Dock=DockStyle.Fill;comment.TextChanged+=(s,e)=>{if(!loading&&session.Started)SaveCurrent();};Add(right,comment,30);
   body.Controls.Add(left,0,0);body.Controls.Add(right,1,0);Add(root,body,444);
   var navigation=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=5};for(int c=0;c<5;c++)navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,20));Configure(start,restored>0?CalibratorText.Get("Продолжить"):CalibratorText.Get("Начать"),"StartCalibration");Configure(previous,CalibratorText.Get("Назад"),"PreviousCase");Configure(next,CalibratorText.Get("Дальше"),"NextCase");Configure(reset,CalibratorText.Get("Сброс выбранных"),"ResetCase");Configure(finish,CalibratorText.Get("Закрыть"),"CloseRestore");navigation.Controls.Add(start,0,0);navigation.Controls.Add(previous,1,0);navigation.Controls.Add(next,2,0);navigation.Controls.Add(reset,3,0);navigation.Controls.Add(finish,4,0);Add(root,navigation,50);
   clock.Name="RecoveryCountdown";clock.Dock=DockStyle.Fill;clock.ForeColor=Color.Silver;Add(root,clock,42);
   zone.SelectedIndexChanged+=(s,e)=>RefreshInputs();step.SelectedIndexChanged+=(s,e)=>UpdateSteps();brightness.SelectedIndexChanged+=(s,e)=>{if(!loading){LoadCase();if(session.Started)QueueApply();}};
   start.Click+=async(s,e)=>await StartAsync();previous.Click+=async(s,e)=>await NavigateAsync(-1);next.Click+=async(s,e)=>await NavigateAsync(1);reset.Click+=(s,e)=>ResetCurrent();finish.Click+=async(s,e)=>await CloseSafelyAsync(false);applyTimer.Tick+=async(s,e)=>{applyTimer.Stop();await FlushAsync();};
   timer.Tick+=async(s,e)=>{if(session.Started){var t=session.Deadline-DateTime.UtcNow;clock.Text=CalibratorText.Get("Автовозврат при бездействии: ")+Math.Max(0,(int)t.TotalMinutes).ToString("00")+":"+Math.Max(0,t.Seconds).ToString("00")+CalibratorText.Get(" · данные сохраняются автоматически");if(t<=TimeSpan.Zero&&!closing)await CloseSafelyAsync(false);}else clock.Text=CalibratorText.Get("4 этапа по 24 цвета. Оценки каждой яркости независимы. Закрытие возвращает исходную подсветку.");};timer.Start();
   FormClosing+=async(s,e)=>{if(!allowClose){e.Cancel=true;await CloseSafelyAsync(false);}};FormClosed+=(s,e)=>{timer.Dispose();applyTimer.Dispose();};int missing=CalibrationSchedule.FirstMissing(plan,session.Results);SetPosition(missing<0?0:missing,false);UpdateSteps();if(verify)Shown+=async(s,e)=>await VerifyControlsAsync();if(verifyLocale)Shown+=async(s,e)=>await VerifyLocalizationAsync();
  }
  private static void Add(TableLayoutPanel root,Control control,int height){int row=root.RowCount++;root.RowStyles.Add(new RowStyle(SizeType.Absolute,height));root.Controls.Add(control,0,row);}
  private static Button MakeButton(string text,string name){var b=new Button();Configure(b,text,name);return b;}
  private static void Configure(Button b,string text,string name){b.Text=CalibratorText.Get(text);b.Name=name;b.Dock=DockStyle.Fill;b.FlatStyle=FlatStyle.Flat;b.BackColor=Color.FromArgb(45,50,61);b.ForeColor=Color.White;b.Margin=new Padding(3);}
  private static void SetupSlider(TrackBar s,string name,string label,int maximum){s.Name=name;s.AccessibleName=CalibratorText.Get(label);s.Minimum=0;s.Maximum=maximum;s.TickStyle=TickStyle.None;s.AutoSize=false;s.Dock=DockStyle.Fill;s.Margin=new Padding(1,2,1,0);}
  private int[] SelectedIds(){return zone.SelectedIndex>0?groups[zone.SelectedIndex-1].hardwareZoneIds:ids;}
  private void UpdateSteps(){foreach(var s in axisSliders.Concat(wireSliders)){s.SmallChange=Step;s.LargeChange=Math.Min(s.Maximum,Step*2);}foreach(var n in rgb)n.Increment=Step;}
  private void RefreshInputs(){if(current.Count==0)return;loading=true;try{int id=SelectedIds()[0];for(int a=0;a<3;a++){int v=(int)Math.Round(axes[id][a]*(a==0?1:100));axisSliders[a].Value=Math.Max(0,Math.Min(axisSliders[a].Maximum,v));axisValues[a].Text=v+(a==0?"°":"%");}for(int c=0;c<3;c++){wireSliders[c].Value=current[id][c];rgb[c].Value=current[id][c];}}finally{loading=false;}}
  private void InvalidateRatings(IEnumerable<int> affected){loading=true;try{foreach(int id in affected)ratings[id].SelectedIndex=0;}finally{loading=false;}}
  private void AdjustAxis(int a,int delta){if(!session.Started||closing||busy)return;foreach(int id in SelectedIds()){axes[id][a]+=delta/(a==0?1.0:100.0);axes[id][a]=a==0?(axes[id][a]%360+360)%360:Math.Max(0,Math.Min(1,axes[id][a]));current[id]=CalibrationMath.Compose(axes[id][0],axes[id][1],axes[id][2]);}Changed(SelectedIds());}
  private void SetAxis(int a,int value){if(!session.Started||closing||busy)return;foreach(int id in SelectedIds()){axes[id][a]=value/(a==0?1.0:100.0);current[id]=CalibrationMath.Compose(axes[id][0],axes[id][1],axes[id][2]);}Changed(SelectedIds());}
  private void SetChannel(int c,int value){if(!session.Started||closing||busy)return;foreach(int id in SelectedIds()){current[id][c]=value;var p=CalibrationMath.Describe(current[id]);if(p[1]==0)p[0]=axes[id][0];if(p[2]==0){p[0]=axes[id][0];p[1]=axes[id][1];}axes[id]=p;}Changed(SelectedIds());}
  private void Changed(int[] affected){InvalidateRatings(affected);RefreshInputs();QueueApply();}
  private void ResetCurrent(){if(!session.Started||closing||busy)return;int[] affected=SelectedIds();foreach(int id in affected){current[id]=(int[])plan.cases[index].initialWireByZone[id.ToString()].Clone();axes[id]=CalibrationMath.Describe(current[id]);}Changed(affected);}
  private async Task StartAsync(){if(previewOnly)return;busy=true;starting=true;UpdateEnabled();status.Text=CalibratorText.Get("Сохраняю исходную подсветку…");try{await session.StartAsync();busy=false;if(!closing)QueueApply();}catch(Exception e){busy=false;status.Text=CalibratorText.Get(e.Message);UpdateEnabled();}finally{starting=false;}}
  private void QueueApply(){generation++;status.Text=CalibratorText.Get("Применяю последнее положение ползунков…");UpdateEnabled();if(!applyRunning&&!applyTimer.Enabled)applyTimer.Start();}
  private async Task FlushAsync()
  {
   applyTimer.Stop();if(applyRunning){while(applyRunning)await Task.Delay(15);}if(!session.Started||busy||generation==appliedGeneration)return;applyRunning=true;UpdateEnabled();
   try{while(generation!=appliedGeneration){long version=generation;var desired=Copy(current);int level=Level;await session.ApplyAsync(desired,level);appliedGeneration=version;}status.Text=CalibratorText.Get("Цвет применён. Оцени все три зоны и нажми «Дальше».");}catch(Exception e){status.Text=CalibratorText.Get(e.Message);busy=true;}finally{applyRunning=false;if(!busy)SaveCurrent();UpdateEnabled();}
  }
  private CaseResult Snapshot(){return new CaseResult{caseId=plan.cases[index].id,referenceSrgb=plan.cases[index].hex,hardwareBrightness=Level,comment=comment.Text,savedUtc=DateTime.UtcNow.ToString("o"),zones=ids.Select(id=>new ZoneResult{zoneId=id,logicalGroup=groups.First(g=>g.hardwareZoneIds.Contains(id)).key,desiredWire=(int[])current[id].Clone(),acknowledgedWire=session.Acknowledged!=null&&session.Acknowledged.ContainsKey(id)?(int[])session.Acknowledged[id].Clone():null,rating=ratingValues[ratings[id].SelectedIndex]}).ToArray()};}
  private void SaveCurrent(){if(!session.Started||!Settled||generation==0)return;try{session.SaveResult(Snapshot());UpdateTitle();}catch(Exception){busy=true;status.Text=CalibratorText.Get("Не удалось сохранить изменения. Закрой окно для возврата исходной подсветки.");}}
  private async Task NavigateAsync(int direction){if(!session.Started||busy||!Settled)return;SaveCurrent();int p=Position+direction;if(p>=plan.cases.Length*4){int missing=CalibrationSchedule.FirstMissing(plan,session.Results);if(missing<0){await CloseSafelyAsync(true);return;}p=missing;}SetPosition(Math.Max(0,p),true);}
  private void SetPosition(int position,bool apply){index=position%plan.cases.Length;loading=true;brightness.SelectedIndex=position/plan.cases.Length;loading=false;LoadCase();if(apply&&session.Started)QueueApply();}
  private void UpdateTitle(){title.Text=CalibratorText.Get("Этап ")+(brightness.SelectedIndex+1)+CalibratorText.Get("/4 · LED яркость ")+Level+CalibratorText.Get("/4 · оценено ")+session.Results.Count(CalibrationSchedule.Reviewed)+"/96\r\n"+(index+1)+" / "+plan.cases.Length+" · "+CalibratorText.Get(plan.cases[index].name)+" · "+plan.cases[index].hex;}
  private void LoadCase()
  {
   loading=true;try{var sample=plan.cases[index];swatch.BackColor=ColorTranslator.FromHtml(sample.hex);comment.Text="";var saved=session.Results.LastOrDefault(r=>r.caseId==sample.id&&r.hardwareBrightness==Level);var seed=saved??session.Results.Where(r=>r.caseId==sample.id).OrderBy(r=>Math.Abs(r.hardwareBrightness-Level)).FirstOrDefault();foreach(int id in ids){current[id]=seed==null?(int[])sample.initialWireByZone[id.ToString()].Clone():(int[])seed.zones.First(z=>z.zoneId==id).desiredWire.Clone();axes[id]=CalibrationMath.Describe(current[id]);var r=saved==null?null:saved.zones.First(z=>z.zoneId==id);ratings[id].SelectedIndex=r==null?0:Math.Max(0,Array.IndexOf(ratingValues,r.rating));}if(saved!=null)comment.Text=saved.comment??"";}finally{loading=false;}UpdateTitle();RefreshInputs();UpdateEnabled();
  }
  private void UpdateEnabled(){bool active=session.Started&&!busy&&!closing;foreach(var c in editControls)c.Enabled=active;zone.Enabled=!busy&&!closing;step.Enabled=!busy&&!closing;brightness.Enabled=!busy&&!closing&&Settled;foreach(var r in ratings.Values)r.Enabled=active&&Settled;comment.Enabled=active&&Settled;start.Enabled=!previewOnly&&!session.Started&&!busy&&!closing;previous.Enabled=active&&Settled&&Position>0;next.Enabled=active&&Settled&&ratings.Values.All(r=>r.SelectedIndex>0);next.Text=Position==95?CalibratorText.Get("Завершить"):CalibratorText.Get("Дальше");reset.Enabled=active&&Settled;finish.Enabled=!closing;}
  private async Task CloseSafelyAsync(bool finished){if(closing)return;closing=true;UpdateEnabled();status.Text=CalibratorText.Get("Сохраняю результаты и возвращаю подсветку…");try{while(starting)await Task.Delay(30);await FlushAsync();SaveCurrent();await session.EndAsync(finished);allowClose=true;Close();}catch(Exception e){status.Text=CalibratorText.Get(e.Message);allowClose=true;Close();}}
  private async Task VerifyLocalizationAsync()
  {
   string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ONEXTweaks","dev","Reports","localization95-calibrator");
   string report=Path.Combine(folder,"locale-"+CalibratorText.Language+".json");
   var checks=new List<object>();bool passed=true;
   try {
    // Preview-only forms never acquire the LED session or load an owner's results.
    if(!previewOnly||session.Started||session.Results.Count!=0||session.ApplyCount!=0)throw new InvalidOperationException("Locale QA must be read-only");
    await Task.Delay(200);
    var controls=new List<Control>();var pending=new Queue<Control>();pending.Enqueue(this);
    while(pending.Count>0){Control c=pending.Dequeue();controls.Add(c);foreach(Control child in c.Controls)pending.Enqueue(child);}
    int captions=0;var leaks=new List<string>();
    foreach(Control c in controls){
     if(!string.IsNullOrEmpty(c.Text)){captions++;if(CalibratorText.Language=="en"&&System.Text.RegularExpressions.Regex.IsMatch(c.Text,"[А-Яа-яЁё]"))leaks.Add(c.GetType().Name);}
     if(CalibratorText.Language=="en"&&System.Text.RegularExpressions.Regex.IsMatch(c.AccessibleName??"","[А-Яа-яЁё]"))leaks.Add(c.GetType().Name+".AccessibleName");
     var combo=c as ComboBox;if(combo!=null)foreach(object item in combo.Items)if(CalibratorText.Language=="en"&&System.Text.RegularExpressions.Regex.IsMatch(Convert.ToString(item),"[А-Яа-яЁё]"))leaks.Add("ComboBoxItem");
    }
    bool canonical=ratingValues.SequenceEqual(new[]{"unrated","accepted","provisional","needs_work","skipped"})&&groups.Select(g=>g.key).SequenceEqual(new[]{"upper","sticks","lower"});
    bool headingsFit=controls.Where(c=>c.Name.StartsWith("ZoneRatingHeading_")||c.Name=="CommentHeading").All(c=>TextRenderer.MeasureText(c.Text,c.Font).Height<=c.ClientSize.Height);
    bool ui=headingsFit&&zone.Items.Count==4&&step.Items.Count==5&&brightness.Items.Count==4&&ratings.Count==4&&ratings.Values.Distinct().Count()==3&&ratings.Values.All(c=>c.Items.Count==5)&&start.Text==CalibratorText.Get("Начать")&&title.Text.Contains(CalibratorText.Get(plan.cases[index].name));
    passed=CalibratorText.WidgetPreferenceApiAvailable&&leaks.Count==0&&canonical&&ui&&!session.Started&&session.ApplyCount==0&&session.Results.Count==0;
    checks.Add(new{knownCaptions=CalibratorText.KnownCaptionCount,renderedTextControls=captions,unexpectedCyrillicControls=leaks,canonicalIdsUnchanged=canonical,controlsPresent=ui,ratingHeadingTextFits=headingsFit,logicalRatingGroups=ratings.Values.Distinct().Count(),hardwareRatingZones=ratings.Count,ledSessionStarted=session.Started,ledApplyCount=session.ApplyCount,loadedOwnerResults=session.Results.Count});
    Directory.CreateDirectory(folder);
    using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(Path.Combine(folder,"locale-"+CalibratorText.Language+".png"),System.Drawing.Imaging.ImageFormat.Png);}
   }catch(Exception error){passed=false;checks.Add(new{errorType=error.GetType().Name,error=error.Message});}
   CalibrationSession.Atomic(report,new JavaScriptSerializer().Serialize(new{passed=passed,version="0.4.1.0",language=CalibratorText.Language,languageSource=CalibratorText.LanguageSource,widgetPreferenceApiAvailable=CalibratorText.WidgetPreferenceApiAvailable,languageReadError=CalibratorText.ReadErrorType,checks=checks,physicalColorAccuracyTested=false}));
   Environment.ExitCode=passed?0:1;allowClose=true;Close();
  }
  private async Task VerifyControlsAsync()
  {
   var checks=new Dictionary<string,bool>();string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ONEXTweaks","dev");Exception failure=null;double localMs=0;int rapidWrites=0;
   try{
    await StartAsync();await FlushAsync();if(busy)throw new IOException(status.Text);checks["sixSliders"]=axisSliders.Concat(wireSliders).Count()==6;checks["fiveStepOptions"]=step.Items.Count==5&&Step==5;
    foreach(var r in ratings.Values)r.SelectedIndex=1;var upper=(int[])current[6].Clone();var lower=(int[])current[5].Clone();zone.SelectedIndex=2;var watch=Stopwatch.StartNew();axisSliders[1].Value=70;localMs=watch.Elapsed.TotalMilliseconds;
    checks["immediateLocalUpdate"]=axisValues[1].Text=="70%"&&current[1][1]>0&&localMs<100;await FlushAsync();checks["sliderSelectedGroupOnly"]=upper.SequenceEqual(current[6])&&lower.SequenceEqual(current[5])&&current[1].SequenceEqual(current[2]);
    foreach(var r in ratings.Values)r.SelectedIndex=1;reset.PerformClick();await FlushAsync();checks["selectedResetKeepsPeers"]=upper.SequenceEqual(current[6])&&lower.SequenceEqual(current[5])&&ratings[6].SelectedIndex==1&&ratings[5].SelectedIndex==1&&ratings[1].SelectedIndex==0&&ratings[2].SelectedIndex==0&&current[1].SequenceEqual(plan.cases[index].initialWireByZone["1"]);
    int before=session.ApplyCount;for(int v=10;v<=70;v++)axisSliders[1].Value=v;await FlushAsync();rapidWrites=session.ApplyCount-before;checks["dragCoalesced"]=rapidWrites==1&&axisSliders[1].Value==70;checks["acknowledgementMatchesFinal"]=ids.All(id=>Enumerable.Range(0,3).All(c=>Math.Abs(current[id][c]-session.Acknowledged[id][c])<=1));
    foreach(var r in ratings.Values)r.SelectedIndex=1;string originalCase=plan.cases[index].id;var level4=session.Results.Last(r=>r.caseId==originalCase&&r.hardwareBrightness==4);string saved4=new JavaScriptSerializer().Serialize(level4);
    brightness.SelectedIndex=1;await FlushAsync();checks["newBrightnessNeedsOwnRatings"]=Level==3&&ratings.Values.All(r=>r.SelectedIndex==0)&&new JavaScriptSerializer().Serialize(session.Results.Last(r=>r.caseId==originalCase&&r.hardwareBrightness==4))==saved4;
    wireSliders[0].Value=222;await FlushAsync();foreach(var r in ratings.Values)r.SelectedIndex=2;var saved3=Copy(current);brightness.SelectedIndex=0;await FlushAsync();checks["brightness4Restored"]=current[1].SequenceEqual(level4.zones.First(z=>z.zoneId==1).desiredWire)&&ratings.Values.All(r=>r.SelectedIndex==1);
    brightness.SelectedIndex=1;await FlushAsync();checks["brightness3Restored"]=ids.All(id=>current[id].SequenceEqual(saved3[id]))&&ratings.Values.All(r=>r.SelectedIndex==2);
    SetPosition(23,false);QueueApply();await FlushAsync();foreach(var r in ratings.Values)r.SelectedIndex=1;await NavigateAsync(1);await FlushAsync();checks["automaticBrightnessStage"]=Level==3&&index==0;checks["dataHasSeparateLevels"]=session.Results.Any(r=>r.caseId==originalCase&&r.hardwareBrightness==4)&&session.Results.Any(r=>r.caseId==originalCase&&r.hardwareBrightness==3);checks["noQueuedApplyRemaining"]=Settled;
   }catch(Exception e){failure=e;}
   await CloseSafelyAsync(false);checks["originalLightingRestored"]=session.OriginalRestored;bool passed=failure==null&&checks.Values.All(v=>v);CalibrationSession.Atomic(Path.Combine(root,"Reports","led-calibrator-v02-ui.json"),new JavaScriptSerializer().Serialize(new{passed=passed,checks=checks,localSliderEventMilliseconds=localMs,rapidSliderEvents=61,actualWritesForRapidEvents=rapidWrites,lastApplyMilliseconds=session.LastApplyMilliseconds,error=failure==null?null:failure.Message,input="injected events in real WinForms controls on the physical console",ownerPhysicalInputTested=false,physicalColorAccuracyMeasured=false}));Environment.ExitCode=passed?0:1;
  }
  [STAThread]public static int Main(string[] args)
  {
   CalibratorText.Initialize(args);
   try{
    string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ONEXTweaks","dev");
    if(args.Length==1&&args[0]=="--verify-lifecycle"){
     var context=new ApplicationContext();var tick=new System.Windows.Forms.Timer{Interval=100};tick.Tick+=async(s,e)=>{tick.Stop();int code=0;try{var session=new CalibrationSession(true);await session.StartAsync();await session.EndAsync(false);CalibrationSession.Atomic(Path.Combine(root,"Reports","led-calibrator-lifecycle.json"),"{\"passed\":"+(session.OriginalRestored?"true":"false")+",\"originalLightingRestored\":"+(session.OriginalRestored?"true":"false")+",\"hardwareColorAccuracyTested\":false}");}catch(Exception error){CalibrationSession.Atomic(Path.Combine(root,"Reports","led-calibrator-lifecycle.json"),new JavaScriptSerializer().Serialize(new{passed=false,error=error.Message}));code=1;}Environment.ExitCode=code;context.ExitThread();};tick.Start();Application.Run(context);tick.Dispose();return Environment.ExitCode;
    }
    var json=new JavaScriptSerializer{MaxJsonLength=1048576,RecursionLimit=40};var plan=json.Deserialize<CalibrationPlan>(File.ReadAllText(Path.Combine(root,"tools","led-palette-survey-cases.json")));
    bool localeQa=args.Contains("--verify-localization");using(var singleton=new Mutex(false,localeQa?"Local\\ONEXTweaks.LedCalibrator.LocaleQA."+CalibratorText.Language:"Local\\ONEXTweaks.LedCalibrator.UI.v1")){if(!singleton.WaitOne(0))return 0;try{Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new MainForm(plan,args.Contains("--preview-only")||localeQa,args.Contains("--ui-qa")||localeQa,args.Contains("--verify-controls"),localeQa));return Environment.ExitCode;}finally{singleton.ReleaseMutex();}}
   }catch(Exception error){MessageBox.Show(CalibratorText.Get(error.Message),CalibratorText.Get("ONEXTweaks · Калибратор LED"));return 1;}
  }
 }
}
