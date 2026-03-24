using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Devices.SerialCommunication;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

// Test program for Pulse Train Hat http://www.pthat.com

namespace PulseTrainHatMecanumRCT
{
    public sealed partial class MainPage : Page
    {
        /// <summary>
        /// Private variables
        /// </summary>
        private SerialDevice serialPort = null;

        private DataWriter dataWriteObject = null;
        private DataReader dataReaderObject = null;

        private ObservableCollection<DeviceInformation> listOfDevices;
        private CancellationTokenSource ReadCancellationTokenSource;

        //Interrupt handler for USB packets coming back from controller
        private TypedEventHandler<HidDevice, HidInputReportReceivedEventArgs> interruptEventHandler;
        private DispatcherTimer timer;
        private int tick = 0;

        //PlaceHolder Image
        public ImageSource placeholder;

        public static class MyStaticValues
        {
            //----Button status:
            // 0: pressed
            // 1: enabled
            // 2: disabled
            public static int Enable = 2;

            //Switch case to determine which direction is active
            public static string Movement_Direction = "";

            //Switch case for whether a button is pressed or released
            public static string Movement_Action = "";

            //stores set axis command
            public static string Xsendstore;
            public static string Ysendstore;
            public static string Zsendstore;
            public static string Esendstore;

            //Catches a button release event
            public static int ACatch;

            //Flag for all axis complete
            public static int setback = 0;

            //Speed Increment
            public static double inc = 10;
            public static int inc_check = 0;

            //Flag for speed change
            public static int spdchange = 1;

            //Flag for if motors are running
            public static int running = 0;

            //----Active Direction:
            // 0: None
            // 1: Right
            // 2: Left
            // 3: Forward
            // 4: Reverse
            // 5: Top Right
            // 6: Top Left
            // 7: Bottom Left
            // 8: Bottom Right
            // 9: Clockwise
            // 10: Counter Clockwise
            public static int Direction_Flag = 0;

            //Flag if movement command has finished
            public static int CommandDone = 1;

            //Stores Receiver Channel values
            public static int RJX;
            public static int RJY;
            public static int LJY;
            public static int LJX;

            //Hertz Store Variables used for predicting Speed change
            public static string tmpHZ;
            public static string hzstore;

            //Stores Travelspeed for checking
            public static string travelspdvar;

            //Flag for Completed Axis Speed Change
            public static int FXYZE = 0;
            public static int FXYZE_Total = 0;

            //Flag for a speed change during incrementing
            public static int runchange = 0;

            //Flag for Emergency Stop Enable/Disable
            public static int Limit_Enable = 0;
        }

        public MainPage()
        {
            this.InitializeComponent();

            //Enable/Disable Controls
            MyStaticValues.Enable = 2;
            MyStaticValues.ACatch = 0;
            comPortInput.IsEnabled = false;
            Firmware1.IsEnabled = false;
            LowSpeedBaud.IsChecked = true;
            HighSpeedBaud.IsChecked = false;
            Reset.IsEnabled = false;
            ToggleEnableLine.IsEnabled = false;

            //Set up Timer
            timer = new DispatcherTimer();
            timer.Tick += Ticker;
            timer.Interval = new TimeSpan(0, 0, 0, 0, 10);

            //Format Boxes
            calculatetravelspeeds();

            //Collect List of Devices
            listOfDevices = new ObservableCollection<DeviceInformation>();
            ListAvailablePorts();
        }

