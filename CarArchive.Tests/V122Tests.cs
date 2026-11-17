using CarArchive.Core;
public static class V122Tests
{
    public static void Register(Action<string,Func<Task>> test)
    {
        void Check(bool value){if(!value)throw new Exception("1.2.2 regression");}
        test("122: generated names remove colon and slash without changing original identity",()=>
        {
            var car=new CarIdentity {Brand="Example",Series="Model",Variant="Model: Sport/Touring",Generation="3"};
            var name=NamingService.DefaultName(car);Check(name=="Model SportTouring (3rd)" && car.Variant=="Model: Sport/Touring");return Task.CompletedTask;
        });
        test("122: global upload selection overrides legacy per-group flags and snapshots inputs",()=>
        {
            var a=new Photo{Id="a",Path="a.jpg"};var b=new Photo{Id="b",Path="b.jpg",Crop=new(.1,.1,.5,.5)};
            var group=new PhotoGroup{Photos=[a,b],LastViewedId="a",UploadMultiple=true};
            Check(UploadSelection.Select(group,false).Single().Id=="a");var selected=UploadSelection.Select(group,true);Check(selected.Single().Id=="b" && !ReferenceEquals(selected[0],b));return Task.CompletedTask;
        });
        test("122: configuration migration preserves settings, key and learning but excludes all group data",async()=>
        {
            var root=Path.Combine(Path.GetTempPath(),"CarArchive-profile-"+Guid.NewGuid().ToString("N"));var source=Path.Combine(root,"old");var target=Path.Combine(root,"new");
            Directory.CreateDirectory(source);var store=new StateStore(source);var state=new WorkspaceState{SchemaVersion=2};state.Settings.ArchiveRoot="D:\\Archive";state.Settings.UploadMultiple=true;state.Settings.ImportVideos=false;
            state.Groups.Add(new(){Photos=[new(){Path="original-private-photo.jpg"}]});state.Names.Add(new("known","Example","Model (2nd)"));store.Save(state);store.RecordNamingClick(new(){FinalName="Model (2nd)"});store.SaveJob(new(){GroupId=state.Groups[0].Id});
            Directory.CreateDirectory(Path.Combine(source,"cache"));File.WriteAllText(Path.Combine(source,"cache","excluded.jpg"),"private");File.WriteAllBytes(Path.Combine(source,"gemini-key.bin"),[1,2,3]);
            await PortableData.ImportConfigurationAsync(source,target);var imported=new StateStore(target);var profile=imported.Load();
            Check(profile.Groups.Count==0&&profile.Assets.Count==0&&profile.Names.Count==1&&imported.NamingClicks().Count==1&&imported.Jobs().Count==0);
            Check(profile.Settings.ArchiveRoot==state.Settings.ArchiveRoot&&profile.Settings.UploadMultiple&&!profile.Settings.ImportVideos&&profile.WorkspaceId!=state.WorkspaceId);
            Check(!Directory.Exists(Path.Combine(target,"cache"))&&File.ReadAllBytes(Path.Combine(target,"gemini-key.bin")).SequenceEqual(new byte[]{1,2,3})&&store.Load().Groups.Count==1);
            imported.ClearNamingClicks();Check(imported.NamingClicks().Count==0);
        });
    }
}
