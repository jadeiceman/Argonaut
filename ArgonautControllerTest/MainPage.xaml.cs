using Windows.UI.Xaml.Controls;
using System.Threading.Tasks;
using System.Diagnostics;
using ArgonautController.Actuators;
using ArgonautController;
using System.Threading;


// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace ArgonautControllerTest
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
            Loaded += MainPage_Loaded;
        }

        private async void MainPage_Loaded(object sender, object args)
        {
            // await MotorControlSmokeTest();
            await ObjectTrackingSmokeTest(60);
        }

        private async Task MotorControlSmokeTest()
        {
            ZumoMotorShieldConfig config = new ZumoMotorShieldConfig();
            config.LeftMotorDirPin = 4;
            config.RightMotorDirPin = 5;
            config.LeftPwmChannel = 0;
            config.RightPwmChannel = 1;
            config.PwmDriverSlaveAddress = 0x40;

            using (ZumoMotorShield motorDriver = new ZumoMotorShield(config))
            {
                await motorDriver.Init();

                bool flipDir = false;
                ZumoMotorDirection dirA = ZumoMotorDirection.Forward;
                ZumoMotorDirection dirB = ZumoMotorDirection.Backward;

                for (int i = 20; i <= 100; i += 20)
                {
                    Debug.WriteLine("Motor Control Ticking");

                    if (!flipDir)
                    {
                        motorDriver.SetLeftMotorPower(dirA, (float)i / 100.0f);
                        motorDriver.SetRightMotorPower(dirB, (float)i / 100.0f);
                    }
                    else
                    {
                        motorDriver.SetLeftMotorPower(dirB, (float)i / 100.0f);
                        motorDriver.SetRightMotorPower(dirA, (float)i / 100.0f);
                    }

                    flipDir = !flipDir;

                    await Task.Delay(1500);
                }

                motorDriver.LeftMotorStop();
                motorDriver.RightMotorStop();

                await Task.Delay(1000);

                motorDriver.SetLeftMotorPower(ZumoMotorDirection.Forward, 0.5f);
                await Task.Delay(2000);
                motorDriver.LeftMotorStop();

                await Task.Delay(500);

                motorDriver.SetRightMotorPower(ZumoMotorDirection.Forward, 0.5f);
                await Task.Delay(2000);
                motorDriver.RightMotorStop();
            }
        }

        private async Task ObjectTrackingSmokeTest(int seconds)
        {
            using (ObjectTrackingController controller = new ObjectTrackingController())
            {
                await controller.Init();
                var task = controller.RunAsync();

                Debug.WriteLine(string.Format("Letting object tracking run for {0} seconds", seconds));
                await Task.Delay(seconds * 1000);

                controller.Shutdown();
                bool graceful = task.Wait(3000);
                Debug.WriteLineIf(graceful, "Shutdown successfully");
                Debug.WriteLineIf(!graceful, "Shutdown timedout");
            }
        }
    }
}
