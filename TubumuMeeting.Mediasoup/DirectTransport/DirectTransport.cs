﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace TubumuMeeting.Mediasoup
{
    public class DirectTransport : Transport
    {
        /// <summary>
        /// Logger.
        /// </summary>
        private readonly ILogger<DirectTransport> _logger;

        #region DirectTransport data.


        #endregion

        /// <summary>
        /// <para>Events:</para>
        /// <para>@emits rtcp - (packet: Buffer)</para>
        /// <para>@emits trace - (trace: TransportTraceEventData)</para>
        /// <para>Observer events:</para>
        /// <para>@emits close</para>
        /// <para>@emits newdataproducer - (dataProducer: DataProducer)</para>
        /// <para>@emits newdataconsumer - (dataProducer: DataProducer)</para>
        /// <para>@emits trace - (trace: TransportTraceEventData)</para>
        /// </summary>
        /// <param name="loggerFactory"></param>
        /// <param name="transportInternalData"></param>
        /// <param name="sctpParameters"></param>
        /// <param name="sctpState"></param>
        /// <param name="channel"></param>
        /// <param name="appData"></param>
        /// <param name="getRouterRtpCapabilities"></param>
        /// <param name="getProducerById"></param>
        /// <param name="getDataProducerById"></param>
        public DirectTransport(ILoggerFactory loggerFactory,
            TransportInternalData transportInternalData,
            SctpParameters? sctpParameters,
            SctpState? sctpState,
            Channel channel,
            PayloadChannel payloadChannel,
            Dictionary<string, object>? appData,
            Func<RtpCapabilities> getRouterRtpCapabilities,
            Func<string, Producer?> getProducerById,
            Func<string, DataProducer?> getDataProducerById
            ) : base(loggerFactory, transportInternalData, sctpParameters, sctpState, channel, payloadChannel, appData, getRouterRtpCapabilities, getProducerById, getDataProducerById)
        {
            _logger = loggerFactory.CreateLogger<DirectTransport>();

            // Data

            HandleWorkerNotifications();
        }

        /// <summary>
        /// Close the PipeTransport.
        /// </summary>
        public override async Task Close()
        {
            if (Closed)
            {
                return;
            }

            await base.Close();
        }

        /// <summary>
        /// Router was closed.
        /// </summary>
        public override async Task RouterClosed()
        {
            if (Closed)
            {
                return;
            }

            await base.RouterClosed();
        }

        /// <summary>
        /// NO-OP method in DirectTransport.
        /// </summary>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public override Task ConnectAsync(object parameters)
        {
            _logger.LogDebug("ConnectAsync()");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Set maximum incoming bitrate for receiving media.
        /// </summary>
        /// <param name="bitrate"></param>
        /// <returns></returns>
        public override Task<string?> SetMaxIncomingBitrateAsync(int bitrate)
        {
            _logger.LogDebug($"SetMaxIncomingBitrateAsync() [bitrate:{bitrate}]");
            throw new NotImplementedException("SetMaxIncomingBitrateAsync() not implemented in DirectTransport");
        }

        /// <summary>
        /// Create a Producer.
        /// </summary>
        public override Task<Producer> ProduceAsync(ProducerOptions producerOptions)
        {
            _logger.LogDebug("ProduceAsync()");
            throw new NotImplementedException("ProduceAsync() not implemented in DirectTransport");
        }

        /// <summary>
        /// Create a Consumer.
        /// </summary>
        /// <param name="consumerOptions"></param>
        /// <returns></returns>
        public override Task<Consumer> ConsumeAsync(ConsumerOptions consumerOptions)
        {
            _logger.LogDebug("ConsumeAsync()");
            throw new NotImplementedException("ConsumeAsync() not implemented in DirectTransport");
        }

        public void SendRtcp(byte[] rtcpPacket)
        {
            PayloadChannel.Notify("transport.sendRtcp", Internal, null, rtcpPacket);
        }

        #region Event Handlers

        private void HandleWorkerNotifications()
        {
            Channel.MessageEvent += OnChannelMessage;
            PayloadChannel.MessageEvent += OnPayloadChannelMessage;
        }

        private void OnChannelMessage(string targetId, string @event, string data)
        {
            if (targetId != Internal.TransportId)
            {
                return;
            }

            switch (@event)
            {
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

        private void OnPayloadChannelMessage(string targetId, string @event, NotifyData notifyData, ArraySegment<byte> payload)
        {
            if (targetId != Internal.TransportId)
            {
                return;
            }

            switch (@event)
            {
                case "rtcp":
                    {
                        if (Closed)
                            break;

                        Emit("rtcp", payload);

                        break;
                    }

                default:
                    {
                        _logger.LogError($"ignoring unknown event \"{@event}\"");
                        break;
                    }
            }
        }

        #endregion
    }
}
