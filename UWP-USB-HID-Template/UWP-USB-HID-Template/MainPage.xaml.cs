using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

using Windows.UI.Xaml.Navigation;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Storage.Streams;
using Windows.Devices.Enumeration;
using System.Collections.ObjectModel;
using System.Threading;
using Windows.Storage;

// Template for testing Pulse Train Hat http://www.pthat.com
//USB HID example template 

namespace UWP_USB_HID_Template
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page

            {

        private DataWriter dataWriteObject = null;
        private DataReader dataReaderObject = null;

        private ObservableCollection<DeviceInformation> listOfDevices;
        private CancellationTokenSource ReadCancellationTokenSource;

        //Interrupt handler for USB packets coming back from controller
        private TypedEventHandler<HidDevice, HidInputReportReceivedEventArgs> interruptEventHandler;

        public MainPage()
        {
            this.InitializeComponent();
        }




        private void Refresh_USB_Devices_Click(object sender, RoutedEventArgs e)
        {
            OpenDevice();
        }

        protected override void OnNavigatedTo(NavigationEventArgs eventArgs)
        {
            OpenDevice();

           
        }

        // Open device
        async void OpenDevice()
        {
            // Create a selector that gets a HID device using VID/PID and a VendorDefined usage
            string selector = HidDevice.GetDeviceSelector(Descriptors.usagePage, Descriptors.usageId, Descriptors.vendorId, Descriptors.productId);

            // Enumerate devices using the selector
            var devices = await DeviceInformation.FindAllAsync(selector);



            // Check we only have 1 device connected
            if (devices.Count > 0)

            {
                // Open the target HID device
                HidDevice device = await HidDevice.FromIdAsync(devices.ElementAt(0).Id, FileAccessMode.ReadWrite);

                // Chech the device is ready to communicate
                if (device != null)
                {
                    // Show user we are connect to the device
                    this.TextBlockEnumerate.Text = "Connected: " + devices.ElementAt(0).Name;

                    // Save the device from the task to a global object so that it can be used later from other scenarios
                    DeviceList.Current.SetCurrentDevice(device);


                    //Turns on interupt for read back
                    TypedEventHandler<HidDevice, HidInputReportReceivedEventArgs> interruptEventHandler =
               new TypedEventHandler<HidDevice, HidInputReportReceivedEventArgs>(this.OnGeneralInterruptEvent);

                    RegisterForInterruptEvent(interruptEventHandler);



                }
                else
                {
                    // Unable to open the target HID device error message
                    this.TextBlockEnumerate.Text = "ERROR - unable to open the target HID device, could be already open";
                }

            }
            else
            {
                // HID devices not found error message
                this.TextBlockEnumerate.Text = "ERROR - " + Descriptors.ProductString + " not found";
            }
        }


        async void sendusb() // Send to
        {
            // Declare the output report
            var outputReport = DeviceList.Current.CurrentDevice.CreateOutputReport();

            // Declare an output buffer
            Byte[] outputBuffer = new Byte[64];

            //Control bits.
            outputBuffer[0] = 1;

            // Copies bytes from bytesToCopy to outputReport 
            WindowsRuntimeBufferExtensions.CopyTo(outputBuffer, 0, outputReport.Data, 1, outputBuffer.Length);

            // Send the output report
            await DeviceList.Current.CurrentDevice.SendOutputReportAsync(outputReport);


        }



        private async void RegisterForInterruptEvent(TypedEventHandler<HidDevice, HidInputReportReceivedEventArgs> eventHandler)
        {


            interruptEventHandler = eventHandler;

            DeviceList.Current.CurrentDevice.InputReportReceived += interruptEventHandler;


        }


        private async void OnGeneralInterruptEvent(HidDevice sender, HidInputReportReceivedEventArgs eventArgs)
        {
                        
            // Retrieve the sensor data
            HidInputReport inputReport = eventArgs.Report;
            IBuffer buffer = inputReport.Data;
            DataReader dr = DataReader.FromBuffer(buffer);
            byte[] BufferIn = new byte[inputReport.Data.Length];
            dr.ReadBytes(BufferIn);


            // Wait for when UI is ready to be updated
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {




                {

                    //   Channel1

                    byte[] vIn = new byte[] { BufferIn[1], BufferIn[2], BufferIn[3], BufferIn[4] };
                    long result = BitConverter.ToInt32(vIn, 0);
                    Channel1.Text = Convert.ToString(result);


                    // Channel2
                    vIn = new byte[] { BufferIn[5], BufferIn[6], BufferIn[7], BufferIn[8] };
                    result = BitConverter.ToInt32(vIn, 0);
                    Channel2.Text = Convert.ToString(result);

                    // Channel3
                    vIn = new byte[] { BufferIn[9], BufferIn[10], BufferIn[11], BufferIn[12] };
                    result = BitConverter.ToInt32(vIn, 0);
                    Channel3.Text = Convert.ToString(result);

                    // Channel4
                    vIn = new byte[] { BufferIn[13], BufferIn[14], BufferIn[15], BufferIn[16] };
                    result = BitConverter.ToInt32(vIn, 0);
                    Channel4.Text = Convert.ToString(result);

                    // Channel5
                    vIn = new byte[] { BufferIn[17], BufferIn[18], BufferIn[19], BufferIn[20] };
                    result = BitConverter.ToInt32(vIn, 0);
                    Channel5.Text = Convert.ToString(result);

                    // Channel6
                    vIn = new byte[] { BufferIn[21], BufferIn[22], BufferIn[23], BufferIn[24] };
                    result = BitConverter.ToInt32(vIn, 0);
                    Channel6.Text = Convert.ToString(result);


                }

            });





        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            sendusb();
        }
    }
}
