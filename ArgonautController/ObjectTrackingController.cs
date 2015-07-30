using ArgonautController.Actuators;
using ArgonautController.Sensors;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.System.Threading;
using Windows.UI;
using Windows.UI.Core;

namespace ArgonautController
{
    public class ObjectBlocksEventArgs : EventArgs
    {
        public ObjectBlock[] Blocks;
    }

    public class ObjectTrackingController : IDisposable
    {
        public event EventHandler<ObjectBlocksEventArgs> BlocksReceived;

        const long X_CENTER = 160;
        const long Y_CENTER = 100;
        const long RCS_MIN_POS = 0;
        const long RCS_MAX_POS = 1000;
        const long RCS_CENTER_POS = ((RCS_MAX_POS - RCS_MIN_POS) / 2);

        const int PIXY_X_MAX = 320;
        const int PIXY_Y_MAX = 200;

        class ServoLoop
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

                if (PrevError != 0x80000000L)
                {
                    velocity = (error * ProportionalGain + (error - PrevError) * DerivativeGain) >> 10;
                    Position += velocity;

                    if (Position > RCS_MAX_POS)
                    {
                        Position = RCS_MAX_POS;
                    }
                    else if (Position < RCS_MIN_POS)
                    {
                        Position = RCS_MIN_POS;
                    }
                }

                PrevError = error;
            }
        }

        public ObjectTrackingController()
        {
            ZumoMotorShieldConfig config;
            config = new ZumoMotorShieldConfig();
            config.LeftMotorDirPin = 5;
            config.RightMotorDirPin = 4;
            config.LeftPwmChannel = 1;
            config.RightPwmChannel = 0;
            config.BuzzerPwmChannel = 2;
            config.PwmDriverSlaveAddress = 0x40;

            watch = new Stopwatch();

            motorDriver = new ZumoMotorShield(config);
            pixyCam = new PixyCam();
            panLoop = new ServoLoop(200, 200);
            tiltLoop = new ServoLoop(150, 200);
        }

        public async Task Init()
        {
            Debug.WriteLine("Initializing ObjectTrackingController");

            await motorDriver.Init();
            await pixyCam.Init();
        }

        public Task RunAsync()
        {
            // Set LED to blue
            pixyCam.SetLED(0, 0, 255);

            // Start reading frames from camera
            return ThreadPool.RunAsync((s) =>
            {
                Debug.WriteLine("Starting ObjectTracking loop");
                long previousTime = 0;

                watch.Start();
                while (!shutdown)
                {
                    long diff = watch.ElapsedMilliseconds - previousTime;
                    if (diff > 20)
                    {
                        //Debug.WriteLine("Diff time: " + diff + "ms");
                    var blocks = pixyCam.GetBlocks(10);

                    if (blocks != null && blocks.Count > 0)
                    {
                        var trackedBlock = trackBlock(blocks);
                        if (trackedBlock != null)
                        {
                            followBlock(trackedBlock);
                        }

                            previousTime = watch.ElapsedMilliseconds;

                            // Commenting out UI debugging
                            //OnBlocksReceived(new ObjectBlocksEventArgs() { Blocks = blocks.ToArray() });
                    }
                    else oldBlock = null;

                    ++frameCount;
                    fps = frameCount / (float)watch.Elapsed.TotalSeconds;

                    Debug.WriteLineIf(
                        watch.ElapsedMilliseconds % 5000 == 0,
                        string.Format("{0}s: FPS={1}, Frame-time={2}ms", watch.Elapsed.TotalSeconds, fps, 1000.0f / fps));
                }


                    // If we lose sight of the object, start slowing down to a stop
                    if (diff > 100)
                    {
                        float currLeftPower = motorDriver.GetLeftMotorPower();
                        float currRightPower = motorDriver.GetRightMotorPower();

                        ZumoMotorDirection leftDir = motorDriver.GetLeftDir();
                        ZumoMotorDirection rightDir = motorDriver.GetRightDir();

                        motorDriver.SetLeftMotorPower(leftDir, Constrain(currLeftPower - 0.05f, 0, 1));
                        motorDriver.SetRightMotorPower(rightDir, Constrain(currRightPower - 0.05f, 0, 1));
                    }
                }
                watch.Stop();

                motorDriver.LeftMotorStop();
                motorDriver.RightMotorStop();

                Debug.WriteLine("Exiting ObjectTracking loop");

            }).AsTask();
        }

        public void Shutdown()
        {
            Debug.WriteLine("Shutdowning ObjectTracking Controller");
            shutdown = true;
        }

        // Track blocks via the Pixy pan/tilt mechanism
        private ObjectBlock trackBlock(List<ObjectBlock> blocks)
        {
            ObjectBlock trackedBlock = null;
            long maxSize = 0;

            foreach (ObjectBlock block in blocks)
            {
                if (oldBlock == null || (block.Signature == oldBlock.Signature))
                {
                    long newSize = block.Height * block.Width;
                    if (newSize > maxSize)
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

            float leftPower, rightPower;
            ZumoMotorDirection leftDir, rightDir;

            if (leftSpeed >= 0)
                leftDir = ZumoMotorDirection.Forward;
            else
                leftDir = ZumoMotorDirection.Backward;

            if (rightSpeed >= 0)
                rightDir = ZumoMotorDirection.Forward;
            else
                rightDir = ZumoMotorDirection.Backward;

            leftPower = Math.Abs(leftSpeed) / 500.0f;
            rightPower = Math.Abs(rightSpeed) / 500.0f;

            Debug.WriteLine(
                string.Format("followError={0}, size={1}, differential={2}, leftPower={3}, rightPower={4}",
                    followError,
                    size,
                    differential,
                    leftPower,
                    rightPower));

            motorDriver.SetLeftMotorPower(leftDir, leftPower);
            motorDriver.SetRightMotorPower(rightDir, rightPower);
        }

        // Constrains number between lower and upper bounds (inclusive)
        public int Constrain(int num, int lower, int upper)
        {
            if (num <= lower)
            {
                return lower;
            }
            else if (num >= upper)
            {
                return upper;
            }

            return num;
        }

        // Constrains number between lower and upper bounds (inclusive)
        public float Constrain(float num, float lower, float upper)
        {
            if (num <= lower)
            {
                return lower;
            }
            else if (num >= upper)
            {
                return upper;
            }

            return num;
        }

        public void Dispose()
        {
            if (pixyCam != null)
            {
                pixyCam.Close();
                pixyCam = null;
            }

            if (motorDriver != null)
            {
                motorDriver.Dispose();
                motorDriver = null;
            }
        }

        protected virtual void OnBlocksReceived(ObjectBlocksEventArgs e)
        {
            EventHandler<ObjectBlocksEventArgs> handler = BlocksReceived;
            if(handler != null)
            {
                handler(this, e);
            }
        }

        public ZumoMotorShield motorDriver;
        PixyCam pixyCam;
        ServoLoop panLoop, tiltLoop;
        bool shutdown = false;
        ObjectBlock oldBlock;
        long size = 400;
        long frameCount = 0;
        float fps = 0;
        Stopwatch watch;
    }
}
