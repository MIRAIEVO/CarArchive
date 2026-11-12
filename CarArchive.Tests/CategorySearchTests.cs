using CarArchive.Core;

static class CategorySearchTests
{
    static void Check(bool condition,string message="Fuzzy category search assertion failed") { if(!condition) throw new Exception(message); }
    public static void Register(Action<string,Func<Task>> test)
    {
        test("Category fuzzy: EWB and full wheelbase expressions are semantic equivalents",()=>
        {
            var index=new CategorySearchIndex([new("Rolls-Royce","Ghost Extended Wheelbase (2nd)","long",true),new("Rolls-Royce","Ghost (2nd)","short",true),new("Rolls-Royce","Ghost EWB (RR4)","missing",false)]);
            Check(index.Match("Ghost EWB","Rolls-Royce")[0].TargetPath=="long");
            Check(index.Match("Ghost long-wheelbase","Rolls-Royce")[0].TargetPath=="long");return Task.CompletedTask;
        });
        test("Category fuzzy: body synonyms and adjacent spelling errors rank nearer than a different body",()=>
        {
            var index=new CategorySearchIndex([new("Audi","A4 Avant (B9)","wagon",true),new("Audi","A4 Sedan (B9)","sedan",true),new("BMW","330i Touring (G21)","touring",true)]);
            Check(index.Match("奥迪 A4 estate B9","Audi")[0].TargetPath=="wagon");
            Check(index.Match("A4 avnat B9","Audi")[0].TargetPath=="wagon");
            Check(index.Match("avnat","Audi")[0].TargetPath=="wagon");
            Check(index.Match("330i toruing","BMW")[0].TargetPath=="touring");
            Check(index.Match("toruing","BMW")[0].TargetPath=="touring");
            Check(index.Match("330i station wagon","BMW")[0].TargetPath=="touring");return Task.CompletedTask;
        });
        test("Category fuzzy: compact designations match spaced models and preserve numeric identity",()=>
        {
            var index=new CategorySearchIndex([new("Mercedes-Benz","S 580 (W223)","580",true),new("Mercedes-Benz","S 500 (W223)","500",true),new("Audi","RS 3 Sportback (4th)","rs3",true),new("Audi","RS5 Coupe (2nd)","rs5",true)]);
            Check(index.Match("Benz S580 4MATIC W223","Mercedes-Benz")[0].TargetPath=="580");
            var rs=index.Match("RS3","Audi");Check(rs[0].TargetPath=="rs3" && rs.All(e=>e.TargetPath!="rs5"));
            Check(index.Match("W222","").Count==0);return Task.CompletedTask;
        });
        test("Category fuzzy: series aliases and LCI/facelift vocabulary work beyond literal model words",()=>
        {
            var index=new CategorySearchIndex([new("BMW","640i Gran Coupe (F06 Facelift)","6",true),new("BMW","330i Sedan (G20)","3",true)]);
            Check(index.Match("6 Series LCI","BMW")[0].TargetPath=="6");return Task.CompletedTask;
        });
        test("Category fuzzy: highest relevance is always first and equal scores have deterministic order",()=>
        {
            var index=new CategorySearchIndex([new("Audi","A4 Sedan (B9)","near",true),new("Audi","A6 Avant (B9)","far",true),new("Audi","A4 Avant (B9)","best",true)]);
            var ranking=index.Match("A4 Avant B9","").Select(item=>item.TargetPath).ToArray();
            Check(ranking.SequenceEqual(new[]{"best","near","far"}),"Unexpected relevance order: "+string.Join(",",ranking));
            var ties=new CategorySearchIndex([new("Zed","BMW","z",true),new("Alpha","BMW","a",true)]);
            var tieOrder=ties.Match("BMW","").Select(item=>item.TargetPath).ToArray();
            Check(tieOrder.SequenceEqual(new[]{"a","z"}),"Unexpected deterministic tie order: "+string.Join(",",tieOrder));return Task.CompletedTask;
        });
        test("Category fuzzy: verified local guide aliases relate generation and chassis without API calls",()=>
        {
            var first=new CategorySuggestion("Rolls-Royce","Ghost (1st)","first",true);var second=new CategorySuggestion("Rolls-Royce","Ghost (2nd)","second",true);
            var guide=new NamingGuide { Inventory=[new() { Brand="Rolls-Royce",Family="Ghost",Model="Ghost",Path="first",Generation="1st",RawCode="RR4" },new() { Brand="Rolls-Royce",Family="Ghost",Model="Ghost",Path="second",Generation="2nd",RawCode="RR21" }] };
            Check(new CategorySearchIndex([first,second],guide).Match("Ghost RR21","Rolls-Royce")[0]==second);return Task.CompletedTask;
        });
        test("Category fuzzy: focus without text returns only physical endpoints, capped and brand-preferred",()=>
        {
            var items=Enumerable.Range(0,30).Select(i=>new CategorySuggestion(i==29?"BMW":"Audi","Model "+i,"p"+i,true)).Append(new("BMW","Absent","absent",false));
            var found=new CategorySearchIndex(items).Match("","BMW");Check(found.Count==20 && found[0].BrandDirectory=="BMW" && found.All(e=>e.Exists));
            Check(found[0].DisplayName==found[0].FolderName+" / BMW");return Task.CompletedTask;
        });
    }
}
