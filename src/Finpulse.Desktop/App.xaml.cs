namespace Finpulse.Desktop;

public partial class App : global::System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("es-BO");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
        System.Windows.FrameworkElement.LanguageProperty.OverrideMetadata(typeof(System.Windows.FrameworkElement),
            new System.Windows.FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
        base.OnStartup(e);
    }
}