        //Dispatch Timer Ticker
        private void Ticker(object sender, object e)
        {
            //Increment ticker
            tick++;

            //Ticker has elapsed set time
            if (tick > 1)
            {
                //Requests Reciever values from STM32 Smart Board
                sendusb();

                //Speed change is inactive
                if (MyStaticValues.spdchange == 1)
                {
                    //Calls method to use Receiver values
                    CheckVals();
                }
                else //Speed change is active
                {
                    //Set Flag
                    MyStaticValues.runchange = 1;
                }

                //Reset tick
                tick = 0;
            }
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
        private async void OpenDevice()
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

        private async void sendusb() // Send to
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
                    // Channel1
                    // Right Joystick X Axis
                    byte[] vIn = new byte[] { BufferIn[1], BufferIn[2], BufferIn[3], BufferIn[4] };
                    MyStaticValues.RJX = BitConverter.ToInt32(vIn, 0);
                    Channel1.Text = Convert.ToString(MyStaticValues.RJX);

                    // Channel2
                    // Right Joystick Y Axis
                    vIn = new byte[] { BufferIn[5], BufferIn[6], BufferIn[7], BufferIn[8] };
                    MyStaticValues.RJY = BitConverter.ToInt32(vIn, 0);
                    Channel2.Text = Convert.ToString(MyStaticValues.RJY);

                    // Channel3
                    // Left Joystick Y Axis
                    vIn = new byte[] { BufferIn[9], BufferIn[10], BufferIn[11], BufferIn[12] };
                    MyStaticValues.LJY = BitConverter.ToInt32(vIn, 0);
                    Channel3.Text = Convert.ToString(MyStaticValues.LJY);

                    // Channel4
                    // Left Joystick X Axis
                    vIn = new byte[] { BufferIn[13], BufferIn[14], BufferIn[15], BufferIn[16] };
                    MyStaticValues.LJX = BitConverter.ToInt32(vIn, 0);
                    Channel4.Text = Convert.ToString(MyStaticValues.LJX);

                    // Channel5
                    vIn = new byte[] { BufferIn[17], BufferIn[18], BufferIn[19], BufferIn[20] };
                    int result = BitConverter.ToInt32(vIn, 0);
                    Channel5.Text = Convert.ToString(result);

                    // Channel6
                    // Deadman switch
                    vIn = new byte[] { BufferIn[21], BufferIn[22], BufferIn[23], BufferIn[24] };
                    result = BitConverter.ToInt32(vIn, 0);
                    Channel6.Text = Convert.ToString(result);

                } // endof n loop
            });
        }

        /// <summary>
        /// ListAvailablePorts
        /// - Use SerialDevice.GetDeviceSelector to enumerate all serial devices
        /// - Attaches the DeviceInformation to the ListBox source so that DeviceIds are displayed
        /// </summary>
        private async void ListAvailablePorts()
        {
            try
            {
                string aqs = SerialDevice.GetDeviceSelector();
                var dis = await DeviceInformation.FindAllAsync(aqs);

                status.Text = "Select a device and connect";

                for (int i = 0; i < dis.Count; i++)
                {
                    listOfDevices.Add(dis[i]);
                }

                DeviceListSource.Source = listOfDevices;
                comPortInput.IsEnabled = true;
                ConnectDevices.SelectedIndex = -1;
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
                Windows.UI.Popups.MessageDialog msg = new Windows.UI.Popups.MessageDialog(ex.Message);
                await msg.ShowAsync(); // this will show error message(if Any)
            }
        }

        /// <summary>
        /// comPortInput_Click: Action to take when 'Connect' button is clicked
        /// - Get the selected device index and use Id to create the SerialDevice object
        /// - Configure default settings for the serial port
        /// - Create the ReadCancellationTokenSource token
        /// - Start listening on the serial port input
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void comPortInput_Click(object sender, RoutedEventArgs e)
        {
            var selection = ConnectDevices.SelectedItems;

            if (selection.Count <= 0)
            {
                status.Text = "Select a device and connect";
                return;
            }

            DeviceInformation entry = (DeviceInformation)selection[0];

            try
            {
                serialPort = await SerialDevice.FromIdAsync(entry.Id);

                // Disable the 'Connect' button
                comPortInput.IsEnabled = false;

                // Configure serial settings
                serialPort.WriteTimeout = TimeSpan.FromMilliseconds(30);
                serialPort.ReadTimeout = TimeSpan.FromMilliseconds(30);

                if (LowSpeedBaud.IsChecked == true)
                {
                    serialPort.BaudRate = 115200;
                }
                else
                {
                    serialPort.BaudRate = 806400;
                }

                serialPort.Parity = SerialParity.None;
                serialPort.StopBits = SerialStopBitCount.One;
                serialPort.DataBits = 8;
                serialPort.Handshake = SerialHandshake.None;

                // Display configured settings
                status.Text = "Serial port configured successfully: ";
                status.Text += serialPort.BaudRate + "-";
                status.Text += serialPort.DataBits + "-";
                status.Text += serialPort.Parity.ToString() + "-";
                status.Text += serialPort.StopBits;

                // Set the RcvdText field to invoke the TextChanged callback
                // The callback launches an async Read task to wait for data
                rcvdText.Text = "Waiting for data...";

                // Create cancellation token object to close I/O operations when closing the device
                ReadCancellationTokenSource = new CancellationTokenSource();

                // Enable 'Start' button to allow sending data
                MyStaticValues.Enable = 1;
                MyStaticValues.ACatch = 0;
                Firmware1.IsEnabled = true;
                Reset.IsEnabled = true;
                ToggleEnableLimits.IsEnabled = true;
                ToggleEnableLine.IsEnabled = true;
                sendText.Text = "";

                Listen();

                //Sends Firmware check
                sendText.Text = "I00FW*";
                SendDataout();
                await Task.Delay(2);

                //Sends Toggle Enable Line
                sendText.Text = "I00HT*";
                SendDataout();
                await Task.Delay(2);

                //Sends Enable Emergency Stop
                sendText.Text = "I00KS1*";
                SendDataout();
                await Task.Delay(2);

                //Starts Timer for 
                timer.Start();
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
                comPortInput.IsEnabled = true;
                MyStaticValues.Enable = 2;
                MyStaticValues.ACatch = 0;
                Firmware1.IsEnabled = false;
                ToggleEnableLimits.IsEnabled = false;
                Reset.IsEnabled = false;
                ToggleEnableLine.IsEnabled = false;
                Windows.UI.Popups.MessageDialog msg = new Windows.UI.Popups.MessageDialog(ex.Message);
                await msg.ShowAsync(); // this will show error message(if Any)
            }
        }

        /// <summary>
        /// WriteAsync: Task that asynchronously writes data from the input text box 'sendText' to the OutputStream
        /// </summary>
        /// <returns></returns>
        private async Task WriteAsync()
        {
            Task<UInt32> storeAsyncTask;

            // Load the text from the sendText input text box to the dataWriter object
            dataWriteObject.WriteString(sendText.Text);

            // Launch an async task to complete the write operation
            storeAsyncTask = dataWriteObject.StoreAsync().AsTask();

            UInt32 bytesWritten = await storeAsyncTask;
            if (bytesWritten > 0)
            {
                status.Text = sendText.Text + ", ";
                status.Text += "bytes written successfully!";
            }
        }

        /// <summary>
        /// - Create a DataReader object
        /// - Create an async task to read from the SerialDevice InputStream
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Listen()
        {
            try
            {
                if (serialPort != null)
                {
                    dataReaderObject = new DataReader(serialPort.InputStream);

                    // keep reading the serial input
                    while (true)
                    {
                        await ReadAsync(ReadCancellationTokenSource.Token);
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex.GetType().Name == "TaskCanceledException")
                {
                    status.Text = "Reading task was cancelled, closing device and cleaning up";
                    CloseDevice();
                }
                else
                {
                    status.Text = ex.Message;
                }
            }
            finally
            {
                // Cleanup once complete
                if (dataReaderObject != null)
                {
                    dataReaderObject.DetachStream();
                    dataReaderObject = null;
                }
            }
        }

        /// <summary>
        /// ReadAsync: Task that waits on data and reads asynchronously from the serial device InputStream
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task ReadAsync(CancellationToken cancellationToken)
        //private async Task ReadAsync()
        {
            Task<UInt32> loadAsyncTask;

            uint ReadBufferLength = 1024;

            // If task cancellation was requested, comply
            cancellationToken.ThrowIfCancellationRequested();

            // Set InputStreamOptions to complete the asynchronous read operation when one or more bytes is available
            dataReaderObject.InputStreamOptions = InputStreamOptions.Partial;

            // Create a task object to wait for data on the serialPort.InputStream
            loadAsyncTask = dataReaderObject.LoadAsync(ReadBufferLength).AsTask(cancellationToken);

            //    loadAsyncTask = dataReaderObject.LoadAsync(ReadBufferLength).AsTask();

            // Launch the task and wait
            UInt32 bytesRead = await loadAsyncTask;

            if (bytesRead > 0)
            {
                rcvdText.Text = dataReaderObject.ReadString(bytesRead);
                string input = rcvdText.Text;

                //Check if received message can be divided by 7 as our return messages are 7 bytes long
                if (input.Length % 7 == 0)

                {
                    for (int i = 0; i < input.Length; i += 7)
                    //  foreach (string match in sub)

                    {
                        string sub = input.Substring(i, 7);

                        //Check if Start ALL command Received
                        if (sub == "RI00SA*")
                        {
                            //Enable/Disable certain controls
                            ToggleEnableLine.IsEnabled = false;
                            Firmware1.IsEnabled = false;
                            if (MyStaticValues.ACatch == 1)
                            {
                                MyStaticValues.Enable = 2;
                                MyStaticValues.Movement_Action = "released";
                                InitMovement();
                            }
                            MyStaticValues.ACatch = 1;

                            //Command Finished
                            MyStaticValues.CommandDone = 1;

                            //Set to running
                            MyStaticValues.running = 1;
                        }

                        //Check if Set X Axis completed
                        if (sub == "CI00CX*")
                        {
                            //Sends Set Y Axis Command
                            sendText.Text = MyStaticValues.Ysendstore;
                            SendDataout();
                        }

                        //Check if Set Y Axis completed
                        if (sub == "CI00CY*")
                        {
                            sendText.Text = MyStaticValues.Zsendstore;
                            SendDataout();
                        }

                        //Check if Set Z Axis completed
                        if (sub == "CI00CZ*")
                        {
                            sendText.Text = MyStaticValues.Esendstore;
                            SendDataout();
                        }

                        //Check if Set E Axis completed
                        if (sub == "CI00CE*")
                        {
                            sendText.Text = "I00SA*";
                            SendDataout();
                        }

                        //Check if X Axis completed amount of pulses
                        if (sub == "CI00SX*")
                        {
                            //Increment Flag
                            MyStaticValues.setback += 1;

                            //Flag is active
                            if (MyStaticValues.setback == 4)
                            {
                                //Reset Variables
                                MyStaticValues.Enable = 1;
                                MyStaticValues.ACatch = 0;
                                MyStaticValues.setback = 0;
                                MyStaticValues.CommandDone = 1;

                                //Call Method
                                CheckVals();
                            }
                        }

                        //Check if Y Axis completed amount of pulses
                        if (sub == "CI00SY*")
                        {
                            MyStaticValues.setback += 1;

                            if (MyStaticValues.setback == 4)
                            {
                                MyStaticValues.Enable = 1;
                                MyStaticValues.ACatch = 0;
                                MyStaticValues.setback = 0;
                                MyStaticValues.CommandDone = 1;
                                CheckVals();
                            }
                        }

                        //Check if Z Axis completed amount of pulses
                        if (sub == "CI00SZ*")
                        {
                            MyStaticValues.setback += 1;

                            if (MyStaticValues.setback == 4)
                            {
                                MyStaticValues.Enable = 1;
                                MyStaticValues.ACatch = 0;
                                MyStaticValues.setback = 0;
                                MyStaticValues.CommandDone = 1;
                                CheckVals();
                            }
                        }

                        //Check if E Axis completed amount of pulses
                        if (sub == "CI00SE*")
                        {
                            MyStaticValues.setback += 1;

                            if (MyStaticValues.setback == 4)
                            {
                                MyStaticValues.Enable = 1;
                                MyStaticValues.ACatch = 0;
                                MyStaticValues.setback = 0;
                                MyStaticValues.CommandDone = 1;
                                CheckVals();
                            }
                        }

                        //Check For Firmware reply Back
                        if (sub == "RI00FW*")
                        {
                            rcvdText.Text = rcvdText.Text.Substring(i + 8, 40);
                        }

                        //Check if ALL Axis Stop button Complete
                        if (sub == "RI00TA*")
                        {
                            if (MyStaticValues.ACatch == 1)
                            {
                                ToggleEnableLine.IsEnabled = true;
                                Firmware1.IsEnabled = true;
                                MyStaticValues.running = 0;
                            }
                        }

                        //X Change Axis Speed Recieved
                        if (sub == "RI00QX*")
                        {
                            //Increment Checker
                            MyStaticValues.FXYZE = MyStaticValues.FXYZE + 1;

                            //All Change axis speed commands recieved
                            if (MyStaticValues.FXYZE == 4)
                            {
                                //Call Change axis speed method
                                sendspd();
                            }
                        }

                        //Y Change Axis Speed Recieved
                        if (sub == "RI00QY*")
                        {
                            MyStaticValues.FXYZE = MyStaticValues.FXYZE + 1;
                            if (MyStaticValues.FXYZE == 4)
                            {
                                sendspd();
                            }
                        }

                        //Z Change Axis Speed Recieved
                        if (sub == "RI00QZ*")
                        {
                            MyStaticValues.FXYZE = MyStaticValues.FXYZE + 1;
                            if (MyStaticValues.FXYZE == 4)
                            {
                                sendspd();
                            }
                        }

                        //E Change Axis Speed Recieved
                        if (sub == "RI00QE*")
                        {
                            MyStaticValues.FXYZE = MyStaticValues.FXYZE + 1;
                            if (MyStaticValues.FXYZE == 4)
                            {
                                sendspd();
                            }
                        }
                    } //End of checking length if
                } //End of checking for bytes
            } //End of byte read
        }

        private void CheckVals()
        {
            //Store Channel Variables

            if (MyStaticValues.CommandDone == 1)
            {
                //----------Right
                //Checks if Joysticks are within value range
                if (MyStaticValues.LJX <= 220 && MyStaticValues.LJX > 180 && MyStaticValues.LJY <= 170 && MyStaticValues.LJY >= 130 && MyStaticValues.RJX <= 170 && MyStaticValues.RJX >= 130)
                {
                    //Check if no direction is active
                    if (MyStaticValues.Direction_Flag == 0)
                    {
                        //Set active direction to Right (1)
                        MyStaticValues.Direction_Flag = 1;

                        //Set flag to disable, Command is being processed
                        MyStaticValues.CommandDone = 0;

                        //Call Method
                        Dir_Right_Press();
                    }
                }
                else
                {
                    //Check if active direction is Right (1)
                    if (MyStaticValues.Direction_Flag == 1)
                    {
                        //Reset to no direction
                        MyStaticValues.Direction_Flag = 0;
                        MyStaticValues.CommandDone = 0;
                        Dir_Right_Release();
                        goto END;
                    }
                }

                //----------Left
                if (MyStaticValues.LJX >= 80 && MyStaticValues.LJX < 120 && MyStaticValues.LJY <= 170 && MyStaticValues.LJY >= 130 && MyStaticValues.RJX <= 170 && MyStaticValues.RJX >= 130)
                {
                    if (MyStaticValues.Direction_Flag == 0)
                    {
                        MyStaticValues.Direction_Flag = 2;
                        MyStaticValues.CommandDone = 0;
                        Dir_Left_Press();
                    }
                }
                else
                {
                    if (MyStaticValues.Direction_Flag == 2)
                    {
                        MyStaticValues.Direction_Flag = 0;
                        MyStaticValues.CommandDone = 0;
                        Dir_Left_Release();
                        goto END;
                    }
                }

                //----------Forward
                if (MyStaticValues.LJY <= 220 && MyStaticValues.LJY > 180 && MyStaticValues.RJX <= 170 && MyStaticValues.RJX >= 130 && MyStaticValues.LJX <= 170 && MyStaticValues.LJX >= 130)
                {
                    if (MyStaticValues.Direction_Flag == 0)
                    {
                        MyStaticValues.Direction_Flag = 3;
                        MyStaticValues.CommandDone = 0;
                        Dir_Forward_Press();
                    }
                }
                else
                {
                    if (MyStaticValues.Direction_Flag == 3)
                    {
                        MyStaticValues.Direction_Flag = 0;
                        MyStaticValues.CommandDone = 0;
                        Dir_Forward_Release();
                        goto END;
                    }
                }

                //----------Reverse
                if (MyStaticValues.LJY >= 80 && MyStaticValues.LJY < 120 && MyStaticValues.RJX <= 170 && MyStaticValues.RJX >= 130 && MyStaticValues.LJX <= 170 && MyStaticValues.LJX >= 130)
                {
                    if (MyStaticValues.Direction_Flag == 0)
                    {
                        MyStaticValues.Direction_Flag = 4;
                        MyStaticValues.CommandDone = 0;
                        Dir_Reverse_Press();
                    }
                }
                else
                {
                    if (MyStaticValues.Direction_Flag == 4)
                    {
                        MyStaticValues.Direction_Flag = 0;
                        MyStaticValues.CommandDone = 0;
                        Dir_Reverse_Release();
                        goto END;
                    }
                }

                //----------Top Right
                if (MyStaticValues.LJY <= 220 && MyStaticValues.LJY > 180 && MyStaticValues.LJX <= 220 && MyStaticValues.LJX > 180 && MyStaticValues.RJX <= 170 && MyStaticValues.RJX >= 130)
                {
                    if (MyStaticValues.Direction_Flag == 0)
                    {
                        MyStaticValues.Direction_Flag = 5;
                        MyStaticValues.CommandDone = 0;
                        Dir_TopRight_Press();
                    }
                }
                else
                {
                    if (MyStaticValues.Direction_Flag == 5)
                    {
                        MyStaticValues.Direction_Flag = 0;
                        MyStaticValues.CommandDone = 0;
                        Dir_TopRight_Release();
                        goto END;
                    }
                }

                //----------Top Left
                if (MyStaticValues.LJY <= 220 && MyStaticValues.LJY > 180 && MyStaticValues.LJX >= 80 && MyStaticValues.LJX < 120 && MyStaticValues.RJX <= 170 && MyStaticValues.RJX >= 130)
                {
                    if (MyStaticValues.Direction_Flag == 0)
                    {
                        MyStaticValues.Direction_Flag = 6;
                        MyStaticValues.CommandDone = 0;
                        Dir_TopLeft_Press();
                    }
                }
                else
                {
                    if (MyStaticValues.Direction_Flag == 6)
                    {
                        MyStaticValues.Direction_Flag = 0;
                        MyStaticValues.CommandDone = 0;
                        Dir_TopLeft_Release();
                        goto END;
                    }
                }
                //----Bottom Right
                if (MyStaticValues.LJY >= 80 && MyStaticValues.LJY < 120 && MyStaticValues.LJX <= 220 && MyStaticValues.LJX > 180 && MyStaticValues.RJX <= 170 && MyStaticValues.RJX >= 130)
                {
                    if (MyStaticValues.Direction_Flag == 0)
                    {
                        MyStaticValues.Direction_Flag = 7;
                        MyStaticValues.CommandDone = 0;
                        Dir_BottomRight_Press();
                    }
                }
                else
                {
                    if (MyStaticValues.Direction_Flag == 7)
                    {
                        MyStaticValues.Direction_Flag = 0;
                        MyStaticValues.CommandDone = 0;
                        Dir_BottomRight_Release();
                        goto END;
                    }
                }

                //----------Bottom Left
                if (MyStaticValues.LJY >= 80 && MyStaticValues.LJY < 120 && MyStaticValues.LJX >= 80 && MyStaticValues.LJX < 120 && MyStaticValues.RJX <= 170 && MyStaticValues.RJX >= 130)
                {
                    if (MyStaticValues.Direction_Flag == 0)
                    {
                        MyStaticValues.Direction_Flag = 8;
                        MyStaticValues.CommandDone = 0;
                        Dir_BottomLeft_Press();
                    }
                }
                else
                {
                    if (MyStaticValues.Direction_Flag == 8)
                    {
                        MyStaticValues.Direction_Flag = 0;
                        MyStaticValues.CommandDone = 0;
                        Dir_BottomLeft_Release();
                        goto END;
                    }
                }

                //----------Clockwise
                //Checks if Joysticks are within value range
                if (MyStaticValues.LJX >= 130 && MyStaticValues.LJX < 170 && MyStaticValues.LJY <= 170 && MyStaticValues.LJY >= 130 && MyStaticValues.RJX <= 120 && MyStaticValues.RJX >= 80)
                {
                    if (MyStaticValues.Direction_Flag == 0)
                    {
                        MyStaticValues.Direction_Flag = 9;
                        MyStaticValues.CommandDone = 0;
                        Dir_RotateCW_Press();
                    }
                }
                else
                {
                    if (MyStaticValues.Direction_Flag == 9)
                    {
                        MyStaticValues.Direction_Flag = 0;
                        MyStaticValues.CommandDone = 0;
                        Dir_RotateCW_Release();
                        goto END;
                    }
                }

                //----------Counter Clockwise
                if (MyStaticValues.LJX >= 130 && MyStaticValues.LJX < 170 && MyStaticValues.LJY <= 170 && MyStaticValues.LJY >= 130 && MyStaticValues.RJX <= 220 && MyStaticValues.RJX >= 180)
                {
                    if (MyStaticValues.Direction_Flag == 0)
                    {
                        MyStaticValues.Direction_Flag = 10;
                        MyStaticValues.CommandDone = 0;
                        Dir_RotateCCW_Press();
                    }
                }
                else
                {
                    if (MyStaticValues.Direction_Flag == 10)
                    {
                        MyStaticValues.Direction_Flag = 0;
                        MyStaticValues.CommandDone = 0;
                        Dir_RotateCCW_Release();
                        goto END;
                    }
                }

                //----------Change Speed
                MyStaticValues.hzstore = HZresult.Text;

                string tmptrav = Travel_Speed.Text;
                MyStaticValues.travelspdvar = Convert.ToString(Math.Round((((Convert.ToDouble(MyStaticValues.RJY) - 100) / 100) * (Convert.ToDouble(max_spd.Text) - Convert.ToDouble(min_spd.Text))) + Convert.ToDouble(min_spd.Text)));

                Travel_Speed.Text = MyStaticValues.travelspdvar;
                calculatetravelspeeds();

                if (tmptrav != MyStaticValues.travelspdvar)
                {
                    //Motors are running
                    if (MyStaticValues.running == 1)
                    {
                        //Speed update is enabled
                        if (MyStaticValues.spdchange == 1)
                        {
                            //Set flag to disable
                            MyStaticValues.spdchange = 0;

                            //Call Change speed method
                            ChangeSpeed();
                        }
                    }
                }

                END:;
            }
        }

        //Change Speed whilst running method
        private async void ChangeSpeed()
        {
            //Declare local variables
            string tmp;
            double hzr = Convert.ToDouble(HZresult.Text);
            double hzs = Convert.ToDouble(MyStaticValues.hzstore);

            MyStaticValues.hzstore = Convert.ToString((hzr - hzs) / MyStaticValues.inc);

            MyStaticValues.tmpHZ = String.Format("{0:000000.000}", Convert.ToDouble(MyStaticValues.hzstore) + hzs);

            MyStaticValues.inc_check = 1;

            //Store substring of axis sendstore
            tmp = MyStaticValues.Ysendstore.Substring(6, 10);

            //Sendstore is not null
            if (tmp != "000000.000")
            {
                //Store Change Axis speed on the fly Command
                MyStaticValues.Ysendstore = "I00QY" + MyStaticValues.tmpHZ + "*";
            }

            tmp = MyStaticValues.Zsendstore.Substring(6, 10);
            if (tmp != "000000.000")
            {
                MyStaticValues.Zsendstore = "I00QZ" + MyStaticValues.tmpHZ + "*";
            }

            tmp = MyStaticValues.Esendstore.Substring(6, 10);
            if (tmp != "000000.000")
            {
                MyStaticValues.Esendstore = "I00QE" + MyStaticValues.tmpHZ + "*";
            }

            tmp = MyStaticValues.Xsendstore.Substring(6, 10);
            if (tmp != "000000.000")
            {
                MyStaticValues.Xsendstore = "I00QX" + MyStaticValues.tmpHZ + "*";
            }

            //Send Command
            sendText.Text = MyStaticValues.Xsendstore;
            SendDataout();
            await Task.Delay(2);

            sendText.Text = MyStaticValues.Ysendstore;
            SendDataout();
            await Task.Delay(2);

            sendText.Text = MyStaticValues.Zsendstore;
            SendDataout();
            await Task.Delay(2);

            sendText.Text = MyStaticValues.Esendstore;
            SendDataout();
            await Task.Delay(2);
        } //End of async read

        //Determines if travelspeed has changed and sends out next increment
        private async void sendspd()
        {
            int X = 0;
            int Y = 0;
            int Z = 0;
            int E = 0;

            await Task.Delay(2);
            //Reset Check Variable
            MyStaticValues.FXYZE = 0;
            //  MyStaticValues.FXYZE_Total = 0;

            //If total speed increments are equal, poll USB
            if (MyStaticValues.inc_check == MyStaticValues.inc)
            {
                //Enable speed change flag
                MyStaticValues.spdchange = 1;
            }
            else
            {
                //Increment checker
                MyStaticValues.inc_check = MyStaticValues.inc_check + 1;

                //Calculate speed increase
                MyStaticValues.tmpHZ = String.Format("{0:000000.000}", Convert.ToDouble(MyStaticValues.tmpHZ) + Convert.ToDouble(MyStaticValues.hzstore));

                //Hertz are below 1 set to 0
                if (Convert.ToDouble(MyStaticValues.tmpHZ) < 1)
                {
                    MyStaticValues.tmpHZ = "000000.000";
                }

                //Store Hertz value of Y Change Axis
                string tmp = MyStaticValues.Ysendstore.Substring(6, 10);

                if (tmp != "000000.000")
                {
                    //Store Change Axis speed on the fly Command
                    MyStaticValues.Ysendstore = "I00QY" + MyStaticValues.tmpHZ + "*";
                }

                tmp = MyStaticValues.Zsendstore.Substring(6, 10);
                if (tmp != "000000.000")
                {
                    MyStaticValues.Zsendstore = "I00QZ" + MyStaticValues.tmpHZ + "*";
                }

                tmp = MyStaticValues.Esendstore.Substring(6, 10);
                if (tmp != "000000.000")
                {
                    MyStaticValues.Esendstore = "I00QE" + MyStaticValues.tmpHZ + "*";
                }

                tmp = MyStaticValues.Xsendstore.Substring(6, 10);
                if (tmp != "000000.000")
                {
                    MyStaticValues.Xsendstore = "I00QX" + MyStaticValues.tmpHZ + "*";
                }

                //Declare Local Variable
                int checkd = 0;

                //Joysticks are centered
                if (MyStaticValues.LJX >= 130 && MyStaticValues.LJX < 170 && MyStaticValues.LJY <= 170 && MyStaticValues.LJY >= 130 && MyStaticValues.RJX <= 170 && MyStaticValues.RJX >= 130)
                {
                    //reset variables
                    MyStaticValues.runchange = 0;
                    MyStaticValues.spdchange = 1;
                    CheckVals();
                }
                else
                {
                    //if a Change axis speed command can be sent
                    if (MyStaticValues.runchange == 1)
                    {
                        //Reset variable
                        MyStaticValues.runchange = 0;

                        //Calculate speed increment
                        string tmptrav = Travel_Speed.Text;
                        MyStaticValues.travelspdvar = Convert.ToString(Math.Round((((Convert.ToDouble(MyStaticValues.RJY) - 100) / 100) * (Convert.ToDouble(max_spd.Text) - Convert.ToDouble(min_spd.Text))) + Convert.ToDouble(min_spd.Text)));

                        Travel_Speed.Text = MyStaticValues.travelspdvar;
                        calculatetravelspeeds();

                        //check if there is a change in speed
                        if (tmptrav != MyStaticValues.travelspdvar)
                        {
                            //Disables sending standard speed change
                            checkd = 1;

                            //Store Speed Increase
                            MyStaticValues.hzstore = MyStaticValues.tmpHZ;
                            ChangeSpeed();
                        }
                    }

                    //If there is no Change in speed
                    if (checkd == 0)
                    {
                        //Send Command

                        sendText.Text = MyStaticValues.Xsendstore;
                        SendDataout();

                        await Task.Delay(2);

                        sendText.Text = MyStaticValues.Ysendstore;
                        SendDataout();

                        await Task.Delay(2);

                        sendText.Text = MyStaticValues.Zsendstore;
                        SendDataout();

                        await Task.Delay(2);

                        sendText.Text = MyStaticValues.Esendstore;
                        SendDataout();

                        await Task.Delay(2);
                    }
                }
            }
        }

        /// <summary>
        /// CancelReadTask:
        /// - Uses the ReadCancellationTokenSource to cancel read operations
        /// </summary>
        private void CancelReadTask()
        {
            if (ReadCancellationTokenSource != null)
            {
                if (!ReadCancellationTokenSource.IsCancellationRequested)
                {
                    ReadCancellationTokenSource.Cancel();
                }
            }
        }

        /// <summary>
        /// CloseDevice:
        /// - Disposes SerialDevice object
        /// - Clears the enumerated device Id list
        /// </summary>
        private void CloseDevice()
        {
            if (serialPort != null)
            {
                serialPort.Dispose();
            }
            serialPort = null;
            comPortInput.IsEnabled = true;
            rcvdText.Text = "";
            listOfDevices.Clear();
        }

        /// <summary>
        /// closeDevice_Click: Action to take when 'Disconnect and Refresh List' is clicked on
        /// - Cancel all read operations
        /// - Close and dispose the SerialDevice object
        /// - Enumerate connected devices
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void closeDevice_Click(object sender, RoutedEventArgs e)
        {
            timer.Stop();
            Disconnectserial();
        }

        private void Disconnectserial()
        {
            try
            {
                status.Text = "";
                CancelReadTask();
                CloseDevice();
                ListAvailablePorts();
                Firmware1.IsEnabled = false;
                Reset.IsEnabled = false;
                MyStaticValues.Enable = 2;
                MyStaticValues.ACatch = 0;
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
            }
        }

        private void Firmware_Click(object sender, RoutedEventArgs e)
        {
            sendText.Text = "I00FW*";
            SendDataout();
        }

        private async void SendDataout()
        {
            try
            {
                if (serialPort != null)
                {
                    // Create the DataWriter object and attach to OutputStream
                    dataWriteObject = new DataWriter(serialPort.OutputStream);

                    //Launch the WriteAsync task to perform the write
                    await WriteAsync();
                }
                else
                {
                    status.Text = "Select a device and connect";
                }
            }
            catch (Exception ex)
            {
                status.Text = "Send Data: " + ex.Message;
            }
            finally
            {
                // Cleanup once complete
                if (dataWriteObject != null)
                {
                    dataWriteObject.DetachStream();
                    dataWriteObject = null;
                }
            }
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            timer.Stop();
            sendText.Text = "N*";
            SendDataout();
            Disconnectserial();
        }
        private void calculatetravelspeeds()
        {
            double tmp = (Convert.ToDouble(Wheel_Diameter.Text) * Math.PI) / 1000000;
            HZresult.Text = String.Format("{0:000000.000}", (((Convert.ToDouble(Travel_Speed.Text)) / tmp) / 3600) * Convert.ToDouble(PulsesPerRev.Text));
            Resolution.Text = Convert.ToString(1.0 / Convert.ToDouble(PulsesPerRev.Text));
            if (Manual_Check.IsChecked == false)
            {
                increment_tx.Text = Convert.ToString((Convert.ToInt16(max_spd.Text) - Convert.ToInt16(min_spd.Text)) * 5);
            }
        }

        private void ToggleEnableLine_Click(object sender, RoutedEventArgs e)
        {
            sendText.Text = "I00HT*";
            SendDataout();
        }

        //--------------------------------Movement Code----------------------------------//
        //Forward has been pressed
        private void Forward_Dir_press(object sender, PointerRoutedEventArgs e)
        {
            Dir_Forward_Press();
        }

        private void Dir_Forward_Press()
        {
            //Set Movement to pressed
            MyStaticValues.Enable = 0;

            //Determine Direction
            MyStaticValues.Movement_Direction = "Forward";

            //Sets Action as a press
            MyStaticValues.Movement_Action = "pressed";

            MyStaticValues.ACatch = 0;

            //initialises Movement set method
            InitMovement();
        }

        //Forward has been released
        private void Forward_Dir_release(object sender, PointerRoutedEventArgs e)
        {
            Dir_Forward_Release();
        }

        private void Dir_Forward_Release()
        {
            //Forward has triggered a release
            if (MyStaticValues.ACatch == 1)
            {
                //Movement has been Pressed
                if (MyStaticValues.Enable == 0)
                {
                    //Movement is Disabled
                    MyStaticValues.Enable = 2;

                    //Determine Direction
                    MyStaticValues.Movement_Direction = "Forward";

                    //Sets Action as a release
                    MyStaticValues.Movement_Action = "released";

                    //initialises Movement set method
                    InitMovement();
                }
            }
            else
            {
                //Set to trigger a release
                MyStaticValues.ACatch = 1;
            }
        }

        //Reverse has been pressed
        private void Reverse_Dir_press(object sender, PointerRoutedEventArgs e)
        {
            Dir_Reverse_Press();
        }

        private void Dir_Reverse_Press()
        {
            if (MyStaticValues.Enable == 1)
            {
                MyStaticValues.Enable = 0;
                MyStaticValues.Movement_Direction = "Reverse";
                MyStaticValues.Movement_Action = "pressed";
                MyStaticValues.ACatch = 0;
                InitMovement();
            }
        }

        //Reverse has been released
        private void Reverse_Dir_release(object sender, PointerRoutedEventArgs e)
        {
            Dir_Reverse_Release();
        }

        private void Dir_Reverse_Release()
        {
            if (MyStaticValues.ACatch == 1)
            {
                if (MyStaticValues.Enable == 0)
                {
                    MyStaticValues.Enable = 2;
                    MyStaticValues.Movement_Direction = "Reverse";
                    MyStaticValues.Movement_Action = "released";
                    InitMovement();
                }
            }
            else
            {
                MyStaticValues.ACatch = 1;
            }
        }

        //Right has been pressed
        private void Right_Dir_press(object sender, PointerRoutedEventArgs e)
        {
            Dir_Right_Press();
        }

        private void Dir_Right_Press()
        {
            if (MyStaticValues.Enable == 1)
            {
                MyStaticValues.Enable = 0;
                MyStaticValues.Movement_Direction = "Right";
                MyStaticValues.Movement_Action = "pressed";
                MyStaticValues.ACatch = 0;
                InitMovement();
            }
        }

        //Right has been released
        private void Right_Dir_release(object sender, PointerRoutedEventArgs e)
        {
            Dir_Right_Release();
        }

        private void Dir_Right_Release()
        {
            if (MyStaticValues.ACatch == 1)
            {
                if (MyStaticValues.Enable == 0)
                {
                    MyStaticValues.Enable = 2;
                    MyStaticValues.Movement_Direction = "Right";
                    MyStaticValues.Movement_Action = "released";
                    InitMovement();
                }
            }
            else
            {
                MyStaticValues.ACatch = 1;
            }
        }

        //Left has been pressed
        private void Left_Dir_press(object sender, PointerRoutedEventArgs e)
        {
            Dir_Left_Press();
        }

        private void Dir_Left_Press()
        {
            if (MyStaticValues.Enable == 1)
            {
                MyStaticValues.Enable = 0;
                MyStaticValues.Movement_Direction = "Left";
                MyStaticValues.Movement_Action = "pressed";
                MyStaticValues.ACatch = 0;
                InitMovement();
            }
        }

        //Left has been released
        private void Left_Dir_release(object sender, PointerRoutedEventArgs e)
        {
            Dir_Left_Release();
        }

        private void Dir_Left_Release()
        {
            if (MyStaticValues.ACatch == 1)
            {
                if (MyStaticValues.Enable == 0)
                {
                    MyStaticValues.Enable = 2;
                    MyStaticValues.Movement_Direction = "Left";
                    MyStaticValues.Movement_Action = "released";
                    InitMovement();
                }
            }
            else
            {
                MyStaticValues.ACatch = 1;
            }
        }

        //Rotate Counterclockwise has been pressed
        private void Rotate_CCW_press(object sender, PointerRoutedEventArgs e)
        {
            Dir_RotateCCW_Press();
        }

        private void Dir_RotateCCW_Press()
        {
            if (MyStaticValues.Enable == 1)
            {
                MyStaticValues.Enable = 0;
                MyStaticValues.Movement_Direction = "Counterclockwise";
                MyStaticValues.Movement_Action = "pressed";
                MyStaticValues.ACatch = 0;
                InitMovement();
            }
        }

        //Rotate Counterclockwise has been released
        private void Rotate_CCW_release(object sender, PointerRoutedEventArgs e)
        {
            Dir_RotateCCW_Release();
        }

        private void Dir_RotateCCW_Release()
        {
            if (MyStaticValues.ACatch == 1)
            {
                if (MyStaticValues.Enable == 0)
                {
                    MyStaticValues.Enable = 2;
                    MyStaticValues.Movement_Direction = "Counterclockwise";
                    MyStaticValues.Movement_Action = "released";
                    InitMovement();
                }
            }
            else
            {
                MyStaticValues.ACatch = 1;
            }
        }

        //Rotate Clockwise has been pressed
        private void Rotate_CW_press(object sender, PointerRoutedEventArgs e)
        {
            Dir_RotateCW_Press();
        }

        private void Dir_RotateCW_Press()
        {
            if (MyStaticValues.Enable == 1)
            {
                MyStaticValues.Enable = 0;
                MyStaticValues.Movement_Direction = "Clockwise";
                MyStaticValues.Movement_Action = "pressed";
                MyStaticValues.ACatch = 0;
                InitMovement();
            }
        }

        //Rotate Clockwise has been released
        private void Rotate_CW_release(object sender, PointerRoutedEventArgs e)

        {
            Dir_RotateCW_Release();
        }

        private void Dir_RotateCW_Release()
        {
            if (MyStaticValues.ACatch == 1)
            {
                if (MyStaticValues.Enable == 0)
                {
                    MyStaticValues.Enable = 2;
                    MyStaticValues.Movement_Direction = "Clockwise";
                    MyStaticValues.Movement_Action = "released";
                    InitMovement();
                }
            }
            else
            {
                MyStaticValues.ACatch = 1;
            }
        }

        //Top Left has been pressed
        private void TopLeft_Dir_press(object sender, PointerRoutedEventArgs e)
        {
            Dir_TopLeft_Press();
        }

        private void Dir_TopLeft_Press()
        {
            if (MyStaticValues.Enable == 1)
            {
                MyStaticValues.Enable = 0;
                MyStaticValues.Movement_Direction = "TopLeft";
                MyStaticValues.Movement_Action = "pressed";
                MyStaticValues.ACatch = 0;
                InitMovement();
            }
        }

        //Top Left has been released
        private void TopLeft_Dir_release(object sender, PointerRoutedEventArgs e)
        {
            Dir_TopLeft_Release();
        }

        private void Dir_TopLeft_Release()
        {
            if (MyStaticValues.ACatch == 1)
            {
                if (MyStaticValues.Enable == 0)
                {
                    MyStaticValues.Enable = 2;
                    MyStaticValues.Movement_Direction = "TopLeft";
                    MyStaticValues.Movement_Action = "released";
                    InitMovement();
                }
            }
            else
            {
                MyStaticValues.ACatch = 1;
            }
        }

        //Top Right has been pressed
        private void TopRight_Dir_press(object sender, PointerRoutedEventArgs e)
        {
            Dir_TopRight_Press();
        }

        private void Dir_TopRight_Press()
        {
            if (MyStaticValues.Enable == 1)
            {
                MyStaticValues.Enable = 0;
                MyStaticValues.Movement_Direction = "TopRight";
                MyStaticValues.Movement_Action = "pressed";
                MyStaticValues.ACatch = 0;
                InitMovement();
            }
        }

        //Top Right has been released
        private void TopRight_Dir_release(object sender, PointerRoutedEventArgs e)
        {
            Dir_TopRight_Release();
        }

        private void Dir_TopRight_Release()
        {
            if (MyStaticValues.ACatch == 1)
            {
                if (MyStaticValues.Enable == 0)
                {
                    MyStaticValues.Enable = 2;
                    MyStaticValues.Movement_Direction = "TopRight";
                    MyStaticValues.Movement_Action = "released";
                    InitMovement();
                }
            }
            else
            {
                MyStaticValues.ACatch = 1;
            }
        }

        //Bottom Left has been pressed
        private void BottomLeft_Dir_press(object sender, PointerRoutedEventArgs e)
        {
            Dir_BottomLeft_Press();
        }

        private void Dir_BottomLeft_Press()
        {
            if (MyStaticValues.Enable == 1)
            {
                MyStaticValues.Enable = 0;
                MyStaticValues.Movement_Direction = "BottomLeft";
                MyStaticValues.Movement_Action = "pressed";
                MyStaticValues.ACatch = 0;
                InitMovement();
            }
        }

        //Bottom Left has been released
        private void BottomLeft_Dir_release(object sender, PointerRoutedEventArgs e)
        {
            Dir_BottomLeft_Release();
        }

        private void Dir_BottomLeft_Release()
        {
            if (MyStaticValues.ACatch == 1)
            {
                if (MyStaticValues.Enable == 0)
                {
                    MyStaticValues.Enable = 2;
                    MyStaticValues.Movement_Direction = "BottomLeft";
                    MyStaticValues.Movement_Action = "released";
                    InitMovement();
                }
            }
            else
            {
                MyStaticValues.ACatch = 1;
            }
        }

        //Bottom Right has been pressed
        private void BottomRight_Dir_press(object sender, PointerRoutedEventArgs e)
        {
            Dir_BottomRight_Press();
        }

        private void Dir_BottomRight_Press()
        {
            if (MyStaticValues.Enable == 1)
            {
                MyStaticValues.Enable = 0;
                MyStaticValues.Movement_Direction = "BottomRight";
                MyStaticValues.Movement_Action = "pressed";
                MyStaticValues.ACatch = 0;
                InitMovement();
            }
        }

        //Bottom Right has been released
        private void BottomRight_Dir_release(object sender, PointerRoutedEventArgs e)
        {
            Dir_BottomRight_Release();
        }

        private void Dir_BottomRight_Release()
        {
            if (MyStaticValues.ACatch == 1)
            {
                if (MyStaticValues.Enable == 0)
                {
                    MyStaticValues.Enable = 2;
                    MyStaticValues.Movement_Direction = "BottomRight";
                    MyStaticValues.Movement_Action = "released";
                    InitMovement();
                }
            }
            else
            {
                MyStaticValues.ACatch = 1;
            }
        }

        //Pointer has exited object Left_Dir
        private void Left_Dir_Exit(object sender, PointerRoutedEventArgs e)
        {
            //Movement has been pressed
            if (MyStaticValues.Enable == 0)
            {
                //calls Left release method
                Dir_Left_Release();
            }
        }

        //Pointer has exited object Right_Dir
        private void Right_Dir_Exit(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.Enable == 0)
            {
                Dir_Right_Release();
            }
        }

        //Pointer has exited object Forward_Dir
        private void Forward_Dir_Exit(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.Enable == 0)
            {
                Dir_Forward_Release();
            }
        }

        //Pointer has exited object Reverse_Dir
        private void Reverse_Dir_Exit(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.Enable == 0)
            {
                Dir_Reverse_Release();
            }
        }

        //Pointer has exited object Rotate_CCW
        private void Rotate_CCW_Exit(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.Enable == 0)
            {
                Dir_RotateCCW_Release();
            }
        }

        //Pointer has exited object Rotate_CW
        private void Rotate_CW_Exit(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.Enable == 0)
            {
                Dir_RotateCW_Release();
            }
        }

        //Pointer has exited object TopRight_Dir
        private void TopRight_Dir_Exit(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.Enable == 0)
            {
                Dir_TopRight_Release();
            }
        }

        //Pointer has exited object BottomRight_Dir
        private void BottomRight_Dir_Exit(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.Enable == 0)
            {
                Dir_BottomRight_Release();
            }
        }

        //Pointer has exited object BottomLeft_Dir
        private void BottomLeft_Dir_Exit(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.Enable == 0)
            {
                Dir_BottomLeft_Release();
            }
        }

        //Pointer has exited object TopLeft_Dir
        private void TopLeft_Dir_Exit(object sender, PointerRoutedEventArgs e)
        {
            if (MyStaticValues.Enable == 0)
            {
                Dir_TopLeft_Release();
            }
        }

        //Method that initialises Movement
        private async void InitMovement()
        {
            //Axis Direction
            string Dir = "";

            //Determines whether the button is pressed or released
            switch (MyStaticValues.Movement_Action)
            {
                //Button is pressed
                case "pressed":

                    //determines which direction to start
                    switch (MyStaticValues.Movement_Direction)
                    {
                        case "Forward":

                            //sets image to Button Down
                            Forward_Dir.Source = new BitmapImage(new Uri("ms-appx:///Assets/Up_D.png"));

                            //Call Graphics Movement Method
                            ME_Forward_press();

                            //sets the pin direction
                            Dir = PinY.Text;

                            //Stores Send Command
                            sendText.Text = "I00C" + "Y" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Ysendstore = sendText.Text;

                            Dir = PinZ.Text;
                            sendText.Text = "I00C" + "Z" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Zsendstore = sendText.Text;

                            Dir = PinE.Text;
                            sendText.Text = "I00C" + "E" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Esendstore = sendText.Text;

                            Dir = PinX.Text;
                            sendText.Text = "I00C" + "X" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Xsendstore = sendText.Text;
                            SendDataout();

                            break;

                        case "Reverse":

                            Reverse_Dir.Source = new BitmapImage(new Uri("ms-appx:///Assets/Down_D.png"));

                            ME_Reverse_press();

                            Dir = (PinY.Text == "1") ? "0" : "1";
                            sendText.Text = "I00C" + "Y" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Ysendstore = sendText.Text;

                            Dir = (PinZ.Text == "1") ? "0" : "1";
                            sendText.Text = "I00C" + "Z" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Zsendstore = sendText.Text;

                            Dir = (PinE.Text == "1") ? "0" : "1";
                            sendText.Text = "I00C" + "E" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Esendstore = sendText.Text;

                            Dir = (PinX.Text == "1") ? "0" : "1";
                            sendText.Text = "I00C" + "X" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Xsendstore = sendText.Text;
                            SendDataout();

                            break;

                        case "Left":

                            Left_Dir.Source = new BitmapImage(new Uri("ms-appx:///Assets/Left_D.png"));

                            ME_Left_press();

                            Dir = (PinY.Text == "1") ? "0" : "1";
                            sendText.Text = "I00C" + "Y" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Ysendstore = sendText.Text;

                            Dir = (PinZ.Text == "1") ? "0" : "1";
                            sendText.Text = "I00C" + "Z" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Zsendstore = sendText.Text;

                            Dir = PinE.Text;
                            sendText.Text = "I00C" + "E" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Esendstore = sendText.Text;

                            Dir = PinX.Text;
                            sendText.Text = "I00C" + "X" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Xsendstore = sendText.Text;
                            SendDataout();

                            break;

                        case "Right":

                            Right_Dir.Source = new BitmapImage(new Uri("ms-appx:///Assets/Right_D.png"));

                            ME_Right_press();

                            Dir = PinY.Text;
                            sendText.Text = "I00C" + "Y" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Ysendstore = sendText.Text;

                            Dir = PinZ.Text;
                            sendText.Text = "I00C" + "Z" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Zsendstore = sendText.Text;

                            Dir = (PinE.Text == "1") ? "0" : "1";
                            sendText.Text = "I00C" + "E" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Esendstore = sendText.Text;

                            Dir = (PinX.Text == "1") ? "0" : "1";
                            sendText.Text = "I00C" + "X" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Xsendstore = sendText.Text;
                            SendDataout();

                            break;

                        case "Counterclockwise":

                            Rotate_CCW.Source = new BitmapImage(new Uri("ms-appx:///Assets/CCWD.png"));

                            ME_CounterClockwise_press();

                            Dir = (PinY.Text == "1") ? "0" : "1";
                            sendText.Text = "I00C" + "Y" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Ysendstore = sendText.Text;

                            Dir = PinZ.Text;
                            sendText.Text = "I00C" + "Z" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Zsendstore = sendText.Text;

                            Dir = PinE.Text;
                            sendText.Text = "I00C" + "E" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Esendstore = sendText.Text;

                            Dir = (PinX.Text == "1") ? "0" : "1";
                            sendText.Text = "I00C" + "X" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Xsendstore = sendText.Text;
                            SendDataout();

                            break;

                        case "Clockwise":

                            Rotate_CW.Source = new BitmapImage(new Uri("ms-appx:///Assets/CWD.png"));

                            ME_Clockwise_press();

                            Dir = PinY.Text;
                            sendText.Text = "I00C" + "Y" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Ysendstore = sendText.Text;

                            Dir = (PinZ.Text == "1") ? "0" : "1";
                            sendText.Text = "I00C" + "Z" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Zsendstore = sendText.Text;

                            Dir = (PinE.Text == "1") ? "0" : "1";
                            sendText.Text = "I00C" + "E" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Esendstore = sendText.Text;

                            Dir = PinX.Text;
                            sendText.Text = "I00C" + "X" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Xsendstore = sendText.Text;
                            SendDataout();

                            break;

                        case "TopRight":

                            TopRight_Dir.Source = new BitmapImage(new Uri("ms-appx:///Assets/trD.png"));

                            ME_TopRight_press();

                            Dir = PinY.Text;
                            sendText.Text = "I00C" + "Y" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Ysendstore = sendText.Text;

                            Dir = PinZ.Text;
                            sendText.Text = "I00C" + "Z" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Zsendstore = sendText.Text;

                            Dir = PinE.Text;
                            sendText.Text = "I00C" + "E" + "000000.000" + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Esendstore = sendText.Text;

                            Dir = PinX.Text;
                            sendText.Text = "I00C" + "X" + "000000.000" + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Xsendstore = sendText.Text;
                            SendDataout();

                            break;

                        case "TopLeft":

                            TopLeft_Dir.Source = new BitmapImage(new Uri("ms-appx:///Assets/tlD.png"));

                            ME_TopLeft_press();

                            Dir = PinY.Text;
                            sendText.Text = "I00C" + "Y" + "000000.000" + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Ysendstore = sendText.Text;

                            Dir = PinZ.Text;
                            sendText.Text = "I00C" + "Z" + "000000.000" + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Zsendstore = sendText.Text;

                            Dir = PinE.Text;
                            sendText.Text = "I00C" + "E" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Esendstore = sendText.Text;

                            Dir = PinX.Text;
                            sendText.Text = "I00C" + "X" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Xsendstore = sendText.Text;
                            SendDataout();

                            break;

                        case "BottomLeft":

                            BottomLeft_Dir.Source = new BitmapImage(new Uri("ms-appx:///Assets/blD.png"));

                            ME_BottomLeft_press();

                            Dir = (PinY.Text == "1") ? "0" : "1";
                            sendText.Text = "I00C" + "Y" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Ysendstore = sendText.Text;

                            Dir = (PinZ.Text == "1") ? "0" : "1";
                            sendText.Text = "I00C" + "Z" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Zsendstore = sendText.Text;

                            Dir = PinE.Text;
                            sendText.Text = "I00C" + "E" + "000000.000" + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Esendstore = sendText.Text;

                            Dir = PinX.Text;
                            sendText.Text = "I00C" + "X" + "000000.000" + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Xsendstore = sendText.Text;
                            SendDataout();

                            break;

                        case "BottomRight":

                            BottomRight_Dir.Source = new BitmapImage(new Uri("ms-appx:///Assets/brD.png"));

                            ME_BottomRight_press();

                            Dir = PinY.Text;
                            sendText.Text = "I00C" + "Y" + "000000.000" + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Ysendstore = sendText.Text;

                            Dir = PinZ.Text;
                            sendText.Text = "I00C" + "Z" + "000000.000" + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Zsendstore = sendText.Text;

                            Dir = (PinE.Text == "1") ? "0" : "1";
                            sendText.Text = "I00C" + "E" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Esendstore = sendText.Text;

                            Dir = (PinX.Text == "1") ? "0" : "1";
                            sendText.Text = "I00C" + "X" + String.Format("{0:000000.000}", Convert.ToDouble(HZresult.Text)) + "0001000000" + Dir + "11" + String.Format("{0:000}", Convert.ToDouble(RampDivide.Text)) + String.Format("{0:000}", Convert.ToDouble(RampPause.Text)) + "0" + String.Format("{0:0}", Convert.ToDouble(EnablePolarity.Text)) + "*";
                            MyStaticValues.Xsendstore = sendText.Text;
                            SendDataout();

                            break;
                    }

                    break;

                // determines that the button is released
                case "released":

                    //determines which direction to stop
                    switch (MyStaticValues.Movement_Direction)
                    {
                        case "Forward":

                            //sets the image to button released
                            Forward_Dir.Source = new BitmapImage(new Uri("ms-appx:///Assets/Up.png"));

                            ME_Forward_release();

                            break;

                        case "Reverse":

                            Reverse_Dir.Source = new BitmapImage(new Uri("ms-appx:///Assets/Down.png"));

                            ME_Reverse_release();

                            break;

                        case "Left":

                            Left_Dir.Source = new BitmapImage(new Uri("ms-appx:///Assets/Left.png"));

                            ME_Left_release();

                            break;

                        case "Right":

                            Right_Dir.Source = new BitmapImage(new Uri("ms-appx:///Assets/Right.png"));

                            ME_Right_release();

                            break;

                        case "Counterclockwise":

                            Rotate_CCW.Source = new BitmapImage(new Uri("ms-appx:///Assets/CCWU.png"));

                            ME_CounterClockwise_release();
                            break;

                        case "Clockwise":

                            Rotate_CW.Source = new BitmapImage(new Uri("ms-appx:///Assets/CWU.png"));

                            ME_Clockwise_release();
                            break;

                        case "TopRight":

                            TopRight_Dir.Source = new BitmapImage(new Uri("ms-appx:///Assets/trU.png"));

                            ME_TopRight_release();
                            break;

                        case "TopLeft":

                            TopLeft_Dir.Source = new BitmapImage(new Uri("ms-appx:///Assets/tlU.png"));

                            ME_TopLeft_release();
                            break;

                        case "BottomLeft":

                            BottomLeft_Dir.Source = new BitmapImage(new Uri("ms-appx:///Assets/blU.png"));

                            ME_BottomLeft_release();
                            break;

                        case "BottomRight":

                            BottomRight_Dir.Source = new BitmapImage(new Uri("ms-appx:///Assets/brU.png"));

                            ME_BottomRight_release();
                            break;
                    }

                    //sends out a stop all command
                    sendText.Text = "I00TA*";
                    SendDataout();

                    break;
            }
        }

        private void ReadRC_Click(object sender, RoutedEventArgs e)
        {
            if (!timer.IsEnabled)
            {
                timer.Start();
            }
        }

        private void E_eyes_Click(object sender, RoutedEventArgs e)
        {
            //Enables/Disables Screen overlay
            eyeBG.Visibility = (eyeBG.Visibility == Visibility.Collapsed) ? Visibility.Visible : Visibility.Collapsed;
            Img_eyes_Placeholder.Visibility = (Img_eyes_Placeholder.Visibility == Visibility.Collapsed) ? Visibility.Visible : Visibility.Collapsed;
            Img_eyes.Visibility = (Img_eyes.Visibility == Visibility.Collapsed) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ME_TopRight_press()
        {
            //check if image is visible
            if (Img_eyes.Visibility == Visibility.Visible)
            {
                //change image source
                Img_eyes.Source = new BitmapImage(new Uri("ms-appx:///Assets/TR.png"));
            }
        }

        private void ME_TopRight_release()
        {
            if (Img_eyes.Visibility == Visibility.Visible)
            {
                Img_eyes.Source = new BitmapImage(new Uri("ms-appx:///Assets/C.png"));
            }
        }

        private void ME_Right_press()
        {
            if (Img_eyes.Visibility == Visibility.Visible)
            {
                Img_eyes.Source = new BitmapImage(new Uri("ms-appx:///Assets/R.png"));
            }
        }

        private void ME_Right_release()
        {
            if (Img_eyes.Visibility == Visibility.Visible)
            {
                Img_eyes.Source = new BitmapImage(new Uri("ms-appx:///Assets/C.png"));
            }
        }

        private void ME_Left_press()
        {
            if (Img_eyes.Visibility == Visibility.Visible)
            {
                Img_eyes.Source = new BitmapImage(new Uri("ms-appx:///Assets/L.png"));
            }
        }

        private void ME_Left_release()
        {
            if (Img_eyes.Visibility == Visibility.Visible)
            {
                Img_eyes.Source = new BitmapImage(new Uri("ms-appx:///Assets/C.png"));
            }
        }

        private void ME_Reverse_press()
        {
            if (Img_eyes.Visibility == Visibility.Visible)
            {
                Img_eyes.Source = new BitmapImage(new Uri("ms-appx:///Assets/D.png"));
            }
        }

        private void ME_Reverse_release()
        {
            if (Img_eyes.Visibility == Visibility.Visible)
            {
                Img_eyes.Source = new BitmapImage(new Uri("ms-appx:///Assets/C.png"));
            }
        }

        private void ME_TopLeft_press()
        {
            if (Img_eyes.Visibility == Visibility.Visible)
            {
                Img_eyes.Source = new BitmapImage(new Uri("ms-appx:///Assets/TL.png"));
            }
        }

        private void ME_TopLeft_release()
        {
            if (Img_eyes.Visibility == Visibility.Visible)
            {
                Img_eyes.Source = new BitmapImage(new Uri("ms-appx:///Assets/C.png"));
            }
        }

        private void ME_BottomLeft_press()
        {
            if (Img_eyes.Visibility == Visibility.Visible)
            {
                Img_eyes.Source = new BitmapImage(new Uri("ms-appx:///Assets/BL.png"));
            }
        }

        private void ME_BottomLeft_release()
        {
            if (Img_eyes.Visibility == Visibility.Visible)
            {
                Img_eyes.Source = new BitmapImage(new Uri("ms-appx:///Assets/C.png"));
            }
        }

        private void ME_BottomRight_press()
        {
            if (Img_eyes.Visibility == Visibility.Visible)
            {
                Img_eyes.Source = new BitmapImage(new Uri("ms-appx:///Assets/BR.png"));
            }
        }

        private void ME_BottomRight_release()
        {
            if (Img_eyes.Visibility == Visibility.Visible)
            {
                Img_eyes.Source = new BitmapImage(new Uri("ms-appx:///Assets/C.png"));
            }
        }

        private void ME_Clockwise_press()
        {
            if (Img_eyes.Visibility == Visibility.Visible)
            {
                Img_eyes.Source = new BitmapImage(new Uri("ms-appx:///Assets/C.png"));
            }
        }

        private void ME_Clockwise_release()
        {
            if (Img_eyes.Visibility == Visibility.Visible)
            {
                Img_eyes.Source = new BitmapImage(new Uri("ms-appx:///Assets/C.png"));
            }
        }

        private void ME_CounterClockwise_press()
        {
            if (Img_eyes.Visibility == Visibility.Visible)
            {
                Img_eyes.Source = new BitmapImage(new Uri("ms-appx:///Assets/C.png"));
            }
        }

        private void ME_CounterClockwise_release()
        {
            if (Img_eyes.Visibility == Visibility.Visible)
            {
                Img_eyes.Source = new BitmapImage(new Uri("ms-appx:///Assets/C.png"));
            }
        }

        private void ME_Forward_press()
        {
            if (Img_eyes.Visibility == Visibility.Visible)
            {
                Img_eyes.Source = new BitmapImage(new Uri("ms-appx:///Assets/U.png"));
            }
        }

        private void ME_Forward_release()
        {
            if (Img_eyes.Visibility == Visibility.Visible)
            {
                Img_eyes.Source = new BitmapImage(new Uri("ms-appx:///Assets/C.png"));
            }
        }

        private void Img_eyes_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            eyeBG.Visibility = (eyeBG.Visibility == Visibility.Collapsed) ? Visibility.Visible : Visibility.Collapsed;
            Img_eyes_Placeholder.Visibility = (Img_eyes_Placeholder.Visibility == Visibility.Collapsed) ? Visibility.Visible : Visibility.Collapsed;
            Img_eyes.Visibility = (Img_eyes.Visibility == Visibility.Collapsed) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void eyeBG_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            eyeBG.Visibility = (eyeBG.Visibility == Visibility.Collapsed) ? Visibility.Visible : Visibility.Collapsed;
            Img_eyes_Placeholder.Visibility = (Img_eyes_Placeholder.Visibility == Visibility.Collapsed) ? Visibility.Visible : Visibility.Collapsed;
            Img_eyes.Visibility = (Img_eyes.Visibility == Visibility.Collapsed) ? Visibility.Visible : Visibility.Collapsed;
        }
        //Enable/Disable Emergency Stop
        private void ToggleEnableLimits_Click(object sender, RoutedEventArgs e)
        {
            if (MyStaticValues.Limit_Enable == 0)
            {
                sendText.Text = "I00KS1*";
                MyStaticValues.Limit_Enable = 1;
            }
            else
            {
                sendText.Text = "I00KS0*";
                MyStaticValues.Limit_Enable = 0;
            }
            SendDataout();
        }

        //On image load populate placeholder image
        private void Img_eyes_ImageOpened(object sender, RoutedEventArgs e)
        {
            Img_eyes_Placeholder.Source = Img_eyes.Source;
        }

        //Increase Max Speed
        private void MaxSpd_Inc_Click(object sender, RoutedEventArgs e)
        {
            max_spd.Text = Convert.ToString(Convert.ToInt16(max_spd.Text) + 1);
            calculatetravelspeeds();
        }

        //Decrease Max Speed
        private void MaxSpd_Dec_Click(object sender, RoutedEventArgs e)
        {
            if (Convert.ToInt16(max_spd.Text) != (Convert.ToInt16(min_spd.Text) + 1))
            {
                max_spd.Text = Convert.ToString(Convert.ToInt16(max_spd.Text) - 1);
            }
            calculatetravelspeeds();
        }

        //Increase Min Speed
        private void MinSpd_Inc_Click(object sender, RoutedEventArgs e)
        {
            if (Convert.ToInt16(min_spd.Text) != (Convert.ToInt16(max_spd.Text) - 1))
            {
                min_spd.Text = Convert.ToString(Convert.ToInt16(min_spd.Text) + 1);
            }
            calculatetravelspeeds();
        }

        //Decrease Min Speed
        private void MinSpd_Dec_Click(object sender, RoutedEventArgs e)
        {
            if (Convert.ToInt16(min_spd.Text) != 0)
            {
                min_spd.Text = Convert.ToString(Convert.ToInt16(min_spd.Text) - 1);
            }
            calculatetravelspeeds();
        }

        private void Travel_Speed_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!String.IsNullOrEmpty(Travel_Speed.Text.Trim()))
            {
                calculatetravelspeeds();
            }
        }

        private void PulsesPerRev_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!String.IsNullOrEmpty(PulsesPerRev.Text.Trim()))
            {
                calculatetravelspeeds();
            }
        }

        private void Wheel_Diameter_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!String.IsNullOrEmpty(Wheel_Diameter.Text.Trim()))
            {
                calculatetravelspeeds();
            }
        }

        //Formats Increment Textbox
        private void increment_tx_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!String.IsNullOrEmpty(increment_tx.Text.Trim()))
            {
                String.Format("{0:000}", Convert.ToDouble(increment_tx.Text));
                MyStaticValues.inc = Convert.ToDouble(increment_tx.Text);
            }
            else
            {
                increment_tx.Text = "0";
            }
        }

        //Formats min_spd Textbox
        private void min_spd_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (String.IsNullOrEmpty(min_spd.Text.Trim()))
            {
                min_spd.Text = "0";
            }
        }

        //Formats max_spd Textbox
        private void max_spd_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (String.IsNullOrEmpty(max_spd.Text.Trim()))
            {
                max_spd.Text = Convert.ToString(Convert.ToInt16(min_spd.Text) + 1);
            }
        }
    }
}