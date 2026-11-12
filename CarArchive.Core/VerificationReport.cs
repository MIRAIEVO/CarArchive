using System.Text.Json.Serialization;

namespace CarArchive.Core;

public enum VerificationStatus { NotRun, Passed, Failed, Insufficient, Incomplete, Canceled }
public enum FeatureMatch { NotVisible, Match, Contradiction }
public sealed class ReferenceFeature
{
    public string Id { get; set; } = "";
    public string Field { get; set; } = "";
    public string Expected { get; set; } = "";
    public string ReferenceUrl { get; set; } = "";
    public bool Critical { get; set; }
}
public sealed class FeatureComparison
{
    public string FeatureId { get; set; } = "";
    public string Match { get; set; } = "not_visible";
    public string Observation { get; set; } = "";
    [JsonIgnore] public FeatureMatch Outcome => Match.ToLowerInvariant() switch { "match"=>FeatureMatch.Match,"contradiction"=>FeatureMatch.Contradiction,_=>FeatureMatch.NotVisible };
}
public sealed class VerificationStage
{
    public string Name { get; set; } = "";
    public bool SearchEnabled { get; set; }
    public RecognitionResult Result { get; set; } = new();
}
public sealed class VerificationReport
{
    public VerificationStatus Status { get; set; }
    public CarIdentity Candidate { get; set; } = new();
    public CarIdentity? SuggestedIdentity { get; set; }
    public List<ReferenceFeature> Features { get; set; } = [];
    public List<FeatureComparison> Checks { get; set; } = [];
    public List<VerificationStage> Stages { get; set; } = [];
    public string Summary { get; set; } = "";
    public string Error { get; set; } = "";
    public bool ConfigVerified { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    [JsonIgnore] public string StatusText => Status switch { VerificationStatus.Passed=>ConfigVerified?"验证通过":"车系验证通过 · 具体配置未证实",VerificationStatus.Failed=>"验证不通过",VerificationStatus.Insufficient=>"证据不足",VerificationStatus.Canceled=>"查证已取消",VerificationStatus.Incomplete=>"查证未完成",_=>"尚未查证" };
    [JsonIgnore] public IReadOnlyList<Citation> Sources => Stages.SelectMany(s=>s.Result.Sources).Distinct().ToList();
    [JsonIgnore] public long InputTokens => Stages.Sum(s=>s.Result.InputTokens);
    [JsonIgnore] public long OutputTokens => Stages.Sum(s=>s.Result.OutputTokens);
    [JsonIgnore] public long? ThinkingTokens => Stages.All(s=>s.Result.ThinkingTokens.HasValue)?Stages.Sum(s=>s.Result.ThinkingTokens!.Value):null;
    [JsonIgnore] public int? SearchCount => Stages.Where(s=>s.SearchEnabled).All(s=>s.Result.SearchCount.HasValue)?Stages.Where(s=>s.SearchEnabled).Sum(s=>s.Result.SearchCount!.Value):null;
}
