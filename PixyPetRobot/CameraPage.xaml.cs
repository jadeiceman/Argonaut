using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.System.Threading;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace PixyPetRobot
{
    /// <summary>
    /// Camera page that does all the camera logic
    /// </summary>
    public sealed partial class CameraPage : Page
    {
        PixyCam pixyCam;
        ServoLoop panLoop, tiltLoop;
        bool stopCameraThread = false;
        bool isCameraThreadRunning = false;
        ObjectBlock oldBlock = null;

        const long X_CENTER = 160;
        const long Y_CENTER = 100;
        const long RCS_MIN_POS = 0;
        const long RCS_MAX_POS = 1000;
        const long RCS_CENTER_POS = ((RCS_MAX_POS - RCS_MIN_POS) / 2);

        public CameraPage()
        {
            this.InitializeComponent();
            panLoop = new ServoLoop(200, 200);
            tiltLoop = new ServoLoop(150, 200);
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            if (pixyCam == null)
            {
                pixyCam = new PixyCam();

                // Initialize camera
                await pixyCam.Initialize();
            }

            if (!isCameraThreadRunning)
            {
                stopCameraThread = false;

                // Start reading data from camera
                await ThreadPool.RunAsync((s) =>
                {
                    isCameraThreadRunning = true;

                    while (!stopCameraThread)
                    {
                        var blocks = pixyCam.GetBlocks(10);

                        if (blocks != null && blocks.Count > 0)
                        {
                            var trackedBlock = trackBlock(blocks.ToArray());
                            followBlock(trackedBlock);
                        }
                    }

                    isCameraThreadRunning = false;
                });
            }
            else
            {

            }
        }

        private ObjectBlock trackBlock(ObjectBlock[] blocks)
        {
            ObjectBlock trackedBlock = null;
            long maxSize = 0;

            foreach(ObjectBlock block in blocks)
            {
                if(oldBlock == null || (block.Signature == oldBlock.Signature))
                {
                    long newSize = block.Height * block.Width;
                    if(newSize >  maxSize)
                    {
                        trackedBlock = block;
                        maxSize = newSize;
                    }
                }
            }

            long panError = X_CENTER - trackedBlock.X;
            long tiltError = trackedBlock.Y - Y_CENTER;

            panLoop.Update(panError);
            tiltLoop.Update(tiltError);
            pixyCam.SetServos(panLoop.Position, tiltLoop.Position);

            oldBlock = trackedBlock;
            return trackedBlock;
        }

        private void followBlock(ObjectBlock block)
        {

        }

        public class ServoLoop
        {
            public long Position;
            public long PrevError;
            public long ProportionalGain;
            public long DerivativeGain;

            public ServoLoop(long proportionalGain, long derivativeGain)
            {
                Position = RCS_CENTER_POS;
                ProportionalGain = proportionalGain;
                DerivativeGain = derivativeGain;
                PrevError = 0x8000000L;
            }

            public void Update(long error)
            {
                long velocity;

                if(PrevError != 0x80000000L)
                {
                    velocity = (error * ProportionalGain + (error - PrevError) * DerivativeGain) >> 10;
                    Position += velocity;

                    if(Position > RCS_MAX_POS)
                    {
                        Position = RCS_MAX_POS;
                    }
                    else if(Position < RCS_MIN_POS)
                    {
                        Position = RCS_MIN_POS;
                    }
                }

                PrevError = error;
            }
        }
    }
}
