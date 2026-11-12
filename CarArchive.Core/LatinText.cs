using System.Globalization;
using System.Text;

namespace CarArchive.Core;

public static class LatinText
{
    // Only spelling is normalized; recognition evidence and existing folder paths stay intact.
    public static string English(string text)
    {
        var ascii=true;foreach(var letter in text) if(letter>127) { ascii=false;break; }
        if(ascii) return text;
        var output=new StringBuilder(text.Length);
        foreach(var c in text.Normalize(NormalizationForm.FormKD))
        {
            if(CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark) continue;
            output.Append(c switch { 'æ'=>"ae",'Æ'=>"AE",'œ'=>"oe",'Œ'=>"OE",'ß'=>"ss",'ẞ'=>"SS",'ø'=>"o",'Ø'=>"O",'ł'=>"l",'Ł'=>"L",'đ' or 'ð'=>"d",'Đ' or 'Ð'=>"D",'þ'=>"th",'Þ'=>"Th",'ħ'=>"h",'Ħ'=>"H",'ı'=>"i",'ŋ'=>"n",'Ŋ'=>"N",_=>c.ToString() });
        }
        return output.ToString();
    }
}
