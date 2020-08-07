﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Nito.AsyncEx;
using Tubumu.Core.Extensions;
using TubumuMeeting.Mediasoup;
using TubumuMeeting.Mediasoup.Extensions;

namespace TubumuMeeting.Meeting.Server
{
    public partial class Peer : IEquatable<Peer>
    {
        public string PeerId { get; }

        public string DisplayName { get; }

        public bool Equals(Peer other)
        {
            return PeerId == other.PeerId;
        }

        public override int GetHashCode()
        {
            return PeerId.GetHashCode();
        }
    }

    public partial class Peer
    {
        /// <summary>
        /// Logger factory for create logger.
        /// </summary>
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// Logger
        /// </summary>
        private readonly ILogger<Peer> _logger;

        private readonly AsyncLock _locker = new AsyncLock();

        private readonly WebRtcTransportSettings _webRtcTransportSettings;

        public bool Closed { get; private set; }

        public RtpCapabilities? RtpCapabilities { get; set; }

        public SctpCapabilities? SctpCapabilities { get; set; }

        private Router _router { get; set; }

        public Dictionary<string, Room> Rooms { get; } = new Dictionary<string, Room>();  // TODO: (alby)改为私有

        public Dictionary<string, Transport> Transports { get; } = new Dictionary<string, Transport>();  // TODO: (alby)改为私有

        public Dictionary<string, Producer> Producers { get; } = new Dictionary<string, Producer>(); // TODO: (alby)改为私有

        public Dictionary<string, Consumer> Consumers { get; } = new Dictionary<string, Consumer>(); // TODO: (alby)改为私有

        public Dictionary<string, DataProducer> DataProducers { get; } = new Dictionary<string, DataProducer>();  // TODO: (alby)改为私有

        public Dictionary<string, DataConsumer> DataConsumers { get; } = new Dictionary<string, DataConsumer>();  // TODO: (alby)改为私有

        public List<ConsumePadding> ConsumePaddings = new List<ConsumePadding>();  // TODO: (alby)改为私有

        public string[] Sources { get; private set; }

        public Dictionary<string, object> AppData { get; private set; }

        public Peer(ILoggerFactory loggerFactory, WebRtcTransportSettings webRtcTransportSettings, Router router, string peerId, string displayName, string[]? sources, Dictionary<string, object>? appData)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<Peer>();
            _webRtcTransportSettings = webRtcTransportSettings;
            _router = router;
            PeerId = peerId;
            DisplayName = displayName.IsNullOrWhiteSpace() ? "Guest" : displayName;
            Sources = sources ?? new string[0];
            AppData = appData ?? new Dictionary<string, object>();
            Closed = false;
        }

        /// <summary>
        /// Close
        /// </summary>
        public void Close()
        {
            if(Closed)
            {
                return;
            }

            using (_locker.Lock())
            {
                if (Closed)
                {
                    return;
                }

                Closed = true;

                RtpCapabilities = null;
                SctpCapabilities = null;

                // Iterate and close all mediasoup Transport associated to this Peer, so all
                // its Producers and Consumers will also be closed.
                Transports.Values.ForEach(m => m.Close());
                Transports.Clear();
            }
        }

        /// <summary>
        /// 创建 WebRtcTransport
        /// </summary>
        /// <param name="createWebRtcTransportRequest"></param>
        /// <returns></returns>
        public async Task<WebRtcTransport> CreateWebRtcTransportAsync(CreateWebRtcTransportRequest createWebRtcTransportRequest)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (!(createWebRtcTransportRequest.Consuming ^ createWebRtcTransportRequest.Producing))
                {
                    throw new Exception("CreateWebRtcTransportAsync() | Consumer or Producing");
                }

                if (createWebRtcTransportRequest.Consuming && HasConsumingTransport())
                {
                    throw new Exception("CreateWebRtcTransportAsync() | Consuming transport exists");
                }

