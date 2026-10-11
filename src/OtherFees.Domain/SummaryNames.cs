using System.Text;
using System.Text.RegularExpressions;

namespace OtherFees.Domain
{
    // 费用校验与Excel识别共用完整名称规则，不按“包含合计/税金”判断。
    public static class SummaryNames
    {
        public static string Compact(string value)
            => Regex.Replace((value ?? string.Empty).Trim().Normalize(NormalizationForm.FormC)
                .Replace("（", "(").Replace("）", ")"), @"\s+", string.Empty);

        public static bool IsFixedItem(string value)
        {
            string name = Compact(value);
            for (int order = 1; order <= 6; order++)
                if (name == Compact(FixedWorkItem.NameForOrder(order))) return true;
            return false;
        }

        public static bool IsTax(string value)
            => Regex.IsMatch(Compact(value), @"^税金\(\d+(?:\.\d+)?%\)$");

        public static bool IsTotal(string value)
        {
            string name = Compact(value);
            return name == "不含税合计" || name == "含税合计" || name == "小计" || name == "合计" || IsTax(name);
        }

        public static bool IsSectionTitle(string value)
        {
            string name = Compact(value);
            return name == "建设安装工程费" || name == "设计费" || name == "其他费用" || name == "其它费用";
        }

        public static bool IsKnownChapter(string value)
        {
            string name = Compact(value);
            return name == "一建设安装工程费" || name == "二设计费" || name == "三不含税合计"
                || (name.StartsWith("四", System.StringComparison.Ordinal) && IsTax(name.Substring(1)))
                || name == "五含税合计";
        }

        public static bool IsReserved(string value)
        {
            string name = Compact(value);
            return IsFixedItem(name) || IsTotal(name) || IsSectionTitle(name)
                || Regex.IsMatch(name, "^[一二三四五六七八九十][、.．]")
                || Regex.IsMatch(name, "^[一二三四五六七八九十]+建设安装工程费");
        }
    }
}
