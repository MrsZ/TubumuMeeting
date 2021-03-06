﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public class ProducerInternalData
    {
        /// <summary>
        /// Router id.
        /// </summary>
        public string RouterId { get; }

        /// <summary>
        /// Transport id.
        /// </summary>
        public string TransportId { get; }

        /// <summary>
        /// Producer id.
        /// </summary>
        public string ProducerId { get; }

        public ProducerInternalData(string routerId, string transportId, string producerId)
        {
            RouterId = routerId;
            TransportId = transportId;
            ProducerId = producerId;
        }
    }

    public class Producer : EventEmitter
    {
        /// <summary>
        /// Logger
        /// </summary>
        private readonly ILogger<Producer> _logger;

        // TODO: (alby) _closed 的使用及线程安全。
        /// <summary>
        /// Whether the Producer is closed.
        /// </summary>
        private bool _closed;

        private readonly object _closeLock = new object();

        // TODO: (alby) _paused 的使用及线程安全。
        /// <summary>
        /// Paused flag.
        /// </summary>
        private bool _paused;

        /// <summary>
        /// Internal data.
        /// </summary>
        private readonly ProducerInternalData _internal;

        private readonly Timer _checkConsumersTimer;

        private const int CheckConsumersTimeSeconds = 10;

        /// <summary>
        /// Producer id.
        /// </summary>
        public string ProducerId => _internal.ProducerId;

        #region Producer data.

        /// <summary>
        /// Media kind.
        /// </summary>
        public MediaKind Kind { get; }

        /// <summary>
        /// RTP parameters.
        /// </summary>
        public RtpParameters RtpParameters { get; }

        /// <summary>
        /// Producer type.
        /// </summary>
        public ProducerType Type { get; }

        /// <summary>
        /// Consumable RTP parameters.
        /// </summary>
        public RtpParameters ConsumableRtpParameters { get; }

        #endregion Producer data.

        /// <summary>
        /// Channel instance.
        /// </summary>
        private readonly Channel _channel;

        /// <summary>
        /// Channel instance.
        /// </summary>
        private readonly PayloadChannel _payloadChannel;

        /// <summary>
        /// App custom data.
        /// </summary>
        public Dictionary<string, object>? AppData { get; private set; }

        /// <summary>
        /// [扩展]Consumers
        /// </summary>
        private readonly Dictionary<string, Consumer> _consumers = new Dictionary<string, Consumer>();

        /// <summary>
        /// [扩展]Source.
        /// </summary>
        public string? Source { get; set; }

        /// <summary>
        /// Current score.
        /// </summary>
        public ProducerScore[] Score = Array.Empty<ProducerScore>();

        /// <summary>
        /// Observer instance.
        /// </summary>
        public EventEmitter Observer { get; } = new EventEmitter();

        /// <summary>
        /// <para>Events:</para>
        /// <para>@emits transportclose</para></para>
        /// <para>@emits score - (score: ProducerScore[])</para>
        /// <para>@emits videoorientationchange - (videoOrientation: ProducerVideoOrientation)</para>
        /// <para>@emits trace - (trace: ProducerTraceEventData)</para>
        /// <para>@emits @close</para>
        /// <para>Observer events:</para>
        /// <para>@emits close</para>
        /// <para>@emits pause</para>
        /// <para>@emits resume</para>
        /// <para>@emits score - (score: ProducerScore[])</para>
        /// <para>@emits videoorientationchange - (videoOrientation: ProducerVideoOrientation)</para>
        /// <para>@emits trace - (trace: ProducerTraceEventData)</para>
        /// </summary>
        /// <param name="loggerFactory"></param>
        /// <param name="producerInternalData"></param>
        /// <param name="kind"></param>
        /// <param name="rtpParameters"></param>
        /// <param name="type"></param>
        /// <param name="consumableRtpParameters"></param>
        /// <param name="channel"></param>
        /// <param name="appData"></param>
        /// <param name="paused"></param>
        public Producer(ILoggerFactory loggerFactory,
            ProducerInternalData producerInternalData,
            MediaKind kind,
            RtpParameters rtpParameters,
            ProducerType type,
            RtpParameters consumableRtpParameters,
            Channel channel,
            PayloadChannel payloadChannel,
            Dictionary<string, object>? appData,
            bool paused
            )
        {
            _logger = loggerFactory.CreateLogger<Producer>();

            // Internal
            _internal = producerInternalData;

            // Data
            Kind = kind;
            RtpParameters = rtpParameters;
            Type = type;
            ConsumableRtpParameters = consumableRtpParameters;

            _channel = channel;
            _payloadChannel = payloadChannel;
            AppData = appData;
            _paused = paused;

            _checkConsumersTimer = new Timer(CheckConsumers, null, TimeSpan.FromSeconds(CheckConsumersTimeSeconds), TimeSpan.FromMilliseconds(-1));

            HandleWorkerNotifications();
        }

        /// <summary>
        /// Close the Producer.
        /// </summary>
        public void Close()
        {
            if (_closed)
            {
                return;
            }

            lock (_closeLock)
            {
                if (_closed)
                {
                    return;
                }

                _logger.LogDebug($"Close() | Producer:{ProducerId}");

                _closed = true;

                _checkConsumersTimer.Dispose();

                // Remove notification subscriptions.
                _channel.MessageEvent -= OnChannelMessage;

                // Fire and forget
                _channel.RequestAsync(MethodId.PRODUCER_CLOSE, _internal).ContinueWithOnFaultedHandleLog(_logger);

                Emit("close");

                // Emit observer event.
                Observer.Emit("close");
            }
        }

        /// <summary>
        /// Transport was closed.
        /// </summary>
        public void TransportClosed()
        {
            if (_closed)
            {
                return;
            }

            lock (_closeLock)
            {
                if (_closed)
                {
                    return;
                }

                _logger.LogDebug($"TransportClosed() | Producer:{ProducerId}");

                _closed = true;

                _checkConsumersTimer.Dispose();

                // Remove notification subscriptions.
                _channel.MessageEvent -= OnChannelMessage;

                Emit("transportclose");

                // Emit observer event.
                Observer.Emit("close");
            }
        }

        /// <summary>
        /// Dump DataProducer.
        /// </summary>
        public Task<string?> DumpAsync()
        {
            _logger.LogDebug($"DumpAsync() | Producer:{ProducerId}");

            return _channel.RequestAsync(MethodId.PRODUCER_DUMP, _internal);
        }

        /// <summary>
        /// Get DataProducer stats.
        /// </summary>
        public Task<string?> GetStatsAsync()
        {
            _logger.LogDebug($"GetStatsAsync() | Producer:{ProducerId}");

            return _channel.RequestAsync(MethodId.PRODUCER_GET_STATS, _internal);
        }

        /// <summary>
        /// Pause the Producer.
        /// </summary>
        public async Task PauseAsync()
        {
            _logger.LogDebug($"PauseAsync() | Producer:{ProducerId}");

            var wasPaused = _paused;

            await _channel.RequestAsync(MethodId.PRODUCER_PAUSE, _internal);

            _paused = true;

            // Emit observer event.
            if (!wasPaused)
            {
                Observer.Emit("pause");
            }
        }

        /// <summary>
        /// Resume the Producer.
        /// </summary>
        public async Task ResumeAsync()
        {
            _logger.LogDebug($"ResumeAsync() | Producer:{ProducerId}");

            var wasPaused = _paused;

            await _channel.RequestAsync(MethodId.PRODUCER_RESUME, _internal);

            _paused = false;

            // Emit observer event.
            if (wasPaused)
            {
                Observer.Emit("resume");
            }
        }

        /// <summary>
        /// Enable 'trace' event.
        /// </summary>
        public Task EnableTraceEventAsync(TraceEventType[] types)
        {
            _logger.LogDebug($"EnableTraceEventAsync() | Producer:{ProducerId}");

            var reqData = new
            {
                Types = types ?? Array.Empty<TraceEventType>()
            };

            return _channel.RequestAsync(MethodId.PRODUCER_ENABLE_TRACE_EVENT, _internal, reqData);
        }

        /// <summary>
        /// Send RTP packet (just valid for Producers created on a DirectTransport).
        /// </summary>
        /// <param name="rtpPacket"></param>
        public void Send(byte[] rtpPacket)
        {
            _payloadChannel.Notify("producer.send", _internal, null, rtpPacket);
        }

        public void AddConsumer(Consumer consumer)
        {
            CheckClosed();
            lock (_closeLock)
            {
                CheckClosed();

                _consumers[consumer.ConsumerId] = consumer;
            }
        }

        public void RemoveConsumer(string consumerId)
        {
            // 关闭后也允许移除
            lock (_closeLock)
            {
                _logger.LogDebug($"RemoveConsumer() | Producer:{ProducerId} ConsumerId:{consumerId}");
                _consumers.Remove(consumerId);
            }
        }

        public void StopConsumers()
        {
            // 关闭后也允许停止
            lock (_closeLock)
            {
                _logger.LogDebug($"StopConsumers() | Producer:{ProducerId}");
                foreach (var consumer in _consumers.Values)
                {
                    consumer.CloseAsync().ContinueWithOnFaultedHandleLog(_logger);
                }
            }
        }

        #region Event Handlers

        private void HandleWorkerNotifications()
        {
            _channel.MessageEvent += OnChannelMessage;
        }

        private void OnChannelMessage(string targetId, string @event, string data)
        {
            if (targetId != ProducerId)
            {
                return;
            }

            switch (@event)
            {
                case "score":
                    {
                        var score = JsonConvert.DeserializeObject<ProducerScore[]>(data);
                        Score = score;

                        Emit("score", score);

                        // Emit observer event.
                        Observer.Emit("score", score);

                        break;
                    }
                case "videoorientationchange":
                    {
                        var videoOrientation = JsonConvert.DeserializeObject<ProducerVideoOrientation>(data);

                        Emit("videoorientationchange", videoOrientation);

                        // Emit observer event.
                        Observer.Emit("videoorientationchange", videoOrientation);

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
                        _logger.LogError($"OnChannelMessage() | Ignoring unknown event{@event}");
                        break;
                    }
            }
        }

        #endregion Event Handlers

        #region Private Methods

        private void CheckConsumers(object state)
        {
            if (_closed)
            {
                _checkConsumersTimer.Dispose();
                return;
            }

            lock (_closeLock)
            {
                if (_closed)
                {
                    _checkConsumersTimer.Dispose();
                    return;
                }

                _logger.LogDebug($"CheckConsumer() | Producer: {_internal.ProducerId} Consumers: {_consumers.Count}");

                if (_consumers.Count == 0)
                {
                    // 防止死锁
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        Close();
                    });
                    _checkConsumersTimer.Dispose();
                }
                else
                {
                    _checkConsumersTimer.Change(TimeSpan.FromSeconds(CheckConsumersTimeSeconds), TimeSpan.FromMilliseconds(-1));
                }
            }
        }

        private void CheckClosed()
        {
            if (_closed)
            {
                throw new Exception($"CheckClosed() | Producer:{ProducerId} was closed");
            }
        }

        #endregion Private Methods
    }
}
