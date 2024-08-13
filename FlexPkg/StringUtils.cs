namespace FlexPkg;

public static class StringUtils
{
    public static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength
            ? value
            : value[..maxLength] + "...";
}