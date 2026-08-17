using System;

namespace WinFIMLog.Configuration
{
    public sealed class ConfigurationValidationException : Exception
    {
        public ConfigurationValidationException(string message) : base(message)
        {
        }
    }
}
