namespace REghZy.Themes;

public enum ThemeType
{
    Dark,
    Red,
    Light,
}

public static class ThemeTypeExtension
{
    public static string? GetName(this ThemeType type)
    {
        return type switch
        {
            ThemeType.Light => "Dark_DarkBackLightBorder",
            ThemeType.Dark => "Dark_DarkBackDarkBorder",
            ThemeType.Red => "RedBlackTheme",
            _ => null,
        };
    }
}