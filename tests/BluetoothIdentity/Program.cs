using System.Text.Json;
using XboxGamingBar;
var checks=new List<string>();void Check(bool condition,string name){if(!condition)throw new Exception(name);checks.Add(name);}
OnexBluetoothEndpoint E(string id,string address=null,string container=null,bool paired=false,bool connected=false,string name="Same name")=>new OnexBluetoothEndpoint{Id=id,Address=address,Container=container,Paired=paired,Connected=connected,Name=name,CanPair=!paired,Present=true};
OnexBluetoothGroup<OnexBluetoothEndpoint>[] G(params OnexBluetoothEndpoint[] records)=>OnexBluetoothIdentity.Group(records,value=>value);
try{
 Check(G(E("a","aa:bb:cc:dd:ee:ff"),E("b","AA-BB-CC-DD-EE-FF"),E("c","aabbccddeeff")).Length==1,"MAC formatting normalizes colon/hyphen/plain and case");
 const string local="01:02:03:04:05:06";var id="Bluetooth#Bluetooth"+local+"-aa:bb:cc:dd:ee:ff";
 Check(OnexBluetoothIdentity.RemoteAddress(id)=="AABBCCDDEEFF"&&G(E(id),E("BLE","AA:BB:CC:DD:EE:FF")).Length==1,"missing address uses peer MAC from known AEP syntax");
 Check(OnexBluetoothIdentity.RemoteAddress("Bluetooth#Bluetooth"+local)==null&&G(E(id),E("other","01:02:03:04:05:06")).Length==2,"local adapter address is never used as peer identity");
 const string c1="11111111-2222-3333-4444-555555555555",c2="aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
 Check(G(E("classic","AA:BB:CC:DD:EE:01",c1),E("ble","AA:BB:CC:DD:EE:02",c1)).Length==1,"AEP container joins classic and BLE with different addresses");
 Check(G(E("a","AA:BB:CC:DD:EE:01"),E("b","AA:BB:CC:DD:EE:02")).Length==2,"same-name distinct physical devices are not merged");
 Check(G(E("a"),E("b")).Length==2,"unknown identities stay separate even with same name");
 Check(G(E("a","00:00:00:00:00:00"),E("b","000000000000")).Length==2&&OnexBluetoothIdentity.Address("FFFFFFFFFFFF")==null,"zero and broadcast addresses are not identities");
 Check(G(E("a","AABBCCDDEE01",c1),E("b","AA-BB-CC-DD-EE-01",c2),E("c","AABBCCDDEE02",c2)).Length==1,"identity overlap reconciles cross-protocol chains");
 var paired=E("paired","AABBCCDDEE01",c1,true,false);var stale=E("discovery","AABBCCDDEE01",c1,false,true);var merged=G(stale,paired).Single();
 Check(merged.Preferred==paired&&merged.Paired&&merged.Connected,"paired target wins over stale connected unpaired advertisement");
 var connected=E("pairedConnected","AABBCCDDEE01",c1,true,true);Check(G(paired,connected).Single().Preferred==connected,"connected paired endpoint is preferred");
 var fresh=E("sameId","AABBCCDDEE01",c1,true);var late=E("sameId","AABBCCDDEE01",c1,false);var reconciled=OnexBluetoothIdentity.Reconcile(new[]{fresh},new[]{late},value=>value);
 Check(reconciled.Single()==fresh,"late discovery Added cannot downgrade authoritative paired snapshot");
 Check(OnexBluetoothIdentity.Reconcile(new[]{fresh},Array.Empty<OnexBluetoothEndpoint>(),value=>value).Single()==fresh,"discovery Removed does not remove paired snapshot");
 Check(OnexBluetoothIdentity.Reconcile(Array.Empty<OnexBluetoothEndpoint>(),new[]{fresh},value=>value).Length==0,"stale paired discovery record cannot resurrect an unpaired device");
 var before=G(E("oldAep","AABBCCDDEE01"))[0];var after=G(E("newAep","AA:BB:CC:DD:EE:01",c1,true));
 Check(OnexBluetoothIdentity.FindSelection(after,before.Id,before.Keys)==after[0],"selection survives endpoint replacement and newly assigned container");
 Check(OnexBluetoothIdentity.FindSelection(G(E("unrelated","AABBCCDDEE02")),before.Id,before.Keys)==null,"same display name never transfers selection to another device");
 Check(G(E("a",container:Guid.Empty.ToString()),E("b",container:Guid.Empty.ToString())).Length==2,"empty containers are ignored");
 Check(OnexBluetoothIdentity.Address("AABBCCDDEEFF0011")==null&&OnexBluetoothIdentity.RemoteAddress("untrusted-"+local)==null,"malformed and ambiguous IDs are not parsed as MACs");
 await File.WriteAllTextAsync(args[0],JsonSerializer.Serialize(new{passed=true,checks,settingsMutations=0,pairingChanges=0,radioChanges=0},new JsonSerializerOptions{WriteIndented=true}));
}catch(Exception error){await File.WriteAllTextAsync(args[0],JsonSerializer.Serialize(new{passed=false,checks,error=error.ToString()}));Environment.ExitCode=1;}
