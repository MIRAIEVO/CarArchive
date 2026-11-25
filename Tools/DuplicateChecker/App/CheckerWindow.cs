using System.Diagnostics;
using DuplicateChecker.Core;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace DuplicateChecker;

internal sealed class CheckerWindow : Window
{
    private readonly TextBox target=new(){ToolTip="选择待检查文件所在的目录"};
    private readonly TextBox comparison=new(){ToolTip="选择参考目录，将检查全部子目录"};
    private readonly TextBox output=new(){ToolTip="留空时使用目标文件夹"};
    private readonly CheckBox recursive=new(){Content="检查目标目录的子文件夹",IsChecked=true};
    private readonly ProgressBar progress=new(){Height=6,Margin=new Thickness(0,8,0,0)};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly Button scan=new(){Content="开始检查"};
    private readonly Button move=new(){Content="移动结果",IsEnabled=false};
    private readonly Button load=new(){Content="载入移动记录"};
    private readonly Button resume=new(){Content="继续移动",IsEnabled=false};
    private readonly Button restore=new(){Content="还原文件",IsEnabled=false};
    private readonly Button open=new(){Content="打开结果目录",IsEnabled=false};
    private readonly Button cancel=new(){Content="取消",IsEnabled=false};
    private readonly StackPanel inputs=new();
    private readonly Grid root=new(){Margin=new Thickness(24,18,24,18)};
    private string? run;
    private CancellationTokenSource? operation;
    private bool closeAfterOperation;

