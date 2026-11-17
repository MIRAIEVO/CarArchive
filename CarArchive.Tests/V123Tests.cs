using CarArchive.Core;
public static class V123Tests
{
 public static void Register(Action<string,Func<Task>> test)
 {
  test("123: merge preserves populated identity, then recognition, then workspace ordering",()=>
  {
   var empty=new PhotoGroup();var manual=new PhotoGroup {Confirmed=new(){Brand="Audi",Series="A4"},FolderName="A4 custom",ManualName=true};
   var recognized=new PhotoGroup {Confirmed=new(){Brand="BMW",Series="3 Series"},Suggestion=new(){Identity=new(){Brand="BMW",Series="3 Series"}},FolderName="3 Series (G20)"};
   var recognizedLater=new PhotoGroup {Confirmed=new(){Brand="Lexus",Series="LS"},Suggestion=new(){Identity=new(){Brand="Lexus",Series="LS"}}};
   if(GroupMergePolicy.Choose([empty,manual])!=manual || GroupMergePolicy.Choose([manual,recognized])!=recognized || GroupMergePolicy.Choose([recognized,recognizedLater])!=recognized || GroupMergePolicy.Choose([manual,empty])!=manual)throw new Exception("Merge lost preferred metadata");
   var pasted=new PhotoGroup {Confirmed=new(){Brand="Lexus"},NamingOriginalIdentity=new(){Brand="Lexus"}};if(GroupMergePolicy.Choose([manual,pasted])!=pasted)throw new Exception("JSON recognition source lost priority");
   if(manual.FolderName!="A4 custom" || !manual.ManualName || recognized.FolderName!="3 Series (G20)")throw new Exception("Winner mutated");return Task.CompletedTask;
  });
 }
}
