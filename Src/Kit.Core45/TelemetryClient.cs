﻿namespace Microsoft.HockeyApp
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Globalization;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Channel;
    using DataContracts;
    using Extensibility;
    using Extensibility.Implementation;
    using Extensibility.Implementation.Platform;
    using Extensibility.Implementation.Tracing;

    /// <summary>
    /// Send events, metrics and other telemetry to the Application Insights service.
    /// </summary>
    internal sealed class TelemetryClient
    {
        private readonly TelemetryConfiguration configuration;
        private readonly IDebugOutput debugOutput;
        private TelemetryContext context;
        private ITelemetryChannel channel;

        /// <summary>
        /// Initializes a new instance of the <see cref="TelemetryClient" /> class. Send telemetry with the active configuration, usually loaded from configuration file.
        /// </summary>
        public TelemetryClient() : this(TelemetryConfiguration.Active)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TelemetryClient" /> class. Send telemetry with the specified <paramref name="configuration"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException">The <paramref name="configuration"/> is null.</exception>
        public TelemetryClient(TelemetryConfiguration configuration)
        {
            if (configuration == null)
            {
                CoreEventSource.Log.LogVerbose("No Telemetry Configuration provided.Using the default TelemetryConfiguration.Active.");
                configuration = TelemetryConfiguration.Active;
            }

            this.configuration = configuration;
            this.debugOutput = PlatformSingleton.Current.GetDebugOutput();
        }

        /// <summary>
        /// Gets or sets the current context that will be used to augment telemetry you send.
        /// </summary>
        internal TelemetryContext Context
        {
            get
            {
                // In order to prevent a deadlock, we are calling async method from sync using Task.Run to offload a work to a ThreadPool
                // thread which does not have a SynchronizationContext and there is no real risk for a deadlock.
                // http://stackoverflow.com/questions/28305968/use-task-run-in-synchronous-method-to-avoid-deadlock-waiting-on-async-method
                LazyInitializer.EnsureInitialized(ref this.context, () => { return Task.Run(async () => { return await this.CreateInitializedContextAsync(); }).Result; });
                return this.context;
            }

            set
            {
                this.context = value;
            }
        }

        /// <summary>
        /// Gets or sets the default instrumentation key for all <see cref="ITelemetry"/> objects logged in this <see cref="TelemetryClient"/>.
        /// </summary>
        internal string InstrumentationKey
        {
            get { return this.Context.InstrumentationKey; }
            set { this.Context.InstrumentationKey = value; }
        }

        /// <summary>
        /// Gets or sets the channel used by the client helper. Note that this doesn't need to be public as a customer can create a new client 
        /// with a new channel via telemetry configuration.
        /// </summary>
        internal ITelemetryChannel Channel
        {
            get
            {
                ITelemetryChannel output = this.channel;
                if (output == null)
                {
                    output = this.configuration.TelemetryChannel;
                    this.channel = output;
                }

                return output;
            }

            set
            {
                this.channel = value;
            }
        }

        /// <summary>
        /// Check to determine if the tracking is enabled.
        /// </summary>
        public bool IsEnabled()
        {
            return !this.configuration.DisableTelemetry;
        }

        /// <summary>
        /// Send an <see cref="EventTelemetry"/> for display in Diagnostic Search and aggregation in Metrics Explorer.
        /// </summary>
        /// <param name="eventName">A name for the event.</param>
        /// <param name="properties">Named string values you can use to search and classify events.</param>
        /// <param name="metrics">Measurements associated with this event.</param>
        public void TrackEvent(string eventName, IDictionary<string, string> properties = null, IDictionary<string, double> metrics = null)
        {
            var telemetry = new EventTelemetry(eventName);

            if (properties != null && properties.Count > 0)
            {
                Utils.CopyDictionary(properties, telemetry.Context.Properties);
            }

            if (metrics != null && metrics.Count > 0)
            {
                Utils.CopyDictionary(metrics, telemetry.Metrics);
            }

            this.TrackEvent(telemetry);
        }

        /// <summary>
        /// Send an <see cref="EventTelemetry"/> for display in Diagnostic Search and aggregation in Metrics Explorer.
        /// </summary>
        /// <param name="telemetry">An event log item.</param>
        internal void TrackEvent(EventTelemetry telemetry)
        {
            if (telemetry == null)
            {
                telemetry = new EventTelemetry();
            }

            this.Track(telemetry);
        }

        /// <summary>
        /// Send a trace message for display in Diagnostic Search.
        /// </summary>
        /// <param name="message">Message to display.</param>
        internal void TrackTrace(string message)
        {
            this.TrackTrace(new TraceTelemetry(message));
        }

        /// <summary>
        /// Send a trace message for display in Diagnostic Search.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="severityLevel">Trace severity level.</param>
        internal void TrackTrace(string message, SeverityLevel severityLevel)
        {
            this.TrackTrace(new TraceTelemetry(message, severityLevel));
        }

        /// <summary>
        /// Send a trace message for display in Diagnostic Search.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="properties">Named string values you can use to search and classify events.</param>
        internal void TrackTrace(string message, IDictionary<string, string> properties)
        {
            TraceTelemetry telemetry = new TraceTelemetry(message);

            if (properties != null && properties.Count > 0)
            {
                Utils.CopyDictionary(properties, telemetry.Context.Properties);
            }

            this.TrackTrace(telemetry);
        }

        /// <summary>
        /// Send a trace message for display in Diagnostic Search.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="severityLevel">Trace severity level.</param>
        /// <param name="properties">Named string values you can use to search and classify events.</param>
        internal void TrackTrace(string message, SeverityLevel severityLevel, IDictionary<string, string> properties)
        {
            TraceTelemetry telemetry = new TraceTelemetry(message, severityLevel);

            if (properties != null && properties.Count > 0)
            {
                Utils.CopyDictionary(properties, telemetry.Context.Properties);
            }

            this.TrackTrace(telemetry);
        }

        /// <summary>
        /// Send a trace message for display in Diagnostic Search.
        /// </summary>
        /// <param name="telemetry">Message with optional properties.</param>
        internal void TrackTrace(TraceTelemetry telemetry)
        {
            telemetry = telemetry ?? new TraceTelemetry();
            this.Track(telemetry);
        }

        /// <summary>
        /// Send a <see cref="MetricTelemetry"/> for aggregation in Metric Explorer.
        /// </summary>
        /// <param name="name">Metric name.</param>
        /// <param name="value">Metric value.</param>
        /// <param name="properties">Named string values you can use to classify and filter metrics.</param>
        internal void TrackMetric(string name, double value, IDictionary<string, string> properties = null)
        {
            var telemetry = new MetricTelemetry(name, value);
            if (properties != null && properties.Count > 0)
            {
                Utils.CopyDictionary(properties, telemetry.Properties);
            }

            this.TrackMetric(telemetry);
        }

        /// <summary>
        /// Send a <see cref="MetricTelemetry"/> for aggregation in Metric Explorer.
        /// </summary>
        internal void TrackMetric(MetricTelemetry telemetry)
        {
            if (telemetry == null)
            {
                telemetry = new MetricTelemetry();
            }

            this.Track(telemetry);
        }

        /// <summary>
        /// Send an <see cref="ExceptionTelemetry"/> for display in Diagnostic Search.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="properties">Named string values you can use to classify and search for this exception.</param>
        /// <param name="metrics">Additional values associated with this exception.</param>
        internal void TrackException(Exception exception, IDictionary<string, string> properties = null, IDictionary<string, double> metrics = null)
        {
            if (exception == null)
            {
                exception = new Exception(Utils.PopulateRequiredStringValue(null, "message", typeof(ExceptionTelemetry).FullName));
            }

            var telemetry = new ExceptionTelemetry(exception) { HandledAt = ExceptionHandledAt.UserCode };

            if (properties != null && properties.Count > 0)
            {
                Utils.CopyDictionary(properties, telemetry.Context.Properties);
            }

            if (metrics != null && metrics.Count > 0)
            {
                Utils.CopyDictionary(metrics, telemetry.Metrics);
            }

            this.TrackException(telemetry);
        }

        /// <summary>
        /// Send an <see cref="ExceptionTelemetry"/> for display in Diagnostic Search.
        /// </summary>
        internal void TrackException(ExceptionTelemetry telemetry)
        {
            if (telemetry == null)
            {
                var exception = new Exception(Utils.PopulateRequiredStringValue(null, "message", typeof(ExceptionTelemetry).FullName));
                telemetry = new ExceptionTelemetry(exception)
                {
                    HandledAt = ExceptionHandledAt.UserCode,
                };
            }

            this.Track(telemetry);
        }

        /// <summary>
        /// Send information about external dependency call in the application.
        /// </summary>
        /// <param name="dependencyName">External dependency name.</param>
        /// <param name="commandName">Dependency call command name.</param>
        /// <param name="startTime">The time when the dependency was called.</param>
        /// <param name="duration">The time taken by the external dependency to handle the call.</param>
        /// <param name="success">True if the dependency call was handled successfully.</param>
        internal void TrackDependency(string dependencyName, string commandName, DateTimeOffset startTime, TimeSpan duration, bool success)
        {
            this.TrackDependency(new DependencyTelemetry(dependencyName, commandName, startTime, duration, success));
        }

        /// <summary>
        /// Send information about external dependency call in the application.
        /// </summary>
        internal void TrackDependency(DependencyTelemetry telemetry)
        {
            if (telemetry == null)
            {
                telemetry = new DependencyTelemetry();
            }

            this.Track(telemetry);
        }

        /// <summary>
        /// This method is an internal part of Application Insights infrastructure. Do not call.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        internal void Track(ITelemetry telemetry)
        {
            // TALK TO YOUR TEAM MATES BEFORE CHANGING THIS.
            // This method needs to be public so that we can build and ship new telemetry types without having to ship core.
            // It is hidden from intellisense to prevent customer confusion.
            if (this.IsEnabled())
            {
                string instrumentationKey = this.Context.InstrumentationKey;

                if (string.IsNullOrEmpty(instrumentationKey))
                {
                    instrumentationKey = this.configuration.InstrumentationKey;
                }

                if (string.IsNullOrEmpty(instrumentationKey))
                {
                    return;
                }

                var telemetryWithProperties = telemetry as ISupportProperties;
                if (telemetryWithProperties != null)
                {
                    if (this.Channel.DeveloperMode.HasValue && this.Channel.DeveloperMode.Value)
                    {
                        if (!telemetryWithProperties.Properties.ContainsKey("DeveloperMode"))
                        {
                            telemetryWithProperties.Properties.Add("DeveloperMode", "true");
                        }
                    }

                    Utils.CopyDictionary(this.Context.Properties, telemetryWithProperties.Properties);
                }

                telemetry.Context.Initialize(this.Context, instrumentationKey);
                foreach (ITelemetryInitializer initializer in this.configuration.TelemetryInitializers)
                {
                    try
                    {
                        initializer.Initialize(telemetry);
                    }
                    catch (Exception exception)
                    {
                        CoreEventSource.Log.LogError(string.Format(
                                                        CultureInfo.InvariantCulture,
                                                        "Exception while initializing {0}, exception message - {1}",
                                                        initializer.GetType().FullName,
                                                        exception.ToString()));
                    }
                }

                telemetry.Sanitize();

                this.Channel.Send(telemetry);

                if (System.Diagnostics.Debugger.IsAttached)
                {
                    this.WriteTelemetryToDebugOutput(telemetry);
                }
            }
        }

        /// <summary>
        /// Send information about the page viewed in the application.
        /// </summary>
        /// <param name="name">Name of the page.</param>
        internal void TrackPageView(string name)
        {
            this.Track(new PageViewTelemetry(name));
        }

        /// <summary>
        /// Send information about the page viewed in the application.
        /// </summary>
        internal void TrackPageView(PageViewTelemetry telemetry)
        {
            if (telemetry == null)
            {
                telemetry = new PageViewTelemetry();
            }

            this.Track(telemetry);
        }

        /// <summary>
        /// Send information about a request handled by the application.
        /// </summary>
        /// <param name="name">The request name.</param>
        /// <param name="startTime">The time when the page was requested.</param>
        /// <param name="duration">The time taken by the application to handle the request.</param>
        /// <param name="responseCode">The response status code.</param>
        /// <param name="success">True if the request was handled successfully by the application.</param>
        internal void TrackRequest(string name, DateTimeOffset startTime, TimeSpan duration, string responseCode, bool success)
        {
            this.Track(new RequestTelemetry(name, startTime, duration, responseCode, success));
        }

        /// <summary>
        /// Send information about a request handled by the application.
        /// </summary>
        internal void TrackRequest(RequestTelemetry request)
        {
            if (request == null)
            {
                request = new RequestTelemetry();
            }

            this.Track(request);
        }
        
        /// <summary>
        /// Flushes the in-memory buffer. 
        /// </summary>
        internal void Flush()
        {
            this.Channel.Flush();
        }

        private async Task<TelemetryContext> CreateInitializedContextAsync()
        {
            var context = new TelemetryContext();
            foreach (IContextInitializer initializer in this.configuration.ContextInitializers)
            {
                await initializer.Initialize(context);
            }

            return context;
        }

        private void WriteTelemetryToDebugOutput(ITelemetry telemetry)
        {
            if (this.debugOutput.IsLogging())
            {
                using (var stringWriter = new StringWriter(CultureInfo.InvariantCulture))
                {
                    string serializedTelemetry = JsonSerializer.SerializeAsString(telemetry);
                    this.debugOutput.WriteLine("HockeySDK Telemetry: " + serializedTelemetry);
                }
            }
        }
    }
}
