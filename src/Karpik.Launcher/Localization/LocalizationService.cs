using System.ComponentModel;
using System.Globalization;

namespace Karpik.Launcher.Localization;

public class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Instance { get; } = new();

    private CultureInfo _culture = CultureInfo.CurrentUICulture;

    public event PropertyChangedEventHandler? PropertyChanged;

    public CultureInfo Culture => _culture;

    public string this[string key] => Strings.ResourceManager.GetString(key, _culture) ?? $"[{key}]";

    public string Format(string key, params object?[] args)
    {
        return string.Format(_culture, this[key], args);
    }

    public void SetCulture(string cultureName)
    {
        SetCulture(CultureInfo.GetCultureInfo(cultureName));
    }

    public void SetCulture(CultureInfo culture)
    {
        if (_culture.Equals(culture))
            return;

        _culture = culture;

        // Язык интерфейса.
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        // Формат чисел, дат и валют.
        CultureInfo.CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;

        // Уведомляем все привязки к индексатору.
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs("Item[]"));
    }
}