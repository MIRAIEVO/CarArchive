using CarArchive.Core;
using System.Text.Json;

static class LocalLearningTests
{
    private static void Check(bool value,string message="Learning assertion failed") { if(!value) throw new Exception(message); }
    private static PhotoGroup Group(string model="Known LX",string series="Known",string brand="Example",string generation="2nd",string code="X2",string from="",string to="")
    {
        var identity=new CarIdentity { Brand=brand,Series=series,Variant=model,Generation=generation,Code=code };
        return new() { Confirmed=identity,LastGeneratedName=from.Length>0?from:model+" ("+generation+")",FolderName=to.Length>0?to:model+" ("+code+")" };
    }
    public static void Register(Action<string,Func<Task>> test)
    {
        test("Learning: only explicit confirmation affects manual mode",()=> {
            var state=new WorkspaceState();state.Settings.Learning=LearningMode.Manual;var group=Group();
            LocalNamingLearning.RecordConfirmed(group,state,false);Check(state.NamingSamples.Count==0 && state.LocalNamingRules.Count==0);
            LocalNamingLearning.RecordConfirmed(group,state,true);Check(state.NamingSamples.Count==1 && state.LocalNamingRules.Single().Kind=="Exact");return Task.CompletedTask;
        });
        test("Learning: confirmed exact name applies locally, but old Names cannot bypass policy",()=> {
            var state=new WorkspaceState();var group=Group();LocalNamingLearning.RecordConfirmed(group,state,true);
            Check(NamingGuideEngine.Generate(group.Confirmed,state).Name=="Known LX (X2)");
            Check(NamingGuideEngine.Generate(new() { Brand="Example",Series="Known",Variant="Known GT",Generation="2nd",Code="X2" },state).Name=="Known GT (2nd)");return Task.CompletedTask;
        });
        test("Learning: repeated same-model confirmations never become family evidence",()=> {
            var state=new WorkspaceState();LocalNamingLearning.RecordConfirmed(Group(),state,true);LocalNamingLearning.RecordConfirmed(Group(generation:"3rd",code:"X3"),state,true);LocalNamingLearning.RecordConfirmed(Group(),state,true);
            Check(state.NamingSamples.Count==2 && state.LocalNamingRules.All(r=>r.Kind=="Exact"));return Task.CompletedTask;
        });
        test("Learning: two configurations propose a family template without auto-applying",()=> {
            var state=new WorkspaceState();LocalNamingLearning.RecordConfirmed(Group(),state,true);var pending=LocalNamingLearning.RecordConfirmed(Group("Known GT"),state,true);
            var rule=pending.Single(r=>r.Kind=="Suffix");Check(rule.Family=="Known" && rule.SampleIds.Count==2 && !rule.Approved);
            var car=Group("Known Sport",generation:"3rd",code:"X3").Confirmed;Check(NamingGuideEngine.Generate(car,state).Name=="Known Sport (3rd)");
            rule.Approved=true;Check(NamingGuideEngine.Generate(car,state).Name=="Known Sport (X3)");rule.Enabled=false;Check(NamingGuideEngine.Generate(car,state).Name=="Known Sport (3rd)");return Task.CompletedTask;
        });
        test("Learning: automatic mode applies eligible family templates, never another series",()=> {
            var state=new WorkspaceState();state.Settings.Learning=LearningMode.Automatic;LocalNamingLearning.RecordConfirmed(Group(),state,false);LocalNamingLearning.RecordConfirmed(Group("Known GT"),state,false);
            Check(state.LocalNamingRules.Single(r=>r.Kind=="Suffix").Approved);
            Check(NamingGuideEngine.Generate(Group("Known Sport",generation:"3rd",code:"X3").Confirmed,state).Name=="Known Sport (X3)");
            Check(NamingGuideEngine.Generate(Group("Other Sport",series:"Other").Confirmed,state).Name=="Other Sport (2nd)");return Task.CompletedTask;
        });
        test("Learning: three distinct families are required for a brand rule",()=> {
            var state=new WorkspaceState();LocalNamingLearning.RecordConfirmed(Group(),state,true);LocalNamingLearning.RecordConfirmed(Group("Other GT","Other"),state,true);
            Check(!state.LocalNamingRules.Any(r=>r.Kind=="Suffix" && r.Family==""));LocalNamingLearning.RecordConfirmed(Group("Third GT","Third"),state,true);
            Check(state.LocalNamingRules.Single(r=>r.Kind=="Suffix" && r.Family=="").SampleIds.Count==3);return Task.CompletedTask;
        });
        test("Learning: changed engine numbers and omitted packages are never generalized",()=> {
            var state=new WorkspaceState();var bad=Group("Known 580",to:"Known 500 (X2)");LocalNamingLearning.RecordConfirmed(bad,state,true);
            Check(state.LocalNamingRules.Single().Conflict.Length>0 && NamingGuideEngine.Generate(bad.Confirmed,state).Name=="Known 580 (2nd)");
            Check(LocalNamingLearning.ValidateExact(new() { Brand="Example",Variant="Known LX Special Edition" },"Known LX").Length>0);return Task.CompletedTask;
        });
        test("Learning: conflicting suffix evidence blocks both proposed scopes",()=> {
            var state=new WorkspaceState();LocalNamingLearning.RecordConfirmed(Group(),state,true);LocalNamingLearning.RecordConfirmed(Group("Known GT"),state,true);
            LocalNamingLearning.RecordConfirmed(Group("Known Sport",to:"Known Sport (II)"),state,true);LocalNamingLearning.RecordConfirmed(Group("Known Race",to:"Known Race (II)"),state,true);
            Check(state.LocalNamingRules.Count(r=>r.Kind=="Suffix" && r.Conflict.Length>0)==2);return Task.CompletedTask;
        });
        test("Learning: BMW and 911 explicit requirements override confirmed exact names",()=> {
            var state=new WorkspaceState();var bmw=Group("330i","3 Series","BMW",code:"G20",from:"330i (G20 LCI)",to:"330i (G20 Facelift)");bmw.Confirmed.Phase="Facelift";LocalNamingLearning.RecordConfirmed(bmw,state,true);
            Check(state.LocalNamingRules.Single().Conflict.Length>0);Check(NamingGuideEngine.Generate(bmw.Confirmed,state).Name.EndsWith("LCI)"));
            var car=new CarIdentity { Brand="Porsche",Series="911",Variant="911 Carrera",Code="991",Generation="7th",Phase="Facelift" };Check(LocalNamingLearning.ValidateExact(car,"911 Carrera (991.1)").Length>0);return Task.CompletedTask;
        });
        test("Learning: modified rules cannot insert drivetrain badges or duplicate Body",()=> {
            Check(LocalNamingLearning.ValidateExact(new() { Brand="BMW",Variant="330i" },"330i xDrive (G20)").Length>0);
            Check(LocalNamingLearning.ValidateExact(new() { Brand="Example",Variant="Known Coupe" },"Known Coupe Coupe (2nd)").Length>0);
            try { LocalNamingLearning.ValidateRule(new() { Brand="Example",Kind="Vocabulary",From="500",To="580" });throw new Exception("Expected rejected number rule"); } catch(ArgumentException) { }
            return Task.CompletedTask;
        });
        test("Learning: missing suffix fields do not borrow literals from prior cars",()=> {
            var state=new WorkspaceState();state.LocalNamingRules.Add(new() { Brand="Example",Family="Known",Kind="Suffix",From="{generation} {phase}",To="{code} {phase}",Approved=true });
            Check(NamingGuideEngine.Generate(new() { Brand="Example",Series="Known",Variant="Known GT",Generation="3rd" },state).Name=="Known GT (3rd)");return Task.CompletedTask;
        });
        test("Learning: approved vocabulary keeps complete variant and body informational",()=> {
            var state=new WorkspaceState();state.LocalNamingRules.Add(new() { Brand="Example",Family="Known",Kind="Vocabulary",From="Wagon",To="Estate",Approved=true });
            var car=new CarIdentity { Brand="Example",Series="Known",Variant="Known GT Wagon Special Edition",Body="Coupe",Generation="3rd" };
            Check(NamingGuideEngine.Generate(car,state).Name=="Known GT Estate Special Edition (3rd)");return Task.CompletedTask;
        });
        test("Learning: disable/delete changes generation, and export includes traceable sources",()=> {
            var state=new WorkspaceState();var group=Group();LocalNamingLearning.RecordConfirmed(group,state,true);state.LocalNamingRules.Single().Enabled=false;
            Check(NamingGuideEngine.Generate(group.Confirmed,state).Name=="Known LX (2nd)");using var doc=JsonDocument.Parse(LocalNamingLearning.Export(state));Check(doc.RootElement.GetProperty("NamingSamples").GetArrayLength()==1);
            state.LocalNamingRules.Clear();Check(NamingGuideEngine.Generate(group.Confirmed,state).Name=="Known LX (2nd)");return Task.CompletedTask;
        });
        test("Learning: replacing a confirmation invalidates stale family support",()=> {
            var state=new WorkspaceState();state.Settings.Learning=LearningMode.Automatic;
            LocalNamingLearning.RecordConfirmed(Group(),state,true);LocalNamingLearning.RecordConfirmed(Group("Known GT"),state,true);
            var rule=state.LocalNamingRules.Single(r=>r.Kind=="Suffix");Check(rule.Conflict.Length==0 && rule.Approved);
            LocalNamingLearning.RecordConfirmed(Group("Known GT",to:"Known GT (2nd)"),state,true);
            Check(rule.SampleIds.Count==1 && rule.Conflict.Length>0);
            Check(NamingGuideEngine.Generate(Group("Known Sport",generation:"3rd",code:"X3").Confirmed,state).Name=="Known Sport (3rd)");return Task.CompletedTask;
        });
        test("Learning: disabling a conflicting rule restores the remaining approved pattern",()=> {
            var state=new WorkspaceState();LocalNamingLearning.RecordConfirmed(Group(),state,true);LocalNamingLearning.RecordConfirmed(Group("Known GT"),state,true);
            LocalNamingLearning.RecordConfirmed(Group("Known Sport",to:"Known Sport (II)"),state,true);LocalNamingLearning.RecordConfirmed(Group("Known Race",to:"Known Race (II)"),state,true);
            var rules=state.LocalNamingRules.Where(r=>r.Kind=="Suffix").ToList();Check(rules.All(r=>r.Conflict.Length>0));
            var keep=rules.Single(r=>r.To=="{code} {phase}");keep.Approved=true;rules.Single(r=>r!=keep).Enabled=false;LocalNamingLearning.Reconcile(state);
            Check(keep.Conflict.Length==0 && NamingGuideEngine.Generate(Group("Known New",generation:"3rd",code:"X3").Confirmed,state).Name=="Known New (X3)");return Task.CompletedTask;
        });
        test("Learning: exact aliases preserve full body words and normalize omitted drivetrain",()=> {
            Check(LocalNamingLearning.ValidateExact(new() { Brand="Example",Variant="Known GT Wagon" },"Known GT").Length>0);
            Check(LocalNamingLearning.ValidateExact(new() { Brand="Example",Variant="Known GT Wagon" },"Known GT Estate (2nd)").Length==0);
            Check(LocalNamingLearning.ValidateExact(new() { Brand="BMW",Variant="330i xDrive",Code="G20" },"330i (G20)").Length==0);return Task.CompletedTask;
        });
        test("Learning: different body types never become vocabulary aliases",()=> {
            var state=new WorkspaceState();LocalNamingLearning.RecordConfirmed(Group("Known GT Coupe",from:"Known GT Coupe (2nd)",to:"Known GT Wagon (2nd)"),state,true);
            LocalNamingLearning.RecordConfirmed(Group("Known LX Coupe",from:"Known LX Coupe (2nd)",to:"Known LX Wagon (2nd)"),state,true);
            Check(state.LocalNamingRules.All(r=>r.Kind=="Exact" && r.Conflict.Length>0));
            try { LocalNamingLearning.ValidateRule(new() { Kind="Vocabulary",Brand="Example",From="Coupe",To="Wagon" });throw new Exception("Expected different-body rejection"); } catch(ArgumentException) { }
            return Task.CompletedTask;
        });
        test("Learning: narrower family rule wins over a supported brand rule",()=> {
            var state=new WorkspaceState();state.LocalNamingRules.Add(new() { Brand="Example",Kind="Suffix",From="{generation} {phase}",To="{roman} {phase}",Approved=true });
            state.LocalNamingRules.Add(new() { Brand="Example",Family="Known",Kind="Suffix",From="{generation} {phase}",To="{code} {phase}",Approved=true });LocalNamingLearning.Reconcile(state);
            Check(NamingGuideEngine.Generate(Group("Known Sport",generation:"3rd",code:"X3").Confirmed,state).Name=="Known Sport (X3)");
            Check(NamingGuideEngine.Generate(Group("Other Sport",series:"Other",generation:"3rd",code:"Y3").Confirmed,state).Name=="Other Sport (III)");return Task.CompletedTask;
        });
        test("Learning: orphaned exact sources stop applying after sample removal",()=> {
            var state=new WorkspaceState();var group=Group();LocalNamingLearning.RecordConfirmed(group,state,true);state.NamingSamples.Clear();LocalNamingLearning.Reconcile(state);
            Check(state.LocalNamingRules.Single().Conflict.Length>0 && NamingGuideEngine.Generate(group.Confirmed,state).Name=="Known LX (2nd)");return Task.CompletedTask;
        });
        test("Learning: new collections and workspace lineage persist through incremental header saves",()=> {
            var root=Path.Combine(Path.GetTempPath(),"CarArchive-learning-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"workspace-id.txt"),"test-lineage");
            var store=new StateStore(root);var state=store.Load();Check(state.WorkspaceId=="test-lineage");LocalNamingLearning.RecordConfirmed(Group(),state,true);store.Save(state);
            var restored=new StateStore(root).Load();Check(restored.WorkspaceId==state.WorkspaceId && restored.NamingSamples.Count==1 && restored.LocalNamingRules.Count==1);return Task.CompletedTask;
        });
    }
}
