using UnityEngine;
using OpenCC.Unity;

public class FontConvert : MonoBehaviour
{
    public static FontConvert Instance { get; private set; }
    OpenChineseConverter converter;
    private void Start()
    {
        Instance = this;
        converter = new OpenChineseConverter();
    }

    public string ConvertToTraditional(string sourceText)
    {
        return converter.S2TW(sourceText);
    }

    public static string NumberToChinese(int number)
    {
        if (number == 0) return "零";

        string[] digits = { "", "一", "二", "三", "四", "五", "六", "七", "八", "九" };
        string[] units = { "", "十", "百", "千", "萬", "十萬", "百萬", "千萬", "億" };

        string result = "";
        bool needZero = false;
        string numStr = number.ToString();
        int len = numStr.Length;

        for (int i = 0; i < len; i++)
        {
            int digit = numStr[i] - '0';
            int unitIndex = len - 1 - i;

            if (digit == 0)
            {
                needZero = true;
            }
            else
            {
                if (needZero) result += "零";
                result += digits[digit] + units[unitIndex];
                needZero = false;
            }
        }

        // 處理「一十」開頭 → 簡化為「十」
        if (result.StartsWith("一十"))
            result = result.Substring(1);

        return result;
    }

}
