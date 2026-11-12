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
        brandSuggestionsActive=false;brandsGroupId=null;TargetBrandEdit.IsSuggestionListOpen=false;TargetBrandEdit.ItemsSource=null;
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
        displayed.TargetBrand=sender.Text.Trim();displayed.ManualName=true;VM.MarkTouched(displayed);
        CloseFolderSuggestions();UpdateExistingCategories();UpdateTargetPath();brandSuggestionsActive=true;UpdateBrandSuggestions();
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
        FlushEditor();displayed.TargetBrand=suggestion.BrandDirectory;displayed.ManualName=true;VM.MarkTouched(displayed);
        rendering=true;try { TargetBrandEdit.Text=suggestion.BrandDirectory;UpdateExistingCategories();UpdateTargetPath(); }finally { rendering=false; }
        CloseBrandSuggestions();CloseFolderSuggestions();VM.SaveGroup(displayed);CrashJournal.Trace("Archive.BrandSelected",displayed.Id);
    }
}