                if (createWebRtcTransportRequest.Producing && HasProducingTransport())
                {
                    throw new Exception("CreateWebRtcTransportAsync() | Producing transport exists");
                }

                var webRtcTransportOptions = new WebRtcTransportOptions
                {
                    ListenIps = _webRtcTransportSettings.ListenIps,
                    InitialAvailableOutgoingBitrate = _webRtcTransportSettings.InitialAvailableOutgoingBitrate,
                    MaxSctpMessageSize = _webRtcTransportSettings.MaxSctpMessageSize,
                    EnableSctp = createWebRtcTransportRequest.SctpCapabilities != null,
                    NumSctpStreams = createWebRtcTransportRequest.SctpCapabilities?.NumStreams,
                    AppData = new Dictionary<string, object>
                    {
                        { "Consuming", createWebRtcTransportRequest.Consuming },
                        { "Producing", createWebRtcTransportRequest.Producing },
                    },
                };

                if (createWebRtcTransportRequest.ForceTcp)
                {
                    webRtcTransportOptions.EnableUdp = false;
                    webRtcTransportOptions.EnableTcp = true;
                }

                var transport = await _router.CreateWebRtcTransportAsync(webRtcTransportOptions);

                if (transport == null)
                {
                    throw new Exception("CreateWebRtcTransportAsync() | Router.CreateWebRtcTransport faild");
                }
                // Store the WebRtcTransport into the Peer data Object.
                Transports[transport.TransportId] = transport;

                // If set, apply max incoming bitrate limit.
                if (_webRtcTransportSettings.MaximumIncomingBitrate.HasValue && _webRtcTransportSettings.MaximumIncomingBitrate.Value > 0)
                {
                    // Fire and forget
                    transport.SetMaxIncomingBitrateAsync(_webRtcTransportSettings.MaximumIncomingBitrate.Value).ContinueWithOnFaultedHandleLog(_logger);
                }

