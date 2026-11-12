using System.Net;
using System.Text.Json;
using CarArchive.Core;

static class NamingTests
{
    static void Check(bool condition,string message="Naming assertion failed") { if(!condition) throw new Exception(message); }
    static string Area() { var root=Path.Combine(Path.GetTempPath(),"CarArchive-naming-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);return root; }
    static NamingGuide Build(string root,WorkspaceState state,params (string Brand,string Folder,CarIdentity? Identity)[] rows)
    {
        state.Settings.ArchiveRoot=root;
        foreach(var row in rows) { Directory.CreateDirectory(Path.Combine(root,row.Brand,row.Folder));if(row.Identity is not null) state.Names.Add(new(row.Identity.NamingKey,row.Identity.Brand,row.Folder)); }
        return NamingGuideBuilder.Build(root,state);
    }
    public static void Register(Action<string,Func<Task>> test)
    {
        test("Naming: Body is informational, and complete names and naming keys do not depend on it",()=> {
            var car=new CarIdentity { Brand="Example",Series="Model",Variant="Model Coupe",Body="Coupe",Generation="3rd Generation",Code="X100",Package="Coupe",Phase="Facelift" };
            var before=car.NamingKey;Check(NamingService.DefaultName(car)=="Model Coupe (3rd Facelift)");car.Body="Sedan";
            Check(before==car.NamingKey && NamingService.DefaultName(car)=="Model Coupe (3rd Facelift)");return Task.CompletedTask;
        });
        test("Naming: ordinal normalization handles English, roman, numeric and ordinal exceptions without chassis guesses",()=> {
            foreach(var (raw,expected) in new[]{("1st Generation","1st"),("2","2nd"),("third generation","3rd"),("Mk VII","7th"),("11","11th"),("12","12th"),("13","13th"),("21","21st"),("3th","3rd")}) Check(NamingText.Generation(raw)==expected,raw);
            Check(NamingText.Generation("W223")=="" && NamingText.Generation("80 Series")=="");
            var result=NamingGuideEngine.Generate(new() { Brand="New",Variant="Model",Code="X100",Phase="Facelift" },new());
            Check(result.Name=="Model" && result.Warnings.Count>0);return Task.CompletedTask;
        });
        test("Naming: complete-name fallback strips official brand, keeps unknown trim conservative and omits pre-facelift",()=> {
            Check(NamingService.DefaultName(new() { Brand="Example",Series="Model",OfficialName="Example Model Shooting Brake",Body="Shooting Brake",Generation="2nd",Phase="Pre-Facelift" })=="Model Shooting Brake (2nd)");
            Check(NamingService.DefaultName(new() { Brand="Example",Series="Model",Variant="Model LX",OfficialName="Example Model",Generation="2nd" })=="Model LX (2nd)");return Task.CompletedTask;
        });
        test("Naming: known family inherits most common style while an unseen family uses the ordinal default",()=> {
            var state=new WorkspaceState();var guide=Build(Area(),state,("Example","Known LX (X1)",new() { Brand="Example",Series="Known",Variant="Known LX",Code="X1",Generation="1st" }),
                ("Example","Known Sport (X2)",new() { Brand="Example",Series="Known",Variant="Known Sport",Code="X2",Generation="2nd" }),
                ("Example","Known (1st)",new() { Brand="Example",Series="Known",Variant="Known",Generation="1st" }));
            Check(NamingGuideEngine.Generate(new() { Brand="Example",Series="Known",Variant="Known GT",Generation="3rd",Code="X3",Phase="Facelift" },state,guide).Name=="Known GT (X3 Facelift)");
            Check(NamingGuideEngine.Generate(new() { Brand="Example",Series="Unseen",Variant="Unseen GT",Generation="2nd",Code="Z9" },state,guide).Name=="Unseen GT (2nd)");return Task.CompletedTask;
        });
        test("Naming: tied formats use the last confirmed format; unresolvable ties use the explicit default",()=> {
            var state=new WorkspaceState();var guide=Build(Area(),state,("Example","Known (1st)",new() { Brand="Example",Series="Known",Variant="Known",Generation="1st" }),
                ("Example","Known (X1)",new() { Brand="Example",Series="Known",Variant="Known",Code="X1",Generation="1st" }));
            Check(NamingGuideEngine.Generate(new() { Brand="Example",Series="Known",Variant="Known",Code="X2",Generation="2nd" },state,guide).Name=="Known (X2)");
            guide=Build(Area(),new(),("Example","Known (1st)",null),("Example","Known (X1)",null));
            Check(guide.Profiles.Any(p=>p.Resolution.Contains("无可区分")));return Task.CompletedTask;
        });
        test("Naming: unmapped legacy folders remain usable through exact model aliases without fuzzy family propagation",()=> {
            var state=new WorkspaceState();var guide=Build(Area(),state,("Unmapped","Special Roadster",null));
            var result=NamingGuideEngine.Generate(new() { Brand="Unmapped",Series="Different API series label",Variant="Special Roadster",Generation="1st" },state,guide);
            Check(result.Name=="Special Roadster (1st)",result.Name);
            Check(NamingGuideEngine.Generate(new() { Brand="Unmapped",Series="Different API series label",Variant="Special Roadster GT",Generation="1st" },state,guide).Name=="Special Roadster GT (1st)");return Task.CompletedTask;
        });
        test("Naming: BMW uses LCI and never reintroduces a historical duplicated body phrase",()=> {
            var state=new WorkspaceState();var guide=Build(Area(),state,("BMW","6 Series Coupe Coupe (F13 Facelift)",new() { Brand="BMW",Series="6 Series",Variant="6 Series Coupe",Body="Coupe",Code="F13",Generation="3rd" }));
            var result=NamingGuideEngine.Generate(new() { Brand="BMW",Series="6 Series",Variant="6 Series Coupe",Body="Coupe",Generation="3rd",Code="F13",Phase="Facelift" },state,guide);
            Check(result.Name=="6 Series Coupe (F13 LCI)",result.Name);Check(guide.Inventory.Single().Warning.Length>0);return Task.CompletedTask;
        });
        test("Naming: model-specific observed facelift vocabulary is retained for brands beyond BMW",()=> {
            var state=new WorkspaceState();var guide=Build(Area(),state,("Alpina","B4 GT Gran Coupe (G26 LCI)",new() { Brand="Alpina",Series="B4",Variant="B4 GT Gran Coupe",Code="G26",Generation="2nd",Phase="Facelift" }));
            var result=NamingGuideEngine.Generate(new() { Brand="Alpina",Series="B4",Variant="B4 GT Gran Coupe",Code="G26",Generation="2nd",Phase="Facelift" },state,guide);
            Check(result.Name=="B4 GT Gran Coupe (G26 LCI)",result.Name);return Task.CompletedTask;
        });
        test("Naming: chassis and revision digits are not interpreted as numbered facelifts in scanned examples",()=> {
            foreach(var text in new[]{"G26 LCI","F13 Facelift","991.2 Facelift","W463 Facelift"}) Check(NamingText.Phase(text)=="Facelift",text);
            Check(NamingText.Phase("W463 3rd Facelift")=="3rd Facelift");
            var state=new WorkspaceState();var guide=Build(Area(),state,("Alpina","B4 GT Gran Coupe (G26 LCI)",new() { Brand="Alpina",Series="B4",Variant="B4 GT Gran Coupe",Code="G26",Generation="2nd" }));
            Check(guide.Inventory.Single().Phase=="Facelift");return Task.CompletedTask;
        });
        test("Naming: Audi uses verified BCD aliases, removes drivetrain quattro and preserves fixed model names",()=> {
            var state=new WorkspaceState();var guide=Build(Area(),state,("Audi","A7 Sportback (C8)",new() { Brand="Audi",Series="A7",Variant="A7 Sportback",Code="4K8",Generation="2nd" }));
            var result=NamingGuideEngine.Generate(new() { Brand="Audi",Series="A7",Variant="A7 Sportback quattro",Body="Sportback",Code="4K8",Generation="2nd",Phase="Facelift" },state,guide);
            Check(result.Name=="A7 Sportback (C8 Facelift)",result.Name);
            Check(NamingGuideEngine.Generate(new() { Brand="Audi",Series="Sport Quattro",Variant="Sport Quattro Concept",Generation="1st" },state,guide).Name=="Sport Quattro Concept (1st)");return Task.CompletedTask;
        });
        test("Naming: Audi RS generation uses its matching foundation platform and never guesses an absent mapping",()=> {
            var state=new WorkspaceState();var guide=Build(Area(),state,("Audi","A3 Sportback (4th)",new() { Brand="Audi",Series="A3",Variant="A3 Sportback",Code="8Y",Generation="4th" }));
            var result=NamingGuideEngine.Generate(new() { Brand="Audi",Series="RS3",Variant="RS 3 Sportback quattro",Generation="3rd",Code="8Y",Phase="Facelift" },state,guide);
            Check(result.Name=="RS3 Sportback (4th Facelift)",result.Name);
            result=NamingGuideEngine.Generate(new() { Brand="Audi",Series="RS3",Variant="RS 3 Sportback",Generation="3rd",Code="UNKNOWN" },state,guide);
            Check(!result.Name.Contains("3rd") && result.Warnings.Count>0);return Task.CompletedTask;
        });
        test("Naming: legacy Audi spelling cannot reintroduce quattro or spaced RS digits after mandatory rules",()=> {
            var state=new WorkspaceState();var guide=Build(Area(),state,("Audi","RS 3 Sportback quattro (4th)",new() { Brand="Audi",Series="RS3",Variant="RS 3 Sportback quattro",Code="8Y",Generation="3rd" }),
                ("Audi","A3 Sportback (4th)",new() { Brand="Audi",Series="A3",Variant="A3 Sportback",Code="8Y",Generation="4th" }));
            var result=NamingGuideEngine.Generate(new() { Brand="Audi",Series="RS3",Variant="RS 3 Sportback quattro",Code="8Y",Generation="3rd" },state,guide);
            Check(result.Name=="RS3 Sportback (4th)",result.Name);return Task.CompletedTask;
        });
        test("Naming: Mercedes, BMW and Audi omit drivetrain markers even when learned vocabulary adds them",()=> {
            var state=new WorkspaceState();
            state.Rules.Add(new() { Brand="Mercedes-Benz",From="AWD",To="4MATIC" });
            state.Rules.Add(new() { Brand="BMW",From="AWD",To="xDrive" });
            state.Rules.Add(new() { Brand="Audi",From="AWD",To="quattro" });
            Check(NamingGuideEngine.Generate(new() { Brand="Mercedes-Benz",Series="E-Class",Variant="E 300 AWD",Generation="6th" },state).Name=="E 300 (6th)");
            Check(NamingGuideEngine.Generate(new() { Brand="BMW",Series="3 Series",Variant="330i xDrive",Generation="7th" },state).Name=="330i (7th)");
            var audi=NamingGuideEngine.Generate(new() { Brand="Audi",Series="A6",Variant="A6 AWD",Code="C8",Generation="8th" },state);
            Check(audi.Name=="A6 (C8)",audi.Name);
            Check(NamingGuideEngine.Generate(new() { Brand="Audi",Series="Sport Quattro",Variant="Sport Quattro Concept",Generation="1st" },state).Name=="Sport Quattro Concept (1st)");
            return Task.CompletedTask;
        });
        test("Naming: Phantom remains roman, modern Rolls-Royce ordinal, classic production range or visible warning",()=> {
            Check(NamingGuideEngine.Generate(new() { Brand="Rolls-Royce",Series="Phantom",Variant="Phantom EWB",Generation="7th Generation",Phase="Facelift" },new()).Name=="Phantom EWB (VII Facelift)");
            Check(NamingGuideEngine.Generate(new() { Brand="Rolls-Royce",Series="Ghost",Variant="Ghost",Generation="2nd" },new()).Name=="Ghost (2nd)");
            Check(NamingGuideEngine.Generate(new() { Brand="Rolls-Royce",Series="Silver Spirit",Variant="Silver Spirit",Generation="1st",ProductionYears="1980–1989" },new()).Name=="Silver Spirit (1980-1989)");
            var result=NamingGuideEngine.Generate(new() { Brand="Rolls-Royce",Series="Silver Spirit",Variant="Silver Spirit",Generation="1st" },new());Check(result.Name=="Silver Spirit" && result.Warnings.Count>0);return Task.CompletedTask;
        });
        test("Naming: full package vocabulary is preserved and appended at most once",()=> {
            var state=new WorkspaceState();var guide=Build(Area(),state,("BMW","640i Gran Coupe M-Sport (F06)",new() { Brand="BMW",Series="6 Series",Variant="640i Gran Coupe",Package="M Sport Package",Code="F06",Generation="3rd" }));
            var car=new CarIdentity { Brand="BMW",Series="6 Series",Variant="640i Gran Coupe",Package="M Sport Package",Code="F06",Generation="3rd" };
            var result=NamingGuideEngine.Generate(car,state,guide);Check(result.Name=="640i Gran Coupe M Sport Package (F06)",result.Name);
            car.Variant="640i Gran Coupe M Sport Package";Check(NamingGuideEngine.Generate(car,state,guide).Name==result.Name);
            car.Variant="640i Gran Coupe M-Sport";Check(NamingGuideEngine.Generate(car,state,guide).Name=="640i Gran Coupe M-Sport (F06)");return Task.CompletedTask;
        });
        test("Naming: combined suffix retains known generation without inventing a missing code or an unnamed model",()=> {
            var guide=new NamingGuide { Profiles=[new() { Brand="Example",Family="Model",Style="{generation}, {code} {phase}" }] };
            var result=NamingGuideEngine.Generate(new() { Brand="Example",Series="Model",Variant="Model",Generation="5th",Phase="Facelift" },new(),guide);
            Check(result.Name=="Model (5th Facelift)" && result.Warnings.Count>0,result.Name);
            Check(NamingGuideEngine.Generate(new() { Generation="5th" },new()).Name=="");return Task.CompletedTask;
        });
        test("Naming: guide export covers every directory, preserves originals, and reloads from local JSON",()=> {
            var root=Area();var state=new WorkspaceState();state.Names.Add(new("identity","Example","Known"));var guide=Build(root,state,("Example","Known (1st)",null),("Other","Known (X1)",null));
            File.WriteAllText(Path.Combine(root,"Example","补充命名要求.txt"),"Local note only");guide=NamingGuideBuilder.Build(root,state);
            var output=Path.Combine(Area(),"guide");NamingGuideStore.Save(output,guide,state);var loaded=NamingGuideStore.Load(output,root)!;
            Check(loaded.Inventory.Count==2 && loaded.Notes.Count==1 && File.ReadAllText(Path.Combine(output,"命名扫描清单.csv")).Contains("完整路径"));
            Check(NamingGuideStore.IsFresh(loaded,state));state.Names[0]=state.Names[0] with { FolderName="Edited name" };
            Check(!NamingGuideStore.IsFresh(loaded,state),"A remembered identity change must invalidate the derived guide.");
            state.BrandFolders["Alias"]="Example";var archived=new PhotoGroup { Id="archived",Archived=true,TargetBrand="Example",FolderName="Known (1st)",
                Confirmed=new() { Brand="Example",Series="Known",Variant="Known",Generation="1st" } };state.Groups.Add(archived);
            guide=NamingGuideBuilder.Build(root,state);Check(NamingGuideStore.IsFresh(guide,state));
            state.BrandFolders["Alias"]="Other";Check(!NamingGuideStore.IsFresh(guide,state),"A brand-folder mapping change must invalidate the derived guide.");
            state.BrandFolders["Alias"]="Example";guide=NamingGuideBuilder.Build(root,state);archived.Confirmed.Generation="2nd";
            Check(!NamingGuideStore.IsFresh(guide,state),"An archived identity change must invalidate the derived guide.");
            Directory.CreateDirectory(Path.Combine(root,"Example","New (2nd)"));Check(!NamingGuideStore.IsFresh(loaded,state));return Task.CompletedTask;
        });
        test("Naming: a literal generation-scope label is not replaced by a chassis code and opaque options are flagged",()=> {
            var state=new WorkspaceState();var guide=Build(Area(),state,("Toyota","Crown Estate (16th Crown)",new() { Brand="Toyota",Series="Crown Estate",Variant="Crown Estate",Generation="16th",Code="AZSH35" }),
                ("Unknown","Model (Screenless)",null));
            var result=NamingGuideEngine.Generate(new() { Brand="Toyota",Series="Crown Estate",Variant="Crown Estate",Generation="16th",Code="AZSH35" },state,guide);
            Check(result.Name=="Crown Estate (16th Crown)",result.Name);Check(guide.Inventory.Single(e=>e.Brand=="Unknown").Warning.Contains("无法安全映射"));return Task.CompletedTask;
        });
        test("Naming: historical spelling and brand prefixes cannot replace a complete model or add Body",()=> {
            var state=new WorkspaceState();state.BrandFolders["Mercedes-Benz"]="Mercedes";
            var guide=Build(Area(),state,("Mercedes","Benz S500 4MATIC (W223)",new() { Brand="Mercedes-Benz",Series="S-Class",Variant="S 500 4MATIC",Code="W223",Generation="7th" }));
            var result=NamingGuideEngine.Generate(new() { Brand="Mercedes-Benz",Series="S-Class",Variant="S 500 4MATIC",Code="w223",Generation="7th",Body="Sedan" },state,guide);
            Check(result.Name=="S 500 (W223)",result.Name);
            Check(!NamingGuideEngine.Generate(new() { Brand="Mercedes-Benz",Series="S-Class",Variant="S 500 4MATIC Coupe",Code="W223",Generation="7th" },state,guide).Name.Contains("Coupe Coupe"));
            Check(NamingText.RemoveRepeatedBodyTerms("Evoque Convertible HSE Dynamic Convertible Black Pack")=="Evoque Convertible HSE Dynamic Black Pack");return Task.CompletedTask;
        });
        test("Naming: Porsche 911 uses chassis codes before facelift and .2 codes after facelift",()=> {
            static NamingDecision Generate(string code,string phase="",string generation="8th") => NamingGuideEngine.Generate(new()
                { Brand="Porsche",Series="911",Variant="Porsche 911 Carrera GTS",Code=code,Phase=phase,Generation=generation },new());
            Check(Generate("991").Name=="911 Carrera GTS (991)");
            Check(Generate("991","Facelift").Name=="911 Carrera GTS (991.2)");
            Check(Generate("997","Facelift").Name=="911 Carrera GTS (997.2)");
            Check(Generate("992.2").Name=="911 Carrera GTS (992.2)");
            Check(Generate("991","Pre-Facelift").Name=="911 Carrera GTS (991)");
            Check(Generate("").Name=="911 Carrera GTS (8th)","Do not guess a Porsche chassis code when it is unknown.");
            var oldGuide=new NamingGuide { Profiles=[new() { Brand="Porsche",Family="911",Style="{generation} {phase}" }] };
            Check(NamingGuideEngine.Generate(new() { Brand="Porsche",Series="911",Variant="911 Carrera S",Code="992",Generation="8th",Phase="Facelift" },new(),oldGuide).Name=="911 Carrera S (992.2)");
            return Task.CompletedTask;
        });
        test("Naming V2: a fuller confirmed marketing name keeps body, trim and special edition while Body remains informational",()=> {
            var state=new WorkspaceState();var guide=Build(Area(),state,("Example","Model Coupe (X1)",new() { Brand="Example",Series="Model",Variant="Model Coupe",Generation="1st",Code="X1" }));
            var car=new CarIdentity { Brand="Example",Series="Model",Variant="Model Coupe",OfficialName="Example Model Coupe Sport Anniversary Edition",Body="Wagon",Package="Sport",Code="X1",Generation="1st" };
            var raw=JsonSerializer.Serialize(car);
            var result=NamingGuideEngine.Generate(car,state,guide);
            Check(result.Name=="Model Coupe Sport Anniversary Edition (X1)",result.Name);
            Check(JsonSerializer.Serialize(car)==raw,"Naming must not rewrite identification evidence.");
            car.OfficialName="Example Other Model Limited";Check(NamingGuideEngine.Generate(car,state,guide).Name=="Model Coupe Sport (X1)");
            Check(NamingText.RemoveRepeatedBodyTerms("Model Gran Coupe Coupe")=="Model Gran Coupe");return Task.CompletedTask;
        });
        test("Naming V2: drivetrain markers are removed in compact and prefixed forms without losing editions",()=> {
            foreach(var (brand,variant,expected) in new[] {
                ("Mercedes-Benz","Mercedes-Benz S 5804MATIC+ Final Edition","S 580 Final Edition (7th)"),
                ("Mercedes-AMG","Mercedes-AMG C 63 S E PERFORMANCE 4MATIC+","C 63 S E PERFORMANCE (7th)"),
                ("Mercedes-Maybach","Mercedes-Maybach S 680 4MATIC Haute Voiture","S 680 Haute Voiture (7th)"),
                ("BMW","BMW 330ixDrive Sedan M Sport","330i Sedan M Sport (7th)"),
                ("BMW","BMW X5 xDrive40i M Sport","X5 40i M Sport (7th)"),
                ("Audi","Audi A3 Sportback quattro Black Edition","A3 Sportback Black Edition (7th)") })
            {
                var result=NamingGuideEngine.Generate(new() { Brand=brand,Variant=variant,Generation="7th" },new());Check(result.Name==expected,result.Name);
            }
            return Task.CompletedTask;
        });
        test("Naming V2: actual split Mercedes directories recover from stale mappings and inherit only their own suffixes",()=> {
            var state=new WorkspaceState();state.BrandFolders["Mercedes-AMG"]="Mercedes";
            var guide=Build(Area(),state,("Mercedes-AMG","A 35 Hatchback (W177)",new() { Brand="Mercedes-AMG",Series="A-Class",Variant="A 35 Hatchback",Generation="4th",Code="W177" }),
                ("Mercedes-Benz","A-Class (W176)",null),("Mercedes-Maybach","S 580 (Z223)",null));
            Check(guide.Inventory.Single(e=>e.Brand=="Mercedes-AMG").HasIdentity);
            Check(guide.Inventory.Single(e=>e.Brand=="Mercedes-AMG").Family=="A-Class");
            var naming=new NamingService { Guide=guide };
            Check(naming.ResolveBrand("Mercedes-AMG",state)=="Mercedes-AMG");
            var result=naming.Suggest(new() { Brand="Mercedes-AMG",Series="A-Class",Variant="Mercedes-AMG A 45 S Hatchback 4MATIC+ Edition 1",Code="W177",Generation="4th" },state,[]);
            Check(result.Name=="A 45 S Hatchback Edition 1 (W177)",result.Name);
            Check(NamingText.Family("Mercedes-AMG","AMG GT","AMG GT 63 Coupe")=="AMG GT");return Task.CompletedTask;
        });
        test("Naming V2: 911 historical revisions compile to accurate guide policies without renaming sources",()=> {
            var state=new WorkspaceState();var guide=Build(Area(),state,("Porsche","911 GT3 (991 Facelift)",null),("Porsche","911 Carrera Cabriolet (992.1)",null),
                ("Porsche","911 Targa 4 GTS (992.2)",null),("Porsche","911 Carrera (997)",null));
            Check(guide.Inventory.All(e=>Directory.Exists(e.Path)) && guide.Profiles.All(p=>p.Style=="{code}"));
            foreach(var row in guide.Inventory)
            {
                var result=NamingGuideEngine.Generate(new() { Brand=row.Brand,Series=row.Family,Variant=row.Model,Code=row.Code,Phase=row.Phase },state,guide);
                Check(!result.Name.Contains(".1") && !result.Name.Contains("Facelift"),result.Name);
                if(row.Folder.Contains("Facelift") || row.Folder.Contains(".2")) Check(result.Name.Contains(".2"),result.Name);
            }
            Check(NamingGuideEngine.Generate(new() { Brand="Porsche",Series="911",Variant="911 Turbo 3.3",Code="930",Phase="Facelift" },state,guide).Name=="911 Turbo 3.3 (930)");
            var conflict=NamingGuideEngine.Generate(new() { Brand="Porsche",Series="911",Variant="911",Code="991.2",Phase="Pre-Facelift" },state,guide);
            Check(conflict.Name=="911 (991)" && conflict.Warnings.Count>0);return Task.CompletedTask;
        });
        test("Naming V2: no-suffix historical folders do not suppress a verified generation; memory-only endpoints stay absent",()=> {
            var state=new WorkspaceState();var guide=Build(Area(),state,("Example","Model",null));
            state.Names.Add(new("old","Example","Remembered-only model"));
            Check(NamingGuideEngine.Generate(new() { Brand="Example",Series="Model",Variant="Model Limited Edition",Generation="2nd" },state,guide).Name=="Model Limited Edition (2nd)");
            Check(CategoryCatalogService.Scan(state.Settings.ArchiveRoot).Items.Count==1);
            Check(guide.Inventory.All(e=>e.Folder!="Remembered-only model"));return Task.CompletedTask;
        });
        test("Naming V2: guide snapshots have isolated indices and obsolete versions cannot bypass rebuilding",()=> {
            var state=new WorkspaceState();var first=Build(Area(),state,("Example","Model (X1)",null));
            var second=Build(Area(),state,("Example","Model (2nd)",null));var car=new CarIdentity { Brand="Example",Series="Model",Variant="Model",Code="X2",Generation="2nd" };
            Check(NamingGuideEngine.Generate(car,state,first).Name=="Model (X2)");
            Check(NamingGuideEngine.Generate(car,state,second).Name=="Model (2nd)");
            var output=Area();second.Version=1;NamingGuideStore.Save(output,second,state);
            try { NamingGuideStore.Load(output,second.SourceRoot);throw new Exception("Old guide was accepted."); }catch(InvalidDataException) { }
            return Task.CompletedTask;
        });
        test("Naming: backup preserves prior naming records and added production years deserialize compatibly",()=> {
            var area=Area();var store=new StateStore(area);var state=new WorkspaceState { Names=[new("legacy","Example","Legacy Name")],Groups=[new() { Confirmed=new() { ProductionYears="1980-1989" } }] };store.Save(state);
            var backup=store.BackupNamingData();Check(File.Exists(backup) && store.Load().Names.Single().FolderName=="Legacy Name" && store.Load().Groups[0].Confirmed.ProductionYears=="1980-1989");
            Check(JsonSerializer.Deserialize<CarIdentity>("{}")!.ProductionYears=="");return Task.CompletedTask;
        });
        test("Naming: API calls contain ten generic fields but no local guide, folders, notes or naming request",async ()=> {
            const string sentinel="LOCAL_NAMING_GUIDE_CANARY_5436";var calls=0;
            using var http=new HttpClient(new FakeHandler(request=> {
                calls++;var json=request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();Check(!json.Contains(sentinel));using var body=JsonDocument.Parse(json);
                var schema=body.RootElement.GetProperty("response_format").GetProperty("schema").GetProperty("properties");
                if(schema.TryGetProperty("identity",out var identity)) { Check(!schema.TryGetProperty("suggestedName",out _));schema=identity.GetProperty("properties"); }
                Check(schema.EnumerateObject().Count()==10 && schema.TryGetProperty("productionYears",out _));
                return new(HttpStatusCode.OK) { Content=new StringContent("{\"output_text\":\"{\\\"brand\\\":\\\"Example\\\",\\\"variant\\\":\\\"Model Coupe\\\",\\\"generation\\\":\\\"3rd\\\",\\\"productionYears\\\":\\\"2000-2005\\\"}\"}") };
            }));
            var naming=new NamingService { Guide=new() { SourceRoot=sentinel,Notes=[new(sentinel,sentinel,sentinel)] } };
            naming.Suggest(new() { Brand="Example",Series="Model",Generation="3rd" },new() { Names=[new(sentinel,sentinel,sentinel)] },[sentinel]);
            var service=new GeminiService(http,new FakeImages(),()=>"test-key",()=>{});
            foreach(var mode in Enum.GetValues<RecognitionMode>()) { var result=await service.RecognizeAsync([new()],new() { Mode=mode },false,default);Check(result.Identity.ProductionYears=="2000-2005"); }
            Check(calls==5);
        });
    }
}
