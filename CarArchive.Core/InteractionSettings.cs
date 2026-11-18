namespace CarArchive.Core;

public sealed record ShortcutBinding(int Key, int Modifiers);
public sealed record AppCommandDefinition(string Id,string Label,string Scope,ShortcutBinding Default,bool Repeat=false);
public static class InteractionSettings
{
    public static readonly AppCommandDefinition[] Commands = [
        new("recognize","AI识别","Global",new(82,1)), new("verify","复制框选图片","Global",new(82,5)),
        new("previous","上一组","Global",new(37,2),true), new("next","下一组","Global",new(39,2),true),
        new("archive","确认命名并移动","Global",new(13,1)), new("removeGroups","移除分组，保留照片","Groups",new(46,0)),
        new("removePhotos","移除选中图片，保留照片","Photos",new(46,0)), new("recycleGroups","删除组并回收源照片","Groups",new(46,5)),
        new("recyclePhotos","删除图片并回收源照片","Photos",new(46,5)), new("undoArchive","撤回最近操作","Global",new(90,1)),new("redoEdit","重做最近操作","Global",new(90,5)) ];
    public static readonly IReadOnlyDictionary<string,string> Confirmations=new Dictionary<string,string> {
        ["clearNamingData"]="清除命名学习数据",["clearCrashLogs"]="清除崩溃日志",
        ["removeGroups"]="移除所选分组",["removePhotos"]="移除组内图片",["clearArchived"]="清理已归档记录",
        ["recycleGroups"]="删除组并回收源照片",["recyclePhotos"]="删除图片并回收源照片",["learnRule"]="采用本地命名规则" };
    public static Dictionary<string,ShortcutBinding> Defaults()=>Commands.ToDictionary(c=>c.Id,c=>c.Default);
    public static ShortcutBinding? Get(AppSettings settings,string id)=>settings.Shortcuts is not null && settings.Shortcuts.TryGetValue(id,out var b)?b:Commands.FirstOrDefault(c=>c.Id==id)?.Default;
    public static string? Validate(string id,ShortcutBinding binding,IReadOnlyDictionary<string,ShortcutBinding> bindings)
    {
        var command=Commands.FirstOrDefault(c=>c.Id==id);if(command is null) return "未知命令";
        if(binding.Key==0) return null;
        if(binding.Key<0 || binding.Key>255 || binding.Modifiers<0 || binding.Modifiers>7 || binding.Key is 16 or 17 or 18 or 91 or 92 or >=160 and <=165) return "请选择有效组合键，不支持 Windows 键";
        if(binding.Key==115 && (binding.Modifiers&2)!=0 || binding.Key==46 && (binding.Modifiers&3)==3 || binding.Key==9 && (binding.Modifiers&2)!=0 || binding.Key==27 && (binding.Modifiers&1)!=0 || binding.Key is 27 or 32 && (binding.Modifiers&2)!=0 || binding.Key==44) return "此组合键由系统保留";
        if(binding.Modifiers==0 && binding.Key>=48 && binding.Key<=90) return "字母与数字需搭配 Ctrl、Alt 或 Shift";
        foreach(var other in Commands.Where(c=>c.Id!=id)) if((bindings.TryGetValue(other.Id,out var b)?b:other.Default)==binding && (command.Scope=="Global" || other.Scope=="Global" || command.Scope==other.Scope)) return "与“"+other.Label+"”冲突";
        return null;
    }
    public static void Normalize(AppSettings settings)
    { settings.Shortcuts ??= Defaults();settings.SkippedConfirmations ??= []; }
    public static bool NativeTextEditing(ShortcutBinding binding)
    {
        // Rebinding an app command must never steal normal selection, clipboard, word navigation or typing.
        var k=binding.Key;var m=binding.Modifiers;
        if(m is 0 or 4) return true;
        if((m&1)!=0 && k is 65 or 67 or 86 or 88 or 89 or 90 or 8 or 46 or 35 or 36 or 37 or 38 or 39 or 40) return true;
        return false;
    }
    public static AppCommandDefinition? Match(AppSettings settings,int key,int modifiers,string scope,bool editing,bool suggestionOpen,bool repeated,bool modal=false)
    {
        if(modal || suggestionOpen && modifiers==0 && key is 13 or 27 or 38 or 40) return null;
        foreach(var command in Commands)
        {
            var binding=Get(settings,command.Id);
            if(binding is null || binding.Key==0 || binding.Key!=key || binding.Modifiers!=modifiers || command.Scope!="Global" && command.Scope!=scope) continue;
            if(repeated && !command.Repeat) return null;
            if(editing && (command.Id=="undoArchive" || command.Id.StartsWith("remove") || command.Id.StartsWith("recycle") || NativeTextEditing(binding))) return null;
            return command;
        }
        return null;
    }
    public static string Format(ShortcutBinding? b)
    {
        if(b is null || b.Key==0) return "未设置";
        var key=b.Key switch { 13=>"Enter",27=>"Esc",37=>"←",38=>"↑",39=>"→",40=>"↓",46=>"Delete",32=>"Space",9=>"Tab",>=112 and <=135=>"F"+(b.Key-111),_=>((char)b.Key).ToString() };
        return ((b.Modifiers&1)>0?"Ctrl+":"")+((b.Modifiers&4)>0?"Shift+":"")+((b.Modifiers&2)>0?"Alt+":"")+key;
    }
}
