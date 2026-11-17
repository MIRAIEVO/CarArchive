using System.Text.RegularExpressions;
namespace CarArchive.Core;
public static class LocalModelSpelling
{
    // Equivalent body terminology and spacing can follow an existing model. Every numeric/model token remains significant.
    public static string Key(string name)
    {
        name=LatinText.English(name);
        name=Regex.Replace(name,@"\b(?:Station Wagon|Estate|Touring|Wagon)\b","wagon",RegexOptions.IgnoreCase);
        name=Regex.Replace(name,@"\b(?:Convertible|Cabriolet)\b","convertible",RegexOptions.IgnoreCase);
        name=Regex.Replace(name,@"\b(?:Sedan|Saloon)\b","sedan",RegexOptions.IgnoreCase);
        return Regex.Replace(NamingText.Key(name),@"\s+","");
    }
    public static string? Resolve(string model,string brand,string family,string code,string generation,NamingGuide? guide)
    {
        if(guide is null)return null;
        var key=Key(model);
        var matches=guide.Index.Family(NamingText.Key(brand),family)
            .Where(entry=>Key(entry.Model)==key)
            .Where(entry=>generation.Length==0 || entry.Generation.Length==0 || entry.Generation==generation)
            .Where(entry=>code.Length==0 || entry.Code.Length==0 || NamingText.Key(entry.Code)==NamingText.Key(code) || NamingText.Key(entry.RawCode)==NamingText.Key(code))
            .Select(entry=>entry.Model).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return matches.Length==1?matches[0]:null;
    }
}
