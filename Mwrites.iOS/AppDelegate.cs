using Avalonia;
using Avalonia.iOS;
using Foundation;
using MwritesApp = Mwrites.Portable.App.App;

namespace Mwrites.iOS;

[Register("AppDelegate")]
public partial class AppDelegate : AvaloniaAppDelegate<MwritesApp>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder);
}
