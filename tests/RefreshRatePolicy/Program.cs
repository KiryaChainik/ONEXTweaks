using XboxGamingBar;
int checks=0;void Check(bool okay){checks++;if(!okay)throw new InvalidOperationException("Refresh policy check "+checks);}
Check(OnexRefreshRatePolicy.MenuRates.SequenceEqual(new[]{30,45,60,90,120,144}));
foreach(int current in new[]{30,45,90,120,144})Check(OnexRefreshRatePolicy.Next(current,new[]{60,144})==60);
Check(OnexRefreshRatePolicy.Next(60,new[]{60,144})==144);
foreach(var test in new[]{(60,new[]{60}),(144,new[]{144}),(0,new[]{60,144}),(90,Array.Empty<int>()),(60,(int[])null)}){bool rejected=false;try{OnexRefreshRatePolicy.Next(test.Item1,test.Item2);}catch(InvalidOperationException){rejected=true;}Check(rejected);}
Check(OnexRefreshRatePolicy.Next(144,new[]{60,60,144})==60);
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{passed=true,checks,hardwareWrites=0,kind="pure-model"}));
