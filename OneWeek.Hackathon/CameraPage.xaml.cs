using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.System.Threading;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace OneWeek.Hackathon
{
    using Argonaut.Sensors;

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
        long size = 400;

        const long X_CENTER = 160;
        const long Y_CENTER = 100;
        const long RCS_MIN_POS = 0;
        const long RCS_MAX_POS = 1000;
        const long RCS_CENTER_POS = ((RCS_MAX_POS - RCS_MIN_POS) / 2);

        const double PIXY_X_MAX = 320;
        const double PIXY_Y_MAX = 200;

        public CameraPage()
        {
            this.InitializeComponent();
            panLoop = new ServoLoop(200, 200);
            tiltLoop = new ServoLoop(150, 200);

            trackedBlockRect.Visibility = Visibility.Collapsed;
            outputTextBlock.Text = "";
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

                // Set LED to blue
                pixyCam.SetLED(0, 0, 255);
                
                // Start reading frames from camera
                await ThreadPool.RunAsync((s) =>
                {
                    isCameraThreadRunning = true;

                    while (!stopCameraThread)
                    {
                        var blocks = pixyCam.GetBlocks(10);

                        if (blocks != null && blocks.Count > 0)
                        {
                            var trackedBlock = trackBlock(blocks.ToArray());

                            if (trackedBlock != null)
                            {
                                followBlock(trackedBlock);
                                updateUI(trackedBlock);
                            }
                        }
                    }

                    isCameraThreadRunning = false;
                });
            }
        }

        // Display what the camera is seeing on the page
        private async void updateUI(ObjectBlock block)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                trackedBlockRect.Visibility = Visibility.Visible;
                trackedBlockRect.Width = block.Width;
                trackedBlockRect.Height = block.Height;
                double xRatio = (block.X - ((double)block.Width / 2)) / PIXY_X_MAX;
                double yRatio = (block.Y - ((double)block.Height / 2)) / PIXY_Y_MAX;
                Canvas.SetLeft(trackedBlockRect, xRatio * canvas.Width);
                Canvas.SetTop(trackedBlockRect, yRatio * canvas.Height);

                outputTextBlock.Text = block.ToString();
            });
        }

        // Track blocks via the Pixy pan/tilt mechanism
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

            if (trackedBlock != null)
            {
                long panError = X_CENTER - trackedBlock.X;
                long tiltError = trackedBlock.Y - Y_CENTER;

                panLoop.Update(panError);
                tiltLoop.Update(tiltError);
                pixyCam.SetServos(panLoop.Position, tiltLoop.Position);

                oldBlock = trackedBlock;
            }

            return trackedBlock;
        }

        // Follow blocks via the Zumo robot drive
        // This code makes the robot base turn and move to follow the pan/tilt tracking of the head
        private void followBlock(ObjectBlock trackedBlock)
        {
            long followError = RCS_CENTER_POS - panLoop.Position;

            // Size is the area of the object
            // We keep a running average of the last 8
            size += trackedBlock.Width * trackedBlock.Height;
            size -= size >> 3;

            // Forward speed decreases as we approach the object (size is larger)
            int forwardSpeed = Constrain(400 - ((int)size / 256), -100, 400);

            // Steering differential is proportional to the error times the forward speed
            long differential = (followError + (followError * forwardSpeed)) >> 8;

            // Adjust the left and right speeds by the steering differential
            int leftSpeed = Constrain((int)(forwardSpeed + differential), -400, 400);
            int rightSpeed = Constrain((int)(forwardSpeed - differential), -400, 400);

            // TODO: Set the motor speeds
        }

        // Constrains number between lower and upper bounds (inclusive)
        public int Constrain(int num, int lower, int upper)
        {
            if(num <= lower)
            {
                return lower;
            }
            else if(num >= upper)
            {
                return upper;
            }

            return num;
        }

        private void homeButton_Click(object sender, RoutedEventArgs e)
        {
            this.Frame.Navigate(typeof(MainPage));
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
