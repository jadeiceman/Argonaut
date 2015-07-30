﻿using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Devices.Gpio;
using Windows.System.Threading;

namespace ArgonautController.Actuators
{
    public class ZumoMotorShieldConfig
    {
        public ZumoMotorShieldConfig()
        {
            LeftMotorDirPin = 0;
            RightPwmChannel = 0;
            LeftPwmChannel = 0;
            RightPwmChannel = 0;
            PwmDriverSlaveAddress = 0;
            BuzzerPwmChannel = 0;
        }

        public int LeftMotorDirPin;
        public int RightMotorDirPin;
        public int LeftPwmChannel;
        public int RightPwmChannel;
        public int PwmDriverSlaveAddress;
        public int BuzzerPwmChannel;

        public override string ToString()
        {
            return string.Format(
                "LeftMotorDirPin={0}, RightMotorDirPin={1}, LeftPwmChannel={2}, RightPwmChannel={3}, PwmDriverSlaveAddress=0x{4}",
                LeftMotorDirPin,
                RightMotorDirPin,
                LeftPwmChannel,
                RightPwmChannel,
                PwmDriverSlaveAddress.ToString("X2"));
        }
    }

    public enum ZumoMotorDirection
    {
        Forward,
        Backward
    }

    public class ZumoMotorShield : IDisposable
    {
        public ZumoMotorShield(ZumoMotorShieldConfig config)
        {
            Config = config;
        }

        public async Task Init()
        {
            Debug.WriteLine("Initializing ZumoMotorShield");

            Debug.WriteLine(Config.ToString());

            var gpioCtrlr = GpioController.GetDefault();

            LeftMotorDir = gpioCtrlr.OpenPin(Config.LeftMotorDirPin);
            Debug.Assert(LeftMotorDir != null);

            RightMotorDir = gpioCtrlr.OpenPin(Config.RightMotorDirPin);
            Debug.Assert(RightMotorDir!= null);

            LeftMotorDir.SetDriveMode(GpioPinDriveMode.Output);
            RightMotorDir.SetDriveMode(GpioPinDriveMode.Output);

            PwmDriver = new PCA9685(Config.PwmDriverSlaveAddress);

            await PwmDriver.Init();
        }

        public void SetLeftMotorPower(ZumoMotorDirection dir, float power)
        {
            Debug.WriteLine("LeftMotor: {0} {1}", dir, power * 100.0f);

            if (dir == ZumoMotorDirection.Forward)
                LeftMotorDir.Write(GpioPinValue.Low);
            else
                LeftMotorDir.Write(GpioPinValue.High);

            PwmDriver.SetChannelDutyCycle(Config.LeftPwmChannel, power);

            leftMotorPower = power;
            leftDir = dir;
        }

        public void SetRightMotorPower(ZumoMotorDirection dir, float power)
        {
            Debug.WriteLine("RightMotor: {0} {1}", dir, power * 100.0f);

            if (dir == ZumoMotorDirection.Forward)
                RightMotorDir.Write(GpioPinValue.Low);
            else
                RightMotorDir.Write(GpioPinValue.High);

            PwmDriver.SetChannelDutyCycle(Config.RightPwmChannel, power);

            rightMotorPower = power;
            rightDir = dir;
        }

        public float GetLeftMotorPower()
        {
            return leftMotorPower;
        }

        public float GetRightMotorPower()
        {
            return rightMotorPower;
        }

        public ZumoMotorDirection GetLeftDir()
        {
            return leftDir;
        }

        public ZumoMotorDirection GetRightDir()
        {
            return rightDir;
        }

        public void LeftMotorStop()
        {
            Debug.WriteLine("LefttMotor: Stop");

            SetLeftMotorPower(ZumoMotorDirection.Forward, 0.0f);
        }

        public void RightMotorStop()
        {
            Debug.WriteLine("RightMotor: Stop");

            SetRightMotorPower(ZumoMotorDirection.Forward, 0.0f);
        }

        public Task Buzz(float frequency, uint durationMs)
        {
            return ThreadPool.RunAsync(async (s) =>
            {
                PwmDriver.SetChannelDutyCycle(Config.BuzzerPwmChannel, 0.5f);
                await Task.Delay((int)durationMs);
                PwmDriver.SetChannelDutyCycle(Config.BuzzerPwmChannel, 0.0f);
            }).AsTask();
        }

        public void Dispose()
        {
            if (PwmDriver != null)
            {
                LeftMotorStop();
                RightMotorStop();

                PwmDriver.Dispose();
                PwmDriver = null;
            }

            LeftMotorDir.Dispose();
            RightMotorDir.Dispose();
        }

        GpioPin LeftMotorDir;
        GpioPin RightMotorDir;
        PCA9685 PwmDriver;
        ZumoMotorShieldConfig Config;
        float leftMotorPower, rightMotorPower;
        ZumoMotorDirection leftDir, rightDir;
    }
}
