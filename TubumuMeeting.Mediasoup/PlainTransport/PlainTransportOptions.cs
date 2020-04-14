﻿namespace TubumuMeeting.Mediasoup
{
	public class PlainTransportOptions
	{
		/// <summary>
		 /// Listening IP address.
		 /// </summary>
		public TransportListenIp ListenIp { get; set; }

		/// <summary>
		 /// Use RTCP-mux (RTP and RTCP in the same port). Default true.
		 /// </summary>
		public bool? RtcpMux { get; set; } = true;

		/// <summary>
		 /// Whether remote IP:port should be auto-detected based on first RTP/RTCP
		 /// packet received. If enabled, connect() method must not be called unless
		 /// SRTP is enabled. If so, it must be called with just remote SRTP parameters.
		 /// Default false.
		 /// </summary>
		public bool? Comedia { get; set; } = false;

	/// <summary>
	 /// Create a SCTP association. Default false.
	 /// </summary>
		public bool? EnableSctp { get; set; } = false;

		/// <summary>
		 /// SCTP streams number.
		 /// </summary>
		public NumSctpStreams? NumSctpStreams { get; set; }

		/// <summary>
		 /// Maximum size of data that can be passed to DataProducer's send() method.
		 /// Default 262144.
		 /// </summary>
		public int? MaxSctpMessageSize { get; set; } = 262144;

		/// <summary>
		 /// Enable SRTP. For this to work, connect() must be called
		 /// with remote SRTP parameters. Default false.
		 /// </summary>
		public bool? EnableSrtp { get; set; } = false;

		/// <summary>
		 /// The SRTP crypto suite to be used if enableSrtp is set. Default
		 /// 'AES_CM_128_HMAC_SHA1_80'.
		 /// </summary>
		public SrtpCryptoSuite? SrtpCryptoSuite { get; set; } = TubumuMeeting.Mediasoup.SrtpCryptoSuite.AES_CM_128_HMAC_SHA1_80;

		/// <summary>
		 /// Custom application data.
		 /// </summary>
		public object? AppData { get; set; }
	}
}