    public CheckerWindow()
    {
        Title="重复文件检查 · CarArchive 工具箱";
        Icon=BitmapFrame.Create(new Uri("pack://application:,,,/Assets/DuplicateChecker.ico"),
            BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
        Width=760;MinWidth=550;SizeToContent=SizeToContent.Height;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        for(var i=0;i<4;i++)root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var heading=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,12)};
        heading.Children.Add(new Image{Source=HeaderIcon(),Width=44,Height=44,Margin=new Thickness(0,0,12,0)});
        var headingText=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
        headingText.Children.Add(new TextBlock{Text="重复文件检查",FontSize=23,FontWeight=FontWeights.SemiBold});
        headingText.Children.Add(new TextBlock{Text="按内容查找已归档文件，核对后再移动",FontSize=12,Opacity=.73});
        heading.Children.Add(headingText);
        Add(root,0,heading);
        inputs.Children.Add(PathRow("目标文件夹",target));
        inputs.Children.Add(PathRow("比较文件夹",comparison));
        inputs.Children.Add(PathRow("输出文件夹",output));
        recursive.Margin=new Thickness(0,8,0,2);
        inputs.Children.Add(recursive);
        scan.SetResourceReference(StyleProperty,"AccentButtonStyle");
        var setupActions=new DockPanel{LastChildFill=false,Margin=new Thickness(0,4,0,0)};
        DockPanel.SetDock(load,Dock.Right);
        load.Margin=new Thickness(0,6,0,0);
        setupActions.Children.Add(load);
        setupActions.Children.Add(Buttons(scan,cancel));
        inputs.Children.Add(setupActions);
        Add(root,1,Panel(inputs,Color.FromArgb(20,76,129,204)));
        var summary=new StackPanel{Margin=new Thickness(0,12,0,0)};
        summary.Children.Add(status);
        summary.Children.Add(progress);
        Add(root,2,summary);
        Add(root,3,Buttons(resume,restore,open));
        Content=root;
        status.Text="输出目录留空时，结果将写入目标文件夹。";
        System.Windows.Automation.AutomationProperties.SetName(progress,"查重和文件处理进度");
        target.TextChanged+=InputChanged;
        comparison.TextChanged+=InputChanged;
        output.TextChanged+=InputChanged;
        recursive.Checked+=InputChanged;
        recursive.Unchecked+=InputChanged;
        scan.Click+=async (_,_)=>await RunAsync(ScanAndMoveAsync,true);
        resume.Click+=async (_,_)=>await RunAsync((p,ct)=>ExecuteAsync(false,p,ct));
        restore.Click+=async (_,_)=>await RunAsync(RestoreAsync);
        cancel.Click+=(_,_)=>operation?.Cancel();
        Closing+=(_,args)=>
        {
            if(operation is null)return;
            args.Cancel=true;
            closeAfterOperation=true;
            operation.Cancel();
        };
        load.Click+=async (_,_)=>await RunAsync(LoadAsync);
        open.Click+=(_,_)=>{try{Process.Start(new ProcessStartInfo(run!){UseShellExecute=true});}catch(Exception error){status.Text=error.Message;}};
    }

    private async Task ScanAndMoveAsync(IProgress<ScanProgress> updates,CancellationToken ct)
    {
        var result=await Scanner.RunAsync(target.Text.Trim(),comparison.Text.Trim(),
            string.IsNullOrWhiteSpace(output.Text)?target.Text.Trim():output.Text.Trim(),recursive.IsChecked==true,updates,ct);
        ct.ThrowIfCancellationRequested();
        var existing=result.Files.Count(item=>item.Status==Classification.Existing);
        var missing=result.Files.Count(item=>item.Status==Classification.Missing);
        var unknown=result.Files.Length-existing-missing;
        if(existing+missing>0)
        {
            run=await Task.Run(()=>MoveRun.Create(result),ct);
            await ExecuteAsync(false,updates,ct);
            status.Text=$"?? {existing:N0} ? ?? {missing:N0} ? ??? {unknown:N0}\n"+status.Text;
        }
        else status.Text=$"?????? ? ??? {unknown:N0}";
        progress.Value=100;
        if(result.Warnings.Length>0)status.Text+=$"\n{result.Warnings.Length} ????? ? {result.Warnings[0]}";
    }

    private async Task RestoreAsync(IProgress<ScanProgress> updates,CancellationToken ct)
    {
        if(Confirm("还原文件","将此批已移动文件还原到原位置。原位置有冲突的文件会保留在结果目录。"))
            await ExecuteAsync(true,updates,ct);
    }

    private async Task ExecuteAsync(bool restoring,IProgress<ScanProgress> updates,CancellationToken ct)
    {
        if(run is null)return;
        var summary=await MoveRun.ExecuteAsync(run,restoring,updates,ct);
        status.Text=$"{(restoring?"已还原":"已移动")} {summary.Done:N0} · 待处理 {summary.Pending:N0} · 异常 {summary.Errors:N0}\n{run}";
        if(summary.Errors>0)await AppendRunErrorAsync(run);
    }

    private async Task AppendRunErrorAsync(string folder)
    {
        var first=(await Task.Run(()=>MoveRun.ReadEntries(folder))).FirstOrDefault(item=>item.Error is not null);
        if(first is not null)status.Text+=$"\n{Path.GetFileName(first.Source.Path)} · {first.Error}";
    }

    private async Task LoadAsync(IProgress<ScanProgress> _,CancellationToken ct)
    {
        var folder=await PickAsync();
        if(folder is null)return;
        var entries=await MoveRun.LoadAsync(folder,ct);
        run=folder;
        status.Text=$"已载入 {entries.Length:N0} 条移动记录 · 已移动 {entries.Count(item=>item.State=="Moved"):N0}\n{folder}";
    }

    private async Task RunAsync(Func<IProgress<ScanProgress>,CancellationToken,Task> work,bool scanAndMove=false)
    {
        if(operation is not null)return;
        using var cts=new CancellationTokenSource();
        operation=cts;
        progress.Value=0;
        UpdateControls();
        var updates=new Progress<ScanProgress>(value=>{
            if(operation!=cts)return;
            progress.IsIndeterminate=value.Total<=0 && progress.Value==0;
            if(value.Total>0)
            {
                var fraction=(double)value.Done/value.Total;
                var stage=value.Stage switch
                {
                    "筛选比较文件"=>40*fraction,
                    "确认文件内容"=>40+60*fraction,
                    _=>100*fraction
                };
                progress.Value=Math.Max(progress.Value,stage);
            }
            status.Text=value.Total>0?$"{value.Stage} · {value.Done:N0} / {value.Total:N0}":value.Stage;
        });
        try{await work(updates,cts.Token);}
        catch(OperationCanceledException) when(cts.IsCancellationRequested){status.Text=run is null?"已取消。":"已取消，可继续移动或还原。"+Environment.NewLine+run;}
        catch(Exception error){status.Text=error.Message;}
        finally
        {
            operation=null;
            progress.IsIndeterminate=false;
            UpdateControls();
            if(closeAfterOperation)Close();
        }
    }

    private void UpdateControls()
    {
        var ready=operation is null;
        target.IsEnabled=comparison.IsEnabled=output.IsEnabled=recursive.IsEnabled=scan.IsEnabled=load.IsEnabled=ready;
        resume.IsEnabled=restore.IsEnabled=open.IsEnabled=ready && run is not null;
        cancel.IsEnabled=!ready;
    }

    private void InputChanged(object sender,RoutedEventArgs args)
    {
        progress.Value=0;
        UpdateControls();
        status.Text="目录或检查范围已更改。请重新检查。";
    }

    private static DrawingImage HeaderIcon()
    {
        var drawing=new DrawingGroup();
        var navy=new SolidColorBrush(Color.FromRgb(17,44,86));
        var blue=new SolidColorBrush(Color.FromRgb(108,177,255));
        var frost=new SolidColorBrush(Color.FromRgb(220,240,255));
        var mint=new SolidColorBrush(Color.FromRgb(99,235,205));
        drawing.Children.Add(new GeometryDrawing(navy,null,new RectangleGeometry(new Rect(0,0,64,64),11,11)));
        drawing.Children.Add(new GeometryDrawing(navy,new Pen(blue,4),new RectangleGeometry(new Rect(9,9,32,30),3,3)));
        drawing.Children.Add(new GeometryDrawing(navy,new Pen(frost,4),new RectangleGeometry(new Rect(20,21,36,34),4,4)));
        drawing.Children.Add(new GeometryDrawing(null,new Pen(mint,6.8){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round},Geometry.Parse("M29,38 L36,45 L48,31")));
        drawing.Freeze();
        return new DrawingImage(drawing);
    }

    private static Border Panel(UIElement child,Color tint)=>new()
    {
        Background=new SolidColorBrush(tint),CornerRadius=new CornerRadius(10),Padding=new Thickness(12,8,12,8),Child=child
    };

    private bool Confirm(string title,string message) =>
        MessageBox.Show(this,message,title,MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes;

    private Task<string?> PickAsync()
    {
        var picker=new OpenFolderDialog();
        return Task.FromResult(picker.ShowDialog(this)==true?picker.FolderName:null);
    }

    private Grid PathRow(string label,TextBox field)
    {
        var row=new Grid{Margin=new Thickness(0,8,0,0)};
        row.ColumnDefinitions.Add(new());
        row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var labelText=new TextBlock{Text=label,Margin=new Thickness(0,0,0,4)};
        var fieldGroup=new StackPanel();
        fieldGroup.Children.Add(labelText);
        fieldGroup.Children.Add(field);
        System.Windows.Automation.AutomationProperties.SetName(field,label);
        row.Children.Add(fieldGroup);
        var button=new Button{Content="浏览…",VerticalAlignment=VerticalAlignment.Bottom};
        System.Windows.Automation.AutomationProperties.SetName(button,"浏览"+label);
        button.Click+=async (_,_)=>{
            if(operation is not null)return;
            try{if(await PickAsync() is { } chosen)field.Text=chosen;}
            catch(Exception error){status.Text=error.Message;}
        };
        Grid.SetColumn(button,1);
        row.Children.Add(button);
        return row;
    }

    private static StackPanel Buttons(params Button[] buttons)
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal};
        foreach(var button in buttons){button.Margin=new Thickness(0,6,8,0);row.Children.Add(button);}
        return row;
    }
    private static void Add(Grid grid,int row,FrameworkElement element){Grid.SetRow(element,row);grid.Children.Add(element);}
}
