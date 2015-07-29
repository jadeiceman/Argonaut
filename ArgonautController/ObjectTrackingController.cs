using ArgonautController.Actuators;
using ArgonautController.Sensors;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.System.Threading;

namespace ArgonautController
{
    public class ObjectTrackingController : IDisposable
    {
        const long X_CENTER = 160;
        const long Y_CENTER = 100;
        const long RCS_MIN_POS = 0;
        const long RCS_MAX_POS = 1000;
        const long RCS_CENTER_POS = ((RCS_MAX_POS - RCS_MIN_POS) / 2);

        const int PIXY_X_MAX = 320;
        const int PIXY_Y_MAX = 200;

        public event BlockDetectedEventHandler OnNewBlocksDetected;

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

                watch.Start();
                while (!shutdown)
                {
                    var blocks = pixyCam.GetBlocks(10);

                    if (blocks != null && blocks.Count > 0)
                    {
                        var trackedBlock = trackBlock(blocks);
                        if (trackedBlock != null)
                        {
                            followBlock(trackedBlock);
                        }
                    }
                    else oldBlock = null;

                    ++frameCount;
                    fps = frameCount / (float)watch.Elapsed.TotalSeconds;

                    Debug.WriteLineIf(
                        watch.ElapsedMilliseconds % 5000 == 0,
                        string.Format("{0}s: FPS={1}, Frame-time={2}ms", watch.Elapsed.TotalSeconds, fps, 1000.0f / fps));
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

            // Get this biggest block
            ObjectBlock biggestBlock = blocks[0];
            for (int index = 1; index < blocks.Count; ++index)
            {
                long newSize = blocks[index].Height * blocks[index].Width;
                if (newSize > maxSize)
                {
                    biggestBlock = blocks[index];
                    maxSize = newSize;
                }
           }

            //// 3 cases:
            //// 1: New block
            //// 2: Same block
            //// 3: Different block
            //// Case 1 and 3 result in the biggest block being assigned as the tracked block

            //// 1: New Block & different block
            //if (oldBlock == null || oldBlock.Signature != biggestBlock.Signature)
            //{
            //    trackedBlock = biggestBlock;
            //    oldBlock = biggestBlock;

            //    // Notify listeners that new object blocks have been detected
            //    if (this.OnNewBlocksDetected != null)
            //    {
            //        //this.OnNewBlocksDetected(this, new BlockDetectedEventArgs(blocks));
            //    }
            //}
            //// 2: Same block
            //else if (oldBlock.Signature == biggestBlock.Signature)
            //{
            //    trackedBlock = oldBlock;
            //}

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

            leftPower = Math.Abs(leftSpeed) / 400.0f;
            rightPower = Math.Abs(rightSpeed) / 400.0f;

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

        public ZumoMotorShield motorDriver { get; set; }
        public PixyCam pixyCam { get; set; }
        ServoLoop panLoop, tiltLoop;
        bool shutdown = false;
        ObjectBlock oldBlock;
        long size = 400;
        long frameCount = 0;
        float fps = 0;
        Stopwatch watch;
    }

    public delegate void BlockDetectedEventHandler(object source, BlockDetectedEventArgs e);

    public class BlockDetectedEventArgs : EventArgs
    {
        public List<ObjectBlock> Blocks
        {
            get; set;
        }
        public BlockDetectedEventArgs(List<ObjectBlock> blocks)
        {
            Blocks = blocks;
        }
    }


}
