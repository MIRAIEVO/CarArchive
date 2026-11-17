using System.Text.RegularExpressions;
namespace CarArchive.Core;
internal static class NamingFamily
{
    internal static string Normalize(string brand,string model)
    {
        var key=NamingText.PolicyBrand(brand);
        if(key=="audi" && Regex.IsMatch(model,@"^A6.*e-tron\b",RegexOptions.IgnoreCase))return "A6 e-tron";
        var pattern=key switch
        {
            "aston martin"=>@"^(DB\d+|DBS|DBX)(?=\b|\d)|\b(Vantage)\b",
            "lexus"=>@"^(CT|IS|ES|GS|LS|HS|SC|RC|LC|LBX|UX|NX|RX|GX|LX|LM|RZ)(?=\b|\d)",
            "bentley"=>@"^(Continental (?:GT|GTC)|(?:Continental )?Flying Spur|Bentayga|Mulsanne|Arnage|Brooklands|Azure)\b",
            "toyota"=>@"^(?:GR )?(Corolla|Crown|Yaris|Supra|86|Land Cruiser|Alphard|Vellfire|Prius|Camry|Sienta|Hiace|Voxy|Noah)\b",
            "honda"=>@"^(Civic|Accord|Fit|Jazz|Freed|Odyssey|Stepwgn|Vezel|CR-V|ZR-V|HR-V)\b",
            _=>""
        };
        if(pattern.Length==0)return "";
        var match=Regex.Match(model,pattern,RegexOptions.IgnoreCase);if(!match.Success)return "";
        var family=match.Groups[1].Success?match.Groups[1].Value:match.Groups[2].Value;
        if(family.Contains("Flying Spur",StringComparison.OrdinalIgnoreCase))return "Flying Spur";
        if(family.Equals("Continental GTC",StringComparison.OrdinalIgnoreCase))return "Continental GT";
        return family;
    }
}
