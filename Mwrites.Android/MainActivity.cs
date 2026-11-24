using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using MwritesApp = Mwrites.Portable.App.App;

namespace Mwrites.Android;

[Activity(
    Label = "Mwrites",
    Theme = "@style/MwritesTheme.NoActionBar",
    Icon = "@drawable/Icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : AvaloniaMainActivity<MwritesApp>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder);
}
