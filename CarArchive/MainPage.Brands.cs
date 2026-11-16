using CarArchive.Core;
using Microsoft.UI.Xaml.Controls;

namespace CarArchive;

public sealed partial class MainPage
{
    private IReadOnlyList<CategorySuggestion> brandItems=[];
    private CategorySearchIndex brandSearch=new([]);
    private bool brandSuggestionsActive;
    private string? brandsGroupId;
    private void CloseBrandSuggestions()
    {
        brandSuggestionsActive=false;brandsGroupId=null;CloseSuggestionSurface(TargetBrandEdit);
    }
    private void OpenBrandSuggestions()
    {
        if(!initialized || rendering || displayed?.Editable!=true || displayed!=VM.Selected) return;
        CloseFolderSuggestions();brandSuggestionsActive=true;UpdateBrandSuggestions();_=RefreshCategoryCatalogAsync(true);
    }
    private void UpdateBrandSuggestions()
    {
        if(displayed?.Editable!=true || displayed!=VM.Selected) { CloseBrandSuggestions();return; }
        if(string.IsNullOrWhiteSpace(TargetBrandEdit.Text)) { CloseBrandSuggestions();return; }
        brandsGroupId=displayed.Id;PresentSuggestions(TargetBrandEdit,brandSearch.Match(TargetBrandEdit.Text,TargetBrandEdit.Text));
    }
    private void Brand_Changed(AutoSuggestBox sender,AutoSuggestBoxTextChangedEventArgs args)
    {
        if(!initialized || rendering || displayed?.Editable!=true || args.Reason!=AutoSuggestionBoxTextChangeReason.UserInput) return;
        EnglishInput(sender);if(sender.Text.Trim()==displayed.TargetBrand) return;
        var before=VM.CaptureEdits([displayed]);displayed.TargetBrand=sender.Text.Trim();displayed.ManualTargetBrand=true;VM.MarkTouched(displayed);
        VM.RecordEdits(before,new(){displayed.Id},true);CloseFolderSuggestions();UpdateExistingCategories();UpdateTargetPath();brandSuggestionsActive=true;UpdateBrandSuggestions();
        CrashJournal.Trace("Archive.BrandEdited",displayed.Id);editorSaveTimer.Stop();editorSaveTimer.Start();
    }
    private void Brand_QuerySubmitted(AutoSuggestBox sender,AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if(args.ChosenSuggestion is CategorySuggestion chosen) ChooseBrand(chosen);else CloseBrandSuggestions();
    }
    private void ChooseBrand(CategorySuggestion suggestion)
    {
        if(rendering || displayed?.Editable!=true || displayed!=VM.Selected || brandsGroupId!=displayed.Id || !brandItems.Contains(suggestion)) return;
        if(categorySignature!=CategorySignature() || !Directory.Exists(Path.Combine(VM.State.Settings.ArchiveRoot,suggestion.BrandDirectory)))
        { CloseBrandSuggestions();_=RefreshCategoryCatalogAsync(true);return; }
        var before=VM.CaptureEdits([displayed]);FlushEditor();displayed.TargetBrand=suggestion.BrandDirectory;displayed.ManualTargetBrand=true;VM.MarkTouched(displayed);
        rendering=true;try { TargetBrandEdit.Text=suggestion.BrandDirectory;UpdateExistingCategories();UpdateTargetPath(); }finally { rendering=false; }
        VM.RecordEdits(before,new(){displayed.Id});CloseBrandSuggestions();CloseFolderSuggestions();VM.SaveGroup(displayed);CrashJournal.Trace("Archive.BrandSelected",displayed.Id);
    }
}
