// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
namespace XboxGamingBar
{
    internal sealed class OnexBluetoothEndpoint
    {
        internal string Id,Address,Container,Name;
        internal bool Paired,Connected,Present,CanPair;
    }
    internal sealed class OnexBluetoothGroup<T>
    {
        internal string Id;
        internal T Preferred;
        internal T[] Endpoints;
        internal HashSet<string> Keys;
        internal bool Paired,Connected;
    }
    internal static class OnexBluetoothIdentity
    {
        internal static string Address(string value)
        {
            string text=(value??"").Trim().ToUpperInvariant();
            if(!Regex.IsMatch(text,@"^(?:[0-9A-F]{12}|(?:[0-9A-F]{2}:){5}[0-9A-F]{2}|(?:[0-9A-F]{2}-){5}[0-9A-F]{2})$"))return null;
            text=text.Replace(":","").Replace("-","");return text=="000000000000"||text=="FFFFFFFFFFFF"?null:text;
        }
        internal static string RemoteAddress(string id)
        {
            // AssociationEndpoint syntax is local-adapter - remote-peer. Never use the local MAC.
            var match=Regex.Match(id??"",@"^(?:Bluetooth#Bluetooth|BluetoothLE#BluetoothLE)(?:[0-9a-f]{2}:){5}[0-9a-f]{2}-(?<peer>(?:[0-9a-f]{2}:){5}[0-9a-f]{2}|[0-9a-f]{12})$",RegexOptions.IgnoreCase);
            return match.Success?Address(match.Groups["peer"].Value):null;
        }
        internal static HashSet<string> Keys(OnexBluetoothEndpoint endpoint)
        {
            var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"endpoint:"+endpoint.Id};
            string address=Address(endpoint.Address)??RemoteAddress(endpoint.Id);if(address!=null)result.Add("address:"+address);
            if(Guid.TryParse(endpoint.Container,out var container)&&container!=Guid.Empty&&container!=new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"))result.Add("container:"+container.ToString("D"));
            return result;
        }
        // The complete paired snapshot is authoritative. Discovery owns only unpaired candidates;
        // a delayed Added/Updated/Removed event must not downgrade or remove a paired endpoint.
        internal static T[] Reconcile<T>(IEnumerable<T> paired,IEnumerable<T> discovery,Func<T,OnexBluetoothEndpoint> describe)
        {
            var result=new Dictionary<string,T>(StringComparer.OrdinalIgnoreCase);
            foreach(var value in discovery){var state=describe(value);if(!state.Paired)result[state.Id]=value;}
            foreach(var value in paired)result[describe(value).Id]=value;
            return result.Values.ToArray();
        }
        internal static OnexBluetoothGroup<T>[] Group<T>(IEnumerable<T> input,Func<T,OnexBluetoothEndpoint> describe)
        {
            var values=input.ToArray();var states=values.Select(describe).ToArray();var keys=states.Select(Keys).ToArray();int[] parent=Enumerable.Range(0,values.Length).ToArray();
            int Root(int index){while(parent[index]!=index){parent[index]=parent[parent[index]];index=parent[index];}return index;}
            var owners=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
            for(int index=0;index<values.Length;index++)foreach(string key in keys[index]){if(owners.TryGetValue(key,out int other))parent[Root(index)]=Root(other);else owners[key]=index;}
            return Enumerable.Range(0,values.Length).GroupBy(Root).Select(group=>
            {
                int[] indices=group.OrderByDescending(i=>states[i].Paired).ThenByDescending(i=>states[i].Connected).ThenByDescending(i=>states[i].Present).ThenByDescending(i=>states[i].CanPair).ThenBy(i=>states[i].Id,StringComparer.OrdinalIgnoreCase).ToArray();
                var aliases=new HashSet<string>(indices.SelectMany(i=>keys[i]),StringComparer.OrdinalIgnoreCase);
                string id=aliases.Where(key=>key.StartsWith("container:")).OrderBy(key=>key,StringComparer.OrdinalIgnoreCase).FirstOrDefault()??aliases.Where(key=>key.StartsWith("address:")).OrderBy(key=>key,StringComparer.OrdinalIgnoreCase).FirstOrDefault()??aliases.OrderBy(key=>key,StringComparer.OrdinalIgnoreCase).First();
                return new OnexBluetoothGroup<T>{Id=id,Preferred=values[indices[0]],Endpoints=indices.Select(i=>values[i]).ToArray(),Keys=aliases,Paired=indices.Any(i=>states[i].Paired),Connected=indices.Any(i=>states[i].Connected)};
            }).ToArray();
        }
        internal static OnexBluetoothGroup<T> FindSelection<T>(IEnumerable<OnexBluetoothGroup<T>> groups,string id,HashSet<string> keys)
        {return groups.FirstOrDefault(group=>group.Id==id)??(keys==null?null:groups.FirstOrDefault(group=>group.Keys.Overlaps(keys)));}
    }
}
