using System.Text.RegularExpressions;

namespace FlexPkg;

public static partial class RegexUtils
{
    [GeneratedRegex(@"\n+", RegexOptions.Compiled)]
    public static partial Regex StripLines();
}