﻿using System;

namespace TubumuMeeting.Libuv
{
    public interface IHandle
    {
        void Ref();
        void Unref();
        bool HasRef { get; }
        bool IsClosed { get; }
        void Close(Action callback);
    }
}
