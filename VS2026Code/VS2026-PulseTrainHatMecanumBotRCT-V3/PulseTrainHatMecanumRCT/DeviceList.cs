using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
//using System;
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
namespace PulseTrainHatMecanumRCT
{
    class DeviceList
    {
        private static DeviceList deviceList;

        // The device that is currently being used/connected by the app
        private HidDevice currentDevice;

        public static DeviceList Current
        {
            get
            {
                if (deviceList == null)
                {
                    deviceList = new DeviceList();
                }

                return deviceList;
            }
        }

        public void SetCurrentDevice(HidDevice device)
        {
            currentDevice = device;
        }

        public HidDevice CurrentDevice
        {
            get
            {
                return currentDevice;
            }
        }
    }
}
