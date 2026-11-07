using System;

namespace Mwrites.Installer.Core
{
    public static class ProductInfo
    {
        public const string Name = "Mwrites";
        public const string Publisher = "MSoft";
        public const string Url = "https://msoft.com.es";
        public const string Description = "Procesador de textos gratuito y de código abierto";
        public const string ExeName = "Mwrites.exe";
        public const string UninstallerName = "Mwrites.Uninstall.exe";

        public static string Version
        {
            get
            {
                var version = typeof(ProductInfo).Assembly.GetName().Version;
                return version == null ? "1.0.0" : version.ToString(3);
            }
        }
    }

    public sealed class InstallerException : Exception
    {
        public InstallerException(string message) : base(message)
        {
        }

        public InstallerException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
