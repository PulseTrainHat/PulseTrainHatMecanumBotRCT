
using Windows.Devices.HumanInterfaceDevice;
namespace UWP_USB_HID_Template
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
