// SPDX-License-Identifier: AGPL-3.0-only
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 // Join the existing header and editor edges with native layout only. No layout
 // observers or geometry writes: opening a card must never trigger a redraw loop.
 internal sealed class OnexConnectedCardOutline
 {
  private readonly Grid owner;
  internal readonly Border Shape=new Border{IsHitTestVisible=false,Height=1,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(1,0,1,0),Visibility=Visibility.Collapsed};
  internal Button Header{get;private set;}
  internal Border Detail{get;private set;}
  private Thickness headerStroke,detailStroke,headerMargin;
  private CornerRadius headerCorners,detailCorners;
  private VerticalAlignment headerAlignment;
  internal bool Connected=>Shape.Visibility==Visibility.Visible&&Header!=null&&Detail?.Visibility==Visibility.Visible&&Header.BorderThickness.Bottom==0;
  internal OnexConnectedCardOutline(Grid owner){this.owner=owner;owner.Children.Add(Shape);Canvas.SetZIndex(Shape,10);}
  private void Restore()
  {
   if(Header!=null){Header.BorderThickness=headerStroke;Header.CornerRadius=headerCorners;Header.Margin=headerMargin;Header.VerticalAlignment=headerAlignment;}
   if(Detail!=null){Detail.BorderThickness=detailStroke;Detail.CornerRadius=detailCorners;}
  }
  internal void Set(Button header,Border detail,Brush stroke)
  {
   if(!ReferenceEquals(Header,header))
   {
    Restore();Header=header;Detail=detail;
    if(header!=null){headerStroke=header.BorderThickness;headerCorners=header.CornerRadius;headerMargin=header.Margin;headerAlignment=header.VerticalAlignment;detailStroke=detail.BorderThickness;detailCorners=detail.CornerRadius;}
   }
   if(Header==null||Detail==null||Detail.Visibility!=Visibility.Visible){Shape.Visibility=Visibility.Collapsed;return;}
   double edge=Math.Max(1,headerStroke.Left);int column=Grid.GetColumn(Header),span=Grid.GetColumnSpan(Header);bool wide=span>=owner.ColumnDefinitions.Count;
   Header.BorderBrush=Detail.BorderBrush=stroke;Header.BorderThickness=new Thickness(edge,edge,edge,0);
   Header.CornerRadius=new CornerRadius(headerCorners.TopLeft,headerCorners.TopRight,0,0);
   Header.Margin=new Thickness(headerMargin.Left,headerMargin.Top,headerMargin.Right,0);Header.VerticalAlignment=VerticalAlignment.Stretch;
   Detail.BorderThickness=new Thickness(edge);Detail.CornerRadius=new CornerRadius(column==0?0:detailCorners.TopLeft,wide||column>0?0:detailCorners.TopRight,detailCorners.BottomRight,detailCorners.BottomLeft);
   Grid.SetRow(Shape,Grid.GetRow(Detail));Grid.SetColumn(Shape,column);Grid.SetColumnSpan(Shape,span);
   Shape.Height=edge;Shape.Margin=new Thickness(edge,0,edge,0);Shape.Background=Detail.Background;Shape.Visibility=Visibility.Visible;
  }
 }
 public sealed partial class GamingWidget
 {
  private OnexConnectedCardOutline _onexPerfConnectedOutline,_onexDcConnectedOutline,_onexAcConnectedOutline;
  private void ApplyOnexConnectedCardOutlines()
  {
   if(_onexControllerSections!=null){var active=System.Linq.Enumerable.FirstOrDefault(_onexControllerSections,card=>card.Id==_onexControllerOpen);_onexControllerOutline.Set(active?.Header,active?.Detail,active==null?null:OnexSolidBrush(OnexControllerAccent(active.Id)));}
   if(_onexPerfSections!=null)
   {
    if(_onexPerfConnectedOutline==null)_onexPerfConnectedOutline=new OnexConnectedCardOutline(OnexPerfCardGrid);
    var active=System.Linq.Enumerable.FirstOrDefault(_onexPerfSections,card=>card.Id==_onexPerfOpen);
    _onexPerfConnectedOutline.Set(active?.Header,active?.Detail,active==null?null:OnexPerformanceAccentBrush(active.Id));
   }
   foreach(var form in new[]{_onexDcEditor,_onexAcEditor})
   {
    if(form==null)continue;var outline=form.Source=="dc"?_onexDcConnectedOutline:_onexAcConnectedOutline;
    if(outline==null){outline=new OnexConnectedCardOutline(form.Grid);if(form.Source=="dc")_onexDcConnectedOutline=outline;else _onexAcConnectedOutline=outline;}
    outline.Set(form.Open?.Header,form.Open?.Detail,form.Open==null?null:OnexPerformanceAccentBrush(form.Open.Id));
   }
  }
 }
}
