using ONEXTweaks.WidgetBridge;
int checks=0;void Check(bool value){checks++;if(!value)throw new InvalidOperationException("Intel Sync policy check "+checks);}
Check(IntelSyncPolicy.Choices(45).SequenceEqual(new[]{0,2,3,5}));Check(!IntelSyncPolicy.Choices(0).Any());Check(IntelSyncPolicy.Choices(63).SequenceEqual(new[]{0,1,2,3,4,5}));
foreach(int value in new[]{1,4,8,32})Check(IntelSyncPolicy.Supports(45,value));foreach(int value in new[]{0,2,16,64,-1})Check(!IntelSyncPolicy.Supports(45,value));
foreach(int index in new[]{0,2,3,5})Check(IntelSyncPolicy.Resolve(index,45)==new[]{1,2,4,8,16,32}[index]);
foreach(int index in new[]{1,4,-1,6}){bool rejected=false;try{IntelSyncPolicy.Resolve(index,45);}catch(ArgumentOutOfRangeException){rejected=true;}catch(InvalidOperationException){rejected=true;}Check(rejected);}
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{passed=true,checks,kind="pure-model",hardwareWrites=0}));
