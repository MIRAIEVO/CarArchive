namespace CarArchive.Core;
public sealed record RevisionFact(string Brand,string Family,string Generation,int FaceliftCount,string Reference);
public static class RevisionEvidence
{
    public static IReadOnlyList<RevisionFact> Confirmed {get;}=[
        new("Audi","Q7","2nd",2,"https://www.audi-mediacenter.com/en/press-releases/strong-in-design-comfort-and-technologythe-revised-audi-q7-15849/download"),
        new("Lexus","CT","1st",2,"https://global.toyota/jp/newsroom/lexus/36918524.html")
    ];
    public static string? Warning(CarIdentity car)
    {
        var family=NamingText.Family(car.Brand,car.Series,NamingText.FullModel(car));
        var fact=Confirmed.FirstOrDefault(item=>NamingText.Key(item.Brand)==NamingText.Key(car.Brand)&&NamingText.Key(item.Family)==NamingText.Key(family)&&item.Generation==NamingText.Generation(car.Generation));
        return fact is not null && NamingText.Phase(car.Phase)=="Facelift"?$"厂家资料表明本代有{fact.FaceliftCount}次改款，请明确填写第几次；资料：{fact.Reference}":null;
    }
}