                return transport;
            }
        }

        /// <summary>
        /// 连接 WebRtcTransport
        /// </summary>
        /// <param name="connectWebRtcTransportRequest"></param>
        /// <returns></returns>
        public async Task<bool> ConnectWebRtcTransportAsync(ConnectWebRtcTransportRequest connectWebRtcTransportRequest)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (!Transports.TryGetValue(connectWebRtcTransportRequest.TransportId, out var transport))
                {
                    throw new Exception($"ConnectWebRtcTransportAsync() | Transport:{connectWebRtcTransportRequest.TransportId} is not exists");
                }

                await transport.ConnectAsync(connectWebRtcTransportRequest.DtlsParameters);
                return true;
            }
        }

        /// <summary>
        /// 生产
        /// </summary>
        /// <param name="produceRequest"></param>
        /// <returns></returns>
        public async Task<Producer> ProduceAsync(ProduceRequest produceRequest)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (produceRequest.AppData == null || !produceRequest.AppData.TryGetValue("source", out var sourceObj))
                {
                    throw new Exception($"ProduceAsync() | Peer:{PeerId} AppData[\"source\"] is null.");
                }
                var source = sourceObj.ToString();

                if (produceRequest.AppData == null || !produceRequest.AppData.TryGetValue("roomId", out var roomIdObj))
                {
                    throw new Exception($"ProduceAsync() | Peer:{ PeerId} AppData[\"roomId\"] is null.");
                }
                var roomId = roomIdObj.ToString();

                if (!Rooms.TryGetValue(roomId, out var room))
                {
                    throw new Exception($"ProduceAsync() | Peer:{ PeerId} is not in Room:{roomId}.");
                }

                var transport = GetProducingTransport();
                if (transport == null)
                {
                    throw new Exception($"ProduceAsync() | Transport:Producing is not exists.");
                }

                if (Sources == null || !Sources.Contains(source))
                {
                    throw new Exception($"ProduceAsync() | Source:\"{ source }\" cannot be produce.");
                }

                // TODO: (alby)线程安全：避免重复 Produce 相同的 Sources
                var producer = Producers.Values.FirstOrDefault(m => m.Source == source);
                if (producer != null)
                {
                    throw new Exception($"ProduceAsync() | Source:\"{ source }\" is exists.");
                }

                // Add peerId into appData to later get the associated Peer during
                // the 'loudest' event of the audioLevelObserver.
                produceRequest.AppData["peerId"] = PeerId;

                producer = await transport.ProduceAsync(new ProducerOptions
                {
                    Kind = produceRequest.Kind,
                    RtpParameters = produceRequest.RtpParameters,
                    AppData = produceRequest.AppData,
                });

                // Store producer source
                producer.Source = source;

                // Store the Producer into the Peer data Object.
                Producers[producer.ProducerId] = producer;

                return producer;
            }
        }

        public async Task<Consumer> ConsumeAsync(Producer producer, string roomId)
        {
            CheckClosed();
            using (_locker.Lock())
            {
                CheckClosed();

                if (RtpCapabilities == null || !_router.CanConsume(producer.ProducerId, RtpCapabilities))
                {
                    _logger.LogWarning("ConsumeAsync() | Can not consume.");
                    throw new Exception($"Consume 失败: Peer:{PeerId} Can not consume.");
                }

                var transport = GetConsumingTransport();

                // This should not happen.
                if (transport == null)
                {
                    _logger.LogWarning("ConsumeAsync() | Transport for consuming not found");
                    throw new Exception($"Consume 失败: Peer:{PeerId} Transport for consuming not found.");
                }

                // Create the Consumer in paused mode.
                var consumer = await transport.ConsumeAsync(new ConsumerOptions
                {
                    ProducerId = producer.ProducerId,
                    RtpCapabilities = RtpCapabilities,
                    Paused = producer.Kind == MediaKind.Video
                });

                // Store RoomId
                consumer.RoomId = roomId;

                // Store producer source
                consumer.Source = producer.Source;

                // Store the Consumer into the consumerPeer data Object.
                Consumers[consumer.ConsumerId] = consumer;

                return consumer;
            }
        }

        public async Task<bool> CloseProducerAsync(string producerId)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (Producers.TryGetValue(producerId, out var producer))
                {
                    throw new Exception($"CloseProducerAsync() | Peer:{PeerId} has no Producer:{producerId}.");
                }

                producer.Close();
                Producers.Remove(producerId);
                return true;
            }
        }

        public async Task<bool> PauseProducerAsync(string producerId)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (Producers.TryGetValue(producerId, out var producer))
                {
                    throw new Exception($"PauseProducerAsync() | Peer:{PeerId} has no Producer:{producerId}.");
                }

                await producer.PauseAsync();
                return true;
            }
        }

        public async Task<bool> ResumeProducerAsync(string producerId)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                if (Producers.TryGetValue(producerId, out var producer))
                {
                    throw new Exception($"ResumeProducerAsync() | Peer:{PeerId} has no Producer:{producerId}.");
                }

                await producer.ResumeAsync();
                return true;
            }
        }

        public async Task<bool> CloseConsumerAsync(string consumerId)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (Consumers.TryGetValue(consumerId, out var consumer))
                {
                    throw new Exception($"CloseConsumerAsync() | Peer:{PeerId} has no Cmonsumer:{consumerId}.");
                }

                consumer.Close();
                Consumers.Remove(consumerId);
                return true;
            }
        }

        public async Task<bool> PauseConsumerAsync(string consumerId)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (Consumers.TryGetValue(consumerId, out var consumer))
                {
                    throw new Exception($"PauseConsumerAsync() | Peer:{PeerId} has no Consumer:{consumerId}.");
                }

                await consumer.PauseAsync();
                return true;
            }
        }

        public async Task<Consumer> ResumeConsumerAsync(string consumerId)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (!Consumers.TryGetValue(consumerId, out var consumer))
                {
                    throw new Exception($"ResumeConsumerAsync() | Peer:{PeerId} has no Consumer:{consumerId}.");
                }

                await consumer.ResumeAsync();
                return consumer;
            }
        }

        public async Task<bool> SetConsumerPreferedLayersAsync(SetConsumerPreferedLayersRequest setConsumerPreferedLayersRequest)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (Consumers.TryGetValue(setConsumerPreferedLayersRequest.ConsumerId, out var consumer))
                {
                    throw new Exception($"SetConsumerPreferedLayersAsync() | Peer:{PeerId} has no Consumer:{setConsumerPreferedLayersRequest.ConsumerId}.");
                }

                await consumer.SetPreferredLayersAsync(setConsumerPreferedLayersRequest);
                return true;
            }
        }

        public async Task<bool> SetConsumerPriorityAsync(SetConsumerPriorityRequest setConsumerPriorityRequest)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (Consumers.TryGetValue(setConsumerPriorityRequest.ConsumerId, out var consumer))
                {
                    throw new Exception($"SetConsumerPriorityAsync() | Peer:{PeerId} has no Consumer:{setConsumerPriorityRequest.ConsumerId}.");
                }

                await consumer.SetPriorityAsync(setConsumerPriorityRequest.Priority);
                return true;
            }
        }

        public async Task<bool> RequestConsumerKeyFrameAsync(string consumerId)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (Consumers.TryGetValue(consumerId, out var consumer))
                {
                    throw new Exception($"RequestConsumerKeyFrameAsync() | Peer:{PeerId} has no Producer:{consumerId}.");
                }

                await consumer.RequestKeyFrameAsync();
                return true;
            }
        }

        public async Task<TransportStat> GetTransportStatsAsync(string transportId)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (Transports.TryGetValue(transportId, out var transport))
                {
                    throw new Exception($"GetTransportStatsAsync() | Peer:{PeerId} has no Transport:{transportId}.");
                }

                var status = await transport.GetStatsAsync();
                // TODO: (alby)考虑不进行反序列化
                // TODO: (alby)实际上有 WebTransportStat、PlainTransportStat、PipeTransportStat 和 DirectTransportStat。这里反序列化后会丢失数据。
                var data = JsonConvert.DeserializeObject<TransportStat>(status!);
                return data;
            }
        }

        public async Task<ProducerStat> GetProducerStatsAsync(string producerId)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (Producers.TryGetValue(producerId, out var producer))
                {
                    throw new Exception($"GetProducerStatsAsync() | Peer:{PeerId} has no Producer:{producerId}.");
                }

                var status = await producer.GetStatsAsync();
                // TODO: (alby)考虑不进行反序列化
                var data = JsonConvert.DeserializeObject<ProducerStat>(status!);
                return data;
            }
        }

        public async Task<ConsumerStat> GetConsumerStatsAsync(string consumerId)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (Consumers.TryGetValue(consumerId, out var consumer))
                {
                    throw new Exception($"GetConsumerStatsAsync() | Peer:{PeerId} has no Consumers:{consumerId}.");
                }

                var status = await consumer.GetStatsAsync();
                // TODO: (alby)考虑不进行反序列化
                var data = JsonConvert.DeserializeObject<ConsumerStat>(status!);
                return data;
            }
        }

        public async Task<IceParameters?> RestartIceAsync(string transportId)
        {
            CheckClosed();
            using (await _locker.LockAsync())
            {
                CheckClosed();

                if (Transports.TryGetValue(transportId, out var transport))
                {
                    throw new Exception($"RestartIceAsync() | Peer:{PeerId} has no Transport:{transportId}.");
                }

                if (!(transport is WebRtcTransport webRtcTransport))
                {
                    throw new Exception($"RestartIceAsync() | Peer:{PeerId} has no Transport:{transportId}.");
                }

                var iceParameters = await webRtcTransport.RestartIceAsync();
                return iceParameters;
            }
        }

        /// <summary>
        /// 关闭其他房间无人消费的 Producer
        /// </summary>
        /// <param name="excludeRoomId"></param>
        public void CloseProducersNoConsumers(string excludeRoomId)
        {
            using (_locker.Lock())
            {
                var producersToClose = new HashSet<Producer>();
                var consumers = from ri in Rooms.Values             // Peer 所在的所有房间
                                from p in ri.Peers.Values           // 的包括本 Peer 在内的所有 Peer
                                from pc in p.Consumers.Values       // 的 Consumer
                                where ri.RoomId != excludeRoomId    // 排除房间
                                select pc;

                foreach (var producer in Producers.Values)
                {
                    // 如果其他 Room 中没有消费 producer，则关闭。
                    if (!consumers.Any(m => m.Internal.ProducerId == producer.ProducerId && m.RoomId == excludeRoomId))
                    {
                        producersToClose.Add(producer);
                    }
                }

                // Producer 关闭后会触发相应的 Consumer `producerclose` 事件，从而拥有 Consumer 的 Peer 能够关闭该 Consumer 并通知客户端。
                foreach (var producerToClose in producersToClose)
                {
                    producerToClose.Close();
                    Producers.Remove(producerToClose.ProducerId);
                }
            }
        }

        /// <summary>
        /// 关闭除指定 Room 里的指定 Peer 外无人消费的 Producer
        /// </summary>
        /// <param name="excludeRoomId"></param>
        /// <param name="excludePeerId"></param>
        public void CloseProducersNoConsumers(string excludeRoomId, string excludePeerId)
        {
            using (_locker.Lock())
            {
                // 关闭无人消费的本 Peer 的 Producer
                var producersToClose = new HashSet<Producer>();
                var otherPeers = from ri in Rooms.Values        // Peer 所在的所有房间
                                 from p in ri.Peers.Values      // 的包括本 Peer 在内的所有 Peer
                                 where !(ri.RoomId == excludeRoomId && p.PeerId == excludePeerId)   // 除指定 Room 里的指定 Peer
                                 select p;

                foreach (var otherPeer in otherPeers)
                {
                    foreach (var producer in Producers.Values)
                    {
                        // 如果没有消费 producer，则关闭。
                        if (!otherPeer.Consumers.Values.Any(m => m.Internal.ProducerId == producer.ProducerId && !(m.RoomId == excludeRoomId && otherPeer.PeerId == excludePeerId)))
                        {
                            producersToClose.Add(producer);
                        }
                    }
                }

                // Producer 关闭后会触发相应的 Consumer `producerclose` 事件，从而拥有 Consumer 的 Peer 能够关闭该 Consumer 并通知客户端。
                foreach (var producerToClose in producersToClose)
                {
                    producerToClose.Close();
                    Producers.Remove(producerToClose.ProducerId);
                }
            }
        }

        public void RemoveConsumer(string consumerId)
        {
            CheckClosed();
            using (_locker.Lock())
            {
                CheckClosed();

                Consumers.Remove(consumerId);
            }
        }

        #region Private Methods

        private Transport GetProducingTransport()
        {
            return Transports.Values.Where(m => m.AppData != null && m.AppData.TryGetValue("Producing", out var value) && (bool)value).FirstOrDefault();
        }

        private Transport GetConsumingTransport()
        {
            return Transports.Values.Where(m => m.AppData != null && m.AppData.TryGetValue("Consuming", out var value) && (bool)value).FirstOrDefault();
        }

        private bool HasProducingTransport()
        {
            return Transports.Values.Any(m => m.AppData != null && m.AppData.TryGetValue("Producing", out var value) && (bool)value);
        }

        private bool HasConsumingTransport()
        {
            return Transports.Values.Any(m => m.AppData != null && m.AppData.TryGetValue("Consuming", out var value) && (bool)value);
        }

        private void CheckClosed()
        {
            if (Closed)
            {
                throw new Exception("Peer was closed");
            }
        }

        #endregion
    }
}
