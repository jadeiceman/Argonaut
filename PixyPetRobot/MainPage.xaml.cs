using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace PixyPetRobot
{
    using Argonaut.Networking;

    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        EventHubSettings eventHubSettings;
        EventSender eventSender;

        public MainPage()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Initializes the underlying communication channels.
        /// </summary>
        /// <remarks>
        /// This is a length operation, and should be turned into an async method.
        /// </remarks>
        private bool InitilizeCommunications()
        {
            bool initialized = false;

            this.eventHubSettings = new EventHubSettings()
            {
                ServiceBusName = "lightningbolt",
                EventHubName = "sumobot",
                Policies = new Dictionary<string, string>()
                {
                    { "manage", "Z6KTK+hqBmMiu0WPLchX0oaYDMd+K2nwSRpZKt4RsqA=" },
                    { "device", "syc3awPWUXN1V+KbdAFmpPwx/r0F3chn1SLZJ6smioY=" },
                    { "processor", "8cxDOUPPQLkFIskUxMFCi9JNrBv6xHN8WcIwDz2fYhA=" }
                }
            };

            var sendLinks = new SendLinkDescription[]
            {
                new SendLinkDescription("temperature", "0"),
                new SendLinkDescription("sonar", "1"),
                new SendLinkDescription("ir", "2")
            };

            this.eventSender = new EventSender("PixyBot", this.eventHubSettings, "device");
            try
            {
                this.eventSender.InitializeChannels(sendLinks);
                initialized = true;
            }
            catch (Exception x)
            {
                StatusTxt.Text = "ERROR: " + x.Message;
                initialized = false;
            }

            return initialized;
        }

        private void OnSendTemperatureUpdate(object sender, RoutedEventArgs e)
        {
            int value = (int)DateTime.Now.Ticks % 50;

            byte[] intBytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) Array.Reverse(intBytes);
            byte[] result = intBytes;

            eventSender.SendEvent("temperature", value.ToString());
            PostStatus("Temperature event sent");
        }

        private void InitChannelsBtn_Click(object sender, RoutedEventArgs e)
        {
            if (this.InitilizeCommunications())
            {
                PostStatus("Channels initialized.");
                StatusTxt.Text = "Initialized!";
                this.SendTemperatureUpdateBtn.IsEnabled = true;
                this.SendIRUpdateBtn.IsEnabled = true;
                this.SendSonarUpdateBtn.IsEnabled = true;
            }
            else
            {
                PostStatus("Failed to initialize communications.");
                this.SendTemperatureUpdateBtn.IsEnabled = false;
                this.SendIRUpdateBtn.IsEnabled = false;
                this.SendSonarUpdateBtn.IsEnabled = false;
            }
        }

        private void OnSendIRUpdate(object sender, RoutedEventArgs e)
        {
            eventSender.SendEvent("ir", (1.0 / (double)(DateTime.Now.Ticks % 50)).ToString());
            PostStatus("IR event sent");
        }

        private void OnSendSonarUpdate(object sender, RoutedEventArgs e)
        {
            eventSender.SendEvent("sonar", (1.0 / (double)(DateTime.Now.Ticks % 20)).ToString());
            PostStatus("Sonar event sent");
        }

        private void InitializeClicked(object sender, RoutedEventArgs e)
        {
            this.InitilizeCommunications();
        }

        private void PostStatus(string message)
        {
            StatusTxt.Text = message;
        }
    }
}
