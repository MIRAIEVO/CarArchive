namespace CarArchive.Core;

public enum RecognitionStrength { Minimal, Low, Medium, High, Xhigh, Max, Ultra }

public sealed record RecognitionRoundReport(string? Effort, int SearchTarget, bool ToolLimit, string Usage, long? ThinkingTokens, int? SearchCount);

public sealed record RecognitionPlan(RecognitionStrength Strength, bool Online, string? Effort, int Budget, int[] SearchTargets, bool ToolLimit)
{
    public static RecognitionStrength FromLegacy(RecognitionMode mode) => mode switch
    {
        RecognitionMode.Economy => RecognitionStrength.Minimal,
        RecognitionMode.Fast => RecognitionStrength.Medium,
        RecognitionMode.Accurate => RecognitionStrength.Max,
        _ => RecognitionStrength.Low
    };

    public static string DisplayLabel(string text) => text.Replace("联网识别","AI识别").Replace("Gemini识别","AI识别");
    public static bool IsOnline(AppSettings settings) => settings.Provider==RecognitionProvider.Gemini
        ? settings.Search==SearchPreference.Grounded : settings.Profile(settings.Provider).Search;
    public static bool IsOnline(AppSettings settings,PhotoGroup group) =>
        group.SearchOverrides.TryGetValue(settings.Provider,out var online)?online:IsOnline(settings);
    public static void SetOnline(AppSettings settings,bool online)
    {
        if(settings.Provider==RecognitionProvider.Gemini)
            settings.Search=online?SearchPreference.Grounded:SearchPreference.Gemini;
        else
        {
            var profile=settings.Profile(settings.Provider);
            profile.Search=online;settings.ProviderProfiles[settings.Provider]=profile;
        }
    }
    public static string Model(AppSettings settings) => settings.Provider==RecognitionProvider.Gemini?settings.Model:settings.Profile(settings.Provider).Model;

    public static RecognitionPlan Create(AppSettings settings,RecognitionStrength? overrideStrength=null,bool? overrideOnline=null)
    {
        var strength=overrideStrength ?? settings.Strength ?? FromLegacy(settings.Mode);
        if (!Enum.IsDefined(strength)) strength=RecognitionStrength.Low;
        var rank=(int)strength;
        var online=overrideOnline??IsOnline(settings);
        var model=Model(settings);
        var provider=settings.Provider;
        string[] desired=["minimal","low","medium","high","xhigh","max","max"];
        string[] supported=ProviderCatalog.Efforts(provider,model);
        string? effort=supported.Length==0?null:desired.Take(rank==6?6:rank+1).Reverse().FirstOrDefault(supported.Contains)??supported[0];
        if(rank==0 && supported.Contains("none")) effort="none";
        if(provider==RecognitionProvider.DeepSeek && supported.Length>0)
            effort=rank switch {0=>"none",1=>"low",2 or 3 or 4=>"high",_=>"max"};
        int[] targets=strength switch
        {
            RecognitionStrength.Medium=>[2], RecognitionStrength.High=>[3], RecognitionStrength.Xhigh=>[2,1],
            RecognitionStrength.Max=>[2,2],RecognitionStrength.Ultra=>[2,2,2],_=>[1]
        };
        if(!online) Array.Fill(targets,0);
        var budget=(provider==RecognitionProvider.OpenAI?new[]{4096,4096,16384,24576,32768,49152,49152}:
            new[]{4096,4096,8192,12288,16384,24576,32768})[rank];
        return new(strength,online,effort,budget,targets,
            provider is RecognitionProvider.Claude or RecognitionProvider.OpenAI);
    }
}
