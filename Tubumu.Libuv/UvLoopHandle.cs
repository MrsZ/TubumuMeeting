using System;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Tubumu.Libuv
{
    public class UvLoopHandle : UvMemory
    {
        public UvLoopHandle(ILogger logger) : base(logger)
        {
        }

        public void Init(LibuvFunctions uv)
        {
            CreateMemory(
                uv,
                Thread.CurrentThread.ManagedThreadId,
                uv.loop_size());

            _uv.loop_init(this);
        }

        public void Run(int mode = 0)
        {
            _uv.run(this, mode);
        }

        public void Stop()
        {
            _uv.stop(this);
        }

        public long Now()
        {
            return _uv.now(this);
        }

        unsafe protected override bool ReleaseHandle()
        {
            var memory = handle;
            if (memory != IntPtr.Zero)
            {
                // loop_close clears the gcHandlePtr
                var gcHandlePtr = *(IntPtr*)memory;

                _uv.loop_close(this);
                handle = IntPtr.Zero;

                DestroyMemory(memory, gcHandlePtr);
            }

            return true;
        }
    }
}
