﻿namespace Microsoft.HockeyApp.Windows
{
    using System;

    using Extensibility;
    using Extensibility.Implementation.Platform;
    using TestFramework;    
    
    using VisualStudio.TestPlatform.UnitTestFramework;

    [TestClass]
    public class WindowsAppInitializerTests
    {
        [TestMethod]
        public void WindowsBootStrapperSettingTheInstrumentationKeyWhenSupplied()
        {
            string instrumentationKey = Guid.NewGuid().ToString();
            WindowsAppInitializer.InitializeAsync(instrumentationKey, new TelemetryConfiguration() { Collectors = WindowsCollectors.Metadata }).GetAwaiter().GetResult();

            Assert.IsFalse(string.IsNullOrEmpty(TelemetryConfiguration.Active.InstrumentationKey));
        }

        [TestMethod]
        public void WindowsBootstraperSetInstrumentationKeyWhenSuppliedAndConfigurationFileIsEmpty()
        {
            string instrumentationKey = Guid.NewGuid().ToString();
            var platform = new StubPlatform { OnReadConfigurationXml = () => Configuration(string.Empty) };
            PlatformSingleton.Current = platform;

            WindowsAppInitializer.InitializeAsync(instrumentationKey, new TelemetryConfiguration() { Collectors = WindowsCollectors.Metadata }).GetAwaiter().GetResult();

            Assert.IsFalse(string.IsNullOrEmpty(TelemetryConfiguration.Active.InstrumentationKey));
            Assert.AreEqual(instrumentationKey, TelemetryConfiguration.Active.InstrumentationKey);
        }

        [TestMethod]
        public void WindowsBootstraperSetInstrumentationKeyWhenSuppliedAndConfigurationFileHasAnEmptyKey()
        {
            string instrumentationKey = Guid.NewGuid().ToString();
            var platform = new StubPlatform { OnReadConfigurationXml = () => Configuration("<InstrumentationKey></InstrumentationKey>") };
            PlatformSingleton.Current = platform;

            WindowsAppInitializer.InitializeAsync(instrumentationKey, new TelemetryConfiguration() { Collectors = WindowsCollectors.Metadata }).GetAwaiter().GetResult();

            Assert.IsFalse(string.IsNullOrEmpty(TelemetryConfiguration.Active.InstrumentationKey));
            Assert.AreEqual(instrumentationKey, TelemetryConfiguration.Active.InstrumentationKey);
        }

        [TestMethod]
        public void WindowsBootstrapperInitializeTheModulesAccordingToInputFlags()
        {
            WindowsAppInitializer.InitializeAsync(Guid.NewGuid().ToString(), new TelemetryConfiguration() { Collectors = WindowsCollectors.Session | WindowsCollectors.Metadata }).GetAwaiter().GetResult();
            TelemetryConfiguration configuration = TelemetryConfiguration.Active;
            Assert.AreEqual(3, configuration.ContextInitializers.Count);
            Assert.AreEqual(3, configuration.TelemetryInitializers.Count);
            Assert.IsNotNull(configuration.TelemetryChannel);
        }

        private static string Configuration(string innerXml)
        {
            return
              @"<?xml version=""1.0"" encoding=""utf-8"" ?>
                <ApplicationInsights xmlns=""http://schemas.microsoft.com/ApplicationInsights/2013/Settings"">
" + innerXml + @"
                </ApplicationInsights>";
        }
    }
}
