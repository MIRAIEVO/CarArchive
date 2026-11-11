using System.Text.Json;
using CarArchive.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CarArchive;

public sealed partial class MainPage
{
    private IReadOnlyList<CategorySuggestion> categoryItems = [];
    private string categorySignature = "";
    private string? suggestionsGroupId;
    private bool folderSuggestionsActive;
    private bool categoryClosed;
    private CancellationTokenSource? categoryCancellation;
    private int categoryVersion;
    private Task categoryRefresh = Task.CompletedTask;

    private string CategorySignature() => JsonSerializer.Serialize(new
    {
        Root = VM.State.Settings.ArchiveRoot.Trim(), VM.State.Names, VM.State.BrandFolders
    }, StateStore.Json);

    private Task RefreshCategoryCatalogAsync(bool force = false)
    {
        if (categoryClosed) return Task.CompletedTask;
        var signature = CategorySignature();
        if (!force && signature == categorySignature) return categoryRefresh;
        var changed = signature != categorySignature;
        categorySignature = signature;
        categoryCancellation?.Cancel();
        categoryCancellation?.Dispose();
        categoryCancellation = new();
        var ct = categoryCancellation.Token;
        var version = ++categoryVersion;
        var root = VM.State.Settings.ArchiveRoot;
        var names = VM.State.Names.ToArray();
        var brands = new Dictionary<string, string>(VM.State.BrandFolders);
        if (changed)
        {
            categoryItems = [];
            CloseFolderSuggestions();
            UpdateExistingCategories();
        }
        return categoryRefresh = LoadAsync();

        async Task LoadAsync()
        {
            try
            {
                var result = await Task.Run(() => CategoryCatalogService.Scan(root, names, brands, ct), ct);
                if (categoryClosed || ct.IsCancellationRequested || version != categoryVersion) return;
                categoryItems = result.Items;
                UpdateExistingCategories();
                if (folderSuggestionsActive && FolderEdit.FocusState != FocusState.Unfocused) UpdateFolderSuggestions();
                if (result.Warnings.Count > 0) VM.Status = string.Join("；", result.Warnings.Take(3));
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (categoryClosed || version != categoryVersion) return;
                categorySignature = "";
                VM.Status = "分类建议暂不可用，可继续手动填写：" + error.Message;
            }
        }
    }

    private void CloseFolderSuggestions()
    {
        folderSuggestionsActive = false;
        suggestionsGroupId = null;
        FolderEdit.IsSuggestionListOpen = false;
        FolderEdit.ItemsSource = null;
    }

    private void UpdateFolderSuggestions()
    {
        if (displayed?.Editable != true || displayed != VM.Selected) { CloseFolderSuggestions(); return; }
        var matches = CategoryCatalogService.Match(categoryItems, FolderEdit.Text, TargetBrandEdit.Text);
        suggestionsGroupId = displayed.Id;
        FolderEdit.ItemsSource = matches;
        FolderEdit.IsSuggestionListOpen = matches.Count > 0;
    }

    private void Folder_Changed(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (!initialized || rendering || displayed?.Editable != true || args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        // The inner TextBox may deliver its text event after a programmatic render has finished.
        // Model-equal text is not a user edit and must not mark another group as manually named.
        if (sender.Text.Trim() == displayed.FolderName) return;
        displayed.ManualName = true;
        displayed.FolderName = sender.Text.Trim();
        displayed.AppliedRule = "手动修改名称";
        VM.MarkTouched(displayed);
        displayed.Refresh();
        RuleText.Text = displayed.AppliedRule;
        var wasRendering = rendering;
        rendering = true;
        try { ExistingBox.SelectedItem = null; }
        finally { rendering = wasRendering; }
        UpdateTargetPath();
        folderSuggestionsActive = true;
        UpdateFolderSuggestions();
        editorSaveTimer.Stop();
        editorSaveTimer.Start();
    }

    private void Folder_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is CategorySuggestion suggestion) ChooseCategory(suggestion);
        else CloseFolderSuggestions();
    }

    private void ChooseCategory(CategorySuggestion suggestion)
    {
        if (rendering || displayed?.Editable != true || displayed != VM.Selected || suggestionsGroupId != displayed.Id) return;
        if (categorySignature != CategorySignature() || !categoryItems.Contains(suggestion))
        { CloseFolderSuggestions(); _ = RefreshCategoryCatalogAsync(); return; }
        ApplyCategory(suggestion);
    }

    private void ApplyCategory(CategorySuggestion suggestion)
    {
        if (displayed?.Editable != true || displayed != VM.Selected) return;
        editorSaveTimer.Stop();
        FlushEditor();
        CategoryCatalogService.Apply(displayed, suggestion);
        VM.MarkTouched(displayed);
        CloseFolderSuggestions();
        rendering = true;
        try
        {
            FolderEdit.Text = displayed.FolderName;
            TargetBrandEdit.Text = displayed.TargetBrand;
            RuleText.Text = displayed.AppliedRule;
            UpdateExistingCategories();
            UpdateTargetPath();
        }
        finally { rendering = false; }
        VM.SaveGroup(displayed);
    }

    private void UpdateExistingCategories()
    {
        var wasRendering = rendering;
        rendering = true;
        try
        {
            ExistingBox.ItemsSource = categoryItems.Where(item => item.Exists && item.BrandDirectory.Equals(TargetBrandEdit.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(item => item.FolderName).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
            ExistingBox.SelectedItem = null;
        }
        finally { rendering = wasRendering; }
    }

    private void UpdateTargetPath()
    {
        var root = VM.State.Settings.ArchiveRoot;
        var brand = TargetBrandEdit.Text.Trim();
        var name = FolderEdit.Text.Trim();
        var text = "";
        try
        {
            if (root.Length > 0 && brand.Length > 0 && name.Length > 0)
                text = Path.GetFullPath(Path.Combine(root, NamingService.SafeSegment(brand), NamingService.SafeSegment(name)));
        }
        catch (ArgumentException) { text = "名称或品牌目录无效，请修改后归档"; }
        TargetPathText.Text = text;
        ToolTipService.SetToolTip(TargetPathText, text);
    }
}
