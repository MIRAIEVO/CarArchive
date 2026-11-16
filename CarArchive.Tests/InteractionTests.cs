using CarArchive.Core;

static class InteractionTests
{
    static void Check(bool value,string message="Interaction assertion failed") { if(!value) throw new Exception(message); }
    public static void Register(Action<string,Func<Task>> test)
    {
        test("Default keyboard workflow keeps group navigation and context-scoped Delete distinct",()=>
        {
            var s=new AppSettings();
            Check(InteractionSettings.Match(s,82,1,"Global",true,false,false)?.Id=="recognize");
            Check(InteractionSettings.Match(s,82,5,"Global",true,false,false)?.Id=="verify");
            Check(InteractionSettings.Match(s,13,1,"Global",true,true,false)?.Id=="archive");
            Check(InteractionSettings.Match(s,46,0,"Groups",false,false,false)?.Id=="removeGroups");
            Check(InteractionSettings.Match(s,46,0,"Photos",false,false,false)?.Id=="removePhotos");
            Check(InteractionSettings.Match(s,46,0,"Global",false,false,false) is null);
            Check(InteractionSettings.Match(s,46,5,"Photos",false,false,false)?.Id=="recyclePhotos");
            return Task.CompletedTask;
        });
        test("Rebound commands cannot steal typing clipboard selection or native undo",()=>
        {
            var s=new AppSettings();
            foreach(var b in new[]{new ShortcutBinding(65,1),new(67,1),new(86,1),new(88,1),new(89,1),new(90,5),new(82,4),new(37,1),new(46,1)})
            { s.Shortcuts["recognize"]=b;Check(InteractionSettings.Match(s,b.Key,b.Modifiers,"Global",true,false,false) is null);Check(InteractionSettings.Match(s,b.Key,b.Modifiers,"Global",false,false,false)?.Id=="recognize"); }
            Check(InteractionSettings.Match(s,46,0,"Groups",true,false,false) is null);return Task.CompletedTask;
        });
        test("Only navigation repeats and dialogs suppress app keyboard commands",()=>
        {
            var s=new AppSettings();Check(InteractionSettings.Match(s,82,1,"Global",false,false,true) is null);
            Check(InteractionSettings.Match(s,39,2,"Global",false,false,true)?.Id=="next");
            Check(InteractionSettings.Match(s,46,5,"Groups",false,false,true) is null);
            Check(InteractionSettings.Match(s,82,1,"Global",false,false,false,true) is null);return Task.CompletedTask;
        });
        test("Suggestion navigation has priority while modified archive remains available",()=>
        {
            var s=new AppSettings();s.Shortcuts["recognize"]=new(13,0);
            Check(InteractionSettings.Match(s,13,0,"Global",false,true,false) is null);
            s.Shortcuts["recognize"]=new(40,0);Check(InteractionSettings.Match(s,40,0,"Global",false,true,false) is null);
            Check(InteractionSettings.Match(s,13,1,"Global",true,true,false)?.Id=="archive");return Task.CompletedTask;
        });
        test("Shortcut validation detects default conflicts and rejects reserved system combinations",()=>
        {
            foreach(var b in new[]{new ShortcutBinding(115,2),new(9,2),new(9,6),new(27,1),new(27,5),new(46,3),new(46,7),new(9,3),new(27,2),new(32,2),new(44,0),new(91,0),new(300,1)}) Check(InteractionSettings.Validate("recognize",b,InteractionSettings.Defaults()) is not null);
            Check(InteractionSettings.Validate("recognize",new(13,1),new Dictionary<string,ShortcutBinding>()) is not null,"Missing explicit entries still retain conflicting defaults.");
            Check(InteractionSettings.Validate("removeGroups",new(46,0),InteractionSettings.Defaults()) is null,"Separate list scopes may share Delete.");return Task.CompletedTask;
        });
        test("Disabled bindings stay disabled and contextual shortcuts roundtrip with confirmation preferences",()=>
        {
            var path=Path.Combine(Path.GetTempPath(),"CarArchive-interactions-"+Guid.NewGuid().ToString("N"));var store=new StateStore(path);var state=new WorkspaceState { SchemaVersion=2 };
            state.Settings.Shortcuts["recognize"]=new(0,0);state.Settings.Shortcuts["next"]=new(78,3);state.Settings.SkippedConfirmations.Add("recyclePhotos");state.Settings.OptimizeRecognitionTokens=true;store.Save(state);
            var restored=store.Load();Check(InteractionSettings.Get(restored.Settings,"recognize")?.Key==0);Check(InteractionSettings.Match(restored.Settings,78,3,"Global",false,false,false)?.Id=="next");
            Check(restored.Settings.SkippedConfirmations.SetEquals(new[]{"recyclePhotos"}) && restored.Settings.OptimizeRecognitionTokens);return Task.CompletedTask;
        });
        test("Legacy null interaction settings normalize without affecting recognition mode",()=>
        {
            var s=new AppSettings { Shortcuts=null!,SkippedConfirmations=null!,Mode=RecognitionMode.Accurate };InteractionSettings.Normalize(s);
            Check(s.Shortcuts.Count==InteractionSettings.Commands.Length && s.SkippedConfirmations.Count==0 && s.Mode==RecognitionMode.Accurate);return Task.CompletedTask;
        });
    }
}
