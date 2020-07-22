﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace TubumuMeeting.Mediasoup
{
    public class PlainTransport : Transport
    {
        /// <summary>
        /// Logger.
        /// </summary>
        private readonly ILogger<PlainTransport> _logger;

        #region Producer data.

        public bool? RtcpMux { get; set; }

        public bool? Comedia { get; set; }

        public TransportTuple Tuple { get; private set; }

        public TransportTuple? RtcpTuple { get; private set; }

        public SrtpParameters? SrtpParameters { get; private set; }

        #endregion

        /// <summary>
        /// <para>Events:</para>
        /// <para>@emits tuple - (tuple: TransportTuple)</para>
        /// <para>@emits rtcptuple - (rtcpTuple: TransportTuple)</para>
        /// <para>@emits sctpstatechange - (sctpState: SctpState)</para>
        /// <para>@emits trace - (trace: TransportTraceEventData)</para>
        /// <para>Observer events:</para>
        /// <para>@emits close</para>
        /// <para>@emits newproducer - (producer: Producer)</para>
        /// <para>@emits newconsumer - (consumer: Consumer)</para>
        /// <para>@emits newdataproducer - (dataProducer: DataProducer)</para>
        /// <para>@emits newdataconsumer - (dataConsumer: DataConsumer)</para>
        /// <para>@emits tuple - (tuple: TransportTuple)</para>
        /// <para>@emits rtcptuple - (rtcpTuple: TransportTuple)</para>
        /// <para>@emits sctpstatechange - (sctpState: SctpState)</para>
        /// <para>@emits trace - (trace: TransportTraceEventData)</para>
        /// </summary>
        /// <param name="loggerFactory"></param>
        /// <param name="transportInternalData"></param>
        /// <param name="sctpParameters"></param>
        /// <param name="sctpState"></param>
        /// <param name="channel"></param>
        /// <param name="payloadChannel"></param>
        /// <param name="appData"></param>
        /// <param name="getRouterRtpCapabilities"></param>
        /// <param name="getProducerById"></param>
        /// <param name="getDataProducerById"></param>
        public PlainTransport(ILoggerFactory loggerFactory,
            TransportInternalData transportInternalData,
            SctpParameters? sctpParameters,
            SctpState? sctpState,
            Channel channel,
            PayloadChannel payloadChannel,
            Dictionary<string, object>? appData,
            Func<RtpCapabilities> getRouterRtpCapabilities,
            Func<string, Producer?> getProducerById,
            Func<string, DataProducer?> getDataProducerById,
            bool? rtcpMux,
            bool? comedia,
            TransportTuple tuple,
            TransportTuple? rtcpTuple,
            SrtpParameters? srtpParameters
            ) : base(loggerFactory, transportInternalData, sctpParameters, sctpState, channel, payloadChannel, appData, getRouterRtpCapabilities, getProducerById, getDataProducerById)
        {
            _logger = loggerFactory.CreateLogger<PlainTransport>();

            // Data
            RtcpMux = rtcpMux;
            Comedia = comedia;
            Tuple = tuple;
            RtcpTuple = rtcpTuple;
            SrtpParameters = srtpParameters;

            HandleWorkerNotifications();
        }

        /// <summary>
        /// Close the PlainTransport.
        /// </summary>
        public override void Close()
        {
            if (Closed)
            {
                return;
            }

            if (SctpState.HasValue)
            {
                SctpState = TubumuMeeting.Mediasoup.SctpState.Closed;
            }

            base.Close();
        }

        /// <summary>
        /// Router was closed.
        /// </summary>
        public override void RouterClosed()
        {
            if (Closed)
            {
                return;
            }

            if (SctpState.HasValue)
            {
                SctpState = TubumuMeeting.Mediasoup.SctpState.Closed;
            }

            base.RouterClosed();
        }

        /// <summary>
        /// Provide the PipeTransport remote parameters.
        /// </summary>
        public override Task ConnectAsync(object parameters)
        {
            _logger.LogDebug("ConnectAsync()");

            if (!(parameters is PlainTransportConnectParameters connectParameters))
            {
                throw new Exception($"{nameof(parameters)} type is not PipTransportConnectParameters");
            }
            return ConnectAsync(connectParameters);
        }

        private async Task ConnectAsync(PlainTransportConnectParameters plainTransportConnectParameters)
        {
            var reqData = plainTransportConnectParameters;

            var status = await Channel.RequestAsync(MethodId.TRANSPORT_CONNECT, Internal, reqData);
            var responseData = JsonConvert.DeserializeObject<PlainTransportConnectResponseData>(status!);

            // Update data.
            if (responseData.Tuple != null)
            {
                Tuple = responseData.Tuple;
            }

            if (responseData.RtcpTuple != null)
            {
                RtcpTuple = responseData.RtcpTuple;
            }

            SrtpParameters = responseData.SrtpParameters;
        }

        #region Event Handlers

        private void HandleWorkerNotifications()
        {
            Channel.MessageEvent += OnChannelMessage;
        }

        private void OnChannelMessage(string targetId, string @event, string data)
        {
            if (targetId != Internal.TransportId)
            {
                return;
            }

            switch (@event)
            {
                case "tuple":
                    {
                        var notification = JsonConvert.DeserializeObject<PlainTransportTupleNotificationData>(data);

                        Tuple = notification.Tuple;

                        Emit("tuple", Tuple);

                        // Emit observer event.
                        Observer.Emit("tuple", Tuple);

                        break;
                    }

                case "rtcptuple":
                    {
                        var notification = JsonConvert.DeserializeObject<PlainTransportRtcpTupleNotificationData>(data);

                        RtcpTuple = notification.RtcpTuple;

                        Emit("rtcptuple", RtcpTuple);

                        // Emit observer event.
                        Observer.Emit("rtcptuple", RtcpTuple);

                        break;
                    }

                case "sctpstatechange":
                    {
                        var notification = JsonConvert.DeserializeObject<TransportSctpStateChangeNotificationData>(data);

                        SctpState = notification.SctpState;

                        Emit("sctpstatechange", SctpState);

                        // Emit observer event.
                        Observer.Emit("sctpstatechange", SctpState);

                        break;
                    }

                case "trace":
                    {
                        var trace = JsonConvert.DeserializeObject<TransportTraceEventData>(data);

                        Emit("trace", trace);

                        // Emit observer event.
                        Observer.Emit("trace", trace);

                        break;
                    }

                default:
                    {
                        _logger.LogError($"OnChannelMessage() | ignoring unknown event{@event}");
                        break;
                    }
            }
        }

        #endregion
    }
}
