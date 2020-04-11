using LibVLCSharp.Shared;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StreamTest
{
    public partial class MainForm : Form
    {
        private static string[] libVLCParams = { "--file-caching=2000",
                                                 "--no-snapshot-preview",
                                                 "--verbose=2",
                                                 "--no-osd" };
        private static LibVLC libVLC;
        private MyMediaInput mMediaInput = new MyMediaInput();
        private Media mMedia;
        private Thread mp4StreamerThread;


        private static void log(String message)
        {
            Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.ffffff") + " - MainForm: " + message);
        }

        public MainForm()
        {
            InitializeComponent();


        }

        private void btnInitLibrary_Click(object sender, EventArgs e)
        {
            log("btnLoadMedia_Click");
            Core.Initialize();
            libVLC = new LibVLC(libVLCParams);
            videoView.MediaPlayer = new MediaPlayer(libVLC);
        }

        private void btnPlayMedia_Click(object sender, EventArgs e)
        {
            log("btnPlayMedia_Click");

            mMediaInput = new MyMediaInput();

            MP4Streamer mp4Streamer = new MP4Streamer(mMediaInput);
            mp4StreamerThread = new Thread(mp4Streamer.DoWork);
            mp4StreamerThread.Start();

            mMedia = new Media(libVLC, mMediaInput);
            MediaConfiguration mediaConfiguration = new MediaConfiguration();
            mediaConfiguration.EnableHardwareDecoding = true;
            mediaConfiguration.NetworkCaching = 150;
            mMedia.AddOption(mediaConfiguration);
            /*mMedia.AddOption(":clock-jitter=0");
            mMedia.AddOption(":clock-synchro=0");*/
            videoView.MediaPlayer.Play(mMedia);
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            videoView.MediaPlayer.Stop();
            mp4StreamerThread.Abort();
            mMediaInput.Dispose();
        }



        public class MP4Streamer
        {
            private static int bufferSize = 4096;
            private byte[] buffer = new byte[bufferSize];
            private int box = 0;
            private MyMediaInput mediaInput;

            private static void log(String message)
            {
                Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.ffffff") + " - MP4Streamer: " + message);
            }

            public MP4Streamer(MyMediaInput m)
            {
                mediaInput = m;
            }

            public void DoWork()
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                Stream mp4file = assembly.GetManifestResourceStream("StreamTest.teststream.mp4");

                int numBytesToRead = (int)mp4file.Length;
                int bytesToRead = 0;
                log("Opening file. Size=" + numBytesToRead);

                try
                {
                    while (0 < numBytesToRead)
                    {
                        if (0 == box) bytesToRead = 28;
                        else if (1 == box) bytesToRead = 726;
                        else bytesToRead = 4096;

                        int read = mp4file.Read(buffer, 0, bytesToRead);

                        // Break when the end of the file is reached.
                        if (0 == read)
                            break;

                        mediaInput.Write(buffer, 0, read);

                        box++;
                        numBytesToRead -= read;

                        Thread.Sleep(400);
                    }
                }
                catch (ThreadAbortException) {
                };
            }
        }

        public class MyMediaInput : MediaInput
        {
            private readonly object mWriteLock = new object();
            private readonly object mReadLock = new object();
            private readonly ConcurrentQueue<byte[]> mPendingSegments = new ConcurrentQueue<byte[]>();
            private byte[] mExtraPendingSegment = null;
            private readonly SemaphoreSlim mSegmentsAvailable = new SemaphoreSlim(0);

            private static void log(String message)
            {
                Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.ffffff") + " - MyMediaInput: " + message);
            }

            public override bool Open(out ulong size)
            {
                log("Open");
                size = ulong.MaxValue;
                return true;
            }

            public override void Close()
            {
                log("Close");
                Dispose();
            }

            protected override void Dispose(bool disposing)
            {
                log("Dispose");
                mSegmentsAvailable.Dispose();
                base.Dispose(disposing);
            }

            public override int Read(IntPtr buf, uint bytesToRead)
            {
                log("Read(buffersize=" + bytesToRead + ")");

                lock (mReadLock)
                {
                    int bytesRead = 0;

                    while (bytesToRead > 0)
                    {
                        byte[] segment;

                        if (mExtraPendingSegment != null)
                        {
                            segment = mExtraPendingSegment;
                            mExtraPendingSegment = null;
                        }
                        else
                        {
                            if (mSegmentsAvailable.CurrentCount == 0 && bytesRead > 0)
                            {
                                log("Read return(size=" + bytesRead + ")");
                                return bytesRead;
                            }

                            mSegmentsAvailable.Wait(10000);

                            if (!mPendingSegments.TryDequeue(out segment))
                            {
                                log("Read return(size=" + bytesRead + ")");
                                return bytesRead;
                            }
                        }

                        int copyCount = (int)Math.Min(bytesToRead, segment.Length);

                        Marshal.Copy(segment, 0, buf, copyCount);

                        bytesToRead -= (uint)copyCount;
                        bytesRead += copyCount;

                        int extraCount = segment.Length - copyCount;
                        if (extraCount > 0)
                        {
                            mExtraPendingSegment = new byte[extraCount];
                            Array.Copy(segment, copyCount, mExtraPendingSegment, 0, extraCount);
                        }
                    }

                    log("Read return(size=" + bytesRead + ")");
                    return bytesRead;
                }
            }

            public override bool Seek(ulong offset)
            {
                log("Seek(" + offset + ")");
                return false;
            }

            public void Write(byte[] buffer, int offset, int length)
            {
                lock (mWriteLock)
                {
                    log("Write: " + length + " bytes");

                    byte[] copy = new byte[length];
                    Array.Copy(buffer, offset, copy, 0, length);

                    mPendingSegments.Enqueue(copy);
                    mSegmentsAvailable?.Release(1);
                }
            }
        }
    }
}
