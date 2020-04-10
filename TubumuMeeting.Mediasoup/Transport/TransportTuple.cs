﻿using System;
using System.Collections.Generic;
using System.Text;

namespace TubumuMeeting.Mediasoup
{
    public class TransportTuple
    {
        public string LocalIp { get; set; }

        public int LocalPort { get; set; }

        public string? RemoteIp { get; set; }

        public int? RemotePort { get; set; }

        public TransportProtocol Protocol { get; set; }
    }
}
