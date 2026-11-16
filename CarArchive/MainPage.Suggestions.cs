using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;

namespace CarArchive;
public sealed partial class MainPage
{
    private readonly Flyout suggestionsFlyout=new();
    private ListView? suggestionsList;
    private AutoSuggestBox? suggestionOwner;
    private IReadOnlyList<CategorySuggestion> suggestionResults=[];
    private FlyoutPlacementMode suggestionPlacement;
    private bool suggestionClosing;
    private long suggestionRequestVersion;
    private Action? pendingSuggestionOpen;
    private AutoSuggestBox? pendingSuggestionOwner;
    private int suggestionOpenCount;
    private void WireSuggestions(AutoSuggestBox box,Action open,Action close)
    {
        box.ItemsSource=null;box.IsSuggestionListOpen=false;
        box.GotFocus+=(_,_)=> { if(box.FocusState!=FocusState.Pointer) { var group=displayed?.Id;DispatcherQueue.TryEnqueue(()=> { if(group==displayed?.Id && InputFocused(box)) open(); }); } };
        box.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((_,e)=> { if(IsInsideSuggestionInput(box,e.OriginalSource as DependencyObject)) { var group=displayed?.Id;DispatcherQueue.TryEnqueue(()=> { if(group==displayed?.Id && InputFocused(box)) open(); }); } }),true);
        box.PreviewKeyDown+=(_,e)=>
        {
            if(!SuggestionOpen(box)) return;
            if(e.Key==VirtualKey.Escape) { close();e.Handled=true; }
            else if(e.Key is VirtualKey.Up or VirtualKey.Down)
            { MoveSuggestionSelection(e.Key==VirtualKey.Down?1:-1);e.Handled=true; }
            else if(e.Key==VirtualKey.Enter && suggestionsList?.SelectedItem is CategorySuggestion item) { AcceptSuggestion(box,item);e.Handled=true; }
        };
        box.Unloaded+=(_,_)=>CloseSuggestionSurface(box);
        if(suggestionsList is not null) return;
        suggestionsList=new ListView { IsItemClickEnabled=true,SelectionMode=ListViewSelectionMode.Single,MaxHeight=200,IsTabStop=false,AllowFocusOnInteraction=false,Padding=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Stretch };
        var style=new Style(typeof(ListViewItem));style.Setters.Add(new Setter(ListViewItem.MinHeightProperty,40d));style.Setters.Add(new Setter(ListViewItem.PaddingProperty,new Thickness(8,3,8,3)));style.Setters.Add(new Setter(ListViewItem.CornerRadiusProperty,new CornerRadius(4)));style.Setters.Add(new Setter(ListViewItem.IsTabStopProperty,false));style.Setters.Add(new Setter(ListViewItem.AllowFocusOnInteractionProperty,false));suggestionsList.ItemContainerStyle=style;
        suggestionsList.ItemClick+=(_,e)=> { if(suggestionOwner is { } owner && e.ClickedItem is CategorySuggestion item) AcceptSuggestion(owner,item); };
        suggestionsFlyout.Content=suggestionsList;
        var presenter=new Style(typeof(FlyoutPresenter));presenter.Setters.Add(new Setter(FlyoutPresenter.PaddingProperty,new Thickness(6)));presenter.Setters.Add(new Setter(FlyoutPresenter.CornerRadiusProperty,new CornerRadius(8)));presenter.Setters.Add(new Setter(FlyoutPresenter.MinWidthProperty,0d));presenter.Setters.Add(new Setter(FlyoutPresenter.MaxWidthProperty,600d));suggestionsFlyout.FlyoutPresenterStyle=presenter;
        suggestionsFlyout.Closed+=(_,_)=>
        {
            suggestionClosing=false;suggestionOwner=null;suggestionResults=[];
            var next=pendingSuggestionOpen;pendingSuggestionOpen=null;pendingSuggestionOwner=null;
            if(next is not null) DispatcherQueue.TryEnqueue(()=>next());
            else { folderSuggestionsActive=false;brandSuggestionsActive=false; }
        };
        SizeChanged+=(_,_)=>CloseAllSuggestions();
        void CloseOnMovement(object? sender,ScrollViewerViewChangingEventArgs e) {if(sender is ScrollViewer view && (Math.Abs(e.NextView.HorizontalOffset-view.HorizontalOffset)>.5 || Math.Abs(e.NextView.VerticalOffset-view.VerticalOffset)>.5))CloseAllSuggestions();}
        EditorScroll.ViewChanging+=CloseOnMovement;ArchiveOptionsScroll.ViewChanging+=CloseOnMovement;
    }
    private static bool IsInsideSuggestionInput(AutoSuggestBox box,DependencyObject? source)
    { if(VisualChild<TextBox>(box) is not { } input) return false;for(var node=source;node is not null;node=VisualTreeHelper.GetParent(node)) { if(ReferenceEquals(node,input)) return true;if(ReferenceEquals(node,box)) break; }return false; }
    private static bool InputFocused(AutoSuggestBox box)=>VisualChild<TextBox>(box) is { } input && input.FocusState!=FocusState.Unfocused;
    private bool SuggestionOpen(AutoSuggestBox box)=>!suggestionClosing && ReferenceEquals(suggestionOwner,box) && suggestionResults.Count>0;
    private IReadOnlyList<CategorySuggestion> SuggestionItems(AutoSuggestBox box)=>SuggestionOpen(box)?suggestionResults:[];
    private Popup? SuggestionPopup(AutoSuggestBox box)=>SuggestionOpen(box)?VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).FirstOrDefault(p=>p.Child is { } child && ReferenceEquals(VisualChild<ListView>(child),suggestionsList)):null;
    private ListView? SuggestionList(AutoSuggestBox box)=>SuggestionOpen(box)?suggestionsList:null;
    private void CloseAllSuggestions() { CloseFolderSuggestions();CloseBrandSuggestions();pendingSuggestionOpen=null;pendingSuggestionOwner=null;suggestionRequestVersion++; }
    private void CloseSuggestionSurface(AutoSuggestBox box)
    {
        box.IsSuggestionListOpen=false;box.ItemsSource=null;
        // Only the active surface owns the flyout. A close from the other input must not dismiss it.
        if(ReferenceEquals(pendingSuggestionOwner,box)) { pendingSuggestionOpen=null;pendingSuggestionOwner=null;suggestionRequestVersion++; }
        if(!ReferenceEquals(suggestionOwner,box)) return;
        pendingSuggestionOpen=null;pendingSuggestionOwner=null;suggestionRequestVersion++;suggestionOwner=null;suggestionResults=[];suggestionClosing=true;suggestionsFlyout.Hide();
    }
    private void MoveSuggestionSelection(int direction)
    {
        if(suggestionsList is null || suggestionResults.Count==0) return;
        var current=suggestionsList.SelectedIndex;
        suggestionsList.SelectedIndex=current<0?(direction>0?0:suggestionResults.Count-1):Math.Clamp(current+direction,0,suggestionResults.Count-1);
        suggestionsList.ScrollIntoView(suggestionsList.SelectedItem);
    }
    private void AcceptSuggestion(AutoSuggestBox box,CategorySuggestion item)
    { if(!SuggestionOpen(box) || !suggestionResults.Contains(item)) return;if(ReferenceEquals(box,FolderEdit)) ChooseCategory(item);else ChooseBrand(item); }
    private void PresentSuggestions(AutoSuggestBox box,IReadOnlyList<CategorySuggestion> matches)
    {
        box.IsSuggestionListOpen=false;box.ItemsSource=null;
        if(matches.Count==0 || string.IsNullOrWhiteSpace(box.Text)) { CloseSuggestionSurface(box);return; }
        if(VisualChild<TextBox>(box) is not { } input || XamlRoot is null) return;
        var group=displayed?.Id;var version=++suggestionRequestVersion;
        var bounds=input.TransformToVisual(this).TransformBounds(new Rect(0,0,input.ActualWidth,input.ActualHeight));
        var available=XamlRoot.Size.Width;var desired=Math.Min(420,Math.Max(input.ActualWidth,260));
        var placement=bounds.Left>=250?FlyoutPlacementMode.LeftEdgeAlignedTop:available-bounds.Right>=250?FlyoutPlacementMode.RightEdgeAlignedTop:FlyoutPlacementMode.BottomEdgeAlignedLeft;
        var width=placement==FlyoutPlacementMode.LeftEdgeAlignedTop?Math.Min(desired,bounds.Left-12):placement==FlyoutPlacementMode.RightEdgeAlignedTop?Math.Min(desired,available-bounds.Right-12):Math.Min(desired,available-24);
        var height=Math.Min(200,Math.Max(40,XamlRoot.Size.Height-bounds.Top-28));
        if(placement==FlyoutPlacementMode.BottomEdgeAlignedLeft) height=Math.Min(200,Math.Max(40,XamlRoot.Size.Height-bounds.Bottom-28));
        var keep=!suggestionClosing && ReferenceEquals(suggestionOwner,box) && suggestionPlacement==placement;
        if(keep) { UpdateSuggestionItems(box,matches,width,height);return; }
        void Show()
        {
            if(version!=suggestionRequestVersion || categoryClosed || group!=displayed?.Id || displayed?.Editable!=true || !InputFocused(box)) return;
            suggestionOwner=box;suggestionPlacement=placement;UpdateSuggestionItems(box,matches,width,height);
            suggestionsFlyout.OverlayInputPassThroughElement=input;
            // Native placement, clipping and transitions honor the current theme and system animation setting.
            suggestionsFlyout.ShowAt(input,new FlyoutShowOptions { Placement=placement,ShowMode=FlyoutShowMode.Transient });suggestionOpenCount++;
        }
        if(suggestionClosing) { pendingSuggestionOpen=Show;pendingSuggestionOwner=box;return; }
        if(suggestionOwner is not null) { pendingSuggestionOpen=Show;pendingSuggestionOwner=box;suggestionClosing=true;suggestionsFlyout.Hide();return; }
        Show();
    }
    private void UpdateSuggestionItems(AutoSuggestBox box,IReadOnlyList<CategorySuggestion> matches,double width,double height)
    {
        var selected=(suggestionsList!.SelectedItem as CategorySuggestion)?.TargetPath;
        suggestionsList.Width=Math.Max(140,width);suggestionsList.MaxHeight=height;suggestionResults=matches.ToArray();suggestionsList.ItemTemplate=box.ItemTemplate;suggestionsList.ItemsSource=suggestionResults;
        suggestionsList.SelectedItem=selected is null?null:suggestionResults.FirstOrDefault(x=>x.TargetPath==selected);
        // Enter remains free-form until the user explicitly navigates to or clicks a suggestion.
        if(suggestionsList.SelectedItem is null) suggestionsList.SelectedIndex=-1;
    }
}
