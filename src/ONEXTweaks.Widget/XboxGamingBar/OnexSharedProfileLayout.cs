// SPDX-License-Identifier: AGPL-3.0-only
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private Border OnexSharedProfileCard(OnexScopedProfilePicker picker,string name,string help)
        {
            picker.Choose.Name="Onex"+name+"ChooseProfile";picker.Create.Name="Onex"+name+"CreateProfile";
            picker.Remove.Name="Onex"+name+"RemoveProfile";picker.Rename.Name="Onex"+name+"RenameProfile";
            picker.RenameRequested=async(id,title)=>{string next=await PromptOnexProfileNameAsync(title,name=="OverlayPreset",OnexProfileNameLimit(name));if(next!=null)await picker.RenameAsync(id,next);};picker.Split.Name="Onex"+name+"ProfileSplit";
            picker.Description.Text=help;picker.Description.Foreground=tileTextBrush;
            var card=OnexSettingsCard(picker.Root);card.Name="Onex"+name+"ProfileHeader";return card;
        }
        private Border _onexPerformanceProfileFooter,_onexFanProfileFooter;
        private void EnsureOnexProfileFooters()
        {
            if(_onexPerformanceProfileFooter==null&&_onexPerformanceProfilePicker!=null&&PerformanceScrollViewer.Content is Panel performance){_onexPerformanceProfileFooter=OnexSettingsCard(_onexPerformanceProfilePicker.Footer);performance.Children.Add(_onexPerformanceProfileFooter);}
            if(_onexFanProfileFooter==null&&_onexFanProfilePicker!=null&&FanScrollViewer.Content is Panel fan){_onexFanProfileFooter=OnexSettingsCard(_onexFanProfilePicker.Footer);if(_onexFanAcForm?.Root!=null&&fan.Children.Contains(_onexFanAcForm.Root))fan.Children.Insert(fan.Children.IndexOf(_onexFanAcForm.Root),_onexFanProfileFooter);else fan.Children.Add(_onexFanProfileFooter);}
            EnsureOnexSourceEditors();
        }
        private Control[][] OnexProfileRows(OnexScopedProfilePicker picker)=>new[]{new Control[]{picker?.Choose,picker?.Create},new Control[]{picker?.Rename,picker?.Remove}};
        private Control OnexProfileNext(OnexScopedProfilePicker picker,Control focus,bool down,Control editor)
        {
            var rows=OnexProfileRows(picker).Select(row=>row.Where(control=>control!=null&&control.IsEnabled&&control.Visibility==Visibility.Visible).ToArray()).Where(row=>row.Length>0).ToArray();
            for(int row=0;row<rows.Length;row++){int column=System.Array.IndexOf(rows[row],focus);if(column<0)continue;int next=row+(down?1:-1);return next>=rows.Length?editor:next<0?focus:rows[next][System.Math.Min(column,rows[next].Length-1)];}return null;
        }
    }
}
