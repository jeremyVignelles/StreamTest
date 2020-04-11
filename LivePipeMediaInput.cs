using LibVLCSharp.Shared;
using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.InteropServices;

namespace StreamTest
{
    /// <summary>
    /// The <see cref="MediaInput"/> implementation that lets you
    /// write "live" data into its writer using System.IO.Pipelines
    /// </summary>
    public class LivePipeMediaInput : MediaInput
    {
        /// <summary>
        /// The private pipe implementation
        /// </summary>
        private readonly Pipe _pipe = new Pipe();

        /// <summary>
        /// The <see cref="PipeWriter"/> in which you can write the stream data as they come.
        /// </summary>
        public PipeWriter Writer => this._pipe.Writer;

        /// <summary>
        /// LibVLC calls this method when it wants to open the media
        /// </summary>
        /// <param name="size">This value will be filled with ulong.MaxValue because it is a live stream</param>
        /// <returns><c>true</c> if the stream opened successfully</returns>
        public override bool Open(out ulong size)
        {
            size = ulong.MaxValue;
            return true;
        }

        /// <summary>
        /// LibVLC calls this method when it wants to read the media
        /// </summary>
        /// <param name="buf">The buffer where read data must be written</param>
        /// <param name="len">The buffer length</param>
        /// <returns>The number of bytes actually read, -1 on error</returns>
        public override int Read(IntPtr buf, uint len)
        {
            var readResult = this._pipe.Reader.ReadAsync().AsTask().GetAwaiter().GetResult();

            if (readResult.IsCanceled)
            {
                return -1;
            }

            if (readResult.IsCompleted)
            {
                return 0;
            }

            var writeLength = (int)Math.Min(Math.Min(len, readResult.Buffer.Length), int.MaxValue);
            var sliceToWrite = readResult.Buffer.Slice(0, writeLength);

            var tempBuffer = new byte[writeLength]; // TODO : avoid copy

            sliceToWrite.CopyTo(tempBuffer);
            Marshal.Copy(tempBuffer, 0, buf, writeLength);

            this._pipe.Reader.AdvanceTo(sliceToWrite.End);

            return writeLength;
        }

        /// <summary>
        /// LibVLC calls this method when it wants to seek to a specific position in the media
        /// </summary>
        /// <param name="offset">The offset, in bytes, since the beginning of the stream</param>
        /// <returns>false since this stream is not seekable</returns>
        public override bool Seek(ulong offset)
        {
            return false;
        }

        /// <summary>
        /// LibVLC calls this method when it wants to close the media.
        /// </summary>
        public override void Close()
        {
            this._pipe.Reader.Complete();
        }
    }
}