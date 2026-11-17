using System.Text.RegularExpressions;
namespace CarArchive.Core;
internal static class NamingSupplementRules
{
    internal static string? Suffix(string brand,string family,string model)
    {
        var key=NamingText.PolicyBrand(brand);
        if(key=="aston martin" && Regex.IsMatch(family,@"^DB\d",RegexOptions.IgnoreCase))return "{phase}";
        if(key=="aston martin" && Regex.IsMatch(model,@"^Rapide(?: S)?$",RegexOptions.IgnoreCase))return "";
        if(key=="chevrolet" && family.Equals("Corvette",StringComparison.OrdinalIgnoreCase))return "{code} {phase}";
        if(key=="honda" && Regex.IsMatch(model,@"^Civic.*Type[ -]?R\b",RegexOptions.IgnoreCase))return "{code} {phase}";
        if(key=="nissan" && Regex.IsMatch(model,@"\bGT[ -]?R\b",RegexOptions.IgnoreCase))return "{code} {phase}";
        if(key=="toyota" && Regex.IsMatch(model,@"^(?:Land Cr[ui]+ser|Prado)\b",RegexOptions.IgnoreCase))return "{phase}";
        return null;
    }
}
