using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CarArchive.Core;

public sealed class NamingGuide
{
    public const int CurrentVersion = 2;
    public int Version { get; set; } = CurrentVersion;
    private NamingGuideIndex? index;
    // A guide is a snapshot: rebuild/replace it when its sources change.
    internal NamingGuideIndex Index => LazyInitializer.EnsureInitialized(ref index, () => new NamingGuideIndex(this));
    public string SourceRoot { get; set; } = "";
    public string SourceStateHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public List<NamingGuideEntry> Inventory { get; set; } = [];
    public List<NamingProfile> Profiles { get; set; } = [];
    public List<NamingNote> Notes { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
public sealed record NamingNote(string Path, string Text, string Hash);
public sealed class NamingGuideEntry
{
    public string Brand { get; set; } = "";
    public string Folder { get; set; } = "";
    public string Path { get; set; } = "";
    public string Model { get; set; } = "";
    public string Family { get; set; } = "";
    public string Suffix { get; set; } = "";
    public string PhaseWord { get; set; } = "Facelift";
    public string Style { get; set; } = "Ordinal";
    public string Generation { get; set; } = "";
    public string Code { get; set; } = "";
    public string RawCode { get; set; } = "";
    public string Package { get; set; } = "";
    public string ProductionYears { get; set; } = "";
    public string Phase { get; set; } = "";
    public bool HasIdentity { get; set; }
    public int ConfirmationOrder { get; set; }
    public string Warning { get; set; } = "";
    public List<string> Aliases { get; set; } = [];
}
public sealed class NamingProfile
{
    public string Id { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Family { get; set; } = "";
    public string Model { get; set; } = "";
    public string Generation { get; set; } = "";
    public string Style { get; set; } = "Ordinal";
    public string PhaseWord { get; set; } = "Facelift";
    public int Votes { get; set; }
    public string Resolution { get; set; } = "";
    public List<string> Sources { get; set; } = [];
}
public sealed record NamingDecision(string Name, string Rule, IReadOnlyList<string> Warnings)
{
    public string Reason => Rule + (Warnings.Count == 0 ? "" : "；" + string.Join("；", Warnings));
}

public static partial class NamingText
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacePattern();
    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex KeyPattern();
    [GeneratedRegex(@"\s+|\bpackage\b")]
    private static partial Regex ModelKeyPattern();
    [GeneratedRegex(@"\b(?:Shooting Brake|Gran Coup[eé]|Cabriolet|Convertible|Sportback|Hatchback|Roadster|Coup[eé]|Sedan|Estate|Wagon|Avant)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BodyTermPattern();
    public static string Clean(string? text) => SpacePattern().Replace(text?.Trim() ?? "", " ");
    public static string Key(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        return KeyPattern().Replace(new string(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant(), " ").Trim();
    }
    public static string Ordinal(int number) => number + (number % 100 is 11 or 12 or 13 ? "th" : (number % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
    public static int RomanNumber(string value)
    {
        if (!Regex.IsMatch(value, @"^[IVX]+$", RegexOptions.IgnoreCase)) return 0;
        var total=0;var previous=0;
        foreach(var c in value.ToUpperInvariant().Reverse()) { var n=c=='I'?1:c=='V'?5:10;total+=n<previous?-n:n;previous=n; }
        return total is >0 and <40 && Roman(total)==value.ToUpperInvariant() ? total : 0;
    }
    public static string Roman(int n)
    {
        if(n is <=0 or >=40) return "";
        var result="";foreach(var (number,symbol) in new[]{(10,"X"),(9,"IX"),(5,"V"),(4,"IV"),(1,"I")}) while(n>=number) { result+=symbol;n-=number; }return result;
    }
    public static string Generation(string text)
    {
        text=Clean(text);
        var m=Regex.Match(text,@"^(?:(?:Mk|Mark|Series|Gen(?:eration)?)[ .-]*|第\s*)?(\d{1,2})(?:st|nd|rd|th|代)?(?:\s*(?:Gen(?:eration)?))?$",RegexOptions.IgnoreCase);
        if(m.Success && int.Parse(m.Groups[1].Value) is >0 and <100) return Ordinal(int.Parse(m.Groups[1].Value));
        var roman=Regex.Match(text,@"^(?:(?:Mk|Mark|Series|Gen(?:eration)?)[ .-]*)?([IVX]+)$",RegexOptions.IgnoreCase);
        if(roman.Success && RomanNumber(roman.Groups[1].Value) is >0 and var n) return Ordinal(n);
        string[] words=["first","second","third","fourth","fifth","sixth","seventh","eighth","ninth","tenth"];
        var word=Regex.Replace(text.ToLowerInvariant(),@"\s+generation$","");var i=Array.IndexOf(words,word);return i>=0?Ordinal(i+1):"";
    }
    public static string Phase(string text)
    {
        text=Clean(text).Replace('_',' ');
        if(text.Length==0 || Regex.IsMatch(text,@"^(pre[ -]?facelift|original|unknown|n/a)$",RegexOptions.IgnoreCase)) return "";
        if(Regex.IsMatch(text,@"\b(late|later)\b",RegexOptions.IgnoreCase)) return "Late";
        var number=Regex.Match(text,@"(?<![\p{L}\p{N}._-])(\d{1,2})(?:st|nd|rd|th)?\s*(?:facelift|lci)",RegexOptions.IgnoreCase);
        if(number.Success && int.Parse(number.Groups[1].Value)>0) return Ordinal(int.Parse(number.Groups[1].Value))+" Facelift";
        return Regex.IsMatch(text,@"facelift|faceclift|\blci\b",RegexOptions.IgnoreCase)?"Facelift":"";
    }
    public static string Years(string text)
    {
        var m=Regex.Match(text,@"^\s*((?:18|19|20)\d{2})\s*[-–—/]\s*((?:18|19|20)\d{2})\s*$");
        return m.Success && int.Parse(m.Groups[1].Value)<=int.Parse(m.Groups[2].Value)?m.Groups[1].Value+"-"+m.Groups[2].Value:"";
    }
    public static string StripBrand(string name,string brand)
    {
        name=Clean(name);
        var key=Key(brand);
        var brands=new List<string> { brand };
        if(PolicyBrand(brand)=="mercedes") brands.AddRange(["Mercedes-Benz","Mercedes Benz","Mercedes-AMG","Mercedes AMG","Mercedes-Maybach","Mercedes Maybach","Mercedes","Benz"]);
        if(key=="mercedes amg" && !name.StartsWith("AMG GT",StringComparison.OrdinalIgnoreCase)) brands.Add("AMG");
        if(key is "mercedes maybach" or "maybach") brands.Add("Maybach");
        if(key=="mini") brands.Add("Rover");
        foreach(var b in brands.Where(x=>x.Length>0).OrderByDescending(x=>x.Length))
            if(name.StartsWith(b+" ",StringComparison.OrdinalIgnoreCase)) name=name[(b.Length+1)..].TrimStart();
        return Clean(name);
    }
    public static string FullModel(CarIdentity car)
    {
        var variant=StripBrand(car.Variant,car.Brand);
        var official=StripBrand(car.OfficialName,car.Brand);
        // A confirmed, fuller official designation may add body/trim/special-edition words.
        // Never replace a specific variant with a shorter official series name.
        var name=variant.Length==0?official:official.Length>variant.Length && ModelKey(official).Contains(ModelKey(variant),StringComparison.Ordinal)?official:variant;
        if(name.Length==0) name=StripBrand(car.Series,car.Brand);
        var package=Clean(car.Package);
        var packageKey=ModelKey(package);
        if(name.Length>0 && packageKey.Length>0 && !ModelKey(name).Contains(packageKey,StringComparison.Ordinal)) name+=" "+package;
        return RemoveRepeatedBodyTerms(name);
    }
    public static string RemoveRepeatedBodyTerms(string name)
    {
        // Repair known duplicate phrases in historical aliases, not the informational Body field.
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        name=BodyTermPattern().Replace(name,match=>
        {
            var key=Key(match.Value);
            if(!seen.Add(key)) return "";
            if(key=="gran coupe") seen.Add("coupe");
            return match.Value;
        });
        return Clean(name);
    }
    public static string ModelKey(string text) => ModelKeyPattern().Replace(Key(text),"");
    public static string PolicyBrand(string brand)
    {
        var key=Key(brand);
        return key is "mercedes" or "mercedes benz" or "mercedes amg" or "mercedes maybach" or "benz" or "maybach" ? "mercedes" : key;
    }
    public static string WithoutDrivetrain(string model,string brand)
    {
        var pattern=PolicyBrand(brand) switch
        {
            "mercedes"=>@"4[ -]?MATIC\+?(?!\p{L})",
            // xDrive is also routinely attached to numeric BMW trims, e.g. 330ixDrive.
            "bmw"=>@"x[ -]?Drive(?!\p{L})",
            "audi"=>@"(?<![\p{L}\p{N}])quattro(?![\p{L}\p{N}])",_=>""
        };
        if(pattern.Length==0) return Clean(model);
        return Clean(Regex.Replace(model,pattern,match=>PolicyBrand(brand)=="audi" && Regex.IsMatch(model[..match.Index],@"\bSport\s+$",RegexOptions.IgnoreCase)?match.Value:"",RegexOptions.IgnoreCase));
    }
    public static string Porsche911Code(string code,string phase,out bool conflict)
    {
        conflict=false;
        var match=Regex.Match(Clean(code),@"^(996|997|991|992)(?:\.([12]))?$");
        if(!match.Success) return "";
        var pre=Regex.IsMatch(Clean(phase),@"^pre[ -]?facelift$",RegexOptions.IgnoreCase);
        var post=Phase(phase).Contains("Facelift",StringComparison.OrdinalIgnoreCase);
        conflict=pre && match.Groups[2].Value=="2" || post && match.Groups[2].Value=="1";
        return match.Groups[1].Value+(!pre && (post || match.Groups[2].Value=="2")?".2":"");
    }
    public static string Family(string brand,string series,string model)
    {
        var text=StripBrand(RemoveRepeatedBodyTerms(model),brand);
        var s=Key(series);var b=PolicyBrand(brand);
        if(b=="rolls royce") text=Regex.Replace(text,@"^Black Badge\s+","",RegexOptions.IgnoreCase);
        if(b=="audi")
        {
            var m=Regex.Match(text,@"^(?:RS\s*Q|SQ|Q)\s*(\d)\b",RegexOptions.IgnoreCase);if(m.Success) return "Q"+m.Groups[1].Value;
            m=Regex.Match(text,@"^(?:RS|S|A)\s*(1|3|4|5|6|7|8)\s*L?\b",RegexOptions.IgnoreCase);if(m.Success) return "A"+m.Groups[1].Value;
            if(Regex.IsMatch(text,@"^TT(?:\s|S|RS|\b)",RegexOptions.IgnoreCase)) return "TT";
        }
        if(b is "bmw" or "alpina")
        {
            var m=Regex.Match(text,@"\b([1-8])\s*Series\b",RegexOptions.IgnoreCase);if(m.Success) return m.Groups[1].Value+" Series";
            m=Regex.Match(text,@"^(?:M)?([1-8])\d\d[diLeI]*(?:\s|\b)",RegexOptions.IgnoreCase);if(m.Success) return m.Groups[1].Value+" Series";
            m=Regex.Match(text,@"^M([2-8])\b",RegexOptions.IgnoreCase);if(m.Success) return m.Groups[1].Value+" Series";
            m=Regex.Match(text,@"^(X\d|Z\d|XM|[BD]\d|XB\d)\b",RegexOptions.IgnoreCase);if(m.Success) return m.Value.ToUpperInvariant();
        }
        // Explicit, library-derived families. Longest names first; no fuzzy cross-series matching.
        if(b=="mercedes")
        {
            text=Regex.Replace(text,@"^(?:Benz|Maybach|AMG(?! GT\b))\s+","",RegexOptions.IgnoreCase);
            var m=Regex.Match(text,@"^(CLE|CLA|CLK|CLS|GLC|GLA|GLS|SLK|SLC|ML|SL|CL|GL|A|C|E|S|G)\s*(?:\d|Class\b|-Class\b)",RegexOptions.IgnoreCase);
            if(m.Success) return m.Groups[1].Value.ToUpperInvariant()+"-Class";
        }
        if(Families.TryGetValue(b,out var choices))
            foreach(var family in choices)
                if(text.StartsWith(family.Name,StringComparison.OrdinalIgnoreCase) && (text.Length==family.Name.Length || !char.IsLetterOrDigit(text[family.Name.Length]) && text[family.Name.Length]!='_') || s==family.Key) return family.Name;
        return series.Length>0?Clean(series):text;
    }
    private static readonly Dictionary<string,(string Name,string Key)[]> Families=CreateFamilies();
    private static Dictionary<string,(string Name,string Key)[]> CreateFamilies()
    {
        var families=new Dictionary<string,string[]> {
            ["mercedes"]=["G-Class","A-Class","C-Class","E-Class","S-Class","CLA-Class","CLS","CLK-Class","CL-Class","GLC","GLA","GLS","EQS SUV","EQS","EQE","SLK-Class","SLC-Class","SL-Class","V-Class","Sprinter","AMG GT","AMG SLS"],
            ["porsche"]=["911","718","Cayenne","Macan","Panamera","Taycan"],
            ["rolls royce"]=["Phantom","Ghost","Cullinan","Silver Spirit","Spectre","Wraith","Dawn"],
            ["land rover"]=["Range Rover Evoque","Range Rover Sport","Range Rover","Defender"],
            ["lexus"]=["ES","HS","IS","LC","LFA","LM","LS","LX","RX","RZ","UX"],
            ["toyota"]=["Land Cruiser Prado","Land Cruiser","Crown Estate","Crown","Alphard","Vellfire","Prius","GR Yaris","GR Corolla","RAV4","HiAce","Hilux Surf","Hilux","Voxy","bZ4X Touring","bZ4X"],
            ["honda"]=["Civic","CR-V","CR-X","Accord","NSX","NSX-T","Fit","Insight","Legend","N-BOX","N-ONE","Prelude","S2000","Beat","Gyro Canopy","CBR250RR"],
            ["volkswagen"]=["Golf","Passat","Polo","Multivan","Transporter","Beetle","Type 2","Type 3","Phaeton","Sagitar"],
            ["bentley"]=["Continental GT","Flying Spur","Bentayga","Mulsanne"],
            ["nissan"]=["Fairlady Z","Skyline Crossover","Skyline","Note Aura","GT-R","Tiida","Serena","Leaf","Fuga","Ariya","Dayz","Kicks","Laurel","180SX"],
            ["mazda"]=["Savanna RX-7","RX-7","Roadster","CX-5","CX-50","CX-80","Atenza"],
            ["lamborghini"]=["Aventador","Gallardo","Huracan","Urus","Countach","Miura","Revuelto","Temerario"],
            ["mini"]=["Mini Cooper","Cooper","Mini-Minor"],
            ["jaguar"]=["F-TYPE","I-Pace","XE","XF","XJ","XK8"],
            ["subaru"]=["Forester","Impreza","Legacy Outback","WRX","Pleo","Solterra","360","XC"],
            ["suzuki"]=["Alto Lapin","Every","Fronx","Hustler","Jimny","Swift","Wagon R"],
            ["tesla"]=["Model 3","Model S","Model X","Model Y"],
            ["ferrari"]=["12Cilindri","296","308","328","458","488","499P","612","849 Testarossa","Amalfi","California","Daytona SP3","Enzo Ferrari","F12berlinetta","F355","F40","F430","F50","F8","F80","GTC4Lusso","Monza","Portofino","Purosangue","Roma","SF-26","SF90"],
            ["maserati"]=["GranTurismo","Quattroporte","Ghibli","Grecale","MC20"],
            ["chevrolet"]=["Corvette","Suburban","Malibu","Chevy Van"],
            ["aston martin"]=["Vantage","DB9","Rapide"],
            ["daihatsu"]=["Copen","Hijet","Move Canbus","Taft","Thor","Wake"]
        };
        return families.ToDictionary(p=>p.Key,p=>p.Value.OrderByDescending(x=>x.Length).Select(x=>(x,Key(x))).ToArray());
    }
}
