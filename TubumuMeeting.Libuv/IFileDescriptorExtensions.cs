using System;

namespace TubumuMeeting.Libuv
{
    public static class IFileDescriptorExtensions
    {
        public static void Open(this IFileDescriptor fileDescriptor, int fd)
        {
            fileDescriptor.Open((IntPtr)fd);
        }
    }
}
