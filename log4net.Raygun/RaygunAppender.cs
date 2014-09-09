﻿using System;
using System.Threading.Tasks;
using log4net.Appender;
using log4net.Core;
using log4net.Util;
using Mindscape.Raygun4Net;
using Mindscape.Raygun4Net.Messages;

namespace log4net.Raygun
{
    public class RaygunAppender : AppenderSkeleton
    {
        public static readonly Type DeclaringType = typeof(RaygunAppender);
        public static readonly TimeSpan DefaultTimeBetweenRetries = TimeSpan.FromMilliseconds(5000);
        private TimeSpan _timeBetweenRetries;

        private readonly IUserCustomDataBuilder _userCustomDataBuilder;
        private readonly IRaygunMessageBuilder _raygunMessageBuilder;
        private readonly Func<string, IRaygunClient> _raygunClientFactory;
        private readonly TaskScheduler _taskScheduler;

        public RaygunAppender()
            : this(new UserCustomDataBuilder(), new RaygunMessageBuilder(() => new HttpContextAdapter()), apiKey => new RaygunClientAdapter(new RaygunClient(apiKey)), TaskScheduler.Default)
        {
        }

        internal RaygunAppender(IUserCustomDataBuilder userCustomDataBuilder, IRaygunMessageBuilder raygunMessageBuilder, Func<string, IRaygunClient> raygunClientFactory, TaskScheduler taskScheduler)
        {
            _userCustomDataBuilder = userCustomDataBuilder;
            _raygunMessageBuilder = raygunMessageBuilder;
            _raygunClientFactory = raygunClientFactory;
            _taskScheduler = taskScheduler;
        }

        public virtual string ApiKey { get; set; }
        public virtual int Retries { get; set; }

        public virtual TimeSpan TimeBetweenRetries
        {
            get { return _timeBetweenRetries == TimeSpan.Zero ? DefaultTimeBetweenRetries : _timeBetweenRetries; }
            set { _timeBetweenRetries = value; }
        }

        public virtual string IgnoredFormNames { get; set; }
        public virtual string ExceptionFilter { get; set; }
        public virtual string RenderedMessageFilter { get; set; }

        protected override void Append(LoggingEvent loggingEvent)
        {
            LogLog.Debug(DeclaringType, string.Format("RaygunAppender: Received Logging Event with Logging Level '{0}'", loggingEvent.Level));

            Exception exception = null;

            var exceptionObject = loggingEvent.ExceptionObject;
            if (exceptionObject != null)
            {
                exception = exceptionObject.GetBaseException();
                LogLog.Debug(DeclaringType, string.Format("RaygunAppender: Setting Exception to BaseException of LoggingEvent.ExceptionObject"));
            }

            if (exception == null)
            {
                var messageObject = loggingEvent.MessageObject;
                if (messageObject != null)
                {
                    var messageObjectAsException = messageObject as Exception;
                    if (messageObjectAsException != null)
                    {
                        exception = messageObjectAsException;
                        LogLog.Debug(DeclaringType, string.Format("RaygunAppender: Setting Exception to MessageObject"));
                    }
                }
            }

            SendExceptionToRaygunInBackground(exception, loggingEvent);
        }

        private void SendExceptionToRaygunInBackground(Exception exception, LoggingEvent loggingEvent)
        {
            LogLog.Debug(DeclaringType, "RaygunAppender: Building UserCustomData dictionary");
            var userCustomData = _userCustomDataBuilder.Build(loggingEvent);
            LogLog.Debug(DeclaringType, "RaygunAppender: Building Raygun message");

            RaygunMessage raygunMessage = _raygunMessageBuilder.BuildMessage(exception, loggingEvent, userCustomData, ExceptionFilter, RenderedMessageFilter, IgnoredFormNames);

            LogLog.Debug(DeclaringType, string.Format("RaygunAppender: Sending Raygun message in a background task. Retries: '{0}', TimeBetweenRetries: '{1}'", Retries, TimeBetweenRetries));
            new TaskFactory(_taskScheduler)
                .StartNew(() => Retry.Action(() =>
                {
                    try
                    {
                        SendExceptionToRaygun(raygunMessage);
                    }
                    catch (Exception ex)
                    {
                        ErrorHandler.Error(string.Format("RaygunAppender: Could not send error to the Raygun API, retried {0} times", Retries), ex);
                    }
                }, Retries, TimeBetweenRetries));
        }

        private void SendExceptionToRaygun(RaygunMessage raygunMessage)
        {
            if (string.IsNullOrEmpty(ApiKey))
            {
                ErrorHandler.Error("RaygunAppender: API Key is empty");
            }

            var raygunClient = _raygunClientFactory(ApiKey);

            raygunClient.Send(raygunMessage);

            LogLog.Debug(DeclaringType, "RaygunAppender: Raygun message sent");
        }

        protected internal static class PropertyKeys
        {
            public const string Tags = "log4net.Raygun.Tags";
        }
    }
}