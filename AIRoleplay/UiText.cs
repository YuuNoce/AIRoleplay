namespace AIRoleplay;

public enum UiLanguage
{
    English,
    Japanese,
}

public static class UiText
{
    public static string T(UiLanguage language, string english, string japanese) =>
        language == UiLanguage.Japanese ? japanese : english;

    public static string LanguageName(UiLanguage language) =>
        language == UiLanguage.Japanese ? "日本語" : "English";
}
