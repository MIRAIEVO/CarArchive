using System.Text.RegularExpressions;

namespace CarArchive.Core;

/// <summary>Offline, immutable search snapshot. All folder/guide I/O happens before construction.</summary>
public sealed partial class CategorySearchIndex
{
    private sealed record Entry(CategorySuggestion Item,string Name,string BrandKey,string[] Model,string[] Candidates,string[] Aliases);
    private readonly Entry[] entries;
    private readonly HashSet<string> vocabulary;
    public CategorySearchIndex(IEnumerable<CategorySuggestion> items,NamingGuide? guide=null)
    {
        entries=items.Where(item=>item.Exists).Select(item=>
        {
            var model=Regex.Replace(item.FolderName,@"\s*\([^()]*\)\s*$","");
            var family=NamingText.Family(item.BrandDirectory,"",model);
            var aliases=new List<string> { family };
            if(guide is not null && guide.Index.Paths.TryGetValue(item.TargetPath,out var known))
            { aliases.AddRange(known.Aliases);aliases.AddRange([known.Generation,known.Code,known.RawCode]); }
            var name=Normalize(item.FolderName);
            // Keep literal spellings as well: a misspelling of Avant must still match even
            // though its semantic form is wagon, and a misspelling of Touring must not disappear.
            var candidates=name.Split(' ',StringSplitOptions.RemoveEmptyEntries).Concat(Tokens(item.BrandDirectory))
                .Concat(NamingText.Key(item.FolderName).Split(' ',StringSplitOptions.RemoveEmptyEntries)).Distinct().ToArray();
            return new Entry(item,name,NamingText.Key(item.BrandDirectory),Tokens(model),candidates,aliases.SelectMany(Tokens).Distinct().ToArray());
        }).ToArray();
        vocabulary=entries.SelectMany(e=>e.Candidates.Concat(e.Aliases)).Where(w=>w.Length>=2).ToHashSet(StringComparer.Ordinal);
    }
    public IReadOnlyList<CategorySuggestion> Match(string query,string currentBrand,int limit=20)
    {
        if(limit<=0) return [];
        var normalized=Normalize(query);var tokens=normalized.Split(' ',StringSplitOptions.RemoveEmptyEntries).SelectMany(Segment).Distinct().ToArray();var brand=NamingText.Key(currentBrand);
        if(tokens.Length==0) return entries.OrderBy(e=>e.BrandKey==brand?0:1)
            .ThenBy(e=>e.Item.FolderName,StringComparer.OrdinalIgnoreCase).ThenBy(e=>e.Item.BrandDirectory,StringComparer.OrdinalIgnoreCase)
            .Take(Math.Min(limit,20)).Select(e=>e.Item).ToArray();
        return entries.Select(entry=>(Entry:entry,Distance:Distance(entry,normalized,tokens)))
            .Where(hit=>hit.Distance<.72).OrderBy(hit=>hit.Distance)
            .ThenBy(hit=>hit.Entry.BrandKey==brand?0:1)
            .ThenBy(hit=>hit.Entry.Item.FolderName,StringComparer.OrdinalIgnoreCase).ThenBy(hit=>hit.Entry.Item.BrandDirectory,StringComparer.OrdinalIgnoreCase)
            .Take(Math.Min(limit,20)).Select(hit=>hit.Entry.Item).ToArray();
    }
    private IEnumerable<string> Segment(string token)
    {
        if(token.Length<5 || token.Length>150 || vocabulary.Contains(token)) return [token];
        // Word segmentation uses the local catalog, so s63coupe, m3competition and
        // 911carreragts can resolve to known words without a language-model call.
        var score=Enumerable.Repeat(double.NegativeInfinity,token.Length+1).ToArray();
        var previous=new int[token.Length+1];score[0]=0;
        for(var end=2;end<=token.Length;end++) for(var start=0;start<=end-2;start++)
        {
            if(double.IsNegativeInfinity(score[start])) continue;
            var word=token[start..end];if(!vocabulary.Contains(word)) continue;
            var candidate=score[start]+word.Length*word.Length;
            if(candidate>score[end]) { score[end]=candidate;previous[end]=start; }
        }
        var last=token.Length;string? tail=null;
        if(double.IsNegativeInfinity(score[^1]))
        {
            // Continue matching while the user is midway through a compact final word.
            for(var cut=token.Length-1;cut>=2;cut--)
                if(!double.IsNegativeInfinity(score[cut]) && vocabulary.Any(word=>word.StartsWith(token[cut..],StringComparison.Ordinal))) { last=cut;tail=token[cut..];break; }
            if(tail is null) return [token];
        }
        var words=new List<string>();if(tail is not null) words.Add(tail);
        for(var end=last;end>0;end=previous[end]) words.Add(token[previous[end]..end]);
        words.Reverse();return words;
    }
    private static double Distance(Entry entry,string query,string[] tokens)
    {
        if(entry.Name==query) return 0;
        double cost=0,total=0;var anchors=0;
        foreach(var token in tokens)
        {
            var weight=Weight(token);total+=weight;
            var best=entry.Candidates.Select(candidate=>WordDistance(token,candidate)).DefaultIfEmpty(1).Min();
            if(best>0 && entry.Aliases.Length>0) best=Math.Min(best,entry.Aliases.Min(candidate=>Math.Min(1,WordDistance(token,candidate)+.025)));
            cost+=weight*best;if(best<=.4) anchors++;
        }
        if(anchors==0) return 1;
        // Prefer the least extra model information; suffix/generation differences remain visible as near matches.
        var extra=entry.Model.Count(token=>!tokens.Any(q=>WordDistance(q,token)<.4));
        return .01+cost/total+Math.Min(.08,extra*.015);
    }
    private static double Weight(string word)=>word.Any(char.IsDigit)?4:BodyWords.Contains(word)?1:2;
    private static double WordDistance(string query,string candidate)
    {
        if(query==candidate) return 0;
        if(candidate.StartsWith(query,StringComparison.Ordinal)) return query.Length>=2?.08:.25;
        // Numeric identities are not spelling errors: A4/A5, W222/W223 and 991/992 remain distinct.
        if(query.Any(char.IsDigit) || candidate.Any(char.IsDigit)) return 1;
        if(query.Length<3 || candidate.Length<3 || Math.Abs(query.Length-candidate.Length)>2) return 1;
        var distance=EditDistance(query,candidate)/(double)Math.Max(query.Length,candidate.Length);
        return distance<=.4?distance:1;
    }
    private static int EditDistance(string left,string right)
    {
        // Optimal-string-alignment distance includes adjacent transpositions (avnat -> avant).
        Span<int> previous=stackalloc int[right.Length+1];Span<int> beforePrevious=stackalloc int[right.Length+1];Span<int> current=stackalloc int[right.Length+1];
        for(var j=0;j<=right.Length;j++) previous[j]=j;
        for(var i=1;i<=left.Length;i++)
        {
            current[0]=i;
            for(var j=1;j<=right.Length;j++)
            {
                current[j]=Math.Min(Math.Min(current[j-1]+1,previous[j]+1),previous[j-1]+(left[i-1]==right[j-1]?0:1));
                if(i>1 && j>1 && left[i-1]==right[j-2] && left[i-2]==right[j-1]) current[j]=Math.Min(current[j],beforePrevious[j-2]+1);
            }
            previous.CopyTo(beforePrevious);current.CopyTo(previous);
        }
        return previous[right.Length];
    }
    private static string[] Tokens(string text)=>Normalize(text).Split(' ',StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
    private static readonly HashSet<string> BodyWords=["wagon","convertible","sedan","coupe","hatchback","sportback","longwheelbase","facelift"];
    private static readonly (Regex Pattern,string Replacement)[] Vocabulary=
    [
        (Phrase(@"mercedes[ -]benz|\bbenz\b|奔驰")," mercedes "),
        (Phrase(@"rolls[ -]?royce|劳斯莱斯")," rollsroyce "),
        (Phrase(@"宝马")," bmw "),(Phrase(@"奥迪")," audi "),(Phrase(@"保时捷")," porsche "),
        (Phrase(@"\b(?:station[ -]?wagon|estate|touring|avant)\b")," wagon "),
        (Phrase(@"\bshooting[ -]?brake\b")," shootingbrake "),
        (Phrase(@"\b(?:drophead[ -]?coup[eé]|cabriolet|cabrio|convertible)\b")," convertible "),
        (Phrase(@"\b(?:sedan|saloon|limousine)\b")," sedan "),
        (Phrase(@"\b(?:extended[ -]?wheelbase|long[ -]?wheelbase|ewb|lwb|extended)\b")," longwheelbase "),
        (Phrase(@"\bblack[ -]?badge\b")," blackbadge "),(Phrase(@"\bs[ -]line\b")," sline "),
        (Phrase(@"\bamg[ -]line\b")," amgline "),(Phrase(@"\bm[ -]sport\b")," msport "),
        (Phrase(@"\bpre[ -]?facelift\b")," prefacelift "),(Phrase(@"\b(?:facelift|faceclift|lci)\b|中改")," facelift "),
        (Phrase(@"\b(?:gen(?:eration)?\s*)?(\d{1,2})(?:st|nd|rd|th)(?:\s*gen(?:eration)?)?\b")," gen$1 "),
        (Phrase(@"\b(\d)\s*series\b|\bseries\s*(\d)\b")," series$1$2 "),
        (Phrase(@"\b(rsq|sq|rs|amg|cla|cls|cl|glc|gle|gls|sl|ml|gt|[asqecmgxzwvfb])\s+(\d+[a-z]*)\b")," $1$2 "),
        (Phrase(@"4[ -]?matic\+?|x[ -]?drive|\bquattro\b")," ")
    ];
    private static Regex Phrase(string pattern)=>new(pattern,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    private static string Normalize(string value)
    {
        value=LatinText.English(value.Trim());
        foreach(var (pattern,replacement) in Vocabulary) value=pattern.Replace(value,replacement);
        // Preserve decimal chassis revisions; punctuation and word order do not restrict matching.
        var folded=new string(value.Normalize(System.Text.NormalizationForm.FormD).Where(c=>System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)!=System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        return Spaces().Replace(SearchPunctuation().Replace(folded.ToLowerInvariant()," ")," ").Trim();
    }
    [GeneratedRegex(@"[^\p{L}\p{N}.]+")]
    private static partial Regex SearchPunctuation();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